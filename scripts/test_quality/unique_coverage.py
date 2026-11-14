#!/usr/bin/env python3
"""List, for each fixture, the lines that no other fixture's cases reach.

Deleting a test needs evidence that it covers nothing on its own, and which lines only it reaches is
one reading of that. This reads the OpenCover files Code Coverage writes when it is given
`generateTestReferences`: one per case, holding what ran between the case's start and its finish.
`scripts/ci/per_case_coverage.py` writes them, gzipped, and `.github/workflows/per-case-coverage.yml`
runs this over both suites' files.

    python3 scripts/test_quality/unique_coverage.py DIR_OR_FILE [...] [--root PREFIX]
        [--group LABEL=REGEX ...]

A line is a case's when a sequence point on it carries a test reference with a visit count above
zero, so a line of a method the case entered is not the case's unless the case ran it. A case is named
by the test method its reference names, `Class::Method`, which makes every row of a parameterised
method one test, and its fixture is the class that declares the method: a case a fixture inherits from
a base class is the base's. `--group LABEL=REGEX` takes each test whose `Class::Method` the pattern
finds out of its fixture and into LABEL, and a test two patterns find goes to the first; that is the
question a pruning asks of the cases it removes, all of them gone together.

A record holds only what ran between its own case's start and finish, so what a fixture's
`[OneTimeSetUp]` reaches before its first case starts is in none. Nor does a record say what a case
checked of a line it reached: a case alone in reaching a line can assert nothing about it, and a case
reaching only lines others reach can be the only one asserting what they compute. The mutation record
and the kill set measure that; this does not.

Paths are printed relative to `--root`, which defaults to where the workflow mounts the checkout in the
editor's container.

Exits 0 with the report, and 2 where it cannot be read: no file among the inputs, a file that is not
OpenCover, no test reference in any file, or a `--group` that took no test.
"""

import argparse
import gzip
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = "/github/workspace"

CANNOT_READ = 2

# A line two groups reach, which is no group's alone.
SHARED = object()


class Unreadable(Exception):
    pass


def test_of(name):
    """(class, method) of a test reference's name, as Code Coverage spells one.

    The name is the method's return type, the declaring class and the method joined by `::`, then the
    parameters: `static System.Void Velvet.Tests.Outer/Inner::Given_X(System.Int32)`. A nested class is
    written with `/` there, and is written back with `+`, the way the runner names it.
    """
    head, separator, tail = name.partition("::")
    if not separator:
        raise Unreadable("the test reference {!r} names no method".format(name))
    return head.rsplit(" ", 1)[-1].replace("/", "+"), tail.split("(", 1)[0]


def coverage_files(inputs):
    """Every OpenCover file among `inputs`, each a file or a directory searched below it."""
    found = []
    for given in inputs:
        path = Path(given)
        if path.is_dir():
            found.extend(sorted(item for pattern in ("*.xml", "*.xml.gz") for item in path.rglob(pattern)
                                if item.is_file()))
        elif path.is_file():
            found.append(path)
        else:
            raise Unreadable("{} is neither a file nor a directory".format(given))
    if not found:
        raise Unreadable("no OpenCover file among: {}".format(" ".join(inputs)))
    return found


def references(path):
    """{(class, method): {(source, line)}} for every test reference in one OpenCover file."""
    opener = gzip.open if path.name.endswith(".gz") else open
    try:
        with opener(str(path), "rb") as stream:
            session = ET.parse(stream).getroot()
    except (OSError, EOFError, ET.ParseError) as error:
        raise Unreadable("{} is not an OpenCover file: {}".format(path, error))
    if session.tag != "CoverageSession":
        raise Unreadable("{} is not an OpenCover file: its root is <{}>".format(path, session.tag))

    found = {}
    for module in session.iter("Module"):
        sources = {item.get("uid"): item.get("fullPath") for item in module.iter("File")}
        tests = {item.get("uid"): test_of(item.get("name", "")) for item in module.iter("TrackedMethod")}
        for method in module.iter("Method"):
            declared = method.find("FileRef")
            fallback = declared.get("uid") if declared is not None else None
            for point in method.iter("SequencePoint"):
                for reference in point.iter("TrackedMethodRef"):
                    try:
                        visits, line = int(reference.get("vc")), int(point.get("sl"))
                    except (TypeError, ValueError):
                        raise Unreadable("{} holds a sequence point without a line or a count".format(path))
                    if visits <= 0:
                        continue
                    test = tests.get(reference.get("uid"))
                    source = sources.get(point.get("fileid", fallback))
                    if test is None or source is None:
                        raise Unreadable(
                            "{} references a test or a file its module does not declare".format(path))
                    found.setdefault(test, set()).add((source, line))
    return found


def parse_groups(options):
    groups = []
    for option in options:
        label, separator, pattern = option.partition("=")
        if not separator or not label:
            raise Unreadable("--group {!r} is not LABEL=REGEX".format(option))
        try:
            groups.append((label, re.compile(pattern)))
        except re.error as error:
            raise Unreadable("--group {}: {}".format(label, error))
    return groups


def group_of(test, groups):
    key = "{}::{}".format(*test)
    for label, pattern in groups:
        if pattern.search(key):
            return label
    return test[0]


def relative(source, root):
    prefix = root.rstrip("/") + "/"
    return source[len(prefix):] if root and source.startswith(prefix) else source


def ranges(lines):
    """`10-12, 20` for [10, 11, 12, 20]."""
    spans = []
    for line in sorted(lines):
        if spans and line == spans[-1][1] + 1:
            spans[-1][1] = line
        else:
            spans.append([line, line])
    return ", ".join(str(first) if first == last else "{}-{}".format(first, last) for first, last in spans)


def read(paths, groups):
    """(owner, labels, tests, records, unnamed): which group alone reaches each line, or SHARED; every
    group a test fell into; every test; how many files held a test reference; how many held none."""
    owner, labels, tests = {}, set(), set()
    records = unnamed = 0
    for path in paths:
        found = references(path)
        if not found:
            unnamed += 1
            continue
        records += 1
        for test, lines in found.items():
            label = group_of(test, groups)
            tests.add(test)
            labels.add(label)
            for line in lines:
                if owner.setdefault(line, label) != label:
                    owner[line] = SHARED
    return owner, labels, tests, records, unnamed


def report(owner, labels, root):
    alone = {}
    for (source, line), label in owner.items():
        if label is not SHARED:
            alone.setdefault(label, {}).setdefault(relative(source, root), set()).add(line)
    printed = []
    for label in sorted(alone):
        printed.append("{}: {} line(s) only it reaches".format(
            label, sum(len(lines) for lines in alone[label].values())))
        printed.extend("  {}: {}".format(source, ranges(lines))
                       for source, lines in sorted(alone[label].items()))
    rest = sorted(labels - set(alone))
    printed.append("{} group(s) reach no line alone{}".format(len(rest), ":" if rest else ""))
    printed.extend("  " + label for label in rest)
    return printed


def main(argv):
    parser = argparse.ArgumentParser(description="List the lines only one fixture's cases reach.")
    parser.add_argument("inputs", nargs="+", help="an OpenCover file, gzipped or not, or a directory of them")
    parser.add_argument("--root", default=ROOT, help="the prefix the printed paths are taken relative to")
    parser.add_argument("--group", action="append", default=[], metavar="LABEL=REGEX",
                        help="pool the tests whose Class::Method REGEX finds under LABEL; repeatable")
    args = parser.parse_args(argv)

    try:
        groups = parse_groups(args.group)
        paths = coverage_files(args.inputs)
        owner, labels, tests, records, unnamed = read(paths, groups)
        if not records:
            raise Unreadable("no file among {} holds a test reference, so none was written with "
                             "generateTestReferences".format(len(paths)))
        empty = [label for label, _ in groups if label not in labels]
        if empty:
            raise Unreadable("--group {} took no test".format(", ".join(empty)))
    except Unreadable as error:
        print("cannot take this reading: {}".format(error), file=sys.stderr)
        return CANNOT_READ

    print("read {} file(s): {} held test references, to {} test(s) in {} group(s); {} held none".format(
        len(paths), records, len(tests), len(labels), unnamed))
    for line in report(owner, labels, args.root):
        print(line)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""Fail when a test case gates the behaviour it is named for behind an `Assume`, against a baseline.

An `Assume` that turns out false reports Inconclusive, which the runner does not count as a failure.
Where the assumption is somebody else's business -- a component with its own tests, an environment
that may or may not grant focus -- that is the right answer. Where it gates what the case exists to
pin, the day the behaviour breaks is the day the case stops saying so: the regression arrives as an
Inconclusive nothing counts, and only the last step of the run reports it. CLAUDE.md's remedy is to
fold the gated state into the assertion instead, as one comparison over a tuple of it and the state
under test.

`assert_no_inconclusive.py` reddens on one that has fired, which is that day and no earlier. What is
checked here is the shape, by inspection, so that the case is sharpened before the day arrives.

Which of the two an `Assume` is cannot be decided in general -- it turns on whether a regression in
the behaviour can falsify the assumption, which is the behaviour's question and not the text's. Two
sub-shapes can be, and both fall out of the Arrange/Act/Assert sections this repository's tests are
required to carry, because those sections are a statement about which lines are the behaviour:

*It gates a value the Act produced.* The Act is the behaviour under test, so anything it introduced
is too. This reads the value, not the line, so it holds wherever the `Assume` was written -- and both
spellings are here, one with the gate inside the Act section and one with it below.

*It sits in the Assert section.* Everything there is a reading of the behaviour, which is what the
section means. A precondition about the environment or about another component belongs above the Act.

Each reading needs the marker that delimits it, and a case carrying an `Assume` without one is
recorded as unreadable -- one entry per reading that could not be taken -- rather than passed.

The baseline records what is here rather than a count, for the reason duplication_check.py's does:
a total nets a fix off against a new one, and the new one is what this exists to catch. An entry the
scan no longer finds fails too, so the record cannot outlive the case and a scan that read nothing
loses every entry rather than passing.

Run: python3 scripts/test_quality/assume_gate_check.py
"""

import argparse
import importlib.util
import re
import sys
from pathlib import Path

DEFAULT_BASELINE = "scripts/test_quality/assume_gate_baseline.txt"
PACKAGE_REL = "Packages/com.velvet.core"

GATES_ACT_VALUE = "gates-act-value"
GATES_IN_ASSERT = "gates-in-assert"
UNREADABLE = "unreadable"

# Read off the raw line, since this is the one thing in a test body that lives in a comment. A line
# names as many sections as it chains, because this repository writes `// Arrange / Act` and
# `// Act + Assert` where one stretch of code is both, 471 times: taking only the first name off
# those recorded 75 cases as missing a marker they carry, and hid 52 gates behind an Act section
# never located. The three separators are the ones the package writes, so a fourth reads as no chain
# and puts the case in the record as unreadable rather than passing it quietly.
SECTION = re.compile(
    r"^\s*//\s*((?:Arrange|Act|Assert)(?:\s*[/+&]\s*(?:Arrange|Act|Assert))*)\b", re.IGNORECASE)
SECTION_NAME = re.compile(r"Arrange|Act|Assert", re.IGNORECASE)
ASSUME = re.compile(r"\bAssume\s*\.\s*That\s*(?:<[^<>()]*>)?\s*\(")
IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
# `out var x`, `var x =`, `out x`, and the deconstruction spelling this repository also writes.
INTRODUCED = re.compile(r"\bvar\s*\(([^)]*)\)|\b(?:out\s+var|var|out)\s+([A-Za-z_][A-Za-z0-9_]*)")
# The typed spelling, which a case reaches for when the local is assigned inside a lambda or a branch
# and so cannot be `var`. Two identifiers have to sit before the `=`, so an assignment to something
# already declared -- `element.style.width = 10` -- is not one. The start anchor holds it to one
# attempt per line, which is what makes that rule mean a declaration; a mutation sweep found nothing
# in this repository that tells the anchored reading from the unanchored one, so no case here fails
# when the anchor goes.
TYPED = re.compile(
    r"^[ \t]*(?:(?:readonly|const|using|await|static)\s+)*"
    r"[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*"
    r"(?:\s*<[^<>;=]*>)?(?:\s*\[\s*\])*\s*\??\s+"
    r"([A-Za-z_][A-Za-z0-9_]*)\s*=",
    re.MULTILINE)


def _sibling(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    path = Path(__file__).resolve().with_name(name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# Which text a C# compiler sees, and where one test case starts and ends, are two questions with one
# answer each, and base_red_check.py owns both. A second reader of the same thing is a second thing to
# get wrong, and the way that matters here is reading fewer cases, which reports as a clean repository.
_base_red_check = _sibling("base_red_check")
code_lines = _base_red_check.code_lines
csharp_cases = _base_red_check.csharp_cases
kind_of = _base_red_check.kind_of


def first_argument(text, open_paren):
    """(start, end) of the first argument of the call whose `(` is at `open_paren`, or None.

    A span rather than the text, so the same offsets read the masked body for what the expression
    names and the raw body for what it says. Two gates differing only inside a string literal are one
    line in the record when the masked form is what gets written there.
    """
    depth = 0
    for offset in range(open_paren, len(text)):
        character = text[offset]
        if character in "([{":
            depth += 1
        elif character in ")]}":
            depth -= 1
            if depth == 0:
                return open_paren + 1, offset
        elif character == "," and depth == 1:
            return open_paren + 1, offset
    return None


def roots_of(text):
    """The identifiers an expression reads in its own right, not the members it reaches through.

    `m.resolvedStyle.opacity` reads `m`. Taking every identifier instead makes a member whose name
    happens to match a local read as a use of it, which put two cases under a reading they did not
    have.
    """
    names = set()
    for match in IDENTIFIER.finditer(text):
        if not text[:match.start()].rstrip().endswith("."):
            names.add(match.group(0))
    return names


def introduced_by(text):
    """The locals a stretch of code declares, over the spellings this repository's Acts are written in.

    Which spellings those are is the whole reach of the reading below: a case declaring its local as
    `VisualElement spacer = null` rather than `var` gates the same behaviour and was invisible here.
    """
    names = set(TYPED.findall(text))
    for match in INTRODUCED.finditer(text):
        if match.group(1) is not None:
            names |= set(IDENTIFIER.findall(match.group(1)))
        else:
            names.add(match.group(2))
    names.discard("_")
    return names


def sections_of(raw_body):
    """Marker -> the index in `raw_body` it sits on, for the markers a case carries."""
    marks = {}
    for index, line in enumerate(raw_body):
        found = SECTION.match(line)
        if found:
            for name in SECTION_NAME.findall(found.group(1)):
                marks.setdefault(name.lower(), index)
    return marks


def readings_of(code_body, raw_body):
    """Every (reading, detail) one case's `Assume` calls are read as. Empty where none gates.

    A missing marker is reported rather than worked around. Each reading needs one: the act-value one
    needs `// Act` to know which lines the behaviour is, the position one needs `// Assert`. Taking
    the readings that remain and saying nothing about the other is a case judged on half the question
    and reported as clean.
    """
    if not any(ASSUME.search(line) for line in code_body):
        return []
    marks = sections_of(raw_body)
    act, assert_at = marks.get("act"), marks.get("assert")
    found = []
    if act is None:
        found.append((UNREADABLE, "no // Act marker, so a gate over what the Act made is not read"))
    if assert_at is None:
        found.append((UNREADABLE, "no // Assert marker, so a gate below it is not read"))
    act_text = "\n".join(code_body[act:assert_at if assert_at is not None else len(code_body)]) \
        if act is not None else ""
    produced = introduced_by(act_text)
    for index, line in enumerate(code_body):
        for match in ASSUME.finditer(line):
            masked = "\n".join(code_body[index:])
            span = first_argument(masked, match.end() - 1)
            if span is None:
                found.append((UNREADABLE, "an Assume argument list does not close in this case"))
                continue
            start, end = span
            subject = masked[start:end]
            # The detail is the raw text at the same offsets: what the case says, not what the mask
            # left of it. `code_lines` keeps each line's length, so the two bodies index together.
            written = " ".join("\n".join(raw_body[index:])[start:end].split())
            if roots_of(subject) & produced:
                found.append((GATES_ACT_VALUE, written))
            elif assert_at is not None and index >= assert_at:
                found.append((GATES_IN_ASSERT, written))
    return found


def scan(project):
    """(the entries, cases read) over every C# test case in the repository.

    The gated expression is part of the entry. Keying on the case alone collapses two gates of one
    kind into one line, and what that hides is a case fixing the gate it was recorded for while
    adding another -- which nets to a record that did not move.
    """
    entries, read = set(), 0
    package = project / PACKAGE_REL
    for path in sorted(package.rglob("*.cs")):
        relative = path.relative_to(project).as_posix()
        if kind_of(relative) != "csharp":
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        code = code_lines(text)
        raw = text.splitlines()
        for case in csharp_cases(text, relative):
            read += 1
            body = slice(case.first_line - 1, case.last_line)
            for reading, detail in readings_of(code[body], raw[body]):
                entries.add("\t".join((reading, relative, case.name, detail)))
    return entries, read


def read_baseline(path):
    lines = [line.rstrip("\n") for line in path.read_text().splitlines() if line.strip()]
    if not lines:
        raise ValueError("baseline file is empty")
    return set(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--baseline", default=DEFAULT_BASELINE,
                        help="compare against this record (default: {})".format(DEFAULT_BASELINE))
    parser.add_argument("--write-baseline", metavar="FILE",
                        help="write what is here now and exit, for a deliberate change")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    entries, read = scan(project)
    print("{} case(s) read, {} gated or unreadable".format(read, len(entries)))

    if args.write_baseline:
        target = (project / args.write_baseline).resolve()
        target.write_text("".join(entry + "\n" for entry in sorted(entries)))
        print("record written to {}".format(target))
        return 0

    baseline_path = (project / args.baseline).resolve()
    if not baseline_path.is_file():
        print("error: no record at {}".format(baseline_path), file=sys.stderr)
        return 1
    baseline = read_baseline(baseline_path)
    added = sorted(entries - baseline)
    removed = sorted(baseline - entries)

    for entry in added:
        reading, relative, name, detail = entry.split("\t")
        print("{}\n  {}\n  {}\n  {}".format(reading, relative, name, detail), file=sys.stderr)
    for entry in removed:
        print("no longer here, so remove it from the record: {}".format(entry.replace("\t", "  ")),
              file=sys.stderr)
    if added:
        print("\nAn Assume that gates the behaviour a case is named for turns that case's regression "
              "into an\nInconclusive, which the runner does not count. Fold the gated state into the "
              "assertion as one\ncomparison over a tuple of it and the state under test; delete it "
              "only where the assertion alone\nwould still fail on the broken behaviour.",
              file=sys.stderr)
    return 1 if added or removed else 0


if __name__ == "__main__":
    sys.exit(main())

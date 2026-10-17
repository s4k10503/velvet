#!/usr/bin/env python3
"""Fail when a Python test packs several values into one assertion, against a record.

AGENTS.md's Tests section owns the rule: one assert per test, comparing one value, and a precondition
either arranged away or carried by that value as a sentinel the expected value can never equal. A
tuple of the precondition and the state under test is the shape the rule replaced, and this reads for
it in the Python suites, where the assertion calls are methods whose arguments `ast` hands over whole.

An argument packs when it is a tuple or a list display of two or more elements and either the other
compared argument is a display too, or it is the first argument and holds an element that is not a
literal. The second clause is what reaches `assertEqual((red, asked), expected)`; the literal exemption
is what leaves `assertEqual(["2.1", "2.0", "1"], series)` alone, an expected sequence written first.
A computed value compared with a display, `assertEqual(read(), (FIRST, SECOND))`, is one value and is
not read as packing -- and neither, since the text cannot tell the two apart, is a display of several
values written second against a name.

The record lists what is here rather than a count, for the reason assume_gate_check.py's does: a total
nets a rewrite off against a new one. An entry the scan no longer finds fails too, so the record shrinks
with each rewrite rather than outliving it.

Run: python3 scripts/test_quality/tuple_assert_check.py
"""

import argparse
import ast
import collections
import sys
from pathlib import Path

DEFAULT_BASELINE = "scripts/test_quality/tuple_assert_baseline.txt"

# Where this repository keeps its Python suites. A suite outside these is read by nothing, which
# reports the same as a clean one.
SOURCE_ROOTS = ("scripts", ".harness")


def is_display(node):
    return isinstance(node, (ast.Tuple, ast.List)) and len(node.elts) >= 2


def is_literal(node):
    """Whether an element is written as a literal, `-1` and a nested tuple of constants included."""
    try:
        ast.literal_eval(node)
    except ValueError:
        return False
    return True


def packs(call):
    """Whether one assertion call compares a packed display."""
    if not (isinstance(call.func, ast.Attribute) and call.func.attr.startswith("assert") and call.args):
        return False
    first = call.args[0]
    if not is_display(first):
        return False
    if len(call.args) > 1 and is_display(call.args[1]):
        return True
    return not all(is_literal(element) for element in first.elts)


def entries_of(text, relative):
    """One "relative<TAB>case<TAB>call" line per packed assertion in a file's source."""
    tree = ast.parse(text)
    found = []

    def visit(node, owner):
        for child in ast.iter_child_nodes(node):
            name = owner
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                name = child.name if owner is None else owner + "." + child.name
            if isinstance(child, ast.Call) and packs(child):
                written = " ".join(ast.get_source_segment(text, child).split())
                found.append("\t".join((relative, owner or "<module>", written)))
            visit(child, name)

    visit(tree, None)
    return found


def scan(project):
    """(a multiset of entries, files read) over every Python test file under the source roots."""
    entries, read = collections.Counter(), 0
    for root in SOURCE_ROOTS:
        for path in sorted((project / root).rglob("test_*.py")):
            read += 1
            relative = path.relative_to(project).as_posix()
            entries.update(entries_of(path.read_text(encoding="utf-8"), relative))
    return entries, read


def read_baseline(path):
    return collections.Counter(line for line in path.read_text(encoding="utf-8").splitlines()
                               if line.strip())


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
    print("{} file(s) read, {} packed assertion(s)".format(read, sum(entries.values())))

    if args.write_baseline:
        target = (project / args.write_baseline).resolve()
        target.write_text("".join(entry + "\n" for entry in sorted(entries.elements())),
                          encoding="utf-8")
        print("record written to {}".format(target))
        return 0

    baseline_path = (project / args.baseline).resolve()
    if not baseline_path.is_file():
        print("error: no record at {}".format(baseline_path), file=sys.stderr)
        return 1
    baseline = read_baseline(baseline_path)
    added = sorted((entries - baseline).elements())
    removed = sorted((baseline - entries).elements())

    for entry in added:
        relative, case, written = entry.split("\t")
        print("packed\n  {}\n  {}\n  {}".format(relative, case, written), file=sys.stderr)
    for entry in removed:
        print("no longer here, so remove it from the record: {}".format(entry.replace("\t", "  ")),
              file=sys.stderr)
    if added:
        print("\nAssert one value. Arrange a precondition so it cannot be false, or let the asserted "
              "value carry it\nas a sentinel the expected value can never equal; AGENTS.md's Tests "
              "section owns both forms.",
              file=sys.stderr)
    return 1 if added or removed else 0


if __name__ == "__main__":
    sys.exit(main())

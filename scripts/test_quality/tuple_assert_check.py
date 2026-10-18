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
A name argument is followed to the last assignment of a display to it earlier in the same function (or
an enclosing one), so `got = (code, "x" in said)` then `assertEqual(got, (2, True))` reads as the
packing it is; the display it resolves to then counts as one written in the call, except that a bound
display written second packs only against a display written first. A computed value compared with a
display written in the call, `assertEqual(read(), (FIRST, SECOND))`, is one value and is not read as
packing -- the text of the call cannot tell it from a packed one, and a name bound some other way is not
followed.

The record lists what is here rather than a count, for the reason assume_gate_check.py's does: a total
nets a rewrite off against a new one. An entry the scan no longer finds fails too, so the record shrinks
with each rewrite rather than outliving it.

Run: python3 scripts/test_quality/tuple_assert_check.py
"""

import argparse
import ast
import collections
import subprocess
import sys
import warnings
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


def display_of(arg, bindings):
    """(the display `arg` stands for, the assignment that bound it or None).

    A display written in the call stands for itself; a name stands for the display last assigned to
    it before the call, unless a later assignment in that scope bound something else.
    """
    if is_display(arg):
        return arg, None
    if isinstance(arg, ast.Name):
        bound = None
        for scope in reversed(bindings):
            if arg.id in scope:
                bound = scope[arg.id]
                break
        if bound is not None and is_display(bound.value):
            return bound.value, bound
    return None, None


def packs(call, bindings=()):
    """The assignment a packed argument was bound by ("" for a display written in the call), or None."""
    if not (isinstance(call.func, ast.Attribute) and call.func.attr.startswith("assert") and call.args):
        return None
    first, first_bound = display_of(call.args[0], bindings)
    second, _ = display_of(call.args[1], bindings) if len(call.args) > 1 else (None, None)
    if first is not None:
        if second is not None or not all(is_literal(element) for element in first.elts):
            return first_bound or ""
    return None


def last_assignments(function):
    """name -> its assignment, ordered by line, over the function's own body (nested defs excluded)."""
    found = []

    def walk(node):
        for child in ast.iter_child_nodes(node):
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
                continue
            if isinstance(child, ast.Assign):
                found.append(child)
            walk(child)

    walk(function)
    return sorted(found, key=lambda assign: assign.lineno)


def entries_of(text, relative):
    """One "relative<TAB>case<TAB>call" line per packed assertion in a file's source."""
    tree = ast.parse(text)
    found = []

    def bindings_before(assigns, line):
        scope = {}
        for assign in assigns:
            if assign.lineno >= line:
                break
            for target in assign.targets:
                if isinstance(target, ast.Name):
                    scope[target.id] = assign
                else:
                    for name in ast.walk(target):
                        if isinstance(name, ast.Name):
                            scope.pop(name.id, None)
        return scope

    def visit(node, owner, assigns_stack):
        for child in ast.iter_child_nodes(node):
            name, stack = owner, assigns_stack
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                name = child.name if owner is None else owner + "." + child.name
                if not isinstance(child, ast.ClassDef):
                    stack = assigns_stack + [last_assignments(child)]
            if isinstance(child, ast.Call):
                scopes = [bindings_before(assigns, child.lineno) for assigns in stack]
                bound = packs(child, scopes)
                if bound is not None:
                    written = " ".join(ast.get_source_segment(text, child).split())
                    if bound:
                        written += "  with  " + " ".join(ast.get_source_segment(text, bound).split())
                    found.append("\t".join((relative, owner or "<module>", written)))
            visit(child, name, stack)

    visit(tree, None, [])
    return found


def tracked_tests(project):
    """The tracked test_*.py paths under the source roots, or None where `project` is not a git work tree."""
    try:
        done = subprocess.run(["git", "-C", str(project), "ls-files", "-z", "--"] + list(SOURCE_ROOTS),
                              capture_output=True, text=True, check=True)
    except (OSError, subprocess.CalledProcessError):
        return None
    paths = [project / name for name in done.stdout.split("\0") if name]
    return sorted(path for path in paths if path.name.startswith("test_") and path.suffix == ".py")


def scan(project):
    """(a multiset of entries, files read) over the tracked Python test files under the source roots.

    An untracked file is not read where `project` is a git work tree, so a scratch suite does not
    change the answer; outside one, every test_*.py under the roots is.
    """
    paths = tracked_tests(project)
    if paths is None:
        paths = sorted(path for root in SOURCE_ROOTS for path in (project / root).rglob("test_*.py"))
    entries = collections.Counter()
    for path in paths:
        relative = path.relative_to(project).as_posix()
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            entries.update(entries_of(path.read_text(encoding="utf-8"), relative))
    return entries, len(paths)


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

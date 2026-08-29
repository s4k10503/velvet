#!/usr/bin/env python3
"""Refuse a Unity test run whose results are not a reading of this worktree.

A results file is written where the caller points, and nothing ties it to the run that reads it.
Measured on a worktree seeded with another checkout's `Library`, with that checkout's extra
fixture deleted here and a compile error introduced: Unity exited 1 and wrote no results, leaving
"Scripts have compiler errors" in the log, and the file already at that path went on reporting one
passing case named for a fixture no source in the tree declares -- under a -testFilter naming
something else, which is what makes it read as a result rather than as a failure to run. Unity's
exit code is the caller's to notice, and nobody was reading it.

One precondition before any of them: the results and the log have to sit inside the project named.
A caller that spells an absolute path to a run's output and lets --project default is naming two
trees, and every reading below is then a comparison across them. Measured on two worktrees of this
repository declaring the same fixtures: exit 0, having compared one tree's sources against the
other's results. It is only when the running tree declares a fixture this one does not that the
third reading catches the pairing -- and there it reports a stranger, which is the sentence for a
stale Library rather than for a call pointed at two trees.

Three readings, and the run has to survive all of them:

  - the log names the results file, which is how a run says the file is its own;
  - the log carries no line rendered as a compiler error, whichever analyzer raised it;
  - every fixture reported is a type some source here declares, unless the assembly reporting it is
    a resolved package's and not this worktree's.

The third asks nothing of the log, so it is what stands where a results file IS the run's own and
the tree is not the one it was taken from -- a checkout that moved between the run and the reading,
or a project pointed at from somewhere else.

Every reading it cannot take is a refusal. A guard that exits 0 because git, the log or the results
went unread is indistinguishable from one that exits 0 having checked, which is the failure this
whole file exists to make impossible.

Run: python3 scripts/test_quality/test_assert_results_from_this_tree.py
"""

import argparse
import importlib.util
import json
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# Not the CS ID space: an analyzer under Generators~ raises its own at error severity, which fails
# the compile with no CS code in the log to find it by. Matched on the rendering a diagnostic line
# carries rather than on a list of ID prefixes -- the test module reads the identifiers the
# analyzers declare and holds this pattern to matching every one.
#
# The whole shape is read, and the test module has a case per way of giving one up. Drop the
# separator and a run's own prose -- "mount error: the target was null" -- refuses the run that
# printed it. Allow a capital as well and the `] Error:` lines Unity's own subsystems print refuse
# a run that compiled cleanly.
COMPILE_ERROR = re.compile(r": error ")

SAVED_RESULTS = re.compile(r"^Saving results to: (.+?)\s*$", re.MULTILINE)

# NUnit names a fixture by its runtime type, so a nested one is joined with a + and a generic one
# carries its arity: Velvet.Tests.PoolHelperTestsBase`1 is how the cases a generic base declares are
# reported, under that base rather than under the closed types that run them.
CLR_NESTING = re.compile(r"\+")
CLR_ARITY = re.compile(r"`\d+")


def _sibling(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    path = Path(__file__).resolve().with_name(name + ".py")
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


# The masking and the brace profile a type is read off below are base_red_check.py's, which owns why
# a declaration is taken off a masked line rather than a raw one. Its namespace reading is not
# taken: that one keeps the last name it saw, and `declared_types` stacks them instead because two
# block namespaces nest to the name NUnit reports.
_base_red_check = _sibling("base_red_check")


class Unreadable(Exception):
    """A reading the check could not take."""


# --------------------------------------------------------------------------------------------------
# What this worktree holds
# --------------------------------------------------------------------------------------------------

def worktree_files(project, pathspec):
    """Every file matching a pathspec that git tracks or does not ignore.

    Asked of git rather than walked, so a fixture written a minute ago and never added counts as
    this worktree's while everything under the directories .gitignore names -- Library/PackageCache
    above all, which holds a resolved package's own fixtures -- does not.
    """
    finished = subprocess.run(
        ["git", "-C", str(project), "ls-files", "-z", "--cached", "--others", "--exclude-standard",
         "--", pathspec],
        capture_output=True, text=True)
    if finished.returncode != 0:
        raise Unreadable("git could not list {} under {}: {}".format(
            pathspec, project, finished.stderr.strip() or finished.returncode))
    return [project / name for name in finished.stdout.split("\0") if name]


def assembly_name(path):
    # utf-8-sig: a package ships an assembly definition with a byte-order mark, and json refuses one.
    try:
        declared = json.loads(path.read_text(encoding="utf-8-sig", errors="replace"))["name"]
    except (OSError, ValueError, KeyError) as error:
        raise Unreadable("{}: unreadable assembly definition ({})".format(path, error))
    return declared


def declared_assemblies(project):
    """The assemblies this worktree's own assembly definitions name."""
    names = {assembly_name(path) for path in worktree_files(project, "*.asmdef")}
    if not names:
        raise Unreadable("{}: no assembly definition here, so no assembly is this worktree's to "
                         "answer for".format(project))
    return names


def resolved_assemblies(project):
    """The assemblies the packages this project resolved name, read out of the imported package cache.

    Read rather than listed. A package's own fixtures run alongside this repository's --
    Unity.Addressables.DocExampleCode.Editor.Tests does -- and a list of the exceptions written here
    would be a mirror of the manifest with nothing failing when it drifts.
    """
    library = project / "Library"
    if not library.is_dir():
        raise Unreadable(
            "{}: no Library, so which assemblies came from a package cannot be read. Run this "
            "against the project the tests ran in.".format(project))
    return {assembly_name(path) for path in library.rglob("*.asmdef")}


def _unwind(stack, entering):
    """Drops every scope whose body opened and whose depth has come back."""
    while stack and stack[-1][2] and entering <= stack[-1][1]:
        stack.pop()


def _opened(stack, entering, peak, leaving):
    """Marks the innermost scope as having a body, and drops it when that body also closed here."""
    if stack and not stack[-1][2] and peak > stack[-1][1]:
        stack[-1][2] = True
        if leaving <= stack[-1][1]:
            stack.pop()


def declared_types(text):
    """Every class, struct or record a C# source declares, fully qualified with its enclosing scopes.

    Same stack discipline as base_red_check.py's case reader, which owns why a scope leaves the
    stack on the depth its body opened at. Namespaces are on a stack of their own rather than a
    last-seen name, because two block namespaces nest to the name NUnit reports and a name that
    reads one level short refuses a fixture that is right here.
    """
    code = _base_red_check.code_lines(text)
    profile = _base_red_check.brace_profile(text)

    file_scoped = None
    namespaces = []
    types = []
    found = set()
    for index, line in enumerate(code):
        entering, peak, leaving = profile[index]
        closes_here = peak == entering and line.rstrip().endswith(";")
        _unwind(types, entering)
        _unwind(namespaces, entering)
        match = _base_red_check.CSHARP_NAMESPACE.search(line)
        if match:
            # A file-scoped namespace is held apart from the stack rather than pushed onto it as a
            # scope that never opens: the first brace anywhere below would otherwise be read as its
            # body opening, and the type that brace belongs to would take it back off again.
            if closes_here:
                file_scoped = match.group(1)
            else:
                namespaces.append([match.group(1), entering, False])
        match = _base_red_check.CSHARP_TYPE.search(line)
        if match and not closes_here:
            types.append([match.group(1), entering, False])
            found.add(".".join(([file_scoped] if file_scoped else [])
                               + [name for name, _, _ in namespaces]
                               + [name for name, _, _ in types]))
        _opened(namespaces, entering, peak, leaving)
        _opened(types, entering, peak, leaving)
    return found


def types_here(project):
    """Every class, struct or record any C# source in this worktree declares."""
    sources = worktree_files(project, "*.cs")
    if not sources:
        raise Unreadable("{}: no C# source here, so no fixture could be attributed to this "
                         "worktree".format(project))
    found = set()
    for path in sources:
        try:
            found |= declared_types(path.read_text(encoding="utf-8", errors="replace"))
        except OSError as error:
            raise Unreadable("{}: unreadable source ({})".format(path, error))
    return found


# --------------------------------------------------------------------------------------------------
# What the run wrote
# --------------------------------------------------------------------------------------------------

def named_files(arguments, suffix):
    """Every file of one suffix a caller named, a directory argument contributing its own.

    A directory means one run's output, so it is not descended into: the other harnesses under
    scripts/ write their own runs into subdirectories of the same Logs, and those are readings of a
    base tree rather than of this one.
    """
    found = []
    for argument in arguments:
        path = Path(argument)
        if path.is_dir():
            found.extend(sorted(path.glob("*" + suffix)))
        elif path.is_file() and path.name.endswith(suffix):
            found.append(path)
        elif not path.exists():
            raise Unreadable("{}: no such file or directory".format(path))
    return found


def read_text(path):
    try:
        return Path(path).read_text(encoding="utf-8", errors="replace")
    except OSError as error:
        raise Unreadable("{}: unreadable ({})".format(path, error))


def fixtures_by_assembly(path):
    """{assembly: {fixture}} for one results file, or None when it is not a test run.

    The assembly a fixture is reported under is the one whose suite encloses it.
    """
    try:
        root = ET.parse(str(path)).getroot()
    except (ET.ParseError, OSError) as error:
        raise Unreadable("{}: unreadable test results ({})".format(path, error))
    if root.tag != "test-run":
        return None

    found = {}

    def walk(node, assembly):
        if node.get("type") == "Assembly":
            assembly = Path(node.get("name") or "").stem
        for case in node.findall("test-case"):
            name = CLR_ARITY.sub("", CLR_NESTING.sub(".", case.get("classname") or ""))
            if name:
                # No suite above it says which assembly ran it, so whether that assembly is this
                # worktree's cannot be read -- and read alongside cases that do carry one, the file
                # would pass on the strength of the ones that could be attributed.
                if assembly is None:
                    raise Unreadable("{}: {} is reported under no assembly suite".format(path, name))
                found.setdefault(assembly, set()).add(name)
        for child in node:
            walk(child, assembly)

    walk(root, None)
    return found


# --------------------------------------------------------------------------------------------------
# The readings
# --------------------------------------------------------------------------------------------------

def compile_errors(logs):
    """Every line any log renders as a compiler error, as (log, line).

    Distinct: the reproduction's three diagnostics came back as nine lines, and the repetition
    buries the other two readings under them.
    """
    found = []
    for path in logs:
        for line in read_text(path).splitlines():
            if COMPILE_ERROR.search(line) and (path, line.strip()) not in found:
                found.append((path, line.strip()))
    return found


def unclaimed_results(results, logs):
    """Every results file no log says a run wrote.

    Compared by name, not by path: game-ci runs the editor in a container and the log carries the
    path it had there, which is not where the check reads the file from afterwards. So two runs
    writing a results file of the same name are one reading here, and what separates them is the
    recipe giving each worktree its own Logs directory rather than anything this function can see.
    """
    claimed = {Path(name).name
               for path in logs
               for name in SAVED_RESULTS.findall(read_text(path))}
    return [path for path in results if Path(path).name not in claimed]


def foreign_fixtures(reported, ours, resolved, types):
    """(assemblies of this worktree's that ran, every fixture checked that no source here declares).

    An assembly this worktree names is checked even where a package names one too, since of the two
    answers available for such a name, only checking it can refuse a stranger. One that is NEITHER
    -- another checkout's test assembly, reported out of a seeded Library no asmdef here names -- is
    checked too: that is the shape a seeded Library reports, and skipping it would take the stranger
    it carries out of scope along with it. It does not count toward the floor, which asks whether
    anything of this worktree's ran and cannot take a stranger for an answer.
    """
    found = []
    ran = []
    for assembly, fixtures in sorted(reported.items()):
        if assembly not in ours and assembly in resolved:
            continue
        if assembly in ours:
            ran.append(assembly)
        found.extend((assembly, fixture) for fixture in sorted(fixtures) if fixture not in types)
    return ran, found


def elsewhere(project, named):
    """Every named file that does not sit under the project the check was pointed at.

    A run writes its results and its log under the tree it ran in, so a file outside that tree is a
    reading of a different one. Paired the other way round the check compares this tree's sources
    against another tree's assemblies, and what it then reports is a fixture the running tree
    declares and this one does not -- which is the sentence it prints for the defect it exists to
    catch, about work that is fine.
    """
    root = project.resolve()
    return [path for path in named if root not in path.resolve().parents and path.resolve() != root]


def refusals(project, results, logs):
    """Every reason the results are not this worktree's reading. Empty means none of them said so."""
    # Before any reading of either: the two arguments have to be of one tree. A caller that names an
    # absolute results path and lets --project default is pointing the check at two, and every
    # reading below would then be a comparison across them.
    strangers = elsewhere(project, results + logs)
    if strangers:
        raise Unreadable(
            "{} sit(s) outside {}, so the results and the sources are of different trees. Pass "
            "--project for the tree the run happened in.".format(
                ", ".join(str(path) for path in strangers), project))

    # First, and returning on its own: a run that did not compile wrote no results, so every reading
    # below would refuse it for the absence rather than for the cause, and the caller would be told
    # a file is missing while the diagnostic explaining why sits unread in the log beside it.
    diagnostics = ["{}: the run did not compile this tree -- {}".format(path, line)
                   for path, line in compile_errors(logs)]
    if diagnostics:
        return diagnostics, 0, []

    if not results:
        raise Unreadable("no results file, and no diagnostic in the log to say why")

    ours = declared_assemblies(project)
    resolved = resolved_assemblies(project)
    types = types_here(project)

    # Only the files that parse as a test run, so a coverage report sitting in the same directory is
    # not asked which run wrote it.
    runs = [(path, reported) for path, reported in
            ((path, fixtures_by_assembly(path)) for path in results) if reported is not None]

    found = []
    for path in unclaimed_results([path for path, _ in runs], logs):
        found.append("{}: no log names this file, so it is some earlier run's".format(path))

    ours_ran = []
    for path, reported in runs:
        ran, foreign = foreign_fixtures(reported, ours, resolved, types)
        ours_ran.extend(ran)
        for assembly, fixture in foreign:
            found.append("{}: {} reports {}, which no source in this worktree declares".format(
                path, assembly, fixture))

    if not found:
        if not runs:
            raise Unreadable("no test run among: {}".format(
                ", ".join(str(path) for path in results)))
        if not ours_ran:
            raise Unreadable("no assembly of this worktree's ran, so nothing here was measured")
    return found, len(runs), ours_ran


def main(argv):
    parser = argparse.ArgumentParser(
        description="Refuse a Unity test run whose results are not this worktree's reading.")
    parser.add_argument("results", nargs="+",
                        help="a results XML, or a directory holding one (and its editor log)")
    parser.add_argument("--project", default=".", help="the project the tests ran in")
    parser.add_argument("--log", action="append", default=[],
                        help="the editor log the run wrote; repeatable")
    args = parser.parse_args(argv)

    try:
        results = named_files(args.results, ".xml")
        logs = named_files(args.log, ".log") + named_files(args.results, ".log")
        if not logs:
            raise Unreadable(
                "no editor log among: {}. A run whose compile nobody read is not a "
                "result -- pass -logFile and name it with --log.".format(
                    " ".join(args.results + args.log)))
        found, runs, ours_ran = refusals(Path(args.project), results, logs)
    except Unreadable as error:
        print("cannot take this reading: {}".format(error), file=sys.stderr)
        return 2

    if not found:
        print("checked {} test run(s) against {}: {} assembl(ies) of this worktree, {} log(s) "
              "rendering no compiler error".format(runs, args.project, len(ours_ran), len(logs)))
        return 0

    print("these results are not this worktree's reading:", file=sys.stderr)
    for line in found:
        print("  {}".format(line), file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

#!/usr/bin/env python3
"""Refuse a `-testFilter` naming a class no test file declares, and name the siblings one leaves out.

A filter written from the names of the files a change touched selects fewer fixtures than its author
believes, because a test file may declare more than one. Measured over this project with the reading
below: 323 of its 349 test files declare a fixture class, 401 classes between them; 46 of those files
declare more than one and 10 declare none matching their own stem, which leaves 88 of the 401 classes
invisible to a filename-derived filter. Neither way it goes wrong is loud. A name nothing declares
selects no case at all and the run reports green over whatever is left of the filter; a name that
leaves its siblings out reports green over the smaller set, and the count reads the same as one taken
over the whole file.

Both shapes are in this project's own history. Replayed over the 1709 distinct `-testFilter` commands
its transcripts hold, 20 filters in 30 commands name a file stem that is still a file today and still
declares no class of that name -- `CommitPhaseEdgeCaseTests` among them, whose file declares five
other classes. And one named `RoutingHooksTests` from the file name, where the two other classes that
file declares held the cases the change under test was about.

The two halves get different treatment because only one of them has a legitimate spelling. Naming one
of a file's several classes is how a deliberate single-fixture run is written, so its siblings are
printed and the command goes through; a name nothing declares selects none of the cases it asks for,
so it is refused. Over that same replay the sibling notice fires on 90 of the 1523 commands whose
filter is a readable literal, and on 16 of those the filter already names more than one class of the
same file -- an author who had read its class list.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import importlib.util
import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from shell_commands import (  # noqa: E402
    UNRESOLVED_CD, command_segments, leading_cd, tokens_of, unexpanded)

HOOK_TOOLS = {"Bash"}

# A filter the shell will rewrite is not the text the runner receives, and the check genuinely does not
# happen for it. Refusing instead would refuse the shape a session writes whenever it names its fixtures
# once and reuses the variable, which is 119 of the 1709 filtered commands this project's transcripts
# hold. A wrong refusal costs the command; the hole costs a notice nobody was going to get anyway.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'Unity -runTests -batchmode -testFilter "$FIXTURES"'

# The readings are a directory walk and a parse of the files in it. Neither git nor gh is consulted,
# so an unreadable repository state has no subject here.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "Unity -runTests -batchmode -testFilter Velvet.Tests.NoSuchFixture"}

FILTER_FLAG = "-testFilter"

# The token that says the segment runs the test runner. Without it the operand after a `-testFilter`
# written as an argument to something else is read as a filter -- `grep -n -testFilter CLAUDE.md`
# offers `CLAUDE.md`. Measured over this project's transcripts, requiring it costs 3 of the 1645
# segments that carry a filter at all.
RUNNER_FLAG = "-runTests"

PROJECT_FLAG = "-projectPath"

# Where a Unity project keeps the sources it compiles. Walking the project root instead reaches
# `Library`, which is gigabytes, and the worktrees a session parks under `.claude`.
SOURCE_ROOTS = ("Packages", "Assets")

# Directories under those with no test source in them. `~` is the suffix Unity's own layout uses for a
# subtree it does not compile, which is where the Roslyn solution and its build output sit.
PRUNED = {"Library", "Temp", "obj", "bin", "node_modules", ".git"}


def _harness():
    """base_red_check.py, which owns how a case's fixture name is read off C#.

    Loaded by path because `scripts` holds no package, and lazily because this runs before every Bash
    command and all but a few of them never reach it.
    """
    path = Path(__file__).resolve().parents[3] / "scripts" / "test_quality" / "base_red_check.py"
    spec = importlib.util.spec_from_file_location("velvet_base_red_check", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def filters_in(command):
    """Every name a `-testFilter` in this command asks for, split at the semicolons that separate them."""
    names = []
    for segment in command_segments(command):
        tokens = tokens_of(segment)
        if RUNNER_FLAG not in tokens:
            continue
        for index, token in enumerate(tokens):
            if token == FILTER_FLAG and index + 1 < len(tokens):
                names += [part.strip() for part in tokens[index + 1].split(";") if part.strip()]
    return names


def project_of(command, cwd):
    """The tree the run will read, or None where the command does not say.

    A literal `-projectPath` wins because that is the tree Unity opens whatever the shell's directory
    is. Falling back to the event's directory where a `cd` cannot be read would answer about the
    session's checkout while the command runs in a worktree, and a fixture that is in one and not the
    other reads there as a class nothing declares.
    """
    for segment in command_segments(command):
        tokens = tokens_of(segment)
        for index, token in enumerate(tokens):
            if token == PROJECT_FLAG and index + 1 < len(tokens) and not unexpanded(tokens[index + 1]):
                named = tokens[index + 1]
                if os.path.isabs(named):
                    return named
    moved = leading_cd(command)
    if moved is UNRESOLVED_CD:
        return None
    if moved and os.path.isabs(moved):
        return moved
    return cwd


class Fixtures:
    """The fixture classes a project's test sources declare, read a file at a time on demand.

    The whole corpus parsed up front is seconds of work, and a filter asks about a handful of names.
    So a scan of the raw text picks the files a name could possibly be declared in -- a declaration
    has to spell `class <name>` somewhere -- and only those are masked and parsed. The scan
    over-approximates on purpose: prose and string literals naming a class are picked up too, which
    costs a parse and can only ever turn a refusal into silence.
    """

    def __init__(self, harness, root):
        self.harness = harness
        self.declarations = {}
        self.namespaces = []
        self.texts = {}
        self.parsed = {}
        for source in SOURCE_ROOTS:
            for directory, children, names in os.walk(os.path.join(root, source)):
                children[:] = [name for name in children
                               if name not in PRUNED and not name.endswith("~")]
                for name in names:
                    if not name.endswith(".cs"):
                        continue
                    path = os.path.join(directory, name)
                    relative = os.path.relpath(path, root)
                    if not harness.platform_of(relative):
                        continue
                    try:
                        text = Path(path).read_text(encoding="utf-8", errors="replace")
                    except OSError:
                        continue
                    self.texts[relative] = text
                    for found in harness.CSHARP_TYPE.finditer(text):
                        self.declarations.setdefault(found.group(1), set()).add(relative)
                    for found in harness.CSHARP_NAMESPACE.finditer(text):
                        self.namespaces.append((found.group(1), relative))

    def __bool__(self):
        return bool(self.texts)

    def of(self, relative):
        """(the fixture classes this file declares, the case names they run under).

        An abstract owner is dropped rather than kept: it is not a name a filter can ask for, and
        `base_red_check.py` owns why.
        """
        if relative not in self.parsed:
            cases = [case for case in self.harness.csharp_cases(self.texts[relative], relative)
                     if not case.abstract_owner]
            self.parsed[relative] = ({case.name.rsplit(".", 1)[0] for case in cases},
                                     {case.name for case in cases})
        return self.parsed[relative]

    def candidates(self, name):
        found = {relative for namespace, relative in self.namespaces
                 if namespace == name or namespace.startswith(name + ".")}
        for segment in name.split("."):
            found |= self.declarations.get(segment, set())
        return sorted(found)

    def resolve(self, name):
        """(the file this name selects cases from, whether it names a class or part of one), or None.

        A name a fixture's begins with -- a namespace, or a type nesting one -- names part of a class
        and is asked the sibling question too: a namespace covers every fixture under it and answers
        none, while a nesting type covers only the ones nested in it and can leave the rest of its own
        file out. A name that goes further, down to a case, is a narrowing below the level the sibling
        question is about, and the class it belongs to is one the author has already named.
        """
        for relative in self.candidates(name):
            classes, cases = self.of(relative)
            if name in classes or any(one.startswith(name + ".") for one in classes):
                return relative, True
            if name in cases:
                return relative, False
        return None


def unnamed_siblings(fixtures, relative, asked):
    """The file's fixture classes that no name in the filter selects."""
    classes, _ = fixtures.of(relative)
    return sorted(one for one in classes
                  if not any(one == name or one.startswith(name + ".") for name in asked))


def stem_declares(fixtures, name):
    """(the file this filter is probably named for, the classes it declares), where the tree holds one."""
    stem = name.rsplit(".", 1)[-1]
    for relative in fixtures.texts:
        if Path(relative).stem != stem:
            continue
        classes, _ = fixtures.of(relative)
        if classes:
            return relative, sorted(classes)
    return None


def refusal(fixtures, unknown):
    lines = [
        "Refusing this -testFilter: it names a class no test file declares, so the run reports green "
        "over whatever is left of the filter.",
        "",
    ]
    for name in unknown:
        lines.append(f"  {name}")
        named = stem_declares(fixtures, name)
        if named:
            relative, classes = named
            lines.append(f"    {relative} declares:")
            lines += [f"      {one}" for one in classes]
    lines += [
        "",
        "A file's name is not a class name. A test file here may declare several fixture classes, and "
        "some declare none matching their own stem, so a filter derived from the files a change "
        "touched selects a set nobody chose. Name the classes the files declare.",
    ]
    return "\n".join(lines)


def notice(missed):
    lines = [
        "This -testFilter runs fewer fixtures than the files it names hold. The count it reports will "
        "be green over the smaller set, and nothing in the results says so.",
        "",
    ]
    for name, relative, siblings in missed:
        lines.append(f"  {name} is declared in {relative}, which also declares:")
        lines += [f"    {one}" for one in siblings]
    lines += [
        "",
        "Add them if the change reaches them. A single-fixture run is a legitimate thing to ask for, "
        "so this is a notice rather than a refusal.",
    ]
    return "\n".join(lines)


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        if not isinstance(command, str) or FILTER_FLAG not in command:
            return 0
        asked = filters_in(command)
        if not asked or any(unexpanded(name) for name in asked):
            return 0
        project = project_of(command, event.get("cwd"))
        if not project or not os.path.isdir(project):
            return 0

        fixtures = Fixtures(_harness(), project)
        if not fixtures:
            return 0

        unknown, missed = [], []
        for name in asked:
            found = fixtures.resolve(name)
            if found is None:
                unknown.append(name)
                continue
            relative, reaches_a_class = found
            if not reaches_a_class:
                continue
            siblings = unnamed_siblings(fixtures, relative, asked)
            if siblings:
                missed.append((name, relative, siblings))

        if unknown:
            sys.stderr.write(refusal(fixtures, unknown) + "\n")
            return 2
        if missed:
            # `systemMessage` and nothing beside it. Any `permissionDecision` answers the permission
            # question as well, and this guard has no answer to that one: an under-selecting filter is
            # not grounds for approving a command the permission system would otherwise put to
            # somebody, nor for refusing it.
            json.dump({"systemMessage": notice(missed)}, sys.stdout)
        return 0
    except Exception as failure:  # noqa: BLE001 - a raise here turns the guard off silently
        print(f"filter_naming_no_fixture: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

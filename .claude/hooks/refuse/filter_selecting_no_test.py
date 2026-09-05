#!/usr/bin/env python3
"""Refuse a `-testFilter` value that selects no test, and name the fixtures a selecting one leaves out.

A filter written from the names of the files a change touched runs a set nobody chose, and neither way
it goes wrong is loud. A value that selects nothing leaves the run green over whatever is left of the
filter; a value that selects one of a file's several fixtures leaves it green over the smaller set,
with a count that reads like a count over the whole file. Both shapes are in this project's own
history: replayed over the 1850 filtered Bash commands its transcripts hold, each judged against the
tree its own command names, 60 values in 90 commands select nothing there --
`Velvet.Tests.CommitPhaseEdgeCaseTests`, whose file declares five other classes and none of that
name, and `Velvet.Tests.PreExpansionPolicyTests`, a Python unittest class posed at Unity in the same
semicolon-separated filter as a C# fixture that did run.

What a value IS decides which question this may ask, and `CLAUDE.md` states that beside the flag.
"Does any file declare this class" is not the question: `StarterSample` is declared by nothing and
selects a whole fixture. "Does this value select any test at all" is, and it is what this answers,
against the names the tree's own test sources give the runner.

Three values it declines to answer for, each because answering would mean guessing:

- one starting with `!`, which excludes rather than selects. An exclusion matching nothing leaves the
  run LARGER than asked, which the count shows, and the failure here is a run smaller than asked.
- one carrying anything but `[A-Za-z0-9_.]`, outside a leading `^` and a trailing `$`. Within that
  alphabet the only pattern character is `.`, which this matches as the wildcard it is; a value
  carrying more is a pattern whose author is writing one. It is also where the reading below is
  weakest, a case name assembled from arguments being spelled with brackets and quotes.
- one whose leading segments name a fixture exactly and whose last segment names no case of it. Not
  every case name is in the sources to be read -- an argument list, or a `SetName` the fixture
  composes -- so the tail may be a name this cannot see.

The reading therefore under-approximates the tree Unity builds, and this arm is where that costs
something: of the 4521 case names one EditMode and one PlayMode run report, 69 are missing from it,
and 68 of those sit under a fixture it does hold, so the arm turns each into silence rather than a
wrong refusal. The 69th is the shape that costs the other way and is not closable here: a test a
package brings with it is declared outside `Packages` and `Assets`, so a value selecting only that is
refused. It is one case of one assembly, and no command in the corpus above names it.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import importlib.util
import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from shell_commands import (  # noqa: E402
    UNRESOLVED_CD, comment_opens_at, leading_cd, tokens_of, unexpanded, visible_segments)

HOOK_TOOLS = {"Bash"}

# A filter the shell will rewrite is not the text the runner receives. It is not among the spellings
# below either, so it stands down with them, and the note there is why refusing instead would be
# wrong: the check genuinely does not happen, and a wrong refusal costs the command.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = 'Unity -runTests -batchmode -testFilter "$FIXTURES"'

# The readings are a directory walk and a parse of the files in it. Neither git nor gh is consulted,
# so an unreadable repository state has no subject here.
UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "Unity -runTests -batchmode -testFilter Velvet.Tests.NoSuchFixture"}

FILTER_FLAG = "-testFilter"

# The token that says the segment runs the test runner. Without it the operand after a `-testFilter`
# written as an argument to something else is read as a filter -- `grep -n -testFilter CLAUDE.md`
# offers `CLAUDE.md`. Measured over this project's transcripts, requiring it costs 1 of the 1692
# segments that carry the flag as a token at all, and that one is a grep for the flag's own name.
RUNNER_FLAG = "-runTests"

PROJECT_FLAG = "-projectPath"

# What the shell expands `-projectPath "$PWD"` to is where the command runs, which is read here rather
# than given up on: it is the spelling `CLAUDE.md` documents beside the flag, and standing down on it
# would stand down on the recipe.
PWD_SPELLINGS = {"$PWD", "${PWD}"}

# Where a Unity project keeps the sources it compiles. Walking the project root instead reaches
# `Library`, which is gigabytes, and the worktrees a session parks under `.claude`.
SOURCE_ROOTS = ("Packages", "Assets")

# Directories under those with no test source in them. `~` is the suffix Unity's own layout uses for a
# subtree it does not compile, which is where the Roslyn solution and its build output sit.
PRUNED = {"Library", "Temp", "obj", "bin", "node_modules", ".git"}

# The alphabet this answers for, once a leading `^` and a trailing `$` are off.
PLAIN = re.compile(r"^[A-Za-z0-9_.]+$")

# A case name written beside the case rather than taken from the method. Read as a whole literal only:
# one the fixture composes leaves no name here to add, which is what the third stand-down is for.
NAMED_CASE = re.compile(r'(?:\bTestName\s*=|\.SetName\s*\()\s*"([^"]*)"')


def _harness():
    """base_red_check.py, which owns how a case's name is read off C#.

    Loaded by path because `scripts` holds no package, and lazily because this runs before every Bash
    command and all but a few of them never reach it.
    """
    path = Path(__file__).resolve().parents[3] / "scripts" / "test_quality" / "base_red_check.py"
    spec = importlib.util.spec_from_file_location("velvet_base_red_check", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def filters_in(command):
    """Every value a `-testFilter` in this command asks for, split at the semicolons that separate them.

    Read over the segments the mask left standing, and each cut at its comment: `command_segments`
    hands back a heredoc body as a segment of its own text, and a body carrying this repository's own
    documented recipe was read as a run of it.
    """
    names = []
    for segment in visible_segments(command):
        at = comment_opens_at(segment)
        tokens = tokens_of(segment if at is None else segment[:at])
        if RUNNER_FLAG not in tokens:
            continue
        for index, token in enumerate(tokens):
            if token == FILTER_FLAG and index + 1 < len(tokens):
                names += [part.strip() for part in tokens[index + 1].split(";") if part.strip()]
    return names


def _named_project(command):
    for segment in visible_segments(command):
        tokens = tokens_of(segment)
        for index, token in enumerate(tokens):
            if token == PROJECT_FLAG and index + 1 < len(tokens):
                return tokens[index + 1]
    return None


def project_of(command, cwd):
    """The tree the run will read, or None where the command does not say.

    A `-projectPath` the shell will rewrite returns None rather than falling back to where the command
    runs: the two are different trees whenever a session drives a sibling worktree, and answering from
    the wrong one reads a fixture the other holds as a name nothing declares. Measured over this
    project's transcripts, that fallback is what refused three runs of a fixture that was there.
    """
    moved = leading_cd(command)
    if moved is UNRESOLVED_CD:
        return None
    base = cwd
    if moved:
        base = moved if os.path.isabs(moved) else (os.path.join(cwd, moved) if cwd else None)
    named = _named_project(command)
    if named is None or named in PWD_SPELLINGS:
        return base
    if unexpanded(named):
        return None
    if os.path.isabs(named):
        return named
    return os.path.join(base, named) if base else None


class Tree:
    """The names Unity's test tree carries, read out of the project's test sources on demand.

    Parsing the whole corpus costs seconds and a filter asks about a handful of names, so the files a
    value could possibly match are picked by a raw-text scan first and only those are parsed. The scan
    is sound for the names this builds: each is spelled out of its file's own namespace, type and
    method declarations, so a file whose text does not hold one of the value's literal segments
    declares no name the value matches.
    """

    def __init__(self, harness, root):
        self.harness = harness
        self.root = root
        self.texts = {}
        self.parsed = {}
        self.declarations = {}
        # The suites above the fixtures, which a value may select whole subtrees by: one per namespace,
        # named for it, and one per assembly, named for the file Unity compiled it into.
        self.suites = set()
        for source in SOURCE_ROOTS:
            for directory, children, names in os.walk(os.path.join(root, source)):
                children[:] = [name for name in children
                               if name not in PRUNED and not name.endswith("~")]
                for name in names:
                    path = os.path.join(directory, name)
                    if name.endswith(".asmdef"):
                        self._read_assembly(path)
                        continue
                    if not name.endswith(".cs"):
                        continue
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
                        declared = found.group(1)
                        self.suites |= {declared[:at] for at, character in enumerate(declared)
                                        if character == "."} | {declared}

    def _read_assembly(self, path):
        try:
            named = json.loads(Path(path).read_text(encoding="utf-8")).get("name")
        except Exception:  # noqa: BLE001 - an unreadable asmdef leaves one suite name unknown
            return
        if named:
            self.suites.add(named + ".dll")

    def __bool__(self):
        return bool(self.texts)

    def classes_in(self, relative):
        """{fixture name, dotted as `base_red_check.py` writes it: the case names under it} for one file.

        A type declared abstract is left out and a class deriving from one is kept whether or not it
        declares a case of its own, because that is where its cases come from; `base_red_check.py`
        owns why a case written in an abstract fixture is reported under the heir.
        """
        if relative not in self.parsed:
            text = self.texts[relative]
            classes = {}
            for case in self.harness.csharp_cases(text, relative):
                if case.abstract_owner:
                    continue
                classes.setdefault(case.name.rsplit(".", 1)[0], set()).add(case.name)
            # A heir is qualified by its namespace alone, so one declared inside another type is named
            # here as though it sat beside it. Its cases are already under the nesting spelling, so a
            # simple name the parse has settled is left alone rather than added twice under two names.
            settled = {owner.rsplit(".", 1)[-1] for owner in classes}
            for heirs in self.harness.concrete_heirs({relative: text}).values():
                for heir in heirs:
                    if heir.rsplit(".", 1)[-1] not in settled:
                        classes.setdefault(heir, set())
            for found in NAMED_CASE.finditer(text):
                for owner in list(classes):
                    classes[owner].add(owner + "." + found.group(1))
            self.parsed[relative] = classes
        return self.parsed[relative]

    def candidates(self, value):
        """The files a value could match a name in -- the ones holding its rarest literal segment."""
        segments = [one for one in value.split(".") if one]
        if not segments:
            return list(self.texts)
        holding = [[relative for relative, text in self.texts.items() if segment in text]
                   for segment in segments]
        return min(holding, key=len)

    def declaring(self, segment):
        """The files declaring a type whose whole name this segment matches.

        For the question asked of a fixture name and nothing under it. The last dotted component of
        such a name is the type, so a whole-name comparison against the fixture reduces to one against
        a declaration. The scan above cannot narrow that at all: a head's leading segments are its
        namespace's, and a file in a namespace spells it.
        """
        pattern = re.compile(segment)
        return {relative for name, files in self.declarations.items()
                if pattern.fullmatch(name) for relative in files}


class Value:
    """One `-testFilter` value, and what it selects."""

    def __init__(self, raw, anchored=None):
        self.raw = raw
        self.excludes = raw.startswith("!")
        text = raw[1:] if self.excludes else raw
        written = len(text) > 1 and text.startswith("^") and text.endswith("$")
        self.text = text[1:-1] if written else text
        self.anchored = written if anchored is None else anchored
        self.readable = bool(PLAIN.match(self.text)) and not self.excludes
        self.pattern = re.compile(self.text) if self.readable else None
        self.reach = None

    def hits(self, name):
        probe = self.pattern.fullmatch if self.anchored else self.pattern.search
        return bool(probe(name))

    def covers(self, name):
        """Whether this selects what runs under a fixture -- by its name, or by a suite above it.

        A type nesting fixtures is a suite of its own, named for the chain down to it, and matching it
        selects every fixture below. Only an anchored value needs the prefixes asked for separately:
        an unanchored one matches a prefix of a name by matching the name.
        """
        return self.hits(name) or any(self.hits(name[:at])
                                      for at, character in enumerate(name) if character == ".")

    def selected(self, tree):
        """{file: the fixtures in it this selects a case of}, or None where it selects the whole tree.

        None rather than every file, because a value matching a suite above the fixtures -- a
        namespace, an assembly -- selects each of them whole, and reading that as a set of files would
        mean parsing every one of them to say what nothing is left out of.
        """
        if self.reach is None:
            self.reach = self._reach(tree)
        return self.reach

    def _reach(self, tree):
        if any(self.hits(one) for one in tree.suites):
            return None
        found = {}
        for relative in tree.candidates(self.text):
            for owner, cases in tree.classes_in(relative).items():
                if self.covers(owner) or any(self.hits(one) for one in cases):
                    found.setdefault(relative, set()).add(owner)
        return found

    def selects(self, tree):
        reach = self.selected(tree)
        return reach is None or bool(reach)

    def names_a_fixture(self, tree):
        """Whether everything before this value's last segment names one fixture exactly.

        Then its last segment is a case name, which the sources need not spell.
        """
        if "." not in self.text:
            return False
        head = Value(self.text.rsplit(".", 1)[0], anchored=True)
        if not head.readable:
            return False
        return any(head.hits(owner)
                   for relative in tree.declaring(head.text.rsplit(".", 1)[-1])
                   for owner in tree.classes_in(relative))


def stem_declares(tree, value):
    """(the file this value is probably named for, the classes it declares), where the tree holds one."""
    stem = value.text.rsplit(".", 1)[-1]
    for relative in tree.texts:
        if Path(relative).stem != stem:
            continue
        classes = {owner for owner, cases in tree.classes_in(relative).items() if cases}
        if classes:
            return relative, sorted(classes)
    return None


def refusal(tree, unselecting):
    lines = [
        "Refusing this -testFilter: it asks for a value that selects no test, so the run reports "
        "green over whatever is left of the filter.",
        "",
    ]
    for value in unselecting:
        lines.append(f"  {value.raw}")
        named = stem_declares(tree, value)
        if named:
            relative, classes = named
            lines.append(f"    {relative} declares:")
            lines += [f"      {one}" for one in classes]
    lines += [
        "",
        "A -testFilter value is matched against each test's full name, never against a file's name: "
        "a test file here may declare several fixture classes, and some declare none matching their "
        "own stem, so a filter derived from the files a change touched selects a set nobody chose. "
        "Name a class a file declares.",
    ]
    return "\n".join(lines)


def notice(missed):
    lines = [
        "This -testFilter runs fewer fixtures than the files it selects from hold. The count it "
        "reports will be green over the smaller set, and nothing in the results says so.",
        "",
    ]
    for relative, unselected in missed:
        lines.append(f"  {relative} also declares:")
        lines += [f"    {one}" for one in unselected]
    lines += [
        "",
        "Add them if the change reaches them. A single-fixture run is a legitimate thing to ask for, "
        "so this is a notice rather than a refusal.",
    ]
    return "\n".join(lines)


def _unselected(tree, values):
    """(file, the fixtures in it no value selects), for each file some value selects from."""
    reached = {}
    for value in values:
        found = value.selected(tree)
        if found is None:
            # A value matching a suite above the fixtures selects the files under it whole, leaving
            # nothing out of them. The whole notice stands down rather than reporting over the values
            # beside it, whose gaps this one may be exactly what fills.
            return []
        for relative, owners in found.items():
            reached.setdefault(relative, set()).update(owners)
    missed = []
    for relative in sorted(reached):
        unselected = sorted(owner for owner, cases in tree.classes_in(relative).items()
                            if cases and owner not in reached[relative])
        if unselected:
            missed.append((relative, unselected))
    return missed


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
        asked = [Value(one) for one in filters_in(command)]
        if not asked:
            return 0
        project = project_of(command, event.get("cwd"))
        if not project or not os.path.isdir(project):
            return 0

        tree = Tree(_harness(), project)
        if not tree:
            return 0

        selecting, unselecting = [], []
        for value in asked:
            if not value.readable:
                continue
            if value.selects(tree):
                selecting.append(value)
            elif not value.names_a_fixture(tree):
                unselecting.append(value)

        if unselecting:
            sys.stderr.write(refusal(tree, unselecting) + "\n")
            return 2
        if len(selecting) < len(asked):
            # One value this cannot read leaves the run's whole set unknown, and a notice over the
            # rest would name fixtures that value may well be selecting.
            return 0
        missed = _unselected(tree, selecting)
        if missed:
            # `systemMessage` and nothing beside it. Any `permissionDecision` answers the permission
            # question as well, and this guard has no answer to that one: an under-selecting filter is
            # not grounds for approving a command the permission system would otherwise put to
            # somebody, nor for refusing it.
            json.dump({"systemMessage": notice(missed)}, sys.stdout)
        return 0
    except Exception as failure:  # noqa: BLE001 - a raise here turns the guard off silently
        print(f"filter_selecting_no_test: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

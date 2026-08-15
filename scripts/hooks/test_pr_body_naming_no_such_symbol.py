#!/usr/bin/env python3
"""Unit tests for the guards that read a pull-request body.

Two halves, and the second is why the first is not enough. The grammar half compares the guard's
span patterns and its walk against the C# fixture they are taken from, because a body checked by a
second grammar could be refused for a sentence a guide is allowed. The verdict half poses bodies,
because every pattern can agree and the guard still decline nothing: the flags gh takes a body
under are four, and one of them reaching no check is the accident the sibling guard was written for.

Run: python3 scripts/hooks/test_pr_body_naming_no_such_symbol.py
"""

import importlib.util
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/pr_body_naming_no_such_symbol.py"
PROVENANCE_HOOK = REPO_ROOT / ".claude/hooks/refuse/pr_body_of_another_branch.py"
DRIFT_FIXTURE = (REPO_ROOT / "Packages/com.velvet.core/Runtime/Component/Tests/Editor"
                 / "DocumentationDriftTests.cs")
CORPUS_FIXTURE = (REPO_ROOT / "Packages/com.velvet.core/Runtime/Component/Tests/Editor"
                  / "DocumentationCorpus.cs")


def load_guard():
    """Imports the hook by path, since .claude holds no packages."""
    sys.path.insert(0, str(REPO_ROOT / ".claude/hooks/lib"))
    spec = importlib.util.spec_from_file_location("body_guard", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


guard = load_guard()

# A C# `new(@"…")` initialiser, taking every verbatim literal `+` joins onto the first. The doubled
# quote a verbatim string escapes with is undoubled, so the two sides are compared as the strings
# each engine compiles rather than as the source that spells them.
CSHARP_REGEX = re.compile(
    r"Regex\s+(?P<name>\w+)\s*=\s*(?:new|new Regex)\(\s*(?P<literals>@\"(?:[^\"]|\"\")*\""
    r"(?:\s*\+\s*@\"(?:[^\"]|\"\")*\")*)")
VERBATIM = re.compile(r"@\"((?:[^\"]|\"\")*)\"")

# The names each side calls the same pattern, which is the only thing a rename here has to move.
PATTERN_PAIRS = {
    "FencedBlockPattern": "FENCED_BLOCK",
    "BacktickSpanPattern": "BACKTICK_SPAN",
    "MachinePathPattern": "MACHINE_PATH",
    "PathReferencePattern": "PATH_REFERENCE",
    "DottedSymbolPattern": "DOTTED_SYMBOL",
}

CSHARP_STRING_LIST = re.compile(r"\{(?P<body>[^{}]*)\}")
QUOTED = re.compile(r"\"([^\"]*)\"")


def csharp_patterns(source):
    """Every `Regex X = new(@"…")` in the file, as {name: the string .NET compiles}."""
    found = {}
    for match in CSHARP_REGEX.finditer(source):
        joined = "".join(literal.replace('""', '"')
                         for literal in VERBATIM.findall(match.group("literals")))
        found[match.group("name")] = joined
    return found


def csharp_string_list(source, declaration):
    """The quoted names in the collection initialiser that follows `declaration`.

    Anchored on the declaration rather than on the name, because both names are used above their
    own declaration in that file and the initialiser found from the first use holds no string at
    all — which reads here as a walk that reaches nothing.
    """
    at = source.index(declaration)
    body = CSHARP_STRING_LIST.search(source, at)
    return set(QUOTED.findall(body.group("body")))


class GrammarPinTests(unittest.TestCase):
    """The guard reads a body with the fixture's grammar, so neither may move without the other."""

    def test_Given_TheSpanPatterns_When_ComparedWithTheFixtureTheyAreTakenFrom_Then_EachIsTheSameString(self):
        # Arrange — both sides read from source rather than listed here, so nothing in this file has
        # to be edited when a pattern legitimately changes; what fails is the pair coming apart.
        compiled = csharp_patterns(DRIFT_FIXTURE.read_text(encoding="utf-8"))

        # Act
        apart = [f"{cs}: {compiled.get(cs)!r} vs {getattr(guard, py).pattern!r}"
                 for cs, py in PATTERN_PAIRS.items()
                 if compiled.get(cs) != getattr(guard, py).pattern]

        # Assert — the read count rides along, because a fixture this extractor stopped matching
        # would hand back nothing and disagree with nothing.
        self.assertEqual((len(compiled) >= len(PATTERN_PAIRS), apart),
                         (True, []))

    def test_Given_TheWalkedRoots_When_ComparedWithTheCorpusWalk_Then_TheHookReachesTheSameTree(self):
        # Arrange — the fixture's roots plus .claude, which is the reading its own walk takes when a
        # caller asks for the agent definitions and the skills.
        source = CORPUS_FIXTURE.read_text(encoding="utf-8")
        walked = csharp_string_list(source, "string[] BaseWalkedRoots =") | {".claude"}

        # Act / Assert
        self.assertEqual(set(guard.WALKED_ROOTS), walked)

    def test_Given_TheUnwalkedDirectories_When_ComparedWithTheCorpusWalk_Then_BothSkipTheSameOnes(self):
        # Arrange — worktrees rides with them for the same reason the fixture adds it: this
        # repository's workflow puts full checkouts of itself under .claude while a suite runs.
        source = CORPUS_FIXTURE.read_text(encoding="utf-8")
        unwalked = csharp_string_list(source, "HashSet<string> BaseUnwalkedDirectories =") | {"worktrees"}

        # Act / Assert
        self.assertEqual(guard.UNWALKED, unwalked)


NESTED_AND_QUOTED = '''"""A script whose module surface is one name."""


def spin(count):
    def hidden():
        return count
    return hidden


SCRIPT = """
quoted_name = 1
"""
'''


class VerdictTests(unittest.TestCase):
    """What the guard answers, posed as whole commands against a tree it can walk."""

    ALLOW, REFUSE = "allow", "refuse"

    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp(prefix="velvet-body-guard-"))
        (cls.root / ".git").write_text("gitdir: /nowhere\n")
        tools = cls.root / "scripts" / "tools"
        tools.mkdir(parents=True)
        (tools / "widget.py").write_text(NESTED_AND_QUOTED)
        cls.write("absent.md", "The rewrite goes through `widget.nope`, which lands it.\n")
        cls.write("present.md", "The rewrite goes through `widget.spin`, which lands it.\n")
        cls.write("nested.md", "The stub is `widget.hidden`, returned by the one above it.\n")
        cls.write("quoted.md", "The synthetic script binds `widget.quoted_name`.\n")
        cls.write("stranger.md", "React reaches this through `ReactFiberHooks.dispatch`.\n")
        # A bare file name, because a path carrying directories matches no dotted span either way
        # and the row would pass with the path check taken out.
        cls.write("path.md", "It lives in `widget.py`, beside the rest of them.\n")
        # A fence with no language tag and one line in it, because that is the shape whose third
        # opening backtick pairs with the first closing one to yield exactly a dotted span; a tagged
        # fence yields the tag and the line together, which matches nothing whether or not the
        # fenced block was removed first.
        cls.write("fenced.md", "How it reads:\n\n```\nwidget.nope\n```\n\nDone.\n")
        cls.write("three.md", "Reached as `widget.Cut.nope`, three segments deep.\n")
        cls.write("called.md", "Reached as `widget.nope()`, with its argument list dropped.\n")
        cls.write("silent.md", "A description naming nothing at all.\n")
        cls.write("origin.md", "Closes #7.\n")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.root, ignore_errors=True)

    @classmethod
    def write(cls, name, text):
        (cls.root / name).write_text(text)

    def answer(self, command, cwd=None):
        payload = json.dumps({"tool_name": "Bash", "cwd": str(cwd or self.root),
                              "tool_input": {"command": command.replace("{DIR}", str(self.root))}})
        finished = subprocess.run([sys.executable, "-B", str(HOOK)],
                                  input=payload, text=True, capture_output=True, timeout=120)
        if finished.returncode == 0:
            return self.ALLOW
        if finished.returncode == 2:
            return self.REFUSE if "does not spell" in finished.stderr else "refused for something else"
        return f"exit {finished.returncode}"

    def provenance_answer(self, command):
        payload = json.dumps({"tool_name": "Bash", "cwd": str(self.root),
                              "tool_input": {"command": command.replace("{DIR}", str(self.root))}})
        finished = subprocess.run([sys.executable, "-B", str(PROVENANCE_HOOK)],
                                  input=payload, text=True, capture_output=True, timeout=120)
        if finished.returncode == 0:
            return self.ALLOW
        if finished.returncode == 2 and "names no issue" in finished.stderr:
            return self.REFUSE
        return f"exit {finished.returncode}: {finished.stderr}"

    def disagreements(self, table):
        """(rows answered, the rows that answered otherwise)."""
        answers = [(command, expected, self.answer(command)) for command, expected in table]
        return (len(answers),
                [f"{c}\n    expected [{e}] got [{g}]" for c, e, g in answers if g != e])

    # Each row is a way of handing gh a body, and one of them reaching no check is what let
    # `-F/tmp/pr-body.md` past the sibling guard with the invocation claimed and no body found.
    SPELLINGS = (
        ("gh pr create --title x --body-file {DIR}/absent.md", REFUSE),
        ("gh pr create --title x --body-file={DIR}/absent.md", REFUSE),
        ("gh pr create --title x -F{DIR}/absent.md", REFUSE),
        ("gh pr create --title x -F {DIR}/absent.md", REFUSE),
        ("gh pr create --title x -dF{DIR}/absent.md", REFUSE),
        ("gh pr create --title x -dF={DIR}/absent.md", REFUSE),
        ("gh pr create --title x --body 'goes through `widget.nope`'", REFUSE),
        ("gh pr create --title x -b 'goes through `widget.nope`'", REFUSE),
        ("gh pr create --title x -dfb'goes through `widget.nope`'", REFUSE),
        ("gh pr create --title x -dfb='goes through `widget.nope`'", REFUSE),
        ("gh pr new --title x --body 'goes through `widget.nope`'", REFUSE),
        # A description corrected after the fact is posted by edit, and reaches the squash message
        # the same way a created one does.
        ("gh pr edit 1 --body-file {DIR}/absent.md", REFUSE),
        ("gh pr edit 1 --body 'goes through `widget.nope`'", REFUSE),
        # A body written by the same command has not been written when this runs, and a body behind
        # a variable is not the text that will be posted. Neither is a defect in the description.
        ("gh pr create --title x --body-file {DIR}/never-written.md", ALLOW),
        ("gh pr create --title x --body-file $BODY", ALLOW),
        ("gh pr create --title x --body-file -", ALLOW),
        # Neither opens or updates a pull request.
        ("gh pr create --title x --dry-run --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x -h --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x -dh --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x -dh=false --body-file {DIR}/absent.md", REFUSE),
        ("gh pr create --title x --help=true --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x --help=TRUE --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x --help=1 --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x --help=false --body-file {DIR}/absent.md", REFUSE),
        ("gh pr create --title x --dry-run=true --body-file {DIR}/absent.md", ALLOW),
        ("gh pr create --title x --dry-run=FALSE --body-file {DIR}/absent.md", REFUSE),
        ("gh pr create --fill", ALLOW),
        ("gh pr new --fill", ALLOW),
        ("gh pr list", ALLOW),
        ("echo 'gh pr create --body-file {DIR}/absent.md'", ALLOW),
    )

    # Each row is a span the walk reaches, and what the corpus answers about it.
    SPANS = (
        ("gh pr create --title x --body-file {DIR}/absent.md", REFUSE),
        ("gh pr create --title x --body-file {DIR}/present.md", ALLOW),
        # The two spellings the fixture's own rule would decline and this one does not: a def inside
        # another, and a binding inside a string literal. Both are names the file spells, and
        # declining them is what a corpus narrower than the fixture's would do.
        ("gh pr create --title x --body-file {DIR}/nested.md", ALLOW),
        ("gh pr create --title x --body-file {DIR}/quoted.md", ALLOW),
        # A head that is no script of this repository's is somebody else's API, which this corpus
        # cannot answer for either way.
        ("gh pr create --title x --body-file {DIR}/stranger.md", ALLOW),
        # A file name is the path check's, which resolves it against the filesystem.
        ("gh pr create --title x --body-file {DIR}/path.md", ALLOW),
        # A fenced sample is removed before spans are read, so its backticks pair with each other
        # rather than with the prose around them.
        ("gh pr create --title x --body-file {DIR}/fenced.md", ALLOW),
        # Three segments is not the shape the grammar claims; an argument list dropped from a call is.
        ("gh pr create --title x --body-file {DIR}/three.md", ALLOW),
        ("gh pr create --title x --body-file {DIR}/called.md", REFUSE),
        ("gh pr create --title x --body-file {DIR}/silent.md", ALLOW),
    )

    def test_Given_EveryWayGhTakesABody_When_TheGuardAnswers_Then_TheSameBodyIsJudgedThroughEachOne(self):
        # Arrange / Act
        answered, apart = self.disagreements(self.SPELLINGS)

        # Assert — the answered count rides along, because a driver that ran nothing disagrees with
        # nothing.
        self.assertEqual((answered, apart), (len(self.SPELLINGS), []))

    def test_Given_ASpanOfEachShape_When_TheGuardResolvesIt_Then_OnlyAnUnspelledSymbolIsDeclined(self):
        # Arrange / Act
        answered, apart = self.disagreements(self.SPANS)

        # Assert
        self.assertEqual((answered, apart), (len(self.SPANS), []))

    def test_Given_APathOnTheReadersOwnMachine_When_TheSpanWalkReadsIt_Then_ItIsNoReferenceAtAll(self):
        # Arrange — asked of the walk rather than of a verdict: such a path carries separators, so
        # no dotted span can be spelled as one and no body can be declined for it either way.
        body = "Measured at `/opt/tools/widget.nope` on the runner."

        # Act / Assert
        self.assertEqual(list(guard.spans(body)), [])

    def test_Given_ACommandRunOutsideAnyWorktree_When_TheGuardLooksForScripts_Then_ItSaysSoRatherThanPassing(self):
        # Arrange — a corpus holding no script resolves every span it is given, which reads exactly
        # like a description with nothing wrong in it.
        elsewhere = Path(tempfile.mkdtemp(prefix="velvet-no-worktree-"))

        try:
            # Act
            verdict = self.answer("gh pr create --title x --body 'goes through `widget.spin`'",
                                  cwd=elsewhere)
        finally:
            shutil.rmtree(elsewhere, ignore_errors=True)

        # Assert — a body this one would allow, so what the row turns on is the empty corpus.
        self.assertEqual(verdict, "refused for something else")

    def test_Given_ACommandPostingNoDescription_When_ItRunsOutsideAnyWorktree_Then_NothingIsAskedOfIt(self):
        # Arrange — the corpus is what an empty walk fails over, and a command with no description
        # in it never needed one.
        elsewhere = Path(tempfile.mkdtemp(prefix="velvet-no-worktree-"))

        try:
            # Act
            verdict = self.answer("gh pr create --fill", cwd=elsewhere)
        finally:
            shutil.rmtree(elsewhere, ignore_errors=True)

        # Assert
        self.assertEqual(verdict, self.ALLOW)

    def test_Given_RepeatedInlineBodies_When_TheGuardAnswers_Then_ItJudgesTheLastBody(self):
        # Arrange
        command = ("gh pr edit 1 --body 'goes through `widget.spin`' "
                   "--body 'goes through `widget.nope`'")

        # Act
        verdict = self.answer(command)

        # Assert
        self.assertEqual(verdict, self.REFUSE)

    def test_Given_AnExemptionSpelledAsATitleValue_When_TheGuardAnswers_Then_ItJudgesTheBody(self):
        # Arrange
        command = "gh pr edit 1 --title -h --body 'goes through `widget.nope`'"

        # Act
        verdict = self.answer(command)

        # Assert
        self.assertEqual(verdict, self.REFUSE)

    def test_Given_InlineAndFileBodies_When_TheGuardAnswers_Then_ItJudgesTheFileBody(self):
        # Arrange
        command = ("gh pr edit 1 --body 'goes through `widget.nope`' "
                   "--body-file {DIR}/present.md")

        # Act
        verdict = self.answer(command)

        # Assert
        self.assertEqual(verdict, self.ALLOW)

    def test_Given_AnUnreadableEditBodyFile_When_TheGuardAnswers_Then_ItRefusesTheEdit(self):
        # Arrange — a directory exists at the path but cannot be read as the body text.
        command = "gh pr edit 1 --body-file {DIR}"

        # Act
        verdict = self.answer(command)

        # Assert
        self.assertEqual(verdict, "refused for something else")

    def test_Given_AnExemptionSpelledAsATitleValue_When_TheProvenanceGuardAnswers_Then_ItJudgesTheBody(self):
        # Arrange
        command = "gh pr create --title -h --body-file {DIR}/silent.md"

        # Act
        verdict = self.provenance_answer(command)

        # Assert
        self.assertEqual(verdict, self.REFUSE)

    def test_Given_InlineAndFileBodies_When_TheProvenanceGuardAnswers_Then_ItJudgesOnlyTheFile(self):
        # Arrange
        command = ("gh pr create --body-file {DIR}/origin.md "
                   "--body 'A change to the pooled reset helper.'")

        # Act
        verdict = self.provenance_answer(command)

        # Assert
        self.assertEqual(verdict, self.ALLOW)

    def test_Given_TheNewAlias_When_TheProvenanceGuardAnswers_Then_ItJudgesTheBody(self):
        # Arrange
        command = "gh pr new --body-file {DIR}/silent.md"

        # Act
        verdict = self.provenance_answer(command)

        # Assert
        self.assertEqual(verdict, self.REFUSE)

    def test_Given_AnEditRemovingProvenance_When_TheProvenanceGuardAnswers_Then_ItRefusesTheEdit(self):
        # Arrange
        command = "gh pr edit 1 --body-file {DIR}/silent.md"

        # Act
        verdict = self.provenance_answer(command)

        # Assert
        self.assertEqual(verdict, self.REFUSE)

    def test_Given_LongBooleanExemptions_When_TheProvenanceGuardAnswers_Then_OnlyTrueSkipsTheBody(self):
        # Arrange
        commands = ("gh pr create --help=true --body-file {DIR}/silent.md",
                    "gh pr create --help=false --body-file {DIR}/silent.md",
                    "gh pr create --dry-run=1 --body-file {DIR}/silent.md",
                    "gh pr create --dry-run=0 --body-file {DIR}/silent.md")

        # Act
        verdicts = tuple(self.provenance_answer(command) for command in commands)

        # Assert
        self.assertEqual(verdicts, (self.ALLOW, self.REFUSE, self.ALLOW, self.REFUSE))

    def test_Given_AliasesEditsAndNoBody_When_TheProvenanceGuardAnswers_Then_ValidUpdatesPass(self):
        # Arrange
        commands = ("gh pr new --body-file {DIR}/origin.md",
                    "gh pr edit 1 --body-file {DIR}/origin.md",
                    "gh pr new --fill",
                    "gh pr edit 1")

        # Act
        verdicts = tuple(self.provenance_answer(command) for command in commands)

        # Assert
        self.assertEqual(verdicts, (self.ALLOW,) * len(commands))

    def test_Given_ThisRepositorysOwnScripts_When_TheWalkIsRun_Then_ItReachesEveryDirectoryHoldingOne(self):
        # Arrange — the walk against git's own listing, so a root dropped from it fails here rather
        # than emptying the corpus in silence.
        listed = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files", "*.py"],
                                capture_output=True, text=True, check=True).stdout.split()
        expected = {Path(entry).stem for entry in listed
                    if Path(entry).parts[0] in guard.WALKED_ROOTS}

        # Act
        found = set(guard.script_words(REPO_ROOT))

        # Assert — the whole tracked set rather than a non-empty answer, which a walk that reached
        # one directory would give as readily.
        self.assertEqual(expected - found, set())


class LiveDefectTests(unittest.TestCase):
    """The span this guard exists for, taken from the description it was merged in."""

    def test_Given_TheSpanMainsHistoryCarries_When_ItIsSoughtInTheScriptItNames_Then_NothingSpellsIt(self):
        # Arrange — `neuter_check.run_suites` is in the squash message of the change that added the
        # scan this reproduces, and the function is `run_suite`.
        scripts = guard.script_words(REPO_ROOT)

        # Act
        found = guard.unresolved("The sampling loop is `neuter_check.run_suites`, already carried.",
                                 scripts)

        # Assert
        self.assertEqual(found, ["neuter_check.run_suites"])


if __name__ == "__main__":
    unittest.main(verbosity=2)

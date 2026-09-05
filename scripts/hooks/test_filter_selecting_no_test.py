#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/filter_selecting_no_test.py.

The guard is posed against a project this file writes, not against this repository's own fixtures: a
case reading the real corpus would be measuring which classes happen to sit in which file today, and
the shapes it exists to separate -- a file declaring several, a file whose stem names none of them, an
abstract base no filter can ask for, a heir that declares no case of its own -- would each stop being
covered the moment somebody split a file.

Run as a subprocess and read on all three channels. The exit code alone cannot separate a notice from
silence, both being 0, and reading it alone is how a guard that has stopped saying anything passes.

Run: python3 scripts/hooks/test_filter_selecting_no_test.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/filter_selecting_no_test.py"

TEST_DIRECTORY = "Packages/com.velvet.core/Runtime/Probe/Tests/Editor"

# The other root a Unity project compiles from, which this repository uses for the starter sample's own
# test assembly. Read as one of two rather than as the package alone, or the sample's fixtures are
# guarded by nothing while every check over the package's keeps passing.
SAMPLE_DIRECTORY = "Assets/VelvetProbeSample.Tests/Editor"

ASSEMBLY = "Velvet.Tests.Probe.Editor"

HEADER = ("using System.Collections.Generic;\nusing NUnit.Framework;\n\n"
          "namespace Velvet.Tests\n{\n")
FOOTER = "}\n"


def fixture(name, base=None, sealed=True, case="Given_A_When_B_Then_C", indent="    ", cases=True):
    kind = "sealed" if sealed else "abstract"
    inherits = " : " + base if base else ""
    body = ("internal {} class {}{}\n".format(kind, name, inherits)
            + "{\n"
            + ("    [Test]\n"
               + "    public void {}()\n".format(case)
               + "    {\n"
               + "        Assert.That(true, Is.True);\n"
               + "    }\n" if cases else "")
            + "}\n")
    return "".join(indent + line if line.strip() else line for line in body.splitlines(True))


def nesting(outer, inner):
    """A type that declares a fixture and holds no case of its own, as the styling fixtures do."""
    return ("    internal sealed class {}\n".format(outer) + "    {\n"
            + fixture(inner, indent="        ") + "    }\n")


def composed(owner, name):
    """A fixture whose case name is written beside the case rather than taken from the method."""
    return ("    internal sealed class {}\n".format(owner) + "    {\n"
            + '        [TestCase(1, TestName = "{}")]\n'.format(name)
            + "        public void Run(int value)\n        {\n"
            + "            Assert.That(value, Is.EqualTo(1));\n        }\n    }\n")


def named_through_a_variable(owner, name):
    """A fixture that hands `SetName` a variable, spelled as this repository's keyed suite spells it.

    The name is a literal in the file and is still not one the guard can attribute: what sits at the
    call is an identifier, so a reader looking for a quoted argument there finds none.
    """
    return ("    internal sealed class {}\n".format(owner) + "    {\n"
            + "        private static IEnumerable<TestCaseData> Cases()\n        {\n"
            + "            TestCaseData C(string label, int value) =>\n"
            + "                new TestCaseData(value).SetName(label);\n\n"
            + '            yield return C("{}", 1);\n'.format(name)
            + "        }\n\n"
            + "        [TestCaseSource(nameof(Cases))]\n"
            + "        public void Run(int value)\n        {\n"
            + "            Assert.That(value, Is.EqualTo(1));\n        }\n    }\n")


# One file per shape the guard separates, and a `Solo` that carries none of them so that a silent
# verdict has something of its own to be silent about.
SOURCES = {
    "ManyFixturesTests.cs": fixture("AlphaTests") + fixture("BetaTests") + fixture("GammaTests"),
    "PairTests.cs": fixture("PairTests") + fixture("PairEdgeTests"),
    "SoloTests.cs": fixture("SoloTests"),
    "PoolBaseTests.cs": fixture("PoolBaseTests", sealed=False),
    "WidgetTests.cs": fixture("WidgetPoolTests", base="PoolBaseTests"),
    "InheritedOnlyTests.cs": fixture("InheritedOnlyPoolTests", base="PoolBaseTests", cases=False),
    "NestedTests.cs": nesting("NestedOuterTests", "InnerTests") + fixture("NestedNeighbourTests"),
    "ComposedTests.cs": composed("ComposedTests", "Given_C_When_D_Then_E")
    + fixture("ComposedNeighbourTests"),
    "VariablyNamedTests.cs": named_through_a_variable("VariablyNamedTests", "Given_V_When_W_Then_X"),
}

SAMPLE_SOURCES = {
    "SampleTests.cs": fixture("SampleTests") + fixture("SampleEdgeTests"),
}

# A second project, holding fixtures the first does not. Which tree a command is judged against is
# invisible while both hold the same names, so the pair is what a case about the wrong tree needs.
# Two of them in the one file, so that reading this tree and reading neither are separable: a filter
# naming one of a file's two fixtures leaves a notice behind, and a stand-down leaves nothing.
SIBLING_SOURCES = {
    "SiblingOnlyTests.cs": fixture("SiblingOnlyTests") + fixture("SiblingOnlyEdgeTests"),
}


class FilterSelectingNoTestTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.project = Path(tempfile.mkdtemp(prefix="velvet-test-filter-"))
        for where, sources in ((TEST_DIRECTORY, SOURCES), (SAMPLE_DIRECTORY, SAMPLE_SOURCES)):
            directory = cls.project / where
            directory.mkdir(parents=True)
            for name, body in sources.items():
                (directory / name).write_text(HEADER + body + FOOTER, encoding="utf-8")
        (cls.project / TEST_DIRECTORY / (ASSEMBLY + ".asmdef")).write_text(
            json.dumps({"name": ASSEMBLY}), encoding="utf-8")
        cls.sibling = Path(tempfile.mkdtemp(prefix="velvet-test-filter-sibling-"))
        (cls.sibling / TEST_DIRECTORY).mkdir(parents=True)
        for name, body in SIBLING_SOURCES.items():
            (cls.sibling / TEST_DIRECTORY / name).write_text(HEADER + body + FOOTER, encoding="utf-8")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.project, ignore_errors=True)
        shutil.rmtree(cls.sibling, ignore_errors=True)

    def judge(self, command, cwd=None):
        """(exit code, stdout, stderr) for one Bash event carrying `command`."""
        event = {"tool_name": "Bash", "cwd": str(cwd or self.project),
                 "tool_input": {"command": command}}
        done = subprocess.run([sys.executable, "-B", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=300)
        return done.returncode, done.stdout, done.stderr

    def run_with(self, names, project=None):
        """One run posed against this fixture's project, which the command names as Unity takes it.

        Spelled out rather than left to the event's directory: the guard reads the tree a command
        names and stands down on one that names none, so a payload without it measures that
        stand-down rather than the reading the case posed it for.
        """
        return self.judge('Unity -runTests -batchmode -projectPath "{}" -testFilter "{}"'.format(
            project or self.project, names))

    def notice(self, answer):
        """The message a notice carries, or "" where the guard said nothing on stdout."""
        code, out, _ = answer
        if code != 0 or not out.strip():
            return ""
        return json.loads(out).get("systemMessage", "")

    # ------------------------------------------------------------------------------------------
    # A value that selects nothing
    # ------------------------------------------------------------------------------------------

    def test_Given_AFilterNamedForAFileWhoseStemNamesNoClass_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the shape a filter derived from the files a change touched takes: the file is
        # there, no test in it is named for it, and the run would report green over nothing.
        code, _, said = self.run_with("Velvet.Tests.ManyFixturesTests")

        # Act / Assert
        self.assertEqual((code, "selects no test" in said), (2, True))

    def test_Given_ARefusal_When_ItIsRead_Then_ItNamesTheClassesTheFileDoesDeclare(self):
        # Arrange -- a refusal that names no replacement leaves the reader where the failure left
        # them, with a file name and no way from it to a filter.
        _, _, said = self.run_with("Velvet.Tests.ManyFixturesTests")
        named = [name for name in ("AlphaTests", "BetaTests", "GammaTests") if name in said]

        # Act / Assert
        self.assertEqual(named, ["AlphaTests", "BetaTests", "GammaTests"])

    def test_Given_AFilterNamingAnAbstractBase_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- a case written in an abstract fixture is reported under each class deriving from
        # it and never under the one it is written in, so the base matches no name the runner carries.
        code, _, said = self.run_with("Velvet.Tests.PoolBaseTests")

        # Act / Assert
        self.assertEqual((code, "PoolBaseTests" in said), (2, True))

    def test_Given_AFilterNamingAHeirOfThatBase_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- the control the case above needs: the derived class is a name the runner does
        # report, so refusing the base is not a guard that refuses the whole family.
        answer = self.run_with("Velvet.Tests.WidgetPoolTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingAHeirThatDeclaresNoCaseOfItsOwn_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- every case it runs is inherited, so a reading that took a fixture to be a class
        # some case is written in resolves this to nothing and refuses a run of the whole class.
        answer = self.run_with("Velvet.Tests.InheritedOnlyPoolTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AMultiFilterWhoseSecondValueSelectsNothing_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the semicolon-separated form, where the value in front is what made the
        # unexamined gap easy to miss: the run comes back green over the half that selects. The
        # selecting value rides along, because a reading that took the whole operand for one value
        # would refuse and quote both, which is a refusal over a filter half of which works.
        code, _, said = self.run_with("Velvet.Tests.SoloTests;Velvet.Tests.NoSuchTests")

        # Act / Assert
        self.assertEqual((code, "NoSuchTests" in said, "SoloTests" in said), (2, True, False))

    def test_Given_AnAnchoredValueEqualToNoFullName_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the one spelling Unity compares whole rather than as a pattern. A fixture's full
        # name carries its namespace, so this equals none of them and selects nothing, while a reading
        # that stood down on the `$` as a variable would never look.
        code, _, said = self.run_with("^SoloTests$")

        # Act / Assert
        self.assertEqual((code, "SoloTests" in said), (2, True))

    # ------------------------------------------------------------------------------------------
    # A value that selects something
    # ------------------------------------------------------------------------------------------

    def test_Given_AFilterNamingNoClassButMatchingSeveral_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- nothing declares `Pair`, and it selects both classes of the file that starts with
        # it. This is the shape a reading that asked which class a value names refuses, and the run it
        # refuses is one that works.
        answer = self.run_with("Pair")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AnAnchoredValueEqualToAFullName_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- the anchored spelling of a fixture that is there. The `$` is a regular
        # expression's end, not a shell variable, and a reading that could not tell them apart would
        # stand down on the one spelling that is compared whole.
        answer = self.run_with("^Velvet.Tests.SoloTests$")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AnAnchoredValueEqualToANestingTypesName_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- compared whole, a value is equal to a name or to nothing, and the type nesting
        # this fixture declares no case of its own, so nothing among the names read here is equal to
        # it. The unanchored spelling of the same name goes through, below, on the nested fixture.
        code, _, said = self.run_with("^Velvet.Tests.NestedOuterTests$")

        # Act / Assert
        self.assertEqual((code, "NestedOuterTests" in said), (2, True))

    def test_Given_AFilterNamingTheTestAssembly_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- an assembly is a suite above the fixtures and selects every one of them, so a
        # reading that knew only namespaces and classes refuses a run of a whole assembly.
        answer = self.run_with(ASSEMBLY)

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingACaseRatherThanAClass_When_ItIsPosed_Then_ItIsNotTakenForAnUnknownName(self):
        # Arrange -- `-testFilter` takes a case name as readily as a class one, and the case segment is
        # not a class anywhere, so a reading that knew only classes would refuse this.
        answer = self.run_with("Velvet.Tests.SoloTests.Given_A_When_B_Then_C")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingACaseOfAKnownFixtureThatIsNotDeclared_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- a case name a fixture composes is nowhere in the sources to be read, so a class
        # that is there and a case under it that is not is the one shape where the reading cannot tell
        # a name it cannot see from a name that is not there.
        answer = self.run_with("Velvet.Tests.ComposedTests.Given_Nothing_When_Composed_Then_Unknown")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingACaseAFixtureComposes_When_ItIsPosed_Then_ItsFilesSiblingIsNamed(self):
        # Arrange -- the control for the case above: this name IS written beside its case, so it is
        # read as one, and a reading that took a case name only from a method signature would stand
        # down on it instead. Going through is what both do, and the notice is what separates them.
        answer = self.run_with("Velvet.Tests.ComposedTests.Given_C_When_D_Then_E")

        # Act / Assert
        self.assertEqual((answer[0], "ComposedNeighbourTests" in self.notice(answer)), (0, True))

    # ------------------------------------------------------------------------------------------
    # A value the guard declines to read
    # ------------------------------------------------------------------------------------------

    def test_Given_AnExclusion_When_ItSelectsNothing_Then_TheGuardStandsDown(self):
        # Arrange -- an exclusion matching nothing excludes nothing, which leaves the run larger than
        # asked rather than smaller, and the count shows that.
        answer = self.run_with("!Velvet.Tests.NoSuchTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AValueCarryingRegexPunctuation_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- an escaped separator is a pattern its author wrote deliberately, and it is also
        # where this reading is weakest, a case name assembled from arguments carrying punctuation the
        # sources do not spell.
        answer = self.run_with(r"Velvet\.Tests\.NoSuchTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    # ------------------------------------------------------------------------------------------
    # The sibling a selecting filter leaves out
    # ------------------------------------------------------------------------------------------

    def test_Given_AFilterSelectingOneClassOfAFileDeclaringTwo_When_ItIsPosed_Then_TheOtherIsNamedOnStdout(self):
        # Arrange -- the half that cost coverage. It must not refuse: filtering to one fixture is a
        # legitimate thing to ask for, so the exit code is 0 either way and the message is the whole
        # evidence that the guard said anything.
        answer = self.run_with("Velvet.Tests.PairTests")

        # Act / Assert
        self.assertEqual((answer[0], "PairEdgeTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterSelectingBothClassesOfThatFile_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- the control: a notice that fires once the author has named the whole file is a
        # notice on a correct command.
        answer = self.run_with("Velvet.Tests.PairTests;Velvet.Tests.PairEdgeTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterSelectingAClassWhoseFileDeclaresNoOther_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- the other control, over the file with one class in it, so that a notice firing on
        # every filter would be separated from one firing on an under-selecting filter.
        answer = self.run_with("Velvet.Tests.SoloTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingATypeNestingFixtures_When_ItIsPosed_Then_ItsNeighbourIsNamed(self):
        # Arrange -- a type holding no case of its own but nesting one selects the cases below it, so
        # it has to go through rather than be refused, and the sibling beside it in the file is still
        # left out.
        answer = self.run_with("Velvet.Tests.NestedOuterTests")

        # Act / Assert
        self.assertEqual((answer[0], "NestedNeighbourTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterSelectingAClassOutsideThePackage_When_ItIsPosed_Then_ItsSiblingIsNamedToo(self):
        # Arrange -- a Unity project compiles the sample assemblies under Assets alongside the
        # package, and a reading that stopped at the package would refuse every name declared there
        # while every check over the package's own fixtures went on passing.
        answer = self.run_with("Velvet.Tests.SampleTests")

        # Act / Assert
        self.assertEqual((answer[0], "SampleEdgeTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterNamingTheNamespace_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- a namespace is a suite above every fixture under it, so each of their files is in
        # the run whole and no file has a class left out of it. Spelled whole, because the unanchored
        # spelling is a substring of every fixture's name and would go through on that alone: dropping
        # the namespace suites entirely leaves it green, and leaves this case measuring nothing.
        answer = self.run_with("^Velvet.Tests$")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterOneValueOfWhichTheGuardCannotRead_When_ItIsPosed_Then_NoNoticeIsWritten(self):
        # Arrange -- the unread value may be selecting the very sibling the notice would name, so a
        # notice over the rest of the filter is a claim about a set that is not known.
        answer = self.run_with("Velvet.Tests.PairTests;.*Edge.*")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    # ------------------------------------------------------------------------------------------
    # What the guard declines to answer
    # ------------------------------------------------------------------------------------------

    def test_Given_ACommandThatOnlyMentionsTheFlag_When_ItIsPosed_Then_ItIsNotReadAsARun(self):
        # Arrange -- the operand after the flag is a file here, not a filter, and the token that says
        # a test runner is being invoked is what separates them.
        answer = self.judge("grep -n -testFilter CLAUDE.md")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterInsideAHeredocBody_When_TheFileIsWritten_Then_ItIsNotReadAsARun(self):
        # Arrange -- the body of a written file is not a command, and this project's own documented
        # recipe is one of the strings a session writes. A refused hook discards the whole command, so
        # reading a body as a run loses the file with it.
        answer = self.judge("cat > notes.md <<'EOF'\nRun a subset with:\n"
                            '"$UNITY" -runTests -batchmode -testFilter "Velvet.Tests.NoSuchTests"\n'
                            "EOF")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterInsideAShellComment_When_ItIsPosed_Then_ItIsNotReadAsARun(self):
        # Arrange -- the same shape one line further in: what follows an unquoted `#` never reaches a
        # program, and the recipe is what a note beside a command quotes.
        answer = self.judge("ls  # Unity -runTests -testFilter Velvet.Tests.NoSuchTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AnUnexpandedFilter_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the shell rewrites this before the runner sees it, so the text here selects
        # nothing whatever the tree holds, which for this guard would be a refusal.
        answer = self.judge('Unity -runTests -testFilter "$FIXTURES"')

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AnUnexpandedProjectPath_When_AFilterIsPosedWithIt_Then_TheGuardStandsDown(self):
        # Arrange -- the run reads a tree the command names by variable. Answering from the event's
        # own directory instead reads whichever checkout the session sits in, where a fixture of the
        # other worktree is a name nothing declares.
        answer = self.judge('Unity -runTests -projectPath "$W" -testFilter Velvet.Tests.NoSuchTests')

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AProjectPathSpelledAsThePresentDirectory_When_ACommandMovesFirst_Then_ThatTreeIsRead(self):
        # Arrange -- the recipe this repository documents, run from another directory. `$PWD` is where
        # the command runs, which the move says, so standing down on it would stand down on the
        # spelling every session copies.
        answer = self.judge('cd {} && Unity -runTests -projectPath "$PWD" '
                            "-testFilter Velvet.Tests.NoSuchTests".format(self.project),
                            cwd=REPO_ROOT)

        # Act / Assert
        self.assertEqual((answer[0], "NoSuchTests" in answer[2]), (2, True))

    def test_Given_ACommandMovingIntoADirectoryItDoesNotSpell_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the run happens in a worktree the command names by variable, and the same wrong
        # tree would be read as in the unexpanded-path case above.
        answer = self.judge('cd "$WORKTREE" && Unity -runTests -testFilter Velvet.Tests.PairTests')

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ACommandMovingIntoTheTreePartwayThrough_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the run happens in the sibling project, which holds the fixture; the tool call
        # started in this one, which does not. Reading `$PWD` as where the call started answers about
        # the wrong tree and refuses a fixture that is there, and the move sits behind a segment that
        # runs a program, which is where the reading of a leading move stops.
        answer = self.judge('echo start; cd {} && Unity -runTests -projectPath "$PWD" '
                            "-testFilter Velvet.Tests.SiblingOnlyTests".format(self.sibling),
                            cwd=self.project)

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ThatSameMoveLeadingTheCommand_When_ItIsPosed_Then_TheSiblingTreeIsRead(self):
        # Arrange -- the control the case above needs. With nothing in front of the move, the tree the
        # run reads is settled, and this value names a fixture of the tree the command LEFT, so the
        # refusal is evidence the sibling was what got read. Standing down on any command carrying a
        # move would pass the case above and leave nothing measuring which tree it stood down on.
        answer = self.judge('cd {} && Unity -runTests -projectPath "$PWD" '
                            "-testFilter Velvet.Tests.SoloTests".format(self.sibling),
                            cwd=self.project)

        # Act / Assert
        self.assertEqual((answer[0], "SoloTests" in answer[2]), (2, True))

    def test_Given_ACommandNamingNoProject_When_AFilterIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- nothing in the command says which tree the runner opens, and the directory the
        # tool call started in is not an answer to that.
        answer = self.judge("Unity -runTests -testFilter Velvet.Tests.NoSuchTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_TwoProjectPathsNamingDifferentTrees_When_AFilterIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- two runs in one command, and the value that selects nothing in one of the trees
        # may be the one the other is posed against. Taking the first operand answers for whichever
        # run happened to be written first.
        answer = self.judge(
            'Unity -runTests -projectPath "{}" -testFilter Velvet.Tests.SiblingOnlyTests; '
            'Unity -runTests -projectPath "{}" -testFilter Velvet.Tests.SiblingOnlyTests'
            .format(self.sibling, self.project))

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ACommandWritingTheFixtureItThenRuns_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the hook is posed before the command, so the source in the heredoc is not on disk
        # yet and the name it declares is one no tree holds. A refused hook discards the whole
        # command, so refusing here destroys the very file that would have made the refusal wrong.
        answer = self.judge("cd {} && cat > {}/FreshTests.cs <<'EOF'\n{}EOF\n"
                            'Unity -runTests -projectPath "$PWD" -testFilter Velvet.Tests.FreshTests'
                            .format(self.project, TEST_DIRECTORY,
                                    HEADER + fixture("FreshTests") + FOOTER))

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ACommandWritingAFileTheTreeDoesNotRead_When_TheSameFilterIsPosed_Then_ItIsRefused(self):
        # Arrange -- the control: a command writes, and what it writes could not declare a fixture, so
        # the missing name is still missing after it runs. Standing down on any write at all passes
        # the case above and leaves this one green over a filter that selects nothing.
        answer = self.judge("cd {} && echo note > Logs/note.txt && "
                            'Unity -runTests -projectPath "$PWD" -testFilter Velvet.Tests.FreshTests'
                            .format(self.project))

        # Act / Assert
        self.assertEqual((answer[0], "FreshTests" in answer[2]), (2, True))

    def test_Given_ABareCaseNameComposedThroughAVariable_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the name is a literal in its file and still not one this can attribute, the call
        # carrying an identifier rather than a quoted argument. With nothing in front of it to pin a
        # fixture, the value is compared against every full name a run carries, case names included.
        # The fixture is what makes the value a name rather than a typo; it moves no verdict, the
        # decline being taken on the value's shape without the tree being read at all.
        answer = self.run_with("Given_V_When_W_Then_X")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ThatSameNameComparedWhole_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the control: a case's full name carries the fixture it runs under, so a value
        # compared whole and carrying no separator is out of reach of one. Declining every value with
        # no separator in it would pass the case above and leave nothing measuring the difference.
        code, _, said = self.run_with("^Given_V_When_W_Then_X$")

        # Act / Assert
        self.assertEqual((code, "Given_V_When_W_Then_X" in said), (2, True))

    def test_Given_ASpaceAfterASemicolon_When_TheFilterIsPosed_Then_TheValueBehindItIsRefused(self):
        # Arrange -- the runner splits its operand at the semicolons and trims nothing, so the space
        # belongs to the value behind it. Trimming it here reads two names that both select and lets
        # a run through that reports green over the first half alone.
        code, _, said = self.run_with("Velvet.Tests.PairTests; Velvet.Tests.PairEdgeTests")

        # Act / Assert
        self.assertEqual((code, "PairEdgeTests" in said), (2, True))

    def test_Given_ARefusalOverAValueCarryingASpace_When_ItIsRead_Then_ItSaysWhereTheSpaceCameFrom(self):
        # Arrange -- the file named under such a value declares exactly the name being refused, and a
        # message that stops there reads as the guard refusing a fixture that is plainly there.
        _, _, said = self.run_with("Velvet.Tests.PairTests; Velvet.Tests.PairEdgeTests")

        # Act / Assert
        self.assertEqual('"' in said and "trims nothing" in said, True)

    def test_Given_ADirectoryHoldingNoTestSource_When_AFilterIsPosedThere_Then_TheGuardStandsDown(self):
        # Arrange -- nothing is declared anywhere here, so a guard that went on answering would refuse
        # every filter posed outside a Unity project.
        empty = Path(tempfile.mkdtemp(prefix="velvet-no-project-"))
        self.addCleanup(shutil.rmtree, empty, ignore_errors=True)
        answer = self.judge("Unity -runTests -testFilter Velvet.Tests.PairTests", cwd=empty)

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AToolNothingRoutesHere_When_TheSameCommandIsPosed_Then_TheGuardSaysNothing(self):
        # Arrange -- a gate reading anything but the event's tool name answers under every name, and
        # the payload is one this guard refuses when it is routed.
        event = {"tool_name": "VelvetNoToolIsCalledThis", "cwd": str(self.project),
                 "tool_input": {"command": "Unity -runTests -testFilter Velvet.Tests.NoSuchTests"}}
        done = subprocess.run([sys.executable, "-B", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=300)

        # Act / Assert
        self.assertEqual((done.returncode, done.stdout, done.stderr), (0, "", ""))


if __name__ == "__main__":
    unittest.main(verbosity=2)

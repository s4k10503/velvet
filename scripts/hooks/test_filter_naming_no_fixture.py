#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/filter_naming_no_fixture.py.

The guard is posed against a project this file writes, not against this repository's own fixtures: a
case reading the real corpus would be measuring which classes happen to sit in which file today, and
the shapes it exists to separate -- a file declaring several, a file whose stem names none of them, an
abstract base no filter can ask for -- would each stop being covered the moment somebody split a file.

Run as a subprocess and read on all three channels. The exit code alone cannot separate a notice from
silence, both being 0, and reading it alone is how a guard that has stopped saying anything passes.

Run: python3 scripts/hooks/test_filter_naming_no_fixture.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/filter_naming_no_fixture.py"

TEST_DIRECTORY = "Packages/com.velvet.core/Runtime/Probe/Tests/Editor"

# The other root a Unity project compiles from, which this repository uses for the starter sample's own
# test assembly. Read as one of two rather than as the package alone, or the sample's fixtures are
# guarded by nothing while every check over the package's keeps passing.
SAMPLE_DIRECTORY = "Assets/VelvetProbeSample.Tests/Editor"

HEADER = "using NUnit.Framework;\n\nnamespace Velvet.Tests\n{\n"
FOOTER = "}\n"


def fixture(name, base=None, sealed=True, case="Given_A_When_B_Then_C", indent="    "):
    kind = "sealed" if sealed else "abstract"
    inherits = " : " + base if base else ""
    body = ("internal {} class {}{}\n".format(kind, name, inherits)
            + "{\n"
            + "    [Test]\n"
            + "    public void {}()\n".format(case)
            + "    {\n"
            + "        Assert.That(true, Is.True);\n"
            + "    }\n"
            + "}\n")
    return "".join(indent + line if line.strip() else line for line in body.splitlines(True))


def nesting(outer, inner):
    """A type that declares a fixture and holds no case of its own, as the styling fixtures do."""
    return ("    internal sealed class {}\n".format(outer) + "    {\n"
            + fixture(inner, indent="        ") + "    }\n")


# One file per shape the guard separates, and a `Solo` that carries none of them so that a silent
# verdict has something of its own to be silent about.
SOURCES = {
    "ManyFixturesTests.cs": fixture("AlphaTests") + fixture("BetaTests") + fixture("GammaTests"),
    "PairTests.cs": fixture("PairTests") + fixture("PairEdgeTests"),
    "SoloTests.cs": fixture("SoloTests"),
    "PoolBaseTests.cs": fixture("PoolBaseTests", sealed=False),
    "WidgetTests.cs": fixture("WidgetPoolTests", base="PoolBaseTests"),
    "NestedTests.cs": nesting("NestedOuterTests", "InnerTests") + fixture("NestedNeighbourTests"),
}

SAMPLE_SOURCES = {
    "SampleTests.cs": fixture("SampleTests") + fixture("SampleEdgeTests"),
}


class FilterNamingNoFixtureTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.project = Path(tempfile.mkdtemp(prefix="velvet-test-filter-"))
        for where, sources in ((TEST_DIRECTORY, SOURCES), (SAMPLE_DIRECTORY, SAMPLE_SOURCES)):
            directory = cls.project / where
            directory.mkdir(parents=True)
            for name, body in sources.items():
                (directory / name).write_text(HEADER + body + FOOTER, encoding="utf-8")

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.project, ignore_errors=True)

    def judge(self, command, cwd=None):
        """(exit code, stdout, stderr) for one Bash event carrying `command`."""
        event = {"tool_name": "Bash", "cwd": str(cwd or self.project),
                 "tool_input": {"command": command}}
        done = subprocess.run([sys.executable, "-B", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=120)
        return done.returncode, done.stdout, done.stderr

    def run_with(self, names):
        return self.judge('Unity -runTests -batchmode -testFilter "{}"'.format(names))

    def notice(self, answer):
        """The message a notice carries, or "" where the guard said nothing on stdout."""
        code, out, _ = answer
        if code != 0 or not out.strip():
            return ""
        return json.loads(out).get("systemMessage", "")

    # ------------------------------------------------------------------------------------------
    # A filter naming a class no file declares
    # ------------------------------------------------------------------------------------------

    def test_Given_AFilterNamedForAFileWhoseStemNamesNoClass_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the shape a filter derived from the files a change touched takes: the file is
        # there, the name is not a class in it, and the run would report green over nothing.
        code, _, said = self.run_with("Velvet.Tests.ManyFixturesTests")

        # Act / Assert
        self.assertEqual((code, "no test file declares" in said), (2, True))

    def test_Given_ARefusal_When_ItIsRead_Then_ItNamesTheClassesTheFileDoesDeclare(self):
        # Arrange -- a refusal that names no replacement leaves the reader where the failure left
        # them, with a file name and no way from it to a filter.
        _, _, said = self.run_with("Velvet.Tests.ManyFixturesTests")
        named = [name for name in ("AlphaTests", "BetaTests", "GammaTests") if name in said]

        # Act / Assert
        self.assertEqual(named, ["AlphaTests", "BetaTests", "GammaTests"])

    def test_Given_AFilterNamingAnAbstractBase_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- a case written in an abstract fixture is reported under each class deriving from
        # it and never under the one it is written in, so the base is a name the runner answers to
        # with nothing at all.
        code, _, said = self.run_with("Velvet.Tests.PoolBaseTests")

        # Act / Assert
        self.assertEqual((code, "PoolBaseTests" in said), (2, True))

    def test_Given_AFilterNamingAHeirOfThatBase_When_ItIsPosed_Then_ItGoesThrough(self):
        # Arrange -- the control the case above needs: the derived class is a name the runner does
        # answer to, so refusing the base is not a guard that refuses the whole family.
        answer = self.run_with("Velvet.Tests.WidgetPoolTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AMultiFilterWhoseSecondNameIsUnknown_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange -- the semicolon-separated form, where the known name in front is what made the
        # unexamined gap easy to miss: the run comes back green over the half that resolved. The
        # resolved name rides along, because a reading that took the whole operand for one name would
        # refuse and quote both, which is a refusal naming a class the tree does declare.
        code, _, said = self.run_with("Velvet.Tests.SoloTests;Velvet.Tests.NoSuchTests")

        # Act / Assert
        self.assertEqual((code, "NoSuchTests" in said, "SoloTests" in said), (2, True, False))

    def test_Given_AFilterNamingACaseRatherThanAClass_When_ItIsPosed_Then_ItIsNotTakenForAnUnknownClass(self):
        # Arrange -- `-testFilter` takes a method name as readily as a class one, and the method
        # segment is not a class anywhere, so a reading that knew only classes would refuse this.
        answer = self.run_with("Velvet.Tests.SoloTests.Given_A_When_B_Then_C")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    # ------------------------------------------------------------------------------------------
    # A filter leaving its siblings out
    # ------------------------------------------------------------------------------------------

    def test_Given_AFilterNamingOneClassOfAFileDeclaringTwo_When_ItIsPosed_Then_TheOtherIsNamedOnStdout(self):
        # Arrange -- the half that cost coverage. It must not refuse: filtering to one fixture is a
        # legitimate thing to ask for, so the exit code is 0 either way and the message is the whole
        # evidence that the guard said anything.
        answer = self.run_with("Velvet.Tests.PairTests")

        # Act / Assert
        self.assertEqual((answer[0], "PairEdgeTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterNamingBothClassesOfThatFile_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- the control: a notice that fires once the author has named the whole file is a
        # notice on a correct command.
        answer = self.run_with("Velvet.Tests.PairTests;Velvet.Tests.PairEdgeTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingAClassWhoseFileDeclaresNoOther_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- the other control, over the file with one class in it, so that a notice firing on
        # every filter would be separated from one firing on an under-selecting filter.
        answer = self.run_with("Velvet.Tests.SoloTests")

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_AFilterNamingATypeNestingFixtures_When_ItIsPosed_Then_ItsNeighbourIsNamed(self):
        # Arrange -- a type holding no case of its own but nesting one is not a class the runner
        # reports anything under, and it still selects the cases below it. So it has to resolve
        # rather than be refused, and the sibling beside it in the file is still left out.
        answer = self.run_with("Velvet.Tests.NestedOuterTests")

        # Act / Assert
        self.assertEqual((answer[0], "NestedNeighbourTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterNamingAClassOutsideThePackage_When_ItIsPosed_Then_ItsSiblingIsNamedToo(self):
        # Arrange -- a Unity project compiles the sample assemblies under Assets alongside the
        # package, and a reading that stopped at the package would refuse every name declared there
        # while every check over the package's own fixtures went on passing.
        answer = self.run_with("Velvet.Tests.SampleTests")

        # Act / Assert
        self.assertEqual((answer[0], "SampleEdgeTests" in self.notice(answer)), (0, True))

    def test_Given_AFilterNamingTheNamespace_When_ItIsPosed_Then_NothingIsLeftToSay(self):
        # Arrange -- a namespace selects every class under it, so the file's other classes are in the
        # run already. Read as a class name it is one nothing declares, which would refuse.
        answer = self.run_with("Velvet.Tests")

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

    def test_Given_AnUnexpandedFilter_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the shell rewrites this before the runner sees it, so the text here names no
        # class and every reading of it would fail, which for this guard would be a refusal.
        answer = self.judge('Unity -runTests -testFilter "$FIXTURES"')

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

    def test_Given_ACommandMovingIntoADirectoryItDoesNotSpell_When_ItIsPosed_Then_TheGuardStandsDown(self):
        # Arrange -- the run happens in a worktree the command names by variable. Answering from the
        # event's own directory instead would read the session's checkout, where this project's
        # classes are not declared and every one of them reads as a class nothing declares.
        answer = self.judge('cd "$WORKTREE" && Unity -runTests -testFilter Velvet.Tests.PairTests')

        # Act / Assert
        self.assertEqual((answer[0], self.notice(answer)), (0, ""))

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
                              capture_output=True, text=True, timeout=120)

        # Act / Assert
        self.assertEqual((done.returncode, done.stdout, done.stderr), (0, "", ""))


if __name__ == "__main__":
    unittest.main(verbosity=2)

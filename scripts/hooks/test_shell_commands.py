#!/usr/bin/env python3
"""Unit tests for .claude/hooks/lib/shell_commands.py's command-word reading.

Every refuse hook that asks "is this command a `git <subcommand>`" goes through this table, so a word
it does not know hides the call from all of them at once — and the failure is silent in the direction
that matters: the command runs unrefused, and nothing reports that a reading was skipped.

Run: python3 scripts/hooks/test_shell_commands.py
"""

import importlib.util
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports shell_commands by path, since .claude holds no packages."""
    path = REPO_ROOT / ".claude/hooks/lib/shell_commands.py"
    sys.path.insert(0, str(path.parent))
    spec = importlib.util.spec_from_file_location("shell_commands", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


shell_commands = load_module()


class LeadingWordTests(unittest.TestCase):
    """Words that may sit in front of a command without changing which command it is.

    The keywords that CONTINUE a construct were here and the ones that OPEN it were not, so
    `then git commit --amend` was seen by every guard and `if git commit --amend; then …` by none —
    and the second is the ordinary spelling.
    """

    def subcommands(self, command):
        return [found[1] for found in shell_commands.git_invocations(command, ("commit",))]

    def test_Given_AGitCallOpeningAConditional_When_TheInvocationsAreRead_Then_ItIsSeen(self):
        # Act / Assert
        self.assertEqual(self.subcommands("if git commit --amend; then echo x; fi"), ["commit"])

    def test_Given_AGitCallOpeningAWhileLoop_When_TheInvocationsAreRead_Then_ItIsSeen(self):
        # Act / Assert
        self.assertEqual(self.subcommands("while git commit --amend; do echo x; done"), ["commit"])

    def test_Given_AGitCallOpeningAnUntilLoop_When_TheInvocationsAreRead_Then_ItIsSeen(self):
        # Act / Assert
        self.assertEqual(self.subcommands("until git commit --amend; do sleep 1; done"), ["commit"])

    # GREEN_ON_BASE(characterization): the base sees this one, which is the half a widening
    # could take with it — the same table decides both.
    def test_Given_AGitCallContinuingAConditional_When_TheInvocationsAreRead_Then_ItIsStillSeen(self):
        # Arrange — the half that already worked, and the one a widening must not take with it.
        # Act / Assert
        self.assertEqual(self.subcommands("then git commit --amend"), ["commit"])

    # GREEN_ON_BASE(characterization): the base sees this one, which is the half a widening
    # could take with it — the same table decides both.
    def test_Given_TheKeywordInsideAQuotedString_When_TheInvocationsAreRead_Then_NothingIsSeen(self):
        # Arrange — the control on the other side: a word the shell will not run is not a command,
        # and a reading that answered yes here would refuse an `echo`.
        # Act / Assert
        self.assertEqual(self.subcommands("echo 'if git commit --amend'"), [])


class HeredocOpenerTailTests(unittest.TestCase):
    """What follows a heredoc's delimiter on the opener's own line.

    Blanking that region hid where the opening command's operands end, so a following command's
    tokens landed in `git commit`'s. Skipping it instead left it unlexed, which is worse in the
    other direction: a separator inside a quoted word became a segment boundary, and the inside of
    an argument reached a guard as a command to judge.
    """

    def test_Given_ASeparatorInsideAQuotedWordOnTheOpenersLine_When_TheSegmentsAreRead_Then_ItIsNotABoundary(self):
        # Arrange — a regular alternation, whose bar is a shell word rather than a pipe.
        command = "cat <<'EOF' | grep -E \"^a|git add .|^b\"\nbody\nEOF\n"

        # Act
        found = shell_commands.command_segments(command)

        # Assert — the argument stays whole, so nothing inside it is offered as a command.
        self.assertIn('grep -E "^a|git add .|^b"', found)

    # GREEN_ON_BASE(characterization): the base answers this too, for the reason this change removes -- it
    # blanks the opener's tail wholesale, so the quoted word never exists as shell there. What moved
    # is the route to the answer, not the answer, and a base run cannot tell those apart.
    def test_Given_AGitCallInsideAQuotedWordOnTheOpenersLine_When_TheInvocationsAreRead_Then_NothingIsSeen(self):
        # Arrange / Act — the same text asked the way a guard asks it.
        found = shell_commands.git_invocations(
            "cat <<'EOF' | grep -E \"^a|git add .|^b\"\nbody\nEOF\n", ("add",))

        # Assert
        self.assertEqual(found, [])

    def test_Given_AStagingCommandAfterASeparatorOnTheOpenersLine_When_TheInvocationsAreRead_Then_ItIsSeen(self):
        # Arrange — the direction this change exists for. Blanking the opener's tail hid a real
        # `git add -A` there from every guard, and a reading that only stops refusing things is not
        # what was asked for.
        command = "cat <<'EOF' && git add -A\nbody\nEOF\n"

        # Act
        found = shell_commands.git_invocations(command, ("add",))

        # Assert
        self.assertEqual([(call[1], call[2]) for call in found], [("add", ["-A"])])

    def test_Given_ARedirectAfterTheDelimiter_When_TheLineIsMasked_Then_TheRedirectSurvives(self):
        # Arrange / Act — a segment carries the ORIGINAL text of its span, so reading one says
        # nothing about what the mask kept: the tail is there either way. What moved is the mask,
        # which is where `tracked_writes` locates a redirect operator.
        masked = shell_commands.mask_shell_literals("cat <<'EOF' > notes.md\nbody\nEOF\n")

        # Assert
        self.assertIn("> notes.md", masked)

    def test_Given_TwoHeredocsOpenedOnOneLine_When_TheLineIsMasked_Then_BothBodiesAreConsumed(self):
        # Arrange — the shell takes the bodies in the order the delimiters were written. A reading
        # that reaches only the first delimiter leaves the second body standing as shell, which is
        # what the base does: its second `<<` sits inside the region it blanks.
        command = "cat <<A <<B > notes.md\na1\nA\nb1\nB\n"

        # Act
        masked = shell_commands.mask_shell_literals(command)

        # Assert — past the opener's own line the mask keeps nothing.
        self.assertEqual([line.strip() for line in masked.splitlines() if line.strip()][1:], [])


class CommandDirectoryTests(unittest.TestCase):
    """Where a command's work runs, once its own directory changes are applied to the event's.

    Reading the move was shared before this; placing it was written three times at three call sites,
    and the three disagreed. One joined a relative target to the hook PROCESS's own directory, so
    `cd sub && git commit --amend` refused the amend over a path nothing holds wherever that
    directory was not the one the event named.
    """

    def where(self, command):
        return shell_commands.command_directory(command, "/handed")

    def test_Given_ARelativeMove_When_TheDirectoryIsPlaced_Then_ItIsPlacedAgainstTheEventsOwn(self):
        # Arrange / Act
        found = self.where("cd sub && git commit --amend")

        # Assert
        self.assertEqual(found, "/handed/sub")

    def test_Given_APushd_When_TheDirectoryIsPlaced_Then_ItMovesTheWorkAsACdWould(self):
        # Arrange / Act — read as a program rather than as a move, the work reads as running where
        # the tool call started, which is a tree it has left.
        found = self.where("pushd /moved && git commit --amend")

        # Assert
        self.assertEqual(found, "/moved")

    def test_Given_APopd_When_TheDirectoryIsPlaced_Then_NothingIsPlaced(self):
        # Arrange / Act — where it lands is on a stack only the running shell holds.
        found = self.where("popd && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveBackToThePreviousDirectory_When_ItIsPlaced_Then_NothingIsPlaced(self):
        # Arrange / Act — `-` is not an option and dropping it as one reads this as no move at all,
        # which sends the caller to the directory the command has left.
        found = self.where("cd - && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_TwoCommandsRunningInTwoDirectories_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — one answer is wrong about one of them, and the contract is to decline
        # rather than choose which one to be wrong about.
        found = self.where("cd /a && git commit --amend && cd /b && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AnAssignmentAheadOfTheMove_When_TheDirectoryIsPlaced_Then_TheMoveIsStillRead(self):
        # Arrange — the shape a session types whenever it names a worktree once and moves into it.
        # An assignment is a segment of its own, and stopping at one reads this as no move.
        # Act
        found = self.where("SP=/moved\ncd /moved && git commit --amend")

        # Assert
        self.assertEqual(found, "/moved")

    def test_Given_AMoveNothingRunsAfter_When_TheDirectoryIsPlaced_Then_TheWorkKeepsItsOwn(self):
        # Arrange / Act — a chain closing on `cd -` changes no reading, so declining it would
        # refuse a command whose every program ran in one directory.
        found = self.where("cd /moved && git commit --amend && cd -")

        # Assert
        self.assertEqual(found, "/moved")

    def test_Given_AMoveInsideASubshell_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the group's move is gone at its close, so the two programs run in two
        # directories. Read as reaching past the close, this answered about the group's directory
        # for a command the shell runs in the one it started in.
        found = self.where("( cd /moved && git rev-parse HEAD ) && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AGroupMovingInsideAnOuterMove_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the outer move is what the trailing command runs in, and the inner one is
        # what the group's own program runs in. Without the close, the inner one answered for both.
        found = self.where("cd /moved; (cd /other; git status); git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveOnTheFailureSideOfAnOr_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — `&&` and `||` bind equally and leftwards, so the amend runs in whichever
        # of the two the shell reached. Taking the last move read answers about one of them.
        found = self.where("cd /a || cd /b && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveWhoseFailureIsCaughtByAnOr_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the other side of the same operator: this one runs, and the `cd /c` runs
        # instead of it wherever it or the move before it failed.
        found = self.where("cd /a && cd /b || cd /c; git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveTheShellBackgrounds_When_TheDirectoryIsPlaced_Then_TheWorkKeepsTheHandedOne(self):
        # Arrange / Act — `&` closes a list that runs in a subshell, so the shell the amend runs in
        # never moved. This is placeable rather than declinable: it is the handed directory.
        found = self.where("cd /moved & git commit --amend")

        # Assert
        self.assertEqual(found, "/handed")

    def test_Given_AMoveInsideAPipeline_When_TheDirectoryIsPlaced_Then_TheWorkKeepsTheHandedOne(self):
        # Arrange / Act — each element of a pipeline runs in a subshell of its own, so a move in one
        # reaches nothing after the pipeline.
        found = self.where("cd /moved | true; git commit --amend")

        # Assert
        self.assertEqual(found, "/handed")

    def test_Given_APushdOntoItsOwnStack_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — `+1` selects an entry of the stack the running shell keeps and rotates to
        # it. Read as a path, it joined to the handed directory and named one nothing holds.
        found = self.where("pushd +1 && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_APopdOfANamedStackEntry_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the same selector on the other mover, and the operand a bare `popd` does
        # not have: without it, the reading that declines `popd` is the one that declines every
        # move carrying no operand at all, and the mover itself is measured by nothing.
        found = self.where("popd +1 && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveWrittenInsideAComment_When_TheDirectoryIsPlaced_Then_OnlyTheRealMoveCounts(self):
        # Arrange / Act — the split finds its boundaries in the mask and slices the original, so a
        # `&&` inside a comment separated segments that the shell never sees as commands.
        found = self.where("cd /moved # && cd /other\ngit commit --amend")

        # Assert
        self.assertEqual(found, "/moved")


class LoopHeadReaderTests(unittest.TestCase):
    """A guard whose subject is the keyword itself cannot read it through this table.

    `hand_rolled_pr_poller` decides that a command repeats by finding `while` or `until` where the
    program word would be. Adding those to `LEADING_WORDS` walked past them, so a backgrounded poll
    over the watcher's state stopped reading as a loop and the guard let it through — measured, and
    what caught it was that guard's own suite rather than anything here.
    """

    def test_Given_ALoopKeyword_When_TheProgramWordIsSought_Then_TheTableWalksPastIt(self):
        # Arrange — the property the poller guard has to compensate for, stated where the table is.
        tokens = ["until", "gh", "pr", "checks", "702"]

        # Act / Assert
        self.assertEqual(tokens[shell_commands.leading_program(tokens)], "gh")


if __name__ == "__main__":
    unittest.main(verbosity=2)

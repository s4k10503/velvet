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

    The shape that is placed is the module's own; what the cases below add to it is which side of it
    each construct falls on. `DeclinedShapeTests` holds the ones whose answer a shell can give and
    this does not.
    """

    def where(self, command):
        return shell_commands.command_directory(command, "/handed")

    def test_Given_ARelativeMove_When_TheDirectoryIsPlaced_Then_ItIsPlacedAgainstTheEventsOwn(self):
        # Arrange / Act
        found = self.where("cd sub && git commit --amend")

        # Assert
        self.assertEqual(found, "/handed/sub")

    def test_Given_APushd_When_TheDirectoryIsPlaced_Then_NothingIsPlaced(self):
        # Arrange / Act — `pushd` moves as `cd` does, and `popd`, its partner, carries no
        # destination in the command's own text. The scan takes all three, so declining this one
        # loses a reading rather than missing a move.
        found = self.where("pushd /moved && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

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

    def test_Given_AMoveAfterTheWork_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the scan runs over everything past the placed steps, not over the steps it
        # placed. Ending it at the first program would place this and place `git status; cd /wt &&
        # git status` the same way, and the second runs its two commands in two directories.
        found = self.where("cd /moved && git commit --amend && cd -")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

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

    def test_Given_TheWorkCarriesARedirection_When_TheDirectoryIsPlaced_Then_TheMoveStillCounts(self):
        # Arrange / Act — the `&` of a `2>&1` is not the one that backgrounds a list. Read as one it
        # undoes every move made since the list began, so this answered about the tree the command
        # left while `2>/dev/null`, one character shorter, answered about the tree it acts in.
        found = self.where("cd /moved && git commit --amend 2>&1 | tail -5")

        # Assert
        self.assertEqual(found, "/moved")

    def test_Given_AnArmOfACaseStatement_When_ThePlacementIsAsked_Then_TheWorkKeepsTheHandedOne(self):
        # Arrange / Act — the arm's `)` closes nothing. Counted as a close it drove a nesting depth
        # below zero and raised, and a guard whose reading raises exits 1, which turns it off:
        # behind a `case` arm the published-commit amend, the blind `git add -A` and the
        # shared-branch checkout all went through.
        found = self.where("case $x in a) echo hi ;; esac; git commit --amend")

        # Assert
        self.assertEqual(found, "/handed")

    def test_Given_ALoopBodyThatMoves_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — a `for` at the top level leaves the shell where its body's last move put
        # it. The construct is not in the shape this places, and what a construct outside that shape
        # earns is a decline rather than a reading of it.
        found = self.where("for f in a b; do cd /moved; done; git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)


class DeclinedShapeTests(unittest.TestCase):
    """Moves a shell places and this does not.

    Every one of these has an answer, and giving it would mean reading a construct rather than
    matching one. That trade is the module's: a reading that answered every construct answered
    wrongly wherever it met one nobody had written it for, and a decline costs a refusal the
    command's author can lift by spelling the move out.
    """

    def where(self, command):
        return shell_commands.command_directory(command, "/handed")

    def test_Given_AMoveTheShellBackgrounds_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — `&` closes a list that runs in a subshell, so the shell the amend runs in
        # is the handed one.
        found = self.where("cd /moved & git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveCarryingARedirection_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — silencing a move's own output is how a session writes one it does not want
        # to see, and the shell runs the amend in `/moved`. Placing it would mean reading the `>`
        # apart from the `&` beside it, which is the reading that made the case above wrong.
        found = self.where("cd /moved >/dev/null 2>&1 && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AnOperatorGluedToTheTarget_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — one token to the tokeniser and two commands to the shell, which runs the
        # move in the background and the amend where it started. Read off the tokens alone this
        # names a directory the shell never enters; what separates the two is the text of the step.
        found = self.where("cd /moved&pwd && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_APipeGluedToTheTarget_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the same shape on the operator that puts both sides in subshells of their
        # own, so the amend runs where the tool call started rather than in the target.
        found = self.where("cd /moved|pwd; git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveCarryingASecondOperand_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — taking the first operand and dropping the rest is the edit this exists to
        # stop: measured, one of the two shells this project's commands run under enters that first
        # operand and the other stays where it is, so there is no one answer to place.
        found = self.where("cd /moved /other && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_ARedirectionGluedToTheTarget_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the shell sets the redirection up and then enters `/moved`; the tokeniser
        # hands back one operand carrying the `>`, and placing it names a path with an operator in
        # it. The step's text is what tells the two apart.
        found = self.where("cd /moved>log && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_ABraceListInTheTarget_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — one word before expansion and two after it, so the operand the `cd`
        # receives is not the one written. Placed as written it names a path holding the braces.
        found = self.where("cd /moved{a,b} && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveInsideASubstitutionAnAssignmentTakes_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the substitution runs in a subshell, so the assignment's own move reaches
        # nothing after it. Read as a step of the prefix, its closing paren rides along on the
        # target and the work is placed in a directory named for one.
        found = self.where("X=$(pwd; cd /other) && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_ATargetTheShellHasYetToExpand_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — a hook is handed the command before the shell expands it, so this literal
        # is not the path the `cd` receives, so placing it as written names a path the command
        # never uses.
        found = self.where("cd \"$WORKTREE\" && git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)

    def test_Given_AMoveWhoseFailureEndsTheShell_When_ThePlacementIsAsked_Then_NothingIsPlaced(self):
        # Arrange / Act — the amend runs in `/moved` or does not run, so one directory answers for
        # it. Reaching that answer means matching a grammar of its own for what the failure branch
        # does, and declining is what this trades for not having one.
        found = self.where("cd /moved || exit 1; git commit --amend")

        # Assert
        self.assertIs(found, shell_commands.UNRESOLVED_CD)


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

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

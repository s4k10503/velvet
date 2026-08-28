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

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/stack_merge_by_hand.py: which spellings reach a stack merge.

`test_hook_verdicts.py` poses the guard its plainest refusal and one near miss. What it leaves out
is the other spellings the guard recognises, and the one that reaches the merge and merges nothing,
since a guard that does not recognise a spelling reports what a guard with nothing to say reports.

A refusal is read together with the guard's own sentence, so a run that exits 2 for any other reason
answers None rather than the refusing code.

Run: python3 scripts/hooks/test_stack_merge_by_hand.py
"""

import json
import subprocess
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/stack_merge_by_hand.py"

REFUSED = 2
ALLOWED = 0

SAID = "Refusing `gh stack merge`"


def verdict(command):
    """The exit code where the guard refused saying why or allowed saying nothing, and None for any
    other answer."""
    event = {"tool_name": "Bash", "cwd": str(REPO_ROOT), "tool_input": {"command": command}}
    done = subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                          capture_output=True, text=True, timeout=60)
    if done.returncode == REFUSED and SAID in done.stderr:
        return REFUSED
    if done.returncode == ALLOWED and not done.stderr:
        return ALLOWED
    return None


class StackMergeSpellingTests(unittest.TestCase):

    def test_Given_TheExtensionsBinaryRunDirectly_When_ItMerges_Then_ItIsRefused(self):
        # Act
        answer = verdict("~/.local/share/gh/extensions/gh-stack/gh-stack merge 1558 --yes --squash")

        # Assert
        self.assertEqual(answer, REFUSED)

    def test_Given_TheExtensionRunThroughExtensionExec_When_ItMerges_Then_ItIsRefused(self):
        # Act
        answer = verdict("gh extension exec stack merge 1558 --yes --squash")

        # Assert
        self.assertEqual(answer, REFUSED)

    def test_Given_TheExtensionRunThroughExtensionsExec_When_ItMerges_Then_ItIsRefused(self):
        # Act
        answer = verdict("gh extensions exec stack merge 1558 --yes")

        # Assert
        self.assertEqual(answer, REFUSED)

    def test_Given_TheExtensionRunThroughExtExec_When_ItMerges_Then_ItIsRefused(self):
        # Act
        answer = verdict("gh ext exec stack merge 1558 --yes")

        # Assert
        self.assertEqual(answer, REFUSED)

    def test_Given_AStackMergeNamingNothing_When_Posed_Then_ItIsRefused(self):
        # Arrange — with no number the extension merges the stack of the checked-out branch.
        command = "gh stack merge --yes --squash"

        # Act / Assert
        self.assertEqual(verdict(command), REFUSED)

    def test_Given_AStackMergeAfterAMove_When_Posed_Then_ItIsRefused(self):
        # Act
        answer = verdict("cd /tmp && gh stack merge 1558 --yes")

        # Assert
        self.assertEqual(answer, REFUSED)

    def test_Given_AStackMergeAskedOnlyForItsHelp_When_Posed_Then_ItIsAllowed(self):
        # Act
        answer = verdict("gh stack merge --help")

        # Assert
        self.assertEqual(answer, ALLOWED)

    def test_Given_AMergeOfOnePullRequest_When_Posed_Then_ItIsLeftToTheOtherMergeGuards(self):
        # Act
        answer = verdict("gh pr merge 1558 --squash --delete-branch")

        # Assert
        self.assertEqual(answer, ALLOWED)


if __name__ == "__main__":
    unittest.main()

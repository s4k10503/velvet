#!/usr/bin/env python3
"""Unit tests for the create-spelling claims in the metadata guard.

`new` is gh's own alias for `create`, on both `pr` and `issue`. A guard that claims one spelling is
skippable by typing the other — measured, `gh pr create --title x` was refused and `gh pr new
--title x` was not.

Run: python3 scripts/hooks/test_create_aliases.py
"""

import json
import subprocess
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
METADATA = REPO_ROOT / ".claude/hooks/refuse/metadata_less_create.py"


def judge(guard, command, cwd=None):
    """The guard's exit code for `command`, in a checkout the caller names."""
    event = {"tool_name": "Bash", "cwd": str(cwd or REPO_ROOT),
             "tool_input": {"command": command}}
    return subprocess.run([sys.executable, str(guard)], input=json.dumps(event),
                          capture_output=True, text=True, timeout=90).returncode


class MetadataSpellingTests(unittest.TestCase):
    # GREEN_ON_BASE(refactor): the receipt guard's cases and helpers left this file; the metadata guard is unchanged.
    def test_Given_APullRequestOpenedAsNew_When_ItCarriesNoMetadata_Then_ItIsRefused(self):
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr new --title x"), 2)

    # GREEN_ON_BASE(refactor): the receipt guard's cases and helpers left this file; the metadata guard is unchanged.
    def test_Given_AnIssueOpenedAsNew_When_ItCarriesNoMetadata_Then_ItIsRefused(self):
        # Arrange — `gh issue new` prints a usage too, so the alias is on both subcommands.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh issue new --title x"), 2)

    # GREEN_ON_BASE(refactor): the receipt guard's cases and helpers left this file; the metadata guard is unchanged.
    def test_Given_APullRequestOpenedAsCreate_When_ItCarriesNoMetadata_Then_ItIsStillRefused(self):
        # Arrange — the half that already worked, and what the widening must not lose.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr create --title x"), 2)

    # GREEN_ON_BASE(refactor): the receipt guard's cases and helpers left this file; the metadata guard is unchanged.
    def test_Given_ANewCarryingItsMetadata_When_Judged_Then_ItGoesThrough(self):
        # Arrange — the control: a widening that refused every `new` would satisfy the cases above.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr new --title x --label tooling --assignee @me"), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for the create-spelling claims in the metadata and receipt guards.

`new` is gh's own alias for `create`, on both `pr` and `issue`. A guard that claims one spelling is
skippable by typing the other — measured, `gh pr create --title x` was refused and `gh pr new
--title x` was not. For the receipt guard that matters most: the gate is asked once, at open time,
and nothing downstream re-asks — not CI, and not `settle.py`, which merges over the REST API.

Run: python3 scripts/hooks/test_create_aliases.py
"""

import contextlib
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
METADATA = REPO_ROOT / ".claude/hooks/refuse/metadata_less_create.py"
RECEIPT = REPO_ROOT / ".claude/hooks/refuse/pr_without_mutation_receipt.py"

# What mutation_check.py exits when a receipt is owed and absent. The guard reads this number and
# nothing else about the harness, so a stub exiting it is a checkout that owes one.
RECEIPT_REFUSAL = 3


def judge(guard, command, cwd=None):
    """The guard's exit code for `command`, in a checkout the caller names."""
    event = {"tool_name": "Bash", "cwd": str(cwd or REPO_ROOT),
             "tool_input": {"command": command}}
    return subprocess.run([sys.executable, str(guard)], input=json.dumps(event),
                          capture_output=True, text=True, timeout=90).returncode


@contextlib.contextmanager
def checkout_owing(owed):
    """A checkout whose campaign harness answers as the caller says, and nothing else.

    The receipt guard's verdict is the harness's: it runs mutation_check.py over the checkout and
    reads the exit code. Asked of this repository, that is whatever the working tree happens to
    hold -- a diff on a branch, nothing on a clean main -- so a case pinned against it says
    something different on every checkout. Measured: the two refusal cases here passed on every
    pull request and failed on every push to main for a day, because a merged main owes no receipt.

    A stub decides instead, so the spelling is what the case is about.
    """
    root = Path(tempfile.mkdtemp(prefix="create-alias-"))
    try:
        subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
        script = root / "scripts" / "test_quality" / "mutation_check.py"
        script.parent.mkdir(parents=True)
        script.write_text("import sys\nsys.exit({})\n".format(RECEIPT_REFUSAL if owed else 0))
        yield root
    finally:
        shutil.rmtree(root, ignore_errors=True)


class MetadataSpellingTests(unittest.TestCase):
    def test_Given_APullRequestOpenedAsNew_When_ItCarriesNoMetadata_Then_ItIsRefused(self):
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr new --title x"), 2)

    def test_Given_AnIssueOpenedAsNew_When_ItCarriesNoMetadata_Then_ItIsRefused(self):
        # Arrange — `gh issue new` prints a usage too, so the alias is on both subcommands.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh issue new --title x"), 2)

    # GREEN_ON_BASE(characterization): the base refuses this too, and it is the half the
    # widening could take with it — only running it says whether it did.
    def test_Given_APullRequestOpenedAsCreate_When_ItCarriesNoMetadata_Then_ItIsStillRefused(self):
        # Arrange — the half that already worked, and what the widening must not lose.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr create --title x"), 2)

    # GREEN_ON_BASE(characterization): the base lets this through, and a widening that
    # refused every `new` would satisfy the red cases above while breaking this.
    def test_Given_ANewCarryingItsMetadata_When_Judged_Then_ItGoesThrough(self):
        # Arrange — the control: a widening that refused every `new` would satisfy the cases above.
        # Act / Assert
        self.assertEqual(judge(METADATA, "gh pr new --title x --label tooling --assignee @me"), 0)


class ReceiptSpellingTests(unittest.TestCase):
    """The gate this one holds is posed once and nowhere else."""

    def test_Given_APullRequestOpenedAsNew_When_NoCampaignMeasuredIt_Then_ItIsRefused(self):
        # Arrange — a checkout that owes one, so the case is about the spelling.
        with checkout_owing(True) as root:
            # Act / Assert
            self.assertEqual(judge(RECEIPT, "gh pr new --title x --label tooling", root), 2)

    # GREEN_ON_BASE(characterization): the base refuses this too, and it is the half the
    # widening could take with it — only running it says whether it did.
    def test_Given_APullRequestOpenedAsCreate_When_NoCampaignMeasuredIt_Then_ItIsStillRefused(self):
        # Arrange — the half that already worked.
        with checkout_owing(True) as root:
            # Act / Assert
            self.assertEqual(judge(RECEIPT, "gh pr create --title x --label tooling", root), 2)

    def test_Given_ACheckoutThatOwesNothing_When_APullRequestIsOpened_Then_ItGoesThrough(self):
        # Arrange — the control the two above need: a guard that refused whatever the harness said
        # would satisfy them.
        with checkout_owing(False) as root:
            # Act / Assert
            self.assertEqual(judge(RECEIPT, "gh pr new --title x --label tooling", root), 0)

    # GREEN_ON_BASE(characterization): the base lets this through, and a widening that
    # refused every `new` would satisfy the red cases above while breaking this.
    def test_Given_ACommandThatOpensNothing_When_Judged_Then_ItIsNotThisGuardsToRefuse(self):
        # Arrange — the control on the other side.
        # Act / Assert
        self.assertEqual(judge(RECEIPT, "gh pr list"), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

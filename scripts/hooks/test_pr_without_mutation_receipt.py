#!/usr/bin/env python3
"""Unit tests for how .claude/hooks/refuse/pr_without_mutation_receipt.py reads its two answers.

The verdict is decided from git's name for the checkout and from what the checkout's campaign
harness answers. A strict decode of either raised where a byte was not UTF-8, and the hook exited 1,
which opens the pull request unasked. Two cases hold the harness's answer, one on each side of the
verdict; one holds a name git gives with no directory behind it, which is refused as unread; two
hold a checkout whose name ends in a space or in a carriage return, each of which a reading of the
root has spent — and with it the harness, so nothing was owed.

Run: python3 scripts/hooks/test_pr_without_mutation_receipt.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/pr_without_mutation_receipt.py"

OPEN = "gh pr create --fill"
REFUSED = 2
ALLOWED = 0

# What mutation_check.py exits when a receipt is owed and absent. The guard's verdict is decided by
# this number, so a stub exiting it is a checkout that owes one.
RECEIPT_REFUSAL = 3
OWED = "no mutation campaign covers this branch's change."
UNREAD = "this guard could not read the state it decides from."

# A harness naming a file whose name holds a byte UTF-8 does not map, then exiting as told.
STRAY_BYTE_HARNESS = "import sys\nsys.stdout.buffer.write(b'lat\\xedn1.cs\\n')\nsys.exit({})\n"

# A checkout name holding the same kind of byte, recorded as `core.worktree`: git names it as the
# toplevel with no directory behind it, and APFS refuses to make one, with `Illegal byte sequence`.
UNDECODABLE_TREE = b"tree\xff"


class ReadingTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-receipt-reading-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def checkout(self, name, harness):
        """A repository named `name` whose campaign harness is `harness`, and nothing else."""
        made = self.root / name
        subprocess.run(["git", "init", "-q", str(made)], check=True, capture_output=True,
                       timeout=60)
        script = made / "scripts" / "test_quality" / "mutation_check.py"
        script.parent.mkdir(parents=True)
        script.write_text(harness, encoding="utf-8")
        return made

    @staticmethod
    def judge(cwd):
        event = {"tool_name": "Bash", "cwd": str(cwd), "tool_input": {"command": OPEN}}
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=90)

    def test_Given_AHarnessWritingAByteThatIsNotUTF8_When_ItFindsAReceiptOwed_Then_TheOpeningIsRefused(self):
        # Arrange
        made = self.checkout("checkout", STRAY_BYTE_HARNESS.format(RECEIPT_REFUSAL))

        # Act
        result = self.judge(made)

        # Assert
        self.assertEqual((result.returncode, OWED in result.stderr), (REFUSED, True))

    def test_Given_AHarnessWritingAByteThatIsNotUTF8_When_ItFindsNothingOwed_Then_TheOpeningGoesThrough(self):
        # Arrange — the other side of the same answer, so a reading that survives the byte by
        # refusing whatever the harness says is not what makes the case above pass. The line the
        # guard says it read the receipt with is compared too, since a guard that never ran the
        # harness exits 0 as well.
        made = self.checkout("checkout", STRAY_BYTE_HARNESS.format(0))

        # Act
        result = self.judge(made)

        # Assert
        self.assertEqual((result.returncode, "Mutation receipt: " in result.stderr),
                         (ALLOWED, True))

    def test_Given_ACheckoutGitNamesWithAByteThatIsNotUTF8_When_APullRequestIsOpened_Then_TheOpeningIsRefusedAsUnread(self):
        # Arrange — the harness stays under the directory that does exist, so a guard that found
        # it there would be reading some checkout other than the one git named. The gate rides in
        # the comparison because a name that decoded would leave nothing to fail on, and this case
        # green having posed nothing.
        made = self.checkout("checkout", STRAY_BYTE_HARNESS.format(RECEIPT_REFUSAL))
        subprocess.run([b"git", b"-C", bytes(made), b"config", b"core.worktree",
                        bytes(made) + b"/" + UNDECODABLE_TREE], check=True, capture_output=True)
        named = subprocess.run(["git", "-C", str(made), "rev-parse", "--show-toplevel"],
                               capture_output=True, check=True, timeout=60).stdout
        arranged = UNDECODABLE_TREE in named

        # Act
        result = self.judge(made)

        # Assert
        self.assertEqual((arranged, result.returncode, UNREAD in result.stderr),
                         (True, REFUSED, True))

    def test_Given_ACheckoutWhoseNameEndsInASpace_When_ItOwesAReceipt_Then_TheOpeningIsRefused(self):
        # Arrange
        made = self.checkout("checkout ", f"import sys\nsys.exit({RECEIPT_REFUSAL})\n")

        # Act
        result = self.judge(made)

        # Assert
        self.assertEqual((result.returncode, OWED in result.stderr), (REFUSED, True))

    def test_Given_ACheckoutWhoseNameEndsInACarriageReturn_When_ItOwesAReceipt_Then_TheOpeningIsRefused(self):
        # Arrange
        made = self.checkout("checkout\r", f"import sys\nsys.exit({RECEIPT_REFUSAL})\n")

        # Act
        result = self.judge(made)

        # Assert
        self.assertEqual((result.returncode, OWED in result.stderr), (REFUSED, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

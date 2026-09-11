#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/blind_git_add.py.

The refusal is a deny decision on stdout, and the untracked names under it are the remedy it offers.
One case holds a listing carrying a byte UTF-8 does not map: a strict decode raised on it, the hook
exited 1 with no decision printed, and the staging went ahead. Two hold a name a line-based reading
listed as something no file is called: one git leaves raw, which `splitlines()` divided, and one git
escapes, which nothing read back.

Run: python3 scripts/hooks/test_blind_git_add.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/blind_git_add.py"

SWEEP = "git add -A"
HEADER = "Untracked right now"

# A tracked name holding a byte UTF-8 does not map, written into the index rather than onto disk,
# where APFS refuses such a name with `Illegal byte sequence`.
UNDECODABLE = b"lat\xedn1.txt"
ORDINARY = "ordinary.txt"

# Three of the boundaries `splitlines()` documents that git leaves raw once `core.quotePath` is off,
# all in one name so that a reading handling two of them still fails, and how the refusal spells it.
RAW_SEPARATORS = "mid\u0085a\u2028b\u2029c.txt"
RAW_SEPARATORS_AS_SHOWN = '"mid\\u0085a\\u2028b\\u2029c.txt"'

# A name git's porcelain escapes with `core.quotePath` on, for its byte over 0x80.
ESCAPED = "lat\u00edn.txt"


def git(cwd, *args, stdin=None):
    """A failed arrangement is not a verdict, so one must not reach the assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *args], input=stdin, check=True,
                          capture_output=True, timeout=60)


class StagingRefusalTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-blind-add-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        git(self.root, "init", "-q", "-b", "main")

    def place(self, name):
        (self.root / name).write_text("untracked\n", encoding="utf-8")

    def index(self, name):
        """A tracked path written straight into the index, for a name this filesystem will not hold."""
        blob = git(self.root, "hash-object", "-w", "--stdin", stdin=b"tracked\n").stdout.strip()
        git(self.root, "update-index", "-z", "--index-info",
            stdin=b"100644 " + blob + b"\t" + name + b"\x00")

    def listing_is_undecodable(self):
        """Whether the arrangement reached the guard's own input, read as bytes rather than through
        the decode under test."""
        raw = git(self.root, "status", "--porcelain", "-z", "--untracked-files=all").stdout
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            return True
        return False

    def ask(self):
        event = {"tool_name": "Bash", "cwd": str(self.root), "tool_input": {"command": SWEEP}}
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60)

    @staticmethod
    def refusal(result):
        """(the decision printed, the untracked block from its header on), each None where absent."""
        try:
            output = json.loads(result.stdout)["hookSpecificOutput"]
        except (ValueError, KeyError, TypeError):
            return None, None
        _, header, block = output.get("permissionDecisionReason", "").partition(HEADER)
        return output.get("permissionDecision"), (header + block).splitlines() if header else None

    def test_Given_ATrackedNameThatIsNotUTF8_When_EverythingIsStaged_Then_TheRefusalListsWhatIsUntracked(self):
        # Arrange — quoting off, since with it on a line-based listing carries the byte as an escape
        # and a strict decode of it never meets one. The gate rides in the comparison because a
        # listing that decoded would leave nothing to fail on, and this case green having posed
        # nothing.
        git(self.root, "config", "core.quotePath", "false")
        self.index(UNDECODABLE)
        self.place(ORDINARY)
        arranged = self.listing_is_undecodable()

        # Act
        result = self.ask()

        # Assert
        self.assertEqual((arranged, result.returncode, *self.refusal(result)),
                         (True, 0, "deny", [f"{HEADER} (1):", f"  {ORDINARY}"]))

    def test_Given_ANameGitLeavesRaw_When_EverythingIsStaged_Then_TheRefusalListsItWhole(self):
        # Arrange — `core.quotePath` off makes no difference to a record-based reading and decides a
        # line-based one, which is the arrangement this is written against.
        git(self.root, "config", "core.quotePath", "false")
        self.place(RAW_SEPARATORS)

        # Act
        result = self.ask()

        # Assert
        self.assertEqual(self.refusal(result),
                         ("deny", [f"{HEADER} (1):", f"  {RAW_SEPARATORS_AS_SHOWN}"]))

    def test_Given_ANameGitEscapes_When_EverythingIsStaged_Then_TheRefusalListsItAsTheFileIs(self):
        # Arrange — set rather than inherited: with quoting off git leaves this name as it is, and
        # the case poses nothing.
        git(self.root, "config", "core.quotePath", "true")
        self.place(ESCAPED)

        # Act
        result = self.ask()

        # Assert
        self.assertEqual(self.refusal(result), ("deny", [f"{HEADER} (1):", f"  {ESCAPED}"]))


if __name__ == "__main__":
    unittest.main(verbosity=2)

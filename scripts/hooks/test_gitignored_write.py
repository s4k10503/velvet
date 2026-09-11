#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/gitignored_write.py.

One case holds that a script written under a worktree's Logs directory goes unnamed, with an ignored
script outside it still named beside it, so exempting every path goes red as well.

Run: python3 scripts/hooks/test_gitignored_write.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/report/gitignored_write.py"


class IgnoredOnPurposeTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-gitignored-write-"))
        self.addCleanup(shutil.rmtree, self.root, True)
        subprocess.run(["git", "init", "-q", str(self.root)], check=True, timeout=60)
        (self.root / ".gitignore").write_text("/[Ll]ogs/\n/ignored/\n")

    def written(self, relative):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("print('probe')\n")
        return path

    def printed(self, path):
        event = {"tool_name": "Write", "cwd": str(self.root), "tool_input": {"file_path": str(path)}}
        return subprocess.run([sys.executable, str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60).stdout

    def ignored(self, path):
        return subprocess.run(["git", "-C", str(self.root), "check-ignore", "-q", "--", str(path)],
                              timeout=60).returncode == 0

    def test_Given_AScriptWrittenUnderTheWorktreesLogs_When_Reported_Then_ItAloneGoesUnnamed(self):
        # Arrange — one name for both halves: a suffix the hook skips would silence the first for the
        # wrong reason, and it silences the second too.
        name = "cuts.py"
        logs, elsewhere = self.written(f"Logs/{name}"), self.written(f"ignored/{name}")

        # Act
        said = (self.printed(logs), self.printed(elsewhere))

        # Assert — git's reading of the first path rides along: a path git does not ignore goes
        # unnamed on the base as well.
        self.assertEqual((self.ignored(logs), said[0], f"ignored/{name}" in said[1]), (True, "", True))


if __name__ == "__main__":
    unittest.main()

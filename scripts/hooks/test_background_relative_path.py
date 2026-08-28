#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/background_relative_path.py's path reading.

A guard routed around by one character is worse than no guard: the refusal that does not fire reads
identically to a command nobody needed to refuse. Measured before the glob was read — backgrounded,
`python3 script?/pr/settle.py watch` was allowed while `python3 scripts/pr/settle.py watch` was
refused, and the two name the same file.

Run: python3 scripts/hooks/test_background_relative_path.py
"""

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/background_relative_path.py"


class PathSpellingTests(unittest.TestCase):
    def judge(self, command):
        """The guard's exit code for a backgrounded command in this repository."""
        event = {"tool_name": "Bash", "cwd": str(REPO_ROOT),
                 "tool_input": {"command": command, "run_in_background": True}}
        return subprocess.run([sys.executable, str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60).returncode

    def test_Given_AGlobSpelledRepoPath_When_Backgrounded_Then_ItIsRefused(self):
        # Arrange — the shell rewrites the selector into the path this exists to refuse.
        # Act / Assert
        self.assertEqual(self.judge("python3 script?/pr/settle.py watch"), 2)

    def test_Given_ThePlainSpellingOfTheSamePath_When_Backgrounded_Then_ItIsStillRefused(self):
        # Arrange — the half that already worked, and what the glob reading must not lose.
        # Act / Assert
        self.assertEqual(self.judge("python3 scripts/pr/settle.py watch"), 2)

    def test_Given_AnAbsolutePath_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — it says where it runs, which is the whole of what this asks.
        # Act / Assert
        self.assertEqual(self.judge("python3 /elsewhere/scripts/pr/settle.py watch"), 0)

    def test_Given_ACommandNamingNoRepoDirectory_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — the control: a reading that refused every glob would refuse this too.
        # Act / Assert
        self.assertEqual(self.judge("echo 'a?b' > /tmp/velvet-probe"), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

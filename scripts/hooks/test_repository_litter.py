#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/repository_litter.py.

The report prints a loop, for a reader to run as printed, that deletes a local branch other than main
when a merged pull request names it. One case runs it in a fixture where one local branch's name
matches a merged head only when read as a pattern.

Run: python3 scripts/hooks/test_repository_litter.py
"""

import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/report/repository_litter.py"

# The merged heads gh names: `a` is a local branch here, `bXc` is none but is what `b.c` matches
# when read as a pattern, and `keep/x` holds the local `keep` as a substring.
STUB_GH = "#!/bin/sh\nprintf 'a\\nbXc\\nkeep/x\\n'\n"


def git(cwd, *args):
    return subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True,
                          text=True, timeout=60).stdout


class DeletionLoopTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-litter-"))
        self.addCleanup(shutil.rmtree, self.root, True)
        self.project = self.root / "project"
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "-c", "user.email=t@velvet", "-c", "user.name=t", "commit", "-q",
            "--allow-empty", "-m", "root")
        for branch in ("a", "b.c", "keep"):
            git(self.project, "branch", branch)
        stub = self.root / "bin"
        stub.mkdir()
        (stub / "gh").write_text(STUB_GH, encoding="utf-8")
        (stub / "gh").chmod(0o755)
        self.environment = dict(os.environ, PATH=f"{stub}{os.pathsep}{os.environ.get('PATH', '')}",
                                CLAUDE_PROJECT_DIR=str(self.project), VELVET_LITTER_BRANCHES="0")

    def printed_loop(self):
        report = subprocess.run([sys.executable, "-B", str(HOOK)], capture_output=True, text=True,
                                cwd=str(self.project), env=self.environment, timeout=120).stdout
        blocks, block = [], []
        for line in report.splitlines() + [""]:
            if line.startswith("  "):
                block.append(line[2:])
            elif block:
                blocks.append("\n".join(block))
                block = []
        return next((text for text in blocks if "branch -D" in text), "")

    def test_Given_BranchesMatchingAMergedHeadOnlyAsAPatternOrASubstring_When_TheLoopIsRun_Then_OnlyTheMergedOneGoes(self):
        # Arrange
        loop = self.printed_loop()

        # Act
        subprocess.run(["/bin/sh", "-c", loop], cwd=str(self.project), env=self.environment,
                       capture_output=True, timeout=120)

        # Assert — `a` rides along: a loop that deleted nothing would keep `b.c` as well.
        self.assertEqual(git(self.project, "branch", "--format=%(refname:short)").split(),
                         ["b.c", "keep", "main"])


if __name__ == "__main__":
    unittest.main()

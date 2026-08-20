#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/stale_main.py.

The report is what a session reads first, and its remedy is a rebase — so naming the wrong branch
there is a destructive instruction rather than a wrong number. It named `main` for every checkout
until a maintenance branch was cut, at which point a release branch on that line was reported as
behind main and told to rebase onto it.

Each case builds a repository with a real `origin` and a branch off a maintenance line, and differs
only in what gh says about that branch's pull request.

Run: python3 scripts/hooks/test_stale_main.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/report/stale_main.py"

STUB_GH = '''#!/bin/sh
printf '%s' "$VELVET_STALE_MAIN_VIEW"
exit "$VELVET_STALE_MAIN_VIEW_CODE"
'''


def git(project, *args):
    subprocess.run(["git", "-C", str(project), *args], check=True,
                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=60)


def commit(project, message):
    git(project, "-c", "user.email=t@velvet", "-c", "user.name=t",
        "commit", "-q", "--allow-empty", "-m", message)


class BranchBaseTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-stale-main-"))
        origin = self.root / "origin.git"
        self.project = self.root / "project"
        subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "remote", "add", "origin", str(origin))
        commit(self.project, "initial")
        git(self.project, "branch", "2.x")
        commit(self.project, "main moves on")
        # The maintenance line moves after the release branch is cut from it, so that branch trails
        # both refs and the two candidate remedies name different branches.
        git(self.project, "branch", "release/2.1.1", "2.x")
        git(self.project, "checkout", "-q", "2.x")
        commit(self.project, "the maintenance patch")
        git(self.project, "checkout", "-q", "release/2.1.1")
        commit(self.project, "the change under review")
        git(self.project, "push", "-q", "origin", "main", "2.x", "release/2.1.1")

        self.stub = self.root / "bin"
        self.stub.mkdir()
        gh = self.stub / "gh"
        gh.write_text(STUB_GH, encoding="utf-8")
        gh.chmod(0o755)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def report(self, view="", view_code=0):
        environment = dict(os.environ)
        environment["PATH"] = str(self.stub) + os.pathsep + environment.get("PATH", "")
        environment["CLAUDE_PROJECT_DIR"] = str(self.project)
        environment["VELVET_STALE_MAIN_VIEW"] = view
        environment["VELVET_STALE_MAIN_VIEW_CODE"] = str(view_code)
        return subprocess.run([sys.executable, "-B", str(HOOK)], capture_output=True, text=True,
                              cwd=str(self.project), env=environment, timeout=120).stdout

    def test_Given_APullRequestNamingAMaintenanceBase_When_TheReportIsTaken_Then_ItRebasesOntoIt(self):
        # Arrange
        named = json.dumps({"headRefName": "release/2.1.1", "baseRefName": "2.x"})

        # Act
        printed = self.report(view=named)

        # Assert
        self.assertIn("git rebase origin/2.x", printed)

    # GREEN_ON_BASE(characterization): the fallback to main that this change leaves in place.
    # The case above says the base comes off the pull request; this one says a branch that has no
    # pull request yet is still measured against main.
    def test_Given_NoPullRequestNamingABase_When_TheReportIsTaken_Then_ItRebasesOntoMain(self):
        # Arrange — the control: gh answers for no branch here, which is the state every branch is
        # in before its pull request exists, and main is what the report falls back to.

        # Act
        printed = self.report(view="no pull requests found", view_code=1)

        # Assert
        self.assertIn("git rebase origin/main", printed)

    def test_Given_ABaseWhoseRefIsNotHere_When_TheReportIsTaken_Then_TheBranchIsNotReportedOn(self):
        # Arrange
        named = json.dumps({"headRefName": "release/2.1.1", "baseRefName": "3.x"})

        # Act
        printed = self.report(view=named)

        # Assert
        self.assertEqual(printed, "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

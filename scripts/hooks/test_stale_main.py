#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/stale_main.py.

The report is what a session reads first, and its remedy is a rebase and a force-push — so the
branch it names is a destructive instruction rather than a wrong number. It named `main` for every
checkout until a maintenance branch was cut, at which point the 2.1.1 release branch was reported as
fifty commits behind main and told to rebase onto it.

The base comes off the branch's pull request, and the cases below are the four states that reading
leaves: a base that names a maintenance line, a base that names main, a base nothing named, and a
base naming a ref this checkout has no way to reach. Only the first two carry a remedy.

Every remote-tracking ref is dropped before each run, so a fetch that stops happening takes the
reading with it rather than answering from what an earlier push left behind.

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
        for ref in ("main", "2.x", "release/2.1.1"):
            git(self.project, "update-ref", "-d", f"refs/remotes/origin/{ref}")

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

    def named(self, base):
        return json.dumps({"headRefName": "release/2.1.1", "baseRefName": base})

    def test_Given_APullRequestNamingAMaintenanceBase_When_TheReportIsTaken_Then_ItRebasesOntoIt(self):
        # Arrange / Act
        printed = self.report(view=self.named("2.x"))

        # Assert
        self.assertIn("git rebase origin/2.x", printed)

    # GREEN_ON_BASE(characterization): the remedy this change still offers for a base it read.
    # Withholding one where nothing named a base is satisfiable by withholding every one, and this
    # is the case that says an ordinary branch keeps its rebase.
    def test_Given_APullRequestNamingMain_When_TheReportIsTaken_Then_ItStillRebasesOntoMain(self):
        # Arrange — the control: a base that was read is a base a remedy may be offered against.
        printed = self.report(view=self.named("main"))

        # Act / Assert
        self.assertIn("git rebase origin/main", printed)

    def test_Given_NothingNamingABaseForABaseBranch_When_TheReportIsTaken_Then_NoForcePushIsOffered(self):
        # Arrange — a branch pull requests target rather than come from, so no pull request of its
        # own names a base for it. It trails main for as long as main goes on moving, and rebasing
        # it onto main rewrites the commits every one of those pull requests sits on.
        git(self.project, "checkout", "-q", "2.x")

        # Act
        printed = self.report(view="no pull requests found", view_code=1)

        # Assert — the distance is still reported; the remedy is what is withheld.
        self.assertEqual(("Branch 2.x is" in printed, "--force-with-lease" in printed),
                         (True, False))

    def test_Given_ABaseWhoseRefIsNotHere_When_TheReportIsTaken_Then_TheBranchIsNotReportedOn(self):
        # Arrange / Act
        printed = self.report(view=self.named("3.x"))

        # Assert
        self.assertEqual(printed, "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/lib/merge_target.py.

The numbered read is exercised end to end by `pull_request_base_check.py`, which drives the guards
themselves. Nothing there poses a merge naming no pull request: that command means the checked-out
branch's, so the world would have to hold the head checked out, and a guard refuses a merge whose
`--delete-branch` would fail against a worktree. So that shape is posed here instead, along with the
two ways a payload leaves the answer unread.

Run: python3 scripts/hooks/test_merge_target.py
"""

import importlib.util
import json
import os
import shutil
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports merge_target by path, since .claude holds no packages."""
    path = REPO_ROOT / ".claude/hooks/lib/merge_target.py"
    spec = importlib.util.spec_from_file_location("merge_target", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


merge_target = load_module()

STUB_GH = '''#!/bin/sh
if [ "$1" = "api" ]; then
  printf '%s' "$VELVET_MERGE_TARGET_API"
  exit "$VELVET_MERGE_TARGET_API_CODE"
fi
printf '%s' "$VELVET_MERGE_TARGET_VIEW"
exit "$VELVET_MERGE_TARGET_VIEW_CODE"
'''


class RefsTests(unittest.TestCase):
    def setUp(self):
        self.workspace = Path(tempfile.mkdtemp(prefix="velvet-merge-target-"))
        stub = self.workspace / "gh"
        stub.write_text(STUB_GH, encoding="utf-8")
        stub.chmod(0o755)
        self.path = os.environ.get("PATH", "")
        os.environ["PATH"] = str(self.workspace) + os.pathsep + self.path
        self.answer(api="", view="")

    def tearDown(self):
        os.environ["PATH"] = self.path
        shutil.rmtree(self.workspace, ignore_errors=True)

    def answer(self, api="", view="", api_code=0, view_code=0):
        os.environ["VELVET_MERGE_TARGET_API"] = api
        os.environ["VELVET_MERGE_TARGET_API_CODE"] = str(api_code)
        os.environ["VELVET_MERGE_TARGET_VIEW"] = view
        os.environ["VELVET_MERGE_TARGET_VIEW_CODE"] = str(view_code)

    def test_Given_ANumberedMerge_When_TheRefsAreRead_Then_TheyAreThatPullRequestsOwnFields(self):
        # Arrange
        self.answer(api=json.dumps({"head": {"ref": "release/2.1.1"}, "base": {"ref": "2.x"}}))

        # Act
        target = merge_target.refs_of(str(self.workspace), "733")

        # Assert
        self.assertEqual(target, ("release/2.1.1", "2.x"))

    def test_Given_AMergeNamingNoPullRequest_When_TheRefsAreRead_Then_GhResolvesItFromTheBranch(self):
        # Arrange — the numbered read is left failing, so an answer can only come from the other one.
        self.answer(api="", api_code=1,
                    view=json.dumps({"headRefName": "release/2.1.1", "baseRefName": "2.x"}))

        # Act
        target = merge_target.refs_of(str(self.workspace), "")

        # Assert
        self.assertEqual(target, ("release/2.1.1", "2.x"))

    def test_Given_AGhThatExitedNonZero_When_TheRefsAreRead_Then_NothingIsReturned(self):
        # Arrange — the payload is well formed, so the exit code is the whole of what makes this not
        # an answer: a body that parses is not evidence the call succeeded.
        self.answer(api=json.dumps({"head": {"ref": "release/2.1.1"}, "base": {"ref": "2.x"}}),
                    api_code=1)

        # Act
        target = merge_target.refs_of(str(self.workspace), "733")

        # Assert
        self.assertIsNone(target)

    def test_Given_AGhThatPrintedSomethingOtherThanJson_When_TheRefsAreRead_Then_NothingIsRaised(self):
        # Arrange — a guard that raises exits 1, and 1 lets the tool through, so a body that does not
        # decode has to arrive as an unread answer rather than as an exception.
        self.answer(api="gh: HTTP 502")

        # Act
        target = merge_target.refs_of(str(self.workspace), "733")

        # Assert
        self.assertIsNone(target)

    def test_Given_APayloadCarryingNoBase_When_TheRefsAreRead_Then_NothingIsReturned(self):
        # Arrange — what a renamed field leaves behind: a successful call whose base is absent, which
        # read as an empty branch name would send every guard to `origin/`.
        self.answer(api=json.dumps({"head": {"ref": "release/2.1.1"}}))

        # Act
        target = merge_target.refs_of(str(self.workspace), "733")

        # Assert
        self.assertIsNone(target)


if __name__ == "__main__":
    unittest.main(verbosity=2)

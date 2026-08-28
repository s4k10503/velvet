#!/usr/bin/env python3
"""Unit tests for the zero-checks reading in .claude/hooks/stop/unsettled_pr.py.

No pull request in this repository is entitled to zero checks: both required workflows subscribe to
`pull_request` without a path filter, so a head with none is a run that did not start. The guard used
to read a CLEAN one as ready and say "merge it", justified by a path filter that does not exist — and
the state was reachable once, through the `pull_request: branches: [main]` filter #776 removed, where
that advice named a pull request nothing had tested.

What the cases hold is that the advice never says merge, and that the two states which do explain an
absent list still say nothing.

Run: python3 scripts/hooks/test_unsettled_pr.py
"""

import importlib.util
import json
import shutil
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/stop/unsettled_pr.py"

_spec = importlib.util.spec_from_file_location("unsettled_pr", GUARD)
unsettled_pr = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(unsettled_pr)


class ZeroChecks(unittest.TestCase):
    def judge_with(self, state, checks="[]"):
        """`judge` with gh answering a fixed check list and merge state."""
        def stub(args):
            if "checks" in args:
                return checks, "", 0
            if "statusCheckRollup" in args:
                # An empty list is the only answer that says "there are none"; the guard reads it
                # whole rather than through a length, because jq cannot tell absent from empty.
                return json.dumps({"statusCheckRollup": []}), "", 0
            # `--jq` is asked for, so gh prints the value rather than the object.
            return state, "", 0

        original = unsettled_pr.gh
        unsettled_pr.gh = stub
        try:
            return unsettled_pr.judge("1")
        finally:
            unsettled_pr.gh = original

    def test_Given_ACleanPullRequestWithNoChecks_When_Judged_Then_ItDoesNotSayMerge(self):
        # Arrange — the advice this replaces named a pull request nothing had tested.
        said = self.judge_with("CLEAN")

        # Act / Assert
        self.assertEqual((said is None, "Merge it" in (said or "")), (False, False))

    def test_Given_ACleanPullRequestWithNoChecks_When_Judged_Then_ItSaysTheRunDidNotStart(self):
        # Act / Assert
        self.assertIn("no check ever ran for its head", self.judge_with("CLEAN"))

    # GREEN_ON_BASE(characterization): the three states that explain an absent list are what this
    # change had to leave alone while removing the fourth, and nothing else says they still do.
    def test_Given_AStateThatExplainsAnAbsentList_When_Judged_Then_NothingIsSaid(self):
        # Arrange — BLOCKED is one of the three; a list absent for a reason it names is not a run
        # that did not start.
        # Act / Assert
        self.assertIsNone(self.judge_with("BLOCKED"))

    # GREEN_ON_BASE(characterization): DIRTY is the state the whole reading was built for, and the
    # branch that names it is the one being rewritten.
    def test_Given_AConflictingPullRequest_When_Judged_Then_ItIsStillNamed(self):
        # Act / Assert
        self.assertIn("DIRTY", self.judge_with("DIRTY"))


class PendingBehindAWatcher(unittest.TestCase):
    """A pending check is forgiven while something is demonstrably watching.

    `alive` answers False for any fresh heartbeat that is not exactly two fields, which is what a
    watcher launched from a checkout predating the pid field writes — five commits in this
    repository's history write one. Something is watching there, and the reading is what failed.
    """

    def judge_with(self, beat):
        """`judge` over one pending check, with the heartbeat file holding `beat` or absent."""
        def stub(args):
            if "checks" in args:
                return json.dumps([{"name": "Unity tests", "bucket": "pending"}]), "", 0
            if "statusCheckRollup" in args:
                return json.dumps({"statusCheckRollup": [{"name": "Unity tests"}]}), "", 0
            return "BLOCKED", "", 0

        directory = Path(tempfile.mkdtemp(prefix="unsettled-beat-"))
        path = directory / "heartbeat"
        if beat is not None:
            path.write_text(beat)
        original = unsettled_pr.gh
        unsettled_pr.gh = stub
        # The guard binds the readings by name, so what has to be redirected is the module they read
        # their paths from.
        state = sys.modules["watcher_state"]
        try:
            with mock.patch.object(state, "HEARTBEAT", path), \
                    mock.patch.object(state, "ASKED", directory / "asked"):
                return unsettled_pr.judge("1")
        finally:
            unsettled_pr.gh = original
            shutil.rmtree(directory, ignore_errors=True)

    def test_Given_AHeartbeatThisCannotRead_When_ACheckIsPending_Then_ItIsForgiven(self):
        # Arrange — a one-field heartbeat inside the window: `alive` is False and something is
        # writing it, which is the state `unreadable_beat` exists to name.
        said = self.judge_with(f"{int(time.time())}\n")

        # Act / Assert
        self.assertIsNone(said)

    # GREEN_ON_BASE(characterization): the base blocks here because it forgives nothing, and this is
    # the half a widened permissive branch could take with it. Only running it says whether it did.
    def test_Given_NoHeartbeatAtAll_When_ACheckIsPending_Then_ItStillBlocks(self):
        # Arrange — the control: forgiving here is the hole this guard was written for, and an
        # absent file and an unreadable one are what the two halves separate.
        said = self.judge_with(None)

        # Act / Assert
        self.assertIn("still pending", said or "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

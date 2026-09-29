#!/usr/bin/env python3
"""Unit tests for what scripts/pr/diagnostics/unsettled_pr.py says about an unsettled pull request.

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
GUARD = REPO_ROOT / "scripts/pr/diagnostics/unsettled_pr.py"

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
    watcher launched from a checkout predating the pid field writes. Something is watching there, and
    the reading is what failed — which is the state `unreadable_beat` exists to name.
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


PASSING = json.dumps([{"name": "Required checks (Unity)", "bucket": "pass"}])
NO_RUNS = json.dumps({"total_count": 0, "workflow_runs": []})
TEST_PENDING = json.dumps({"total_count": 1, "workflow_runs": [{"name": "Test", "status": "pending"}]})
REQUIRING_UNITY = json.dumps([{"type": "required_status_checks", "parameters": {
    "required_status_checks": [{"context": "Required checks (Unity)"}]}}])
REQUIRING_BOTH = json.dumps([{"type": "required_status_checks", "parameters": {
    "required_status_checks": [{"context": "Required checks (Unity)"},
                               {"context": "Required checks (generators)"}]}}])


class OwedChecks(unittest.TestCase):
    """A head whose check list is not yet everything it will carry, named for what it is owed."""

    def judge_with(self, checks, state, runs=NO_RUNS, rules=REQUIRING_UNITY):
        """`judge` with gh answering the check list, the merge state, the runs and the rules."""
        def stub(args):
            if args[0] == "api" and "/actions/runs?" in args[1]:
                return runs, "", 0
            if args[0] == "api" and "/rules/branches/main?" in args[1]:
                return rules, "", 0
            if "headRefOid,baseRefName" in args:
                return json.dumps({"headRefOid": "a" * 40, "baseRefName": "main"}), "", 0
            if "checks" in args:
                return checks, "", 0
            if "statusCheckRollup" in args:
                return json.dumps({"statusCheckRollup": json.loads(checks)}), "", 0
            return state, "", 0

        with mock.patch.object(unsettled_pr, "gh", stub):
            return unsettled_pr.judge("1")

    def test_Given_EveryCheckPassedAndARunNotStarted_When_Judged_Then_TheRunIsNamed(self):
        # Arrange — the merge state GitHub gives a head whose required check has not reported.
        said = self.judge_with(PASSING, "BLOCKED", runs=TEST_PENDING)

        # Act / Assert
        self.assertIn("workflow runs not finished: Test (pending)", said or "")

    def test_Given_EveryCheckPassedAndARequiredOneNeverReported_When_Judged_Then_ItIsNamed(self):
        # Act
        said = self.judge_with(PASSING, "BLOCKED", rules=REQUIRING_BOTH)

        # Assert
        self.assertIn("required by main and not reported: Required checks (generators)", said or "")

    def test_Given_NoCheckYetAndARunPending_When_Judged_Then_TheRunIsNamed(self):
        # Arrange — CLEAN, so the state is not one that explains an absent list and the head blocks.
        said = self.judge_with("[]", "CLEAN", runs=TEST_PENDING)

        # Act / Assert
        self.assertIn("workflow runs not finished: Test (pending)", said or "")

    # GREEN_ON_BASE(characterization): a green, clean, unmerged head blocks with "merge it" on the base.
    # What it pins is that nothing owed leaves that advice alone: `if still[0] or still[2] or True:`
    # reddens it.
    def test_Given_NothingOwed_When_Judged_Then_ItStillSaysMergeIt(self):
        # Act
        said = self.judge_with(PASSING, "CLEAN")

        # Assert
        self.assertIn("every check passed and it is unmerged", said or "")

    def test_Given_ARunsReadingThatFails_When_Judged_Then_ItBlocksNamingTheGuardsBlindness(self):
        # Act
        said = self.judge_with(PASSING, "CLEAN", runs="")

        # Assert
        self.assertIn("what its head is still owed could not be read", said or "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

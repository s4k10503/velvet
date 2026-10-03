#!/usr/bin/env python3
"""Campaign display statuses are bound to the current PR head and newest live run."""

import json
import os
import re
import unittest
from pathlib import Path
from unittest import mock

import campaign_status as status

SHA = "a" * 40
SLUG = "owner/repo"


def pull():
    return {"number": 7, "state": "open", "labels": [{"name": "automerge"}],
            "head": {"sha": SHA, "repo": {"full_name": SLUG}},
            "base": {"repo": {"full_name": SLUG}}}


def run():
    return {"id": 12, "run_attempt": 1, "run_number": 8, "path": status.settle.campaign.PATH,
            "head_sha": SHA, "head_repository": {"full_name": SLUG}, "event": "workflow_dispatch",
            "status": "in_progress", "conclusion": None}


def api_read(responses):
    pulls = iter(item for item in responses if "head" in item)
    runs = iter(item for item in responses if "workflow_runs" in item)
    return lambda path: next(pulls if "/pulls/" in path else runs)


class CampaignStatusTests(unittest.TestCase):
    def test_Given_ALabelledHeadWithoutARun_When_Read_Then_TheDisplayIsPending(self):
        # Act
        result = status.reading(pull(), [], SLUG)

        # Assert
        self.assertEqual((result["sha"], result["state"]), (SHA, "pending"))

    def test_Given_ACampaignAfterEarlierSuccess_When_TheNewRunStarts_Then_TheDisplayReturnsToPending(self):
        # Arrange
        previous = {**run(), "id": 11, "run_number": 7, "status": "completed", "conclusion": "success"}

        # Act
        result = status.reading(pull(), [previous, run()], SLUG, run())

        # Assert
        self.assertEqual(result["state"], "pending")

    def test_Given_ACompletedCampaign_When_Read_Then_OnlySuccessProducesAGreenDisplay(self):
        # Arrange
        conclusions = ["success", "failure", "cancelled", "timed_out", None]

        # Act
        states = [status.reading(pull(), [{**run(), "status": "completed", "conclusion": value}], SLUG)["state"]
                  for value in conclusions]

        # Assert
        self.assertEqual(states, ["success", "failure", "error", "failure", "pending"])

    def test_Given_AStartEventDeliveredAfterCompletion_When_Read_Then_TheLiveCompletedVerdictStands(self):
        # Arrange
        finished = {**run(), "status": "completed", "conclusion": "success"}

        # Act
        result = status.reading(pull(), [finished], SLUG, run())

        # Assert
        self.assertEqual(result["state"], "success")

    def test_Given_TheLabelIsRemoved_When_NoCampaignOrACancelledOneIsOwed_Then_TheDisplayDoesNotBlockManualMerging(self):
        # Arrange
        unlabelled = pull()
        unlabelled["labels"] = []
        cancelled = {**run(), "status": "completed", "conclusion": "cancelled"}
        failed = {**run(), "status": "completed", "conclusion": "failure"}

        # Act
        states = [status.reading(unlabelled, runs, SLUG, released=True)["state"]
                  for runs in [[], [cancelled], [failed]]]

        # Assert
        self.assertEqual(states, ["success", "success", "failure"])

    def test_Given_TheLabelIsAddedAgain_When_TheLiveHeadStillCarriesIt_Then_AnOldRemovalEventCannotClearPending(self):
        # Act
        result = status.reading(pull(), [], SLUG, released=True)

        # Assert
        self.assertEqual(result["state"], "pending")

    def test_Given_AnOlderCompletion_When_ANewerCampaignIsRunning_Then_NothingIsPublishedForIt(self):
        # Arrange
        older = {**run(), "id": 11, "run_number": 7, "status": "completed", "conclusion": "success"}

        # Act
        result = status.reading(pull(), [older, run()], SLUG, older)

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((result, control["state"] if control else None), (None, "pending"))

    def test_Given_AnOlderAttempt_When_TheSameRunIsRerun_Then_ItsCompletionCannotClearPending(self):
        # Arrange
        latest = {**run(), "run_attempt": 2}

        # Act
        result = status.reading(pull(), [latest], SLUG, run())

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((result, control["state"] if control else None), (None, "pending"))

    def test_Given_UnrelatedOrForeignOrStaleRuns_When_Read_Then_NoneCanClearTheCurrentHead(self):
        # Arrange
        invalid = [{**run(), "path": ".github/workflows/other.yml"},
                   {**run(), "head_repository": {"full_name": "fork/repo"}},
                   {**run(), "head_sha": "b" * 40}]
        unlabelled = pull()
        unlabelled["labels"] = []

        # Act
        read = [status.reading(unlabelled, [item], SLUG, item) for item in invalid]

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((read, control["state"] if control else None), ([None, None, None], "pending"))

    def test_Given_AForeignOrClosedOrMovedPull_When_Read_Then_NothingIsPublished(self):
        # Arrange
        foreign = pull()
        foreign["head"]["repo"]["full_name"] = "fork/repo"
        closed = {**pull(), "state": "closed"}
        moved = pull()
        moved["head"]["sha"] = "b" * 40

        # Act
        results = [status.reading(item, [run()], SLUG, run()) for item in [foreign, closed, moved]]

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((results, control["state"] if control else None), ([None, None, None], "pending"))

    def test_Given_TheHeadMovesDuringTheApiReading_When_Published_Then_NoStatusIsWritten(self):
        # Arrange
        moved = pull()
        moved["head"]["sha"] = "b" * 40
        responses = api_read([pull(), {"workflow_runs": [run()]}, moved, {"workflow_runs": [run()]}])
        posted = []

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            status.publish(Path("."), 7, run(), read=responses, post=posted.append)

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((posted, control["state"] if control else None), ([], "pending"))

    def test_Given_TheLabelIsAddedDuringTheApiReading_When_Published_Then_AFormerlyOptionalCancellationCannotReportSuccess(self):
        # Arrange
        unlabelled = pull()
        unlabelled["labels"] = []
        cancelled = {**run(), "status": "completed", "conclusion": "cancelled"}
        responses = api_read([unlabelled, {"workflow_runs": [cancelled]}, pull(), {"workflow_runs": [cancelled]}])
        posted = []

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            status.publish(Path("."), 7, cancelled, read=responses, post=posted.append)

        # Assert
        self.assertEqual([item["state"] for item in posted], ["error"])

    def test_Given_ANewerRunAppearsDuringTheApiReading_When_Published_Then_AnOldCompletionWritesNothing(self):
        # Arrange
        old = {**run(), "status": "completed", "conclusion": "success"}
        newer = {**run(), "id": 13, "run_number": 9}
        responses = api_read([pull(), {"workflow_runs": [old]}, pull(), {"workflow_runs": [old, newer]}])
        posted = []

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            status.publish(Path("."), 7, old, read=responses, post=posted.append)

        # Assert
        control = status.reading(pull(), [run()], SLUG, run())
        self.assertEqual((posted, control["state"] if control else None), ([], "pending"))

    def test_Given_ACompleteStableReading_When_Published_Then_TheCurrentHeadReceivesTheSharedContext(self):
        # Arrange
        responses = api_read([pull(), {"workflow_runs": [run()]}, pull(), {"workflow_runs": [run()]}])

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG), mock.patch.object(status.settle, "gh") as gh:
            status.publish(Path("."), 7, run(), read=responses)

        # Assert
        self.assertEqual((gh.call_args.args[3], "context=Mutation campaign" in gh.call_args.args),
                         (f"repos/{SLUG}/statuses/{SHA}", True))

    def test_Given_ARecentRunWithNoOrderingIdentity_When_Read_Then_AnOlderSuccessCannotBecomeItsVerdict(self):
        # Arrange
        previous = {**run(), "id": 11, "run_number": 7, "status": "completed", "conclusion": "success"}
        unknown = {**run(), "id": 13}
        del unknown["run_number"]

        # Act
        result = status.reading(pull(), [previous, unknown], SLUG)
        control = status.reading(pull(), [run()], SLUG, run())

        # Assert
        self.assertEqual((result, control["state"] if control else None), (None, "pending"))

    def test_Given_AnIncompleteRunListing_When_Published_Then_NoVerdictIsInvented(self):
        # Arrange
        responses = api_read([pull(), {"workflow_runs": [run()], "total_count": 2}])

        # Act / Assert
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            self.assertRaises(RuntimeError, status.publish, Path("."), 7, run(),
                              read=responses, post=lambda result: None)


class PublisherMainTests(unittest.TestCase):
    def test_Given_ThePublisherFinishes_When_OnlyASuccessfulCampaignIsReported_Then_TheMergeWakeWaitsForThisRun(self):
        # Arrange
        event = {"pull_request": {"number": 7}, "repository": {"default_branch": "main"}}
        observed = []

        # Act
        for state in ["pending", "failure", "error", "success"]:
            with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                    mock.patch.object(status.settle, "repository", return_value=SLUG), \
                    mock.patch.object(status, "publish", return_value={"state": state}), \
                    mock.patch.object(status.automerge, "dispatch") as dispatch, \
                    mock.patch.dict(os.environ, {"GITHUB_EVENT_PATH": "event", "GITHUB_RUN_ID": "44"}):
                status.main([])
                observed.append(dispatch.call_args)

        # Assert
        self.assertEqual(observed, [None, None, None,
                                   mock.call(Path(".").resolve(), status.automerge.MERGE_WORKFLOW,
                                             "main", number=7, after_run="44")])

    def test_Given_AnotherWorkflowCompletes_When_ThePublisherReceivesIt_Then_NoHeadStatusIsReadOrWritten(self):
        # Arrange
        event = {"workflow_run": {**run(), "path": ".github/workflows/other.yml"}}

        # Act
        with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                mock.patch.object(status.settle, "repository", return_value=SLUG), \
                mock.patch.object(status, "publish") as publish, \
                mock.patch.dict(os.environ, {"GITHUB_EVENT_PATH": "event"}):
            status.main([])

        # Assert
        self.assertFalse(publish.called)

    def test_Given_TheLabelIsRemoved_When_TheDisplayRequirementIsCleared_Then_NoAutomergeIsRequested(self):
        # Arrange
        event = {"pull_request": {"number": 7}, "action": "unlabeled", "label": {"name": "automerge"}}

        # Act
        with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                mock.patch.object(status.settle, "repository", return_value=SLUG), \
                mock.patch.object(status, "publish", return_value={"state": "success"}), \
                mock.patch.object(status.automerge, "dispatch") as dispatch, \
                mock.patch.dict(os.environ, {"GITHUB_EVENT_PATH": "event"}):
            status.main([])

        # Assert
        self.assertFalse(dispatch.called)


class PublisherWorkflowTests(unittest.TestCase):
    def test_Given_TheStatusWorkflow_When_Read_Then_WriteAccessRunsOnlyDefaultBranchCodeAndCoversReruns(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        text = (root / ".github/workflows/campaign-status.yml").read_text()
        mutation = (root / ".github/workflows/mutation.yml").read_text()

        # Act
        publishers = re.findall(r"ref: (.*)", text)
        privileged_shard = "statuses: write" in mutation or "checks: write" in mutation

        # Assert
        self.assertEqual((publishers, "types: [requested, in_progress, completed]" in text,
                          "statuses: write" in text, privileged_shard),
                         (["${{ github.event.repository.default_branch }}"], True, True, False))


if __name__ == "__main__":
    unittest.main(verbosity=2)

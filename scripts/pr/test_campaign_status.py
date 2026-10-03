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
    statuses = iter(item for item in responses if "statuses" in item)
    def read(path):
        if "/status?" in path:
            return next(statuses, {"sha": SHA, "total_count": 0, "statuses": []})
        return next(pulls if "/pulls/" in path else runs)
    return read


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
    # GREEN_ON_BASE(characterization): the existing success hand-off still waits for this publisher run.
    def test_Given_ThePublisherFinishes_When_OnlyASuccessfulCampaignIsReported_Then_TheMergeWakeWaitsForThisRun(self):
        # Arrange
        event = {"pull_request": {"number": 7}, "repository": {"default_branch": "main", "full_name": SLUG}}
        observed = []

        # Act
        for state in ["pending", "failure", "error", "success"]:
            with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                    mock.patch.object(status.settle, "repository", return_value=SLUG), \
                    mock.patch.object(status.settle, "rest_json", return_value={"full_name": SLUG, "default_branch": "main"}), \
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

    # GREEN_ON_BASE(characterization): clearing the display after label removal does not request automerge.
    def test_Given_TheLabelIsRemoved_When_TheDisplayRequirementIsCleared_Then_NoAutomergeIsRequested(self):
        # Arrange
        event = {"pull_request": {"number": 7}, "action": "unlabeled", "label": {"name": "automerge"},
                 "repository": {"default_branch": "main", "full_name": SLUG}}

        # Act
        with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                mock.patch.object(status.settle, "repository", return_value=SLUG), \
                mock.patch.object(status.settle, "rest_json", return_value={"full_name": SLUG, "default_branch": "main"}), \
                    mock.patch.object(status, "publish", return_value={"state": "success"}), \
                mock.patch.object(status.automerge, "dispatch") as dispatch, \
                mock.patch.dict(os.environ, {"GITHUB_EVENT_PATH": "event"}):
            status.main([])

        # Assert
        self.assertFalse(dispatch.called)


class ReconciliationTests(unittest.TestCase):
    def execute(self, event_name, pulls, *, verdict="success", existing=None, selected="", metadata=None):
        event = {"repository": {"full_name": SLUG, "default_branch": "main"},
                 "inputs": {"pull_request": selected}}
        if metadata is not None:
            event["repository"] = metadata
        posted, dispatched, paths = [], [], []
        by_number = {item["number"]: item for item in pulls if isinstance(item, dict)}
        finished = {**run(), "status": "completed", "conclusion": verdict}
        def read(path):
            paths.append(path)
            if path == f"repos/{SLUG}":
                return {"full_name": SLUG, "default_branch": "main"}
            if "pulls?" in path:
                return pulls
            if "/pulls/" in path:
                return by_number[int(path.rsplit("/", 1)[1])]
            sha = re.search(r"(?:head_sha=|commits/)([0-9a-f]{40})", path).group(1)
            if "/status?" in path:
                entries = [] if existing is None else [{"context": status.CONTEXT, **existing}]
                return {"sha": sha, "total_count": len(entries), "statuses": entries}
            return {"workflow_runs": [{**finished, "head_sha": sha}]}
        live_publish = status.publish
        def publish(project, number, event_run=None, released=False):
            return live_publish(project, number, event_run, read=read, post=posted.append, released=released)
        with mock.patch.object(Path, "read_text", return_value=json.dumps(event)), \
                mock.patch.object(status.settle, "repository", return_value=SLUG), \
                mock.patch.object(status.settle, "rest_json", side_effect=read), \
                mock.patch.object(status, "publish", side_effect=publish), \
                mock.patch.object(status.automerge, "dispatch", side_effect=lambda *a, **kw: dispatched.append(kw)), \
                mock.patch.dict(os.environ, {"GITHUB_EVENT_PATH": "event", "GITHUB_EVENT_NAME": event_name,
                                             "GITHUB_RUN_ID": "44"}):
            try:
                status.main([])
                error = None
            except RuntimeError as exc:
                error = str(exc)
        return posted, dispatched, paths, error

    def test_Given_TwoOpenHeadsWithoutPublisherEvents_When_TheScheduleRuns_Then_BothCurrentVerdictsArePublishedAndWoken(self):
        # Arrange
        second = pull()
        second["number"], second["head"]["sha"] = 8, "b" * 40

        # Act
        posted, wakes, paths, error = self.execute("schedule", [pull(), second])

        # Assert
        self.assertEqual(([p["sha"] for p in posted], [w["number"] for w in wakes], error),
                         ([SHA, "b" * 40], [7, 8], None))

    def test_Given_IdenticalPendingAndSuccessDisplays_When_Reconciled_Then_NeitherStatusesNorMergeWakesRepeat(self):
        # Arrange
        observed = []

        # Act
        for verdict in [None, "success"]:
            state_run = {**run(), "status": "completed", "conclusion": verdict}
            result = status.reading(pull(), [state_run], SLUG)
            posted, wakes, paths, error = self.execute("schedule", [pull()], verdict=verdict, existing=result)
            control, control_wakes, _, control_error = self.execute(
                "schedule", [pull()], verdict=verdict, existing={**result, "target_url": "https://github.com/old/run"})
            observed.append((posted, wakes, error, len(control), len(control_wakes), control_error))

        # Assert
        self.assertEqual(observed, [([], [], None, 1, 0, None), ([], [], None, 1, 1, None)])

    def test_Given_EachOwnedDisplayFieldChanges_When_Reconciled_Then_AFreshSuccessIsPublishedAndWoken(self):
        # Arrange
        current = status.reading(pull(), [{**run(), "status": "completed", "conclusion": "success"}], SLUG)
        observed = []

        # Act
        for field, changed in [("state", "failure"), ("description", "Old result"),
                               ("target_url", f"https://github.com/{SLUG}/actions/runs/11")]:
            posted, wakes, paths, error = self.execute("schedule", [pull()], existing={**current, field: changed})
            observed.append((len(posted), len(wakes), error))

        # Assert
        self.assertEqual(observed, [(1, 1, None)] * 3)

    def test_Given_AFailedCampaignBesideAnOldGreenDisplay_When_Reconciled_Then_FailureIsPublishedWithoutAMergeWake(self):
        # Arrange
        old = status.reading(pull(), [{**run(), "status": "completed", "conclusion": "success"}], SLUG)

        # Act
        posted, wakes, paths, error = self.execute("schedule", [pull()], verdict="failure", existing=old)

        # Assert
        self.assertEqual(([p["state"] for p in posted], wakes, error), (["failure"], [], None))

    def test_Given_AForkBesideACurrentRepositoryHead_When_Reconciled_Then_OnlyTheCurrentRepositoryReceivesWrites(self):
        # Arrange
        foreign = pull()
        foreign["number"], foreign["head"]["repo"]["full_name"] = 8, "fork/repo"

        # Act
        posted, wakes, paths, error = self.execute("schedule", [foreign, pull()])

        # Assert
        self.assertEqual(([p["sha"] for p in posted], [w["number"] for w in wakes],
                          f"repos/{SLUG}/pulls/8" in paths, error), ([SHA], [7], False, None))

    def test_Given_AFullOrMalformedOpenListing_When_Reconciled_Then_NoPartialHeadIsPublished(self):
        # Arrange
        malformed = {**pull(), "number": "7"}
        observed = []

        # Act
        for listed, reason in [([pull()] * 100, "listing is incomplete"),
                               ([pull(), malformed], "listing is malformed")]:
            posted, wakes, paths, error = self.execute("schedule", listed)
            observed.append((posted, wakes, reason in (error or "")))

        # Assert
        self.assertEqual(observed, [([], [], True), ([], [], True)])

    def test_Given_MoreThanEightEligiblePulls_When_Reconciled_Then_TheBudgetRefusesBeforeAnyHeadIsReadOrWritten(self):
        # Arrange
        listed = [{**pull(), "number": n} for n in range(1, 10)]

        # Act
        posted, wakes, paths, error = self.execute("schedule", listed)

        # Assert
        self.assertEqual((posted, wakes, any("/pulls/" in p for p in paths),
                          "API budget" in (error or "")), ([], [], False, True))

    def test_Given_EightEligiblePulls_When_Reconciled_Then_TheCompleteBoundaryFitsTheReadAndWriteBudget(self):
        # Arrange
        listed = [{**pull(), "number": n} for n in range(1, 9)]

        # Act
        posted, wakes, paths, error = self.execute("schedule", listed)

        # Assert
        self.assertEqual((len(posted), len(wakes), len(paths) + len(posted) <= 50, error),
                         (8, 8, True, None))

    def test_Given_ASelectedManualRecoveryAboveSweepCapacity_When_Dispatched_Then_OnlyTheSelectedHeadUsesTheSamePublisher(self):
        # Arrange
        listed = [{**pull(), "number": n} for n in range(1, 10)]

        # Act
        posted, wakes, paths, error = self.execute("workflow_dispatch", listed, selected="7")

        # Assert
        self.assertEqual(([p["sha"] for p in posted], [w["number"] for w in wakes],
                          any("pulls?" in p for p in paths), error), ([SHA], [7], False, None))

    def test_Given_ManualRecoveryWithoutASelection_When_Dispatched_Then_ItReconcilesTheCompleteListing(self):
        # Arrange
        second = {**pull(), "number": 8}

        # Act
        posted, wakes, paths, error = self.execute("workflow_dispatch", [pull(), second])

        # Assert
        self.assertEqual((len(posted), [w["number"] for w in wakes], error), (2, [7, 8], None))

    def test_Given_ForeignOrChangedDefaultBranchMetadata_When_Reconciled_Then_NoHeadIsReadOrPublished(self):
        # Arrange
        observed = []

        # Act
        for metadata in [{"full_name": "fork/repo", "default_branch": "main"},
                         {"full_name": SLUG, "default_branch": "feature"}, {"full_name": SLUG}]:
            posted, wakes, paths, error = self.execute("schedule", [pull()], metadata=metadata)
            observed.append((posted, wakes, any("/pulls" in p for p in paths), bool(error)))

        # Assert
        self.assertEqual(observed, [([], [], False, True)] * 3)

    def test_Given_AnInvalidManualNumber_When_Dispatched_Then_NoHeadIsReadOrPublished(self):
        # Arrange
        observed = []

        # Act
        for selected in ["0", "-7", "7/commits", " 7", 0]:
            posted, wakes, paths, error = self.execute("workflow_dispatch", [pull()], selected=selected)
            observed.append((posted, wakes, any("/pulls" in p for p in paths), bool(error)))

        # Assert
        self.assertEqual(observed, [([], [], False, True)] * 5)

    def test_Given_AnIncompleteOrForeignStatusListing_When_Published_Then_DeduplicationCannotHideItsUnreadState(self):
        # Arrange
        observed = []
        invalid = [{"sha": SHA, "statuses": [], "total_count": 1},
                   {"sha": "b" * 40, "statuses": [], "total_count": 0},
                   {"sha": SHA, "statuses": [{"context": status.CONTEXT}] * 2, "total_count": 2}]

        # Act
        for payload in invalid:
            read = api_read([pull(), {"workflow_runs": [run()]}, payload,
                             pull(), {"workflow_runs": [run()]}])
            posted = []
            with mock.patch.object(status.settle, "repository", return_value=SLUG):
                try:
                    status.publish(Path("."), 7, read=read, post=posted.append)
                    refused = False
                except RuntimeError:
                    refused = True
            observed.append((refused, posted))

        # Assert
        self.assertEqual(observed, [(True, [])] * 3)

    # GREEN_ON_BASE(characterization): recovery keeps the newest live run authoritative over an older green display.
    def test_Given_AnOldGreenDisplay_When_ANewerRunAppearsDuringRecovery_Then_TheLivePendingVerdictReplacesIt(self):
        # Arrange
        old = {**run(), "status": "completed", "conclusion": "success"}
        newer = {**run(), "id": 13, "run_number": 9}
        green = {"context": status.CONTEXT, **status.reading(pull(), [old], SLUG)}
        read = api_read([pull(), {"workflow_runs": [old]}, {"sha": SHA, "statuses": [green], "total_count": 1},
                         pull(), {"workflow_runs": [old, newer]}])
        posted = []

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            status.publish(Path("."), 7, read=read, post=posted.append)

        # Assert
        self.assertEqual([item["state"] for item in posted], ["pending"])

    def test_Given_AMissedLabelRemovalWithoutACampaign_When_Reconciled_Then_OnlyAnExistingOwnedPendingDisplayIsCleared(self):
        # Arrange
        unlabelled = pull()
        unlabelled["labels"] = []
        old = {"context": status.CONTEXT, "state": "pending", "description": "Awaiting mutation campaign",
               "target_url": f"https://github.com/{SLUG}/pull/7"}
        observed = []

        # Act
        for existing in [[], [old]]:
            read = api_read([unlabelled, {"workflow_runs": []},
                             {"sha": SHA, "statuses": existing, "total_count": len(existing)},
                             unlabelled, {"workflow_runs": []}])
            posted = []
            with mock.patch.object(status.settle, "repository", return_value=SLUG):
                result = status.publish(Path("."), 7, read=read, post=posted.append)
            observed.append(([p["state"] for p in posted], result))

        # Assert
        self.assertEqual(observed, [([], None), (["success"], None)])

    # GREEN_ON_BASE(characterization): removing the label does not make a failed campaign green.
    def test_Given_AMissedLabelRemovalAfterAFailedCampaign_When_Reconciled_Then_TheExistingDisplayCannotBecomeGreen(self):
        # Arrange
        unlabelled = pull()
        unlabelled["labels"] = []
        failed = {**run(), "status": "completed", "conclusion": "failure"}
        old = {"context": status.CONTEXT, "state": "pending"}
        read = api_read([unlabelled, {"workflow_runs": [failed]},
                         {"sha": SHA, "statuses": [old], "total_count": 1},
                         unlabelled, {"workflow_runs": [failed]}])
        posted = []

        # Act
        with mock.patch.object(status.settle, "repository", return_value=SLUG):
            result = status.publish(Path("."), 7, read=read, post=posted.append)

        # Assert
        self.assertEqual([p["state"] for p in posted], ["failure"])


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

    def test_Given_TheRecoveryWorkflow_When_Read_Then_ScheduledAndManualJobsSerializeWithEventPublishers(self):
        # Arrange
        text = (Path(__file__).resolve().parents[2] / ".github/workflows/campaign-status.yml").read_text()

        # Act
        values = (bool(re.search(r'schedule:\s*\n\s*- cron: "\*/5 \* \* \* \*"', text)),
                  "workflow_dispatch:" in text, "Optional pull request number" in text,
                  "group: campaign-status-${{ github.repository }}" in text,
                  "cancel-in-progress: false" in text)

        # Assert
        self.assertEqual(values, (True, True, True, True, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

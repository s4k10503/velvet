#!/usr/bin/env python3
"""Unit tests for automerge.py: which completed run, which pull request, and when settle is asked.

settle's own decision is not re-tested here; test_settle.py holds it. What these hold is the part a
workflow adds in front of it, with every reading fabricated.

Run: python3 scripts/pr/test_automerge.py
"""

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

TESTED = "a" * 40
NEWER = "b" * 40


def load_module():
    """Imports automerge by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("automerge",
                                                  Path(__file__).with_name("automerge.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


automerge = load_module()


def completed_run(event="pull_request", conclusion="success", numbers=(7,), head=TESTED):
    return {"event": event, "conclusion": conclusion, "head_sha": head,
            "pull_requests": [{"number": number} for number in numbers]}


def pull(number=7, head=TESTED, labels=("automerge",), state="open"):
    return {"number": number, "state": state, "head": {"sha": head},
            "labels": [{"name": name} for name in labels]}


class Invocation:
    """One run of `main` over fabricated readings: what settle was asked to merge, and the exit code.

    `merged` is the numbers alone and `calls` the whole of each call, so that only the case about the
    call's arguments reddens when they change.
    """

    def __init__(self, pulls, event=None, argv=(), token="a-token", merge_code=0, reading_fails=False):
        self.pulls = pulls
        self.calls = []
        self.printed = io.StringIO()
        self.read = []
        with tempfile.TemporaryDirectory(prefix="automerge-") as directory:
            arguments = list(argv)
            if event is not None:
                path = Path(directory) / "event.json"
                path.write_text(json.dumps(event))
                arguments += ["--event", str(path)]

            def rest_json(path):
                self.read.append(path)
                if reading_fails:
                    raise RuntimeError("gh api failed: HTTP 502")
                if "pulls?state=open" in path:
                    return list(self.pulls.values())
                return self.pulls[int(path.rsplit("/", 1)[1])]

            def merge(project, number, base, dry_run):
                self.calls.append((number, base, dry_run))
                return merge_code

            with contextlib.ExitStack() as stack:
                stack.enter_context(mock.patch.object(automerge.settle, "repository",
                                                      return_value="owner/name"))
                stack.enter_context(mock.patch.object(automerge.settle, "rest_json", rest_json))
                stack.enter_context(mock.patch.object(automerge.settle, "merge", merge))
                stack.enter_context(contextlib.redirect_stdout(self.printed))
                self.code = automerge.main(arguments, {"GH_TOKEN": token} if token else {})
        self.merged = [number for number, _, _ in self.calls]


class CompletedRunTests(unittest.TestCase):
    def test_Given_APushRunOfTheBase_When_ItCompletes_Then_ItStartsNoMerge(self):
        # Arrange
        run = completed_run(event="push", numbers=())

        # Act
        reason = automerge.run_skip_reason(run)

        # Assert
        self.assertEqual(reason, "the run was started by push, not by a pull request")

    def test_Given_APullRequestRunThatFailed_When_ItCompletes_Then_ItStartsNoMerge(self):
        # Arrange
        run = completed_run(conclusion="failure")

        # Act
        reason = automerge.run_skip_reason(run)

        # Assert
        self.assertEqual(reason, "the run concluded failure")

    def test_Given_APullRequestRunThatPassed_When_ItCompletes_Then_NothingAboutTheRunHoldsItBack(self):
        # Act
        reason = automerge.run_skip_reason(completed_run())

        # Assert
        self.assertIsNone(reason)


class PullRequestResolutionTests(unittest.TestCase):
    def test_Given_APayloadNamingItsPullRequests_When_Resolved_Then_ThoseAreTheOnesAsked(self):
        # Arrange — an open pull request on the same head that the payload does not name is not asked.
        run = completed_run(numbers=(9, 4))

        # Act
        numbers = automerge.run_pull_requests(run, [pull(number=5)])

        # Assert
        self.assertEqual(numbers, [4, 9])

    def test_Given_APayloadNamingNone_When_Resolved_Then_TheOpenPullRequestOnThatHeadIsAsked(self):
        # Arrange
        run = completed_run(numbers=())

        # Act
        numbers = automerge.run_pull_requests(run, [pull(number=5, head=NEWER), pull(number=6)])

        # Assert
        self.assertEqual(numbers, [6])

    def test_Given_APayloadNamingNoneAndNoHead_When_Resolved_Then_NoPullRequestIsAsked(self):
        # Arrange — a missing head must not match a pull request whose head also reads as missing.
        run = {"event": "pull_request", "conclusion": "success", "pull_requests": []}

        # Act
        numbers = automerge.run_pull_requests(run, [{"number": 3, "head": {}}])

        # Assert
        self.assertEqual(numbers, [])


class PullRequestSkipTests(unittest.TestCase):
    def test_Given_APullRequestWithoutTheLabel_When_Decided_Then_ItIsLeftAlone(self):
        # Act
        reason = automerge.pull_skip_reason(pull(labels=("bug",)), TESTED)

        # Assert
        self.assertEqual(reason, "it does not carry the automerge label")

    def test_Given_AClosedPullRequest_When_Decided_Then_ItIsLeftAlone(self):
        # Act
        reason = automerge.pull_skip_reason(pull(state="closed"), TESTED)

        # Assert
        self.assertEqual(reason, "it is not open")

    def test_Given_AHeadThatMovedAfterTheRun_When_Decided_Then_ItIsLeftToTheNewerHeadsRuns(self):
        # Act
        reason = automerge.pull_skip_reason(pull(head=NEWER), TESTED)

        # Assert
        self.assertEqual(reason, "its head is bbbbbbb, not the aaaaaaa this run tested: the newer "
                                 "head's own runs ask again when they complete")

    def test_Given_ALabelledOpenPullRequestOnTheTestedHead_When_Decided_Then_NothingHoldsItBack(self):
        # Act
        reason = automerge.pull_skip_reason(pull(), TESTED)

        # Assert
        self.assertIsNone(reason)

    def test_Given_ADispatchNamingNoTestedHead_When_Decided_Then_TheCurrentHeadIsNotComparedToOne(self):
        # Act
        reason = automerge.pull_skip_reason(pull(head=NEWER), None)

        # Assert
        self.assertIsNone(reason)


class MergeInvocationTests(unittest.TestCase):
    def test_Given_NoToken_When_Run_Then_NothingIsReadOrMerged(self):
        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run()}, token="")

        # Assert
        self.assertEqual((ran.code, ran.read, ran.merged), (0, [], []))

    def test_Given_ACompletedRunForALabelledPullRequest_When_Run_Then_SettleMergesItForReal(self):
        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run()})

        # Assert — its own base, and not a dry run.
        self.assertEqual(ran.calls, [(7, None, False)])

    def test_Given_ACompletedRunForAnUnlabelledPullRequest_When_Run_Then_SettleIsNotAsked(self):
        # Act
        ran = Invocation({7: pull(labels=())}, event={"workflow_run": completed_run()})

        # Assert
        self.assertEqual((ran.code, ran.merged), (0, []))

    def test_Given_AFailedRun_When_Run_Then_NoPullRequestIsRead(self):
        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run(conclusion="failure")})

        # Assert
        self.assertEqual((ran.code, ran.read), (0, []))

    def test_Given_APayloadNamingNone_When_Run_Then_TheOpenPullRequestOnTheTestedHeadIsMerged(self):
        # Act
        ran = Invocation({5: pull(number=5, head=NEWER), 6: pull(number=6)},
                         event={"workflow_run": completed_run(numbers=())})

        # Assert
        self.assertEqual(ran.merged, [6])

    def test_Given_SettleRefuses_When_Run_Then_TheRunStillSucceeds(self):
        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run()}, merge_code=1)

        # Assert — the refusal reached settle and is not the job failing.
        self.assertEqual((ran.merged, ran.code), ([7], 0))

    def test_Given_AReadingThatFails_When_Run_Then_TheRunFails(self):
        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run()}, reading_fails=True)

        # Assert
        self.assertEqual(ran.code, 1)

    def test_Given_ADispatchWithEmptyInputs_When_Run_Then_TheEventIsRead(self):
        # Arrange — the workflow passes both inputs whether or not they were given.
        argv = ["--number", "", "--after-run", ""]

        # Act
        ran = Invocation({7: pull()}, event={"workflow_run": completed_run()}, argv=argv)

        # Assert
        self.assertEqual(ran.merged, [7])

    def test_Given_ADispatchNamingAPullRequest_When_Run_Then_ItIsMergedWhateverItsHead(self):
        # Act
        ran = Invocation({7: pull(head=NEWER)}, argv=["--number", "7", "--after-run", ""])

        # Assert
        self.assertEqual(ran.merged, [7])


class AfterRunTests(unittest.TestCase):
    def test_Given_ARunThatCompletesOnTheThirdReading_When_Waited_Then_ItReportsCompletion(self):
        # Arrange
        statuses = iter(["queued", "in_progress", "completed"])

        # Act
        done = automerge.wait_for_run(Path("."), 1, read=lambda: next(statuses),
                                      clock=lambda: 0, sleep=lambda seconds: None)

        # Assert
        self.assertTrue(done)

    def test_Given_ARunThatNeverCompletes_When_TheBoundPasses_Then_ItReportsThatItDidNot(self):
        # Arrange — each sleep advances the clock by the poll interval.
        now = [0]

        def sleep(seconds):
            now[0] += seconds

        # Act
        done = automerge.wait_for_run(Path("."), 1, read=lambda: "in_progress",
                                      clock=lambda: now[0], sleep=sleep, timeout=30)

        # Assert
        self.assertFalse(done)

    def test_Given_AHandOffRunThatNeverCompletes_When_Dispatched_Then_NothingIsMerged(self):
        # Arrange
        never = mock.patch.object(automerge, "wait_for_run", return_value=False)

        # Act
        with never:
            ran = Invocation({7: pull()}, argv=["--number", "7", "--after-run", "42"])

        # Assert
        self.assertEqual((ran.code, ran.merged), (1, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)

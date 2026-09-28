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
import re
import tempfile
import unittest
from pathlib import Path
from unittest import mock

TESTED = "a" * 40
NEWER = "b" * 40

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO_ROOT / ".github" / "workflows"


def load_module():
    """Imports automerge by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("automerge",
                                                  Path(__file__).with_name("automerge.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


automerge = load_module()


def completed_run(event="pull_request", conclusion="success", numbers=(7,), head=TESTED,
                  name="Test", branch="topic"):
    return {"name": name, "event": event, "conclusion": conclusion, "head_sha": head,
            "head_branch": branch, "pull_requests": [{"number": number} for number in numbers]}


def base_push(head=NEWER, name="Test"):
    """A push run on the default branch, which tested no pull request."""
    return completed_run(event="push", numbers=(), head=head, name=name, branch="main")


def completed(run):
    """The workflow_run event GitHub hands the job, for a repository whose default branch is main."""
    return {"workflow_run": run, "repository": {"default_branch": "main"}}


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


class RunSelectionTests(unittest.TestCase):
    def select(self, run):
        return automerge.run_selection(run, "main")

    def test_Given_APullRequestRunThatPassed_When_Selected_Then_ItAsksAboutThePullRequestsItTested(self):
        # Act
        selection = self.select(completed_run())

        # Assert
        self.assertEqual(selection, (automerge.TESTED_PULLS, None))

    def test_Given_APullRequestRunThatFailed_When_Selected_Then_ItAsksAboutNothing(self):
        # Act
        selection = self.select(completed_run(conclusion="failure"))

        # Assert
        self.assertEqual(selection, (None, "the run concluded failure"))

    def test_Given_APullRequestRunOfAWorkflowNotSubscribedTo_When_Selected_Then_ItAsksAboutNothing(self):
        # Act
        selection = self.select(completed_run(name="Docs"))

        # Assert
        self.assertEqual(selection, (None, "a pull_request run of Docs on topic clears no refusal"))

    def test_Given_APushRunOfTheDefaultBranchThatPassed_When_Selected_Then_ItSweeps(self):
        # Act
        selection = self.select(base_push())

        # Assert
        self.assertEqual(selection, (automerge.SWEEP, None))

    def test_Given_APushRunOfAnotherBranch_When_Selected_Then_ItAsksAboutNothing(self):
        # Act
        selection = self.select(completed_run(event="push", numbers=(), branch="2.x"))

        # Assert
        self.assertEqual(selection, (None, "a push run of Test on 2.x clears no refusal"))

    def test_Given_AReleaseDispatchOfThePublishWorkflowThatPassed_When_Selected_Then_ItSweeps(self):
        # Act
        selection = self.select(completed_run(event="workflow_dispatch", numbers=(), name="UPM",
                                              branch="main"))

        # Assert
        self.assertEqual(selection, (automerge.SWEEP, None))

    def test_Given_APushRunOfThePublishWorkflow_When_Selected_Then_ItAsksAboutNothing(self):
        # Act — a push run splits the mirror and tags no release.
        selection = self.select(base_push(name="UPM"))

        # Assert
        self.assertEqual(selection, (None, "a push run of UPM on main clears no refusal"))


class WorkflowNameTests(unittest.TestCase):
    """The names automerge.py selects on against the ones automerge.yml subscribes to and tests."""

    def automerge_yml(self):
        return (WORKFLOWS / "automerge.yml").read_text(encoding="utf-8")

    def subscribed(self):
        listed = re.search(r"^\s*workflows:\s*\[(.*)\]\s*$", self.automerge_yml(), re.MULTILINE)
        return sorted(name.strip() for name in listed.group(1).split(","))

    def test_Given_TheSubscription_When_Read_Then_ItNamesTheWorkflowsSelectedOn(self):
        # Act
        subscribed = self.subscribed()

        # Assert
        self.assertEqual(subscribed,
                         sorted(automerge.CHECK_WORKFLOWS | {automerge.PUBLISH_WORKFLOW}))

    def test_Given_TheSubscription_When_Read_Then_EachNameIsOneAWorkflowDeclares(self):
        # Arrange
        declared = set()
        for path in WORKFLOWS.glob("*.y*ml"):
            declared.update(re.findall(r"^name:\s*(.+?)\s*$", path.read_text(encoding="utf-8"),
                                       re.MULTILINE))

        # Act
        undeclared = [name for name in self.subscribed() if name not in declared]

        # Assert
        self.assertEqual(undeclared, [])

    def test_Given_TheJobConditions_When_Read_Then_TheyNameTheWorkflowsSelectedOn(self):
        # Arrange — the merge job and the sweep each list the check workflows, and the sweep names
        # the publish workflow.
        text = self.automerge_yml()

        # Act
        named = ([sorted(json.loads(listed)) for listed in re.findall(r"fromJSON\('(\[.*?\])'\)", text)],
                 re.findall(r"workflow_run\.name == '([^']+)'", text))

        # Assert
        self.assertEqual(named, ([sorted(automerge.CHECK_WORKFLOWS)] * 2, [automerge.PUBLISH_WORKFLOW]))


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
        run = {"name": "Test", "event": "pull_request", "conclusion": "success", "pull_requests": []}

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
        ran = Invocation({7: pull()}, event=completed(completed_run()), token="")

        # Assert
        self.assertEqual((ran.code, ran.read, ran.merged), (0, [], []))

    def test_Given_ACompletedRunForALabelledPullRequest_When_Run_Then_SettleMergesItForReal(self):
        # Act
        ran = Invocation({7: pull()}, event=completed(completed_run()))

        # Assert — its own base, and not a dry run.
        self.assertEqual(ran.calls, [(7, None, False)])

    def test_Given_ACompletedRunForAnUnlabelledPullRequest_When_Run_Then_SettleIsNotAsked(self):
        # Act
        ran = Invocation({7: pull(labels=())}, event=completed(completed_run()))

        # Assert
        self.assertEqual((ran.code, ran.merged), (0, []))

    def test_Given_AFailedRun_When_Run_Then_NoPullRequestIsRead(self):
        # Act
        ran = Invocation({7: pull()}, event=completed(completed_run(conclusion="failure")))

        # Assert
        self.assertEqual((ran.code, ran.read), (0, []))

    def test_Given_APayloadNamingNone_When_Run_Then_TheOpenPullRequestOnTheTestedHeadIsMerged(self):
        # Act
        ran = Invocation({5: pull(number=5, head=NEWER), 6: pull(number=6)},
                         event=completed(completed_run(numbers=())))

        # Assert
        self.assertEqual(ran.merged, [6])

    def test_Given_SettleRefuses_When_Run_Then_TheRunStillSucceeds(self):
        # Act
        ran = Invocation({7: pull()}, event=completed(completed_run()), merge_code=1)

        # Assert — the refusal reached settle and is not the job failing.
        self.assertEqual((ran.merged, ran.code), ([7], 0))

    def test_Given_AReadingThatFails_When_Run_Then_TheRunFails(self):
        # Act
        ran = Invocation({7: pull()}, event=completed(completed_run()), reading_fails=True)

        # Assert
        self.assertEqual(ran.code, 1)

    def test_Given_ADispatchWithEmptyInputs_When_Run_Then_TheEventIsRead(self):
        # Arrange — the workflow passes both inputs whether or not they were given.
        argv = ["--number", "", "--after-run", ""]

        # Act
        ran = Invocation({7: pull()}, event=completed(completed_run()), argv=argv)

        # Assert
        self.assertEqual(ran.merged, [7])

    def test_Given_ADispatchNamingAPullRequest_When_Run_Then_ItIsMergedWhateverItsHead(self):
        # Act
        ran = Invocation({7: pull(head=NEWER)}, argv=["--number", "7", "--after-run", ""])

        # Assert
        self.assertEqual(ran.merged, [7])


class SweepTests(unittest.TestCase):
    def test_Given_ABasePushThatPassed_When_Run_Then_EachLabelledOpenPullRequestIsReadAndMerged(self):
        # Arrange — the pull requests' heads are the run's own, so no head comparison can decline one.
        pulls = {3: pull(number=3, head=NEWER), 4: pull(number=4, head=NEWER, labels=()),
                 5: pull(number=5, head=NEWER)}

        # Act
        ran = Invocation(pulls, event=completed(base_push()))

        # Assert — the unlabelled one is not even read, which is the listing's filter rather than
        # settle_one's.
        self.assertEqual((ran.merged, ran.read[1:]),
                         ([3, 5], ["repos/owner/name/pulls/3", "repos/owner/name/pulls/5"]))

    def test_Given_ABasePush_When_Run_Then_APullRequestIsNotHeldToTheBasesHead(self):
        # Arrange — a push run's head is the default branch's commit, which no pull request's head is.
        pulls = {3: pull(number=3, head=TESTED)}

        # Act
        ran = Invocation(pulls, event=completed(base_push(head=NEWER)))

        # Assert
        self.assertEqual(ran.merged, [3])

    def test_Given_AReleaseDispatchThatPassed_When_Run_Then_TheLabelledPullRequestIsMerged(self):
        # Arrange
        run = completed_run(event="workflow_dispatch", numbers=(), name="UPM", branch="main")

        # Act
        ran = Invocation({3: pull(number=3)}, event=completed(run))

        # Assert
        self.assertEqual(ran.merged, [3])


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

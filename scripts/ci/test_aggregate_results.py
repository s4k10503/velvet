#!/usr/bin/env python3
"""Unit tests for aggregate_results.py: when an aggregate passes, fails, or cancels its own run.

Run: python3 scripts/ci/test_aggregate_results.py
"""

import contextlib
import importlib.util
import io
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
WORKFLOWS = REPO_ROOT / ".github/workflows"


def load_module():
    spec = importlib.util.spec_from_file_location("aggregate_results",
                                                  Path(__file__).with_name("aggregate_results.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


aggregate_results = load_module()

OLD, NEW = "a" * 40, "b" * 40


class Api:
    """A stand-in for `gh`: the head each path reads back, and the runs asked to cancel."""

    def __init__(self, current=NEW, cancels=True):
        self.current, self.cancels, self.read, self.cancelled = current, cancels, [], []

    def read_head(self, path, jq):
        self.read.append((path, jq))
        return self.current

    def cancel(self, repository, run_id):
        self.cancelled.append((repository, run_id))
        return self.cancels


def environment(results, event="pull_request", number="7", ref="refs/pull/7/merge", head=OLD):
    return {"RESULTS": results, "GITHUB_EVENT_NAME": event, "NUMBER": number, "GITHUB_REF": ref,
            "HEAD": head, "GITHUB_REPOSITORY": "owner/name", "GITHUB_RUN_ID": "41"}


def run(environ, api):
    printed = io.StringIO()
    with contextlib.redirect_stdout(printed), contextlib.redirect_stderr(io.StringIO()):
        code = aggregate_results.main(environ, api.read_head, api.cancel, sleep=lambda seconds: None)
    return code, printed.getvalue()


class VerdictTests(unittest.TestCase):
    def test_Given_OnlySuccessAndSkipped_When_Decided_Then_ItPassesWithoutReadingTheHead(self):
        # Arrange
        api = Api()

        # Act
        code, _ = run(environment("success skipped success"), api)

        # Assert
        self.assertEqual((code, api.read, api.cancelled), (0, [], []))

    def test_Given_AFailureBesideACancellation_When_TheHeadMoved_Then_ItFailsWithoutCancelling(self):
        # Arrange — a failure is a verdict about the old head, and the run that reports it is not
        # one nobody reads: the head moving does not turn it into a cancellation.
        api = Api(current=NEW)

        # Act
        code, _ = run(environment("success failure cancelled"), api)

        # Assert
        self.assertEqual((code, api.cancelled), (1, []))

    def test_Given_OnlyCancellationsOnAPullRequest_When_ItsHeadMoved_Then_TheRunCancelsItself(self):
        # Arrange
        api = Api(current=NEW)

        # Act
        code, printed = run(environment("success cancelled cancelled"), api)

        # Assert — still non-zero: the job ends cancelled because the run is, and where the
        # cancellation never lands the step must not read as a pass.
        self.assertEqual((code, api.read, api.cancelled, "dependency finished as 'cancelled'" in printed),
                         (1, [("repos/owner/name/pulls/7", ".head.sha")], [("owner/name", "41")], True))

    def test_Given_OnlyCancellationsOnAPullRequest_When_ItsHeadIsStillTheRunsHead_Then_ItFails(self):
        # Arrange — a run somebody stopped on the head a merge would take.
        api = Api(current=OLD)

        # Act
        code, _ = run(environment("cancelled success"), api)

        # Assert
        self.assertEqual((code, api.cancelled), (1, []))

    def test_Given_OnlyCancellations_When_TheHeadCannotBeRead_Then_ItFailsWithoutCancelling(self):
        # Arrange
        api = Api(current=None)

        # Act
        code, _ = run(environment("cancelled"), api)

        # Assert
        self.assertEqual((code, api.cancelled), (1, []))

    def test_Given_OnlyCancellations_When_TheRunsHeadWasNotHandedOver_Then_ItFailsWithoutCancelling(self):
        # Arrange — an empty HEAD differs from every head read back.
        api = Api(current=NEW)

        # Act
        code, _ = run(environment("cancelled", head=""), api)

        # Assert
        self.assertEqual((code, api.cancelled), (1, []))

    def test_Given_OnlyCancellationsOnPushOrMergeGroup_When_Decided_Then_TheHeadIsNotAskedAndItFails(self):
        # Arrange — a merge-group entry is the commit the queue merges and a push run is main's own
        # verdict, so a cancelled one fails whatever its branch points at now.
        events = ("push", "merge_group")

        # Act
        outcomes = {}
        for event in events:
            api = Api(current=NEW)
            code, _ = run(environment("cancelled", event=event, number="", ref="refs/heads/main"), api)
            outcomes[event] = (code, api.read, api.cancelled)

        # Assert
        self.assertEqual(outcomes, {event: (1, [], []) for event in events})

    def test_Given_ADispatchWithNoPullRequest_When_ItsBranchMoved_Then_TheBranchIsReadAndTheRunCancelled(self):
        # Arrange
        api = Api(current=NEW)

        # Act
        run(environment("cancelled", event="workflow_dispatch", number="", ref="refs/heads/fix/a b"), api)

        # Assert
        self.assertEqual((api.read, api.cancelled),
                         ([("repos/owner/name/git/ref/heads/fix/a%20b", ".object.sha")], [("owner/name", "41")]))

    def test_Given_ADispatchWithAPullRequest_When_Decided_Then_ThePullRequestsHeadIsRead(self):
        # Arrange
        api = Api(current=OLD)

        # Act
        run(environment("cancelled", event="workflow_dispatch", number="12", ref="refs/heads/topic"), api)

        # Assert
        self.assertEqual(api.read, [("repos/owner/name/pulls/12", ".head.sha")])

    def test_Given_ADispatchOntoATag_When_Decided_Then_ItsHeadIsNotAsked(self):
        # Arrange — an annotated tag's ref reads back the tag object rather than the commit the run
        # measured, so asking would read every such run as superseded.
        api = Api(current=NEW)

        # Act
        run(environment("cancelled", event="workflow_dispatch", number="", ref="refs/tags/v1.0.0"), api)

        # Assert
        self.assertEqual(api.read, [])

    def test_Given_NoResults_When_Decided_Then_ItFails(self):
        # Arrange — an expression that went empty would otherwise pass over nothing.
        api = Api()

        # Act
        code, _ = run(environment(""), api)

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ASupersededRun_When_ItsCancellationIsRefused_Then_ItFails(self):
        # Arrange — the cancellation is refused, as it is for a token without actions: write.
        api = Api(current=NEW, cancels=False)

        # Act
        code, printed = run(environment("cancelled"), api)

        # Assert
        self.assertEqual((code, "could not cancel itself" in printed), (1, True))


def jobs(text):
    body = text.partition("\njobs:\n")[2]
    keys = re.findall(r"^  ([A-Za-z_][A-Za-z0-9_-]*):\s*(?:#.*)?$", body, re.MULTILINE)
    blocks = re.split(r"^  [A-Za-z_][A-Za-z0-9_-]*:\s*(?:#.*)?$", body, flags=re.MULTILINE)[1:]
    return dict(zip(keys, blocks))


def aggregates():
    """(workflow, job key, block) for each required-checks aggregate and the campaign's."""
    found = []
    for path in sorted(WORKFLOWS.glob("*.yml")):
        for key, block in jobs(path.read_text()).items():
            if re.search(r"^    name: Required checks \(", block, re.MULTILINE) or (
                    path.name == "mutation.yml" and key == "campaign"):
                found.append((path.name, key, block))
    return found


class WorkflowTests(unittest.TestCase):
    def test_Given_EveryAggregate_When_ItsStepIsRead_Then_ItDecidesThroughTheScriptWithWhatItReads(self):
        # Arrange — an aggregate left on the inline loop fails a superseded run, and one missing a
        # permission or a variable fails it too, with nothing else reporting the difference.
        # Each is matched as a whole line, so a comment naming a permission does not stand in for it.
        wanted = (r"        run: python3 scripts/ci/aggregate_results\.py", r"          RESULTS: \$\{\{ needs\..*",
                  r"          NUMBER: \$\{\{ .+ \}\}", r"          GH_TOKEN: \$\{\{ github\.token \}\}",
                  r"      actions: write", r"      pull-requests: read", r"      contents: read",
                  r"          sparse-checkout: scripts/ci")
        found = aggregates()

        # Act
        missing = ["{}:{}: {}".format(workflow, key, line) for workflow, key, block in found
                   for line in wanted if not re.search("^" + line + "$", block, re.MULTILINE)]

        # Assert
        self.assertEqual((sorted((workflow, key) for workflow, key, _ in found), missing),
                         ([("generators.yml", "required-checks"), ("mutation.yml", "campaign"),
                           ("test.yml", "required-checks")], []))

    def test_Given_AnAggregateOnAPullRequest_When_ItsHeadIsRead_Then_ItIsThePullRequestsHeadNotTheMergeCommit(self):
        # Arrange — github.sha on a pull request is the test merge rather than the pull request's
        # head, so a cancellation there would read as superseded.
        found = [(workflow, block) for workflow, _, block in aggregates() if workflow != "mutation.yml"]

        # Act
        heads = sorted((workflow, re.findall(r"^\s*HEAD: (.*)$", block, re.MULTILINE)) for workflow, block in found)

        # Assert
        self.assertEqual(heads, [(workflow, ["${{ github.event.pull_request.head.sha || github.sha }}"])
                                 for workflow in ("generators.yml", "test.yml")])


if __name__ == "__main__":
    unittest.main()

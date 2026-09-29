#!/usr/bin/env python3
"""Unit tests for settle.py's merge decision.

The decision is separated from the readings precisely so these run without a network, since a guard
exercised only against live pull requests is exercised only in the states those happen to be in.

Run: python3 scripts/pr/test_settle.py
"""

import collections
import contextlib
import importlib.util
import io
import os
import re
import subprocess
import sys
import tempfile
import time
import types
import unittest
from pathlib import Path
from unittest import mock

GREEN = "a" * 40
MOVED = "b" * 40
BROKE = "c" * 40


def load_module():
    """Imports settle by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("settle", Path(__file__).with_name("settle.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


settle = load_module()


def reasons(before=GREEN, after=GREEN, results=None, branch="topic", base="main",
            held_by_worktree=False, unpublished_release=None, draft=False, merge_state="clean",
            fork=False, failing_runs=(), behind_release=None, long_lived_head=False, runs_unfinished=(),
            runs_failed=(), required=()):
    if results is None:
        results = [{"name": "Required checks (Unity)", "bucket": "pass"}]
    return settle.reasons_from(before, after, results, branch, base,
                               held_by_worktree=held_by_worktree,
                               unpublished_release=unpublished_release, draft=draft,
                               merge_state=merge_state, fork=fork, failing_runs=failing_runs,
                               behind_release=behind_release, long_lived_head=long_lived_head,
                               runs_unfinished=runs_unfinished, runs_failed=runs_failed,
                               required=required)


def failing(workflow="test.yml", sha=BROKE):
    return settle.red_base.Failing(workflow, sha)


RED_REASON = ("origin/main's last test.yml push run failed at ccccccc: fix or revert it on main "
              "first. A head is exempt only where it contains that commit and its own Unity tests "
              "ran and passed")

RELEASED = "d" * 40

RELEASE_REASON = ("does not contain ddddddd, which dated 2.1.0 on main: take main in with "
                  "`settle.py update`, so its checks run again over the section that commit closed")


class MergeDecisionTests(unittest.TestCase):
    def test_Given_EveryCheckPassedAndNothingElseBlocks_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertEqual(reasons(), [])

    def test_Given_TheHeadMovedWhileChecksWereRead_When_Decided_Then_NothingElseIsReported(self):
        # Arrange — the readings straddle a force-push, so they are not about one commit.
        results = [{"name": "Unity", "bucket": "fail"}]

        # Act
        decided = reasons(after=MOVED, results=results, failing_runs=[failing()], held_by_worktree=True)

        # Assert
        self.assertEqual(len(decided), 1)

    def test_Given_TheBaseHoldsAnUnpublishedRelease_When_EverythingElsePasses_Then_ItStillBlocks(self):
        # Arrange — a non-empty reason from the release guard, with every other input clean.
        unpublished = "v2.0.1 was never published"

        # Act
        decided = reasons(unpublished_release=unpublished)

        # Assert
        self.assertEqual(decided, [unpublished])

    def test_Given_TheHeadMovedAndTheBaseIsUnpublished_When_Decided_Then_BothAreReported(self):
        # Arrange — the force-push voids the readings about the head, not the one about the base.
        unpublished = "v2.0.1 was never published"

        # Act
        decided = reasons(after=MOVED, unpublished_release=unpublished)

        # Assert
        self.assertEqual(len(decided), 2)

    def test_Given_NoCheckEverRan_When_Decided_Then_ThatIsNotReadAsPending(self):
        # Arrange — no bucket at all, which the module docstring's fourth precondition reads as a
        # workflow never triggered rather than as one still to come.
        decided = reasons(results=[])

        # Act / Assert
        self.assertTrue(any("never triggered" in reason for reason in decided))

    def test_Given_ACheckStillRunning_When_Decided_Then_ItIsNamed(self):
        # Arrange
        results = [{"name": "Unity tests (PlayMode)", "bucket": "pending"},
                   {"name": "Release notes", "bucket": "pass"}]

        # Act
        decided = reasons(results=results)

        # Assert
        self.assertEqual(decided, ["still pending at aaaaaaa: Unity tests (PlayMode)"])

    def test_Given_ACancelledCheck_When_Decided_Then_ItBlocksRatherThanCountingAsPassed(self):
        # Arrange — a superseded run and a run somebody stopped both arrive as cancel.
        results = [{"name": "Unity tests (EditMode)", "bucket": "cancel"}]

        # Act
        decided = reasons(results=results)

        # Assert
        self.assertEqual(decided, ["failing at aaaaaaa: Unity tests (EditMode)=cancel"])

    def test_Given_ASkippedCheck_When_Decided_Then_ItPasses(self):
        # Arrange — the Unity jobs skip wholesale without a licence, which is what lets a fork merge.
        results = [{"name": "Unity tests (EditMode)", "bucket": "skipping"}]

        # Act / Assert
        self.assertEqual(reasons(results=results), [])

    def test_Given_ABaseWhoseLastPushRunFailed_When_Decided_Then_ItBlocksThoughEveryCheckPassed(self):
        # Arrange — a failing run the head does not contain, with every other input clean.
        decided = reasons(failing_runs=[failing()])

        # Act / Assert
        self.assertEqual(decided, [RED_REASON])

    def test_Given_AWorktreeHoldingTheBranch_When_Decided_Then_ItBlocksBeforeTheMergeHappens(self):
        # Arrange — the local delete would otherwise fail once the merge had already happened.
        decided = reasons(held_by_worktree=True)

        # Act / Assert
        self.assertTrue(any("worktree holds topic" in reason for reason in decided))

    def test_Given_SeveralIndependentProblems_When_Decided_Then_EachIsReported(self):
        # Arrange — reporting one at a time costs a round of CI per reason.
        results = [{"name": "Unity", "bucket": "pending"}]

        # Act
        decided = reasons(results=results, failing_runs=[failing()], held_by_worktree=True)

        # Assert
        self.assertEqual(len(decided), 3)

    def test_Given_ADraftWhoseChecksAllPassed_When_Decided_Then_ItBlocks(self):
        # Arrange — a deliberate hold reads green, and green was the whole of the readiness question.
        decided = reasons(draft=True)

        # Act / Assert
        self.assertEqual(decided, ["it is a draft: mark it ready for review first"])

    def test_Given_ADraftWhoseHeadMoved_When_Decided_Then_BothAreReported(self):
        # Arrange — a push voids what was read about the head; it does not take a pull request out of
        # draft, so that reason survives the early return the way the publication one does.
        decided = reasons(draft=True, after=MOVED)

        # Act / Assert
        self.assertEqual(len(decided), 2)

    def test_Given_APullRequestConflictingWithTheBase_When_Decided_Then_TheConflictIsNamed(self):
        # Arrange — nothing else blocks, so the conflict is the only thing that can.
        decided = reasons(merge_state="dirty")

        # Act / Assert
        self.assertEqual(decided, [
            "it conflicts with main: resolve the conflict in the branch, which `settle.py update` "
            "declines to do"])

    def test_Given_AHeadOnAFork_When_Decided_Then_ItBlocks(self):
        # Act
        decided = reasons(fork=True)

        # Assert
        self.assertEqual(decided, ["its head is on another repository: this settles branches on origin"])

    def test_Given_ADirtyStateAndAnUnknownOne_When_Decided_Then_OnlyTheDirtyOneBlocks(self):
        # Arrange — posed beside `unknown` to keep the absence of a reading from becoming a reason.
        counted = (len(reasons(merge_state="dirty")), len(reasons(merge_state="unknown")))

        # Act / Assert
        self.assertEqual(counted, (1, 0))


# One pull request's whole state, so `watch` and `merge` can be posed the same table. `holds` is the
# base commits the head contains, which is what the red-base exemption asks of it.
Fabricated = collections.namedtuple(
    "Fabricated", "sha after branch base draft merge_state results holds held fork runs state jobs")

PASSING = [{"name": "Required checks (Unity)", "bucket": "pass"}]

# The suites ran as well as the aggregate passing, which is what the red-base exemption asks.
SUITES_RAN = PASSING + [{"name": "Unity tests (EditMode)", "bucket": "pass"},
                        {"name": "Unity tests (PlayMode)", "bucket": "pass"}]
SUITES_SKIPPED = PASSING + [{"name": "Unity tests (EditMode)", "bucket": "skipping"},
                            {"name": "Unity tests (PlayMode)", "bucket": "skipping"}]

# The head that was called mergeable: `Source generators` had finished green, and `Test` was pending,
# so none of its jobs had put a check on the head.
GENERATOR_CHECKS = [{"name": "Required checks (generators)", "bucket": "pass"},
                    {"name": "Source generators (dotnet)", "bucket": "pass"}]
GENERATORS_DONE = {"id": 36512896257, "name": "Source generators", "workflow_id": 2,
                   "run_number": 90, "status": "completed", "conclusion": "success"}
TEST_PENDING = {"id": 36512896182, "name": "Test", "workflow_id": 1, "run_number": 70,
                "status": "pending", "conclusion": None}
TEST_PENDING_EXIT = "Test (pending, run 36512896182: `gh run cancel 36512896182` if it is stuck)"

# A run GitHub left `queued` after every job of its latest attempt had completed. Its `updated_at` is
# left out, so the jobs are all that separate it from a run still going.
STUCK = {"id": 31128456870, "name": "Source generators", "workflow_id": 2, "run_number": 80,
         "run_attempt": 1, "status": "queued", "conclusion": None}
COMPLETED_JOBS = [{"name": "Source generators (dotnet)", "status": "completed"},
                  {"name": "Required checks (generators)", "status": "completed"}]

# The base whose required workflows last failed in the tables below, at `BROKE`. No case poses it
# for anything else, so a case posing another base reads it green.
RED = "1.x"

# The base whose newest release commit is `RELEASED`, dating 2.1.0. No case poses it for anything else.
RELEASING = "3.x"

# What every base in the tables below requires, unless a case hands `fabricated_readings` another
# answer for it.
REQUIRED = ("Required checks (Unity)",)


def fabricate(number, results=PASSING, draft=False, merge_state="clean", holds=(),
              held=False, moved=False, fork=False, base="main", branch=None, runs=(), state="open",
              jobs=None):
    sha = str(number).rjust(40, "0")
    return Fabricated(sha=sha, after=MOVED if moved else sha, branch=branch or f"topic-{number}",
                      base=base,
                      draft=draft, merge_state=merge_state, results=results, holds=holds,
                      held=held, fork=fork, runs=runs, state=state, jobs=jobs or {})


def base_state(held, base, red=(RED,), releasing=(RELEASING,), required=None):
    """What `project_state` answers for one base, with each base in `red` failing at `BROKE`, each
    in `releasing` last released at `RELEASED`, and each requiring what `required` maps it to, or
    `REQUIRED`.

    The attributes the decision reads rather than settle.ProjectState itself, for the reason
    `fabricated_readings` gives about the pull request.
    """
    return types.SimpleNamespace(held=held, unpublished_release=None,
                                 red=[failing()] if base in red else [],
                                 release=(RELEASED, "2.1.0") if base in releasing else None,
                                 required=(required or {}).get(base, REQUIRED))


@contextlib.contextmanager
def fabricated_readings(states, red=(RED,), releasing=(RELEASING,), required=None):
    """Every reading a poll takes from git or the API, answered from a table of pull request states.

    Patched at the readings rather than at `blocking_reasons`, so the decision itself is what runs:
    stubbing the verdict would make the agreement below true by construction.
    """
    by_sha = {state.sha: state for state in states.values()}
    by_branch = {state.branch: state for state in states.values()}
    by_run = {run: jobs for state in states.values() for run, jobs in state.jobs.items()}
    with contextlib.ExitStack() as stack:
        for name, answer in (
            ("repository", lambda *_: "owner/name"),
            ("open_pull_requests", lambda *_: sorted(states)),
            # The attributes the decision reads, rather than settle.PullRequest itself:
            # base_red_check.py poses these cases against the merge base as well, and a construction
            # that raises there reports a case as unanswerable instead of as red.
            ("pull_request", lambda _project, number: types.SimpleNamespace(
                sha=states[number].sha, branch=states[number].branch, base=states[number].base,
                draft=states[number].draft, merge_state=states[number].merge_state,
                fork=states[number].fork, state=states[number].state)),
            ("checks", lambda _project, sha: by_sha[sha].results),
            ("head_runs", lambda _project, sha: by_sha[sha].runs),
            ("run_jobs", lambda _project, runs, _now: {
                run.get("id"): by_run[run.get("id")] for run in runs if run.get("id") in by_run}),
            ("head_sha", lambda _project, number: states[number].after),
            ("contains_commit", lambda _project, branch, sha: (
                _refuse_for_a_fork(by_branch[branch]) if by_branch[branch].fork
                else sha in by_branch[branch].holds)),
            ("project_state", lambda _project, base: base_state(
                {state.branch for state in states.values() if state.held}, base, red,
                releasing, required)),
        ):
            # `create` so the readings still answer on a tree without one of these names, where the
            # alternative is a case that stops before it disagrees.
            stack.enter_context(mock.patch.object(settle, name, answer, create=True))
        yield stack


def _refuse_for_a_fork(state):
    """git is what would run here on a real fork, and it exits 128 rather than answering."""
    raise RuntimeError(f"fatal: ambiguous argument 'origin/{state.branch}': unknown revision")


class Polled(Exception):
    """Raised out of the watcher's sleep, which is the only way one poll of it ends."""


# What a poll recorded, what it said, and what it left in the heartbeat. A pull request whose
# readings raised is dropped from the poll rather than reported, and the two are the same ready set
# — the saying is where they differ.
Poll = collections.namedtuple("Poll", "ready output beat")


def poll(states, listing_answers=True):
    """One poll of `watch` over a table of fabricated readings.

    Every file the watcher touches is redirected, the lock included: taking the real one would make
    this case's answer depend on whether a watcher happens to be running on the machine.
    """
    printed = io.StringIO()
    with tempfile.TemporaryDirectory(prefix="settle-ready-") as directory:
        ready_state = Path(directory) / "ready"
        heartbeat = Path(directory) / "beat"
        with fabricated_readings(states) as stack:
            if not listing_answers:
                stack.enter_context(
                    mock.patch.object(settle, "open_pull_requests", refuse_to_answer))
            for name, path in (("READY_STATE", ready_state),
                               ("HEARTBEAT", heartbeat),
                               ("LOCK", Path(directory) / "lock"),
                               ("ASKED", Path(directory) / "asked")):
                # `create` so the redirect survives being carried onto a tree without the name, where
                # the alternative is a case that stops before it disagrees.
                stack.enter_context(mock.patch.object(settle.watcher_state, name, path, create=True))
            stack.enter_context(mock.patch.object(settle.time, "sleep", side_effect=Polled))
            stack.enter_context(contextlib.redirect_stdout(printed))
            try:
                settle.watch(Path("."), None)
            except Polled:
                pass
        # None rather than an empty set when the file was never written: a poll that recorded
        # nothing and a poll that got as far as truncating the file are different facts, and this
        # harness would otherwise report the second as the first.
        recorded = (None if not ready_state.exists() else
                    {int(line.split()[0]) for line in ready_state.read_text().splitlines() if line})
        written = heartbeat.read_text() if heartbeat.exists() else None
    return Poll(recorded, printed.getvalue(), written)


def polled(states):
    """The pull request numbers one poll recorded as ready."""
    return poll(states).ready


def merge_would_take(states):
    """The pull request numbers `settle.py merge` would find nothing blocking, over the same table."""
    with fabricated_readings(states):
        return {number for number in states
                if not settle.blocking_reasons(Path("."), number).reasons}


class ReadinessTests(unittest.TestCase):
    """What the watcher records as ready, against what the merge would take.

    Two readings of one question is what this is here to stop. Asked separately they disagreed on a
    draft with conflicts, which sat in the ready state while `refuse/edit_while_a_ready_pr_sits.py`
    refused every Edit and Write in every session and named a merge that could not take it.
    """

    # Every per-pull-request state the two readings could differ on, plus the ordinary green one so
    # the table is not made of exceptions alone. The publication reason is not among them: it is one
    # reading for the whole repository, so it cannot differ between entries of a table like this.
    # The red base and the release commit are, because whether each blocks is decided per head.
    TABLE = {
        1: fabricate(1),
        2: fabricate(2, draft=True),
        3: fabricate(3, merge_state="dirty"),
        4: fabricate(4, base=RED),
        5: fabricate(5, held=True),
        6: fabricate(6, results=[{"name": "Unity", "bucket": "pending"}]),
        7: fabricate(7, results=[{"name": "Unity", "bucket": "fail"}]),
        8: fabricate(8, results=[]),
        9: fabricate(9, moved=True),
        10: fabricate(10, draft=True, merge_state="dirty"),
        11: fabricate(11, fork=True),
        12: fabricate(12, base=RED, holds=(BROKE,), results=SUITES_RAN),
        13: fabricate(13, base=RED, holds=(BROKE,), results=SUITES_SKIPPED),
        14: fabricate(14, base=RELEASING),
        15: fabricate(15, base=RELEASING, holds=(RELEASED,)),
        16: fabricate(16, runs=(TEST_PENDING,)),
        17: fabricate(17, results=GENERATOR_CHECKS),
    }

    def test_Given_ATableOfPullRequestStates_When_BothReadingsAreTaken_Then_TheyNameTheSameSet(self):
        # Act
        recorded = polled(self.TABLE)

        # Assert
        self.assertEqual(recorded, merge_would_take(self.TABLE))

    def test_Given_ADraftWhoseChecksAllPassed_When_TheWatcherPolls_Then_ItIsNotRecordedAsReady(self):
        # Arrange — draft and nothing else, so no second reason can keep this green while the one
        # the case is named for stops being asked. The state that raised it carried three.
        table = {377: fabricate(377, draft=True)}

        # Act / Assert
        self.assertEqual(polled(table), set())

    def test_Given_AForkPullRequest_When_TheWatcherPolls_Then_ItIsCarriedRatherThanDropped(self):
        # Arrange — the readings raise the way git does on `origin/<a branch on the fork>`. Both
        # outcomes leave the same ready set, so the ready set alone separates nothing; what a drop
        # costs is the poll's report of it — an error line where the checks should be.
        outcome = poll({1: fabricate(1), 11: fabricate(11, fork=True)})

        # Act / Assert
        self.assertEqual((outcome.ready, "! PR#11" in outcome.output, "PR#11" in outcome.output),
                         ({1}, False, True))

    def test_Given_APullRequestNothingBlocks_When_TheWatcherPolls_Then_ItIsStillRecordedAsReady(self):
        # Arrange — the state the guard exists for, which a stricter reading must not stop reporting.
        table = {592: fabricate(592)}

        # Act / Assert
        self.assertEqual(polled(table), {592})


class ExpectedCheckTests(unittest.TestCase):
    """A head is not green on the checks it lists while it is still owed others."""

    def test_Given_EveryListedCheckPassedAndARunNotStarted_When_Decided_Then_TheRunIsNamed(self):
        # Arrange — the required context has reported, so the run is all that is left to refuse on.
        states = {1: fabricate(1, runs=(GENERATORS_DONE, TEST_PENDING))}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [f"workflow runs not finished at 0000000: {TEST_PENDING_EXIT}"])

    def test_Given_EveryListedCheckPassedAndARequiredOneNeverReported_When_Decided_Then_ItIsNamed(self):
        # Arrange — every run has finished, so the absent context is all that is left to refuse on.
        states = {1: fabricate(1, results=GENERATOR_CHECKS, runs=(GENERATORS_DONE,))}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided,
                         ["required by main and not reported at 0000000: Required checks (Unity)"])

    def test_Given_NoCheckYetAndARunPending_When_Decided_Then_ItIsRefusedAsPendingNotNeverTriggered(self):
        # Arrange
        states = {1: fabricate(1, results=[], runs=(TEST_PENDING,))}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert — the refusal rides along because a head merged with nothing said names no trigger
        # either.
        self.assertEqual((bool(decided), any("never triggered" in reason for reason in decided)),
                         (True, False))

    # GREEN_ON_BASE(characterization): the base reads no run, so a run GitHub left queued holds nothing.
    # What it pins is that the jobs end it: `if not (jobs.get(run.get("id"))` spelled `if not (False`
    # reddens it.
    def test_Given_ARunLeftQueuedWhoseJobsAllCompleted_When_Decided_Then_NothingBlocks(self):
        # Arrange
        states = {1: fabricate(1, runs=(STUCK,), jobs={STUCK["id"]: COMPLETED_JOBS})}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    def test_Given_ARunLeftQueuedWithAJobStillGoing_When_Decided_Then_ItIsNamedWithItsExit(self):
        # Arrange — one job completed and one not, so the jobs of the attempt do not end the run.
        jobs = [COMPLETED_JOBS[0], {"name": "Required checks (generators)", "status": "queued"}]
        states = {1: fabricate(1, runs=(STUCK,), jobs={STUCK["id"]: jobs})}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, ["workflow runs not finished at 0000000: Source generators "
                                   "(queued, run 31128456870: `gh run cancel 31128456870` if it is "
                                   "stuck)"])

    # GREEN_ON_BASE(characterization): the base reads no run, so a run untouched for weeks holds nothing.
    # What it pins is the bound: `untouched_for(run, now) < STALE_AFTER` spelled `True` reddens it.
    def test_Given_ARunUntouchedForLongerThanTheBound_When_Decided_Then_NothingBlocks(self):
        # Arrange — no job listed at all, so the bound is all that ends it.
        stale = dict(STUCK, updated_at="2026-08-06T21:55:26Z")
        states = {1: fabricate(1, runs=(stale,))}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    def test_Given_TheNewestRunOfAWorkflowFailedWithNoCheck_When_Decided_Then_ItIsNamed(self):
        # Arrange — a base requiring nothing, so no absent context stands in for the failed run.
        failed = dict(TEST_PENDING, status="completed", conclusion="startup_failure")
        states = {1: fabricate(1, base="4.x", results=GENERATOR_CHECKS,
                               runs=(GENERATORS_DONE, failed))}

        # Act
        with fabricated_readings(states, required={"4.x": ()}):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, ["workflow runs failed at 0000000: Test (startup_failure, run "
                                   "36512896182)"])

    # GREEN_ON_BASE(characterization): the base reads no run, so an older failed run holds nothing.
    # What it pins is that only the newest run of a workflow is read: `if workflow not in newest or
    # order > newest[workflow][0]:` spelled `if True:` reddens it.
    def test_Given_AFailedRunSupersededByANewerOne_When_Decided_Then_NothingBlocks(self):
        # Arrange — newest first, the order the runs listing answers in.
        newer = dict(TEST_PENDING, id=2, run_number=71, status="completed", conclusion="success")
        older = dict(TEST_PENDING, id=1, run_number=70, status="completed", conclusion="failure")
        states = {1: fabricate(1, runs=(newer, older))}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    # GREEN_ON_BASE(characterization): the base merges a head no ruleset asks anything more of.
    # What it pins is that neither reading is a wall: requiring the aggregates whatever the ruleset
    # says, or counting a completed run as unfinished, reddens it.
    def test_Given_ABaseRequiringNothingAndAWorkflowThatNeverStarted_When_Decided_Then_NothingBlocks(self):
        # Arrange — `Test` has no run at all on this head, the way a path filter leaves one.
        states = {1: fabricate(1, base="4.x", results=GENERATOR_CHECKS, runs=(GENERATORS_DONE,))}

        # Act
        with fabricated_readings(states, required={"4.x": ()}):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    def test_Given_AHeadWhoseRunHasNotStarted_When_TheWatcherPolls_Then_ItIsNotRecordedAsReady(self):
        # Arrange
        table = {1: fabricate(1, runs=(TEST_PENDING,))}

        # Act / Assert
        self.assertEqual(polled(table), set())

    def test_Given_ABase_When_ItsStateIsRead_Then_ItCarriesWhatItsRulesetsRequire(self):
        # Arrange — answered for 2.x alone, so a reading that asks about another base raises.
        rules = [{"type": "pull_request", "parameters": {}},
                 {"type": "required_status_checks", "parameters": {"required_status_checks": [
                     {"context": "Required checks (generators)"},
                     {"context": "Required checks (Unity)"}]}}]
        answers = {"repos/owner/name/rules/branches/2.x?per_page=100": rules}
        with contextlib.ExitStack() as stack:
            for name, answer in (("gh_git", lambda *_: ""),
                                 ("repository", lambda *_: "owner/name"),
                                 ("worktree_branches", lambda *_: set()),
                                 ("rest_json", lambda path: answers[path] if "/rules/" in path
                                  else {"workflow_runs": []}),
                                 ("release_commit", lambda *_: None)):
                stack.enter_context(mock.patch.object(settle, name, answer))
            stack.enter_context(mock.patch.object(settle.published_check, "unpublished_reason",
                                                  lambda *_, **__: None))

            # Act
            state = settle.project_state(Path("."), "2.x")

        # Assert
        self.assertEqual(getattr(state, "required", None),
                         ["Required checks (Unity)", "Required checks (generators)"])

    def test_Given_ARuleOfAnotherTypeCarryingContexts_When_Read_Then_TheyAreNotRequired(self):
        # Arrange — the same parameter key under a rule that is not a status-check rule, so the rule's
        # type is all that separates the two.
        rules = [{"type": "workflows", "parameters": {"required_status_checks": [
                     {"context": "Deploy"}]}},
                 {"type": "required_status_checks", "parameters": {"required_status_checks": [
                     {"context": "Required checks (Unity)"}]}}]
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda _path: rules))

            # Act
            required = settle.required_contexts(Path("."), "main")

        # Assert
        self.assertEqual(required, ["Required checks (Unity)"])

    def test_Given_RulesFillingAWholePage_When_Read_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — the listing carries no total, so a full page is read as one that may have
        # dropped a rule.
        rules = [{"type": "pull_request", "parameters": {}}] * 100
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda _path: rules))

            # Act / Assert
            self.assertRaises(RuntimeError, settle.required_contexts, Path("."), "main")

    def test_Given_AHead_When_ItsRunsAreRead_Then_ThePathNamesItsShaAndAPageSize(self):
        # Arrange
        asked = []
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(
                settle, "rest_json",
                lambda path: (asked.append(path), {"total_count": 0, "workflow_runs": []})[1]))

            # Act
            settle.head_runs(Path("."), GREEN)

        # Assert
        self.assertEqual(asked, [f"repos/owner/name/actions/runs?head_sha={GREEN}&per_page=100"])

    def test_Given_AJobsPageCarryingLessThanItsTotal_When_Read_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — the job that fell off the page could be the one still going.
        truncated = {"total_count": 2, "jobs": [COMPLETED_JOBS[0]]}
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda _path: truncated))

            # Act / Assert
            self.assertRaises(RuntimeError, settle.run_jobs, Path("."), [STUCK], 0)

    def test_Given_ARunsPageCarryingLessThanItsTotal_When_Read_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — the run that fell off the page could be the one still going.
        truncated = {"total_count": 2, "workflow_runs": [GENERATORS_DONE]}
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda _path: truncated))

            # Act / Assert
            self.assertRaises(RuntimeError, settle.head_runs, Path("."), GREEN)


class ClosedPullRequestTests(unittest.TestCase):
    """A pull request the listing named and that stopped being open before it was decided."""

    def test_Given_AMergedPullRequestWhoseChecksPassed_When_ItsMergeIsDryRun_Then_ItIsRefusedByItsState(self):
        # Arrange — every other reading is clean, so the state is all that is left to refuse on.
        printed = io.StringIO()
        with fabricated_readings({1157: fabricate(1157, state="merged")}):
            with contextlib.redirect_stderr(printed), contextlib.redirect_stdout(io.StringIO()):
                # Act
                code = settle.merge(Path("."), 1157, None, dry_run=True)

        # Assert
        self.assertEqual((code, "it is merged, not open" in printed.getvalue()), (1, True))

    def test_Given_APullRequestClosedAfterTheListing_When_TheWatcherPolls_Then_NothingIsSaidOrRecorded(self):
        # Arrange
        table = {1: fabricate(1, state="closed")}

        # Act
        outcome = poll(table)

        # Assert
        self.assertEqual((outcome.ready, outcome.output), (set(), ""))

    def test_Given_APayloadOfAMergedPullRequest_When_Read_Then_ItsStateIsMerged(self):
        # Arrange — GitHub reports a merged pull request as `closed`, with `merged` beside it.
        payload = {"head": {"sha": GREEN, "ref": "topic", "repo": {"full_name": "o/v"}},
                   "base": {"ref": "main", "repo": {"full_name": "o/v"}},
                   "state": "closed", "merged": True}
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "o/v"))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda *_: payload))

            # Act
            read = settle.pull_request(Path("."), 1)

        # Assert
        self.assertEqual(getattr(read, "state", None), "merged")


class HeartbeatDuringAPollTests(unittest.TestCase):
    """What the heartbeat says while a poll is failing, which is what the guards read it for."""

    def test_Given_APollWhoseListingNeverAnswers_When_ItEnds_Then_NoHeartbeatVouchesForIt(self):
        # Arrange — the wedge the call bounds exist for, reaching the guards as a file they believe.
        # A stamp written on the way in holds it inside the staleness window for the whole of it,
        # and the guards then read a live watcher beside a ready file this poll emptied.
        outcome = poll({1: fabricate(1)}, listing_answers=False)

        # Act / Assert — and the ready file is unwritten beside it, which is the state a guard would
        # otherwise read as "a live watcher, and nothing ready".
        self.assertEqual((outcome.beat, outcome.ready), (None, None))

    def test_Given_APollOverSeveralPullRequests_When_ItRuns_Then_ItStampsOncePerReading(self):
        # Arrange — the per-pull-request write, which the per-cycle one cannot stand in for: a poll
        # over several of them can outlast the window a reader believes a stamp for, and one stamp at
        # the top of the cycle is the whole of what such a reader would have.
        stamps = []
        with mock.patch.object(settle, "beat", lambda: stamps.append(1)):
            poll({1: fabricate(1), 2: fabricate(2)})

        # Act / Assert — one for the readings that open the cycle, one per pull request read.
        self.assertEqual(len(stamps), 3)

    def test_Given_APollThatRead_When_ItEnds_Then_TheHeartbeatNamesThisProcess(self):
        # Arrange — the control: a heartbeat nothing ever writes is not a heartbeat.
        outcome = poll({1: fabricate(1)})

        # Act / Assert
        self.assertIn(f" {os.getpid()}", outcome.beat or "")


# Far enough from the epoch that a case can date a record hours before a run starts and still write
# a positive stamp, which is the only kind `watcher_state.stamp_age` reads.
CLOCK_START = 100000


# The staleness window in seconds, written out rather than spelled from `watcher_state.STALE_AFTER`:
# an arrangement naming the symbol moves with a change to it, and this branch made that width decide
# a second file besides the heartbeat. It reaches the heartbeat's own window only through the carry
# that shares the width, so a later `read_ready_state` with a width of its own leaves that one
# pinned by nothing.
STALE_SECONDS = 180


class FakeClock:
    """Time that moves only when the watcher sleeps, so a poll cycle costs no wall clock.

    Bounded, because a watcher that fails to retire would hang the run rather than fail it, and a
    hang reports nothing.
    """

    def __init__(self, limit, between_polls=None):
        self.now = float(CLOCK_START)
        self.polls = 0
        self.limit = limit
        self.between_polls = between_polls

    def time(self):
        return self.now

    def sleep(self, seconds):
        if self.polls >= self.limit:
            raise Polled
        self.polls += 1
        self.now += seconds
        if self.between_polls is not None:
            self.between_polls(self.now)


# How a run of `watch` ended: its exit code, or None where it was still polling when the clock gave
# up, how many polls it took, what it printed on the way out, whether the lock was free by then, and
# the ready record it left.
Watched = collections.namedtuple("Watched", "code polls output lock_released ready")


def keeping_the_lock(handles):
    """`hold_the_watch`, with the handle it opened kept where the run can be read after it ends."""
    took_it = settle.hold_the_watch

    def held():
        handle, holder = took_it()
        handles.append(handle)
        return handle, holder
    return held


# Where the clock gives up, past the hundred and twenty polls the interval takes, so a watcher that
# retires has stopped before it.
CLOCK_BOUND = 180


def watch_until(between_polls=None, asked=None, listing_answers=True, seed_ready=None,
                states=None, bound=CLOCK_BOUND, seed_ready_at=None, seed_asked=None):
    """Run `watch` over green pull requests and a clock only its own sleep advances.

    `asked` sends the record somewhere else, for a case about it not being written; `bound` is how
    many polls the clock allows before it gives up. The `seed_` arguments write what a watcher that
    has already stopped leaves behind: its ready record, when that record was written, and the
    record of the last read.
    """
    clock = FakeClock(bound, between_polls)
    printed = io.StringIO()
    handles = []
    with tempfile.TemporaryDirectory(prefix="settle-retire-") as directory:
        ready_state = Path(directory) / "ready_state"
        if isinstance(seed_ready, bytes):
            ready_state.write_bytes(seed_ready)
        elif seed_ready is not None:
            ready_state.write_text(seed_ready, encoding="utf-8")
        if seed_ready_at is not None:
            # The clock the watcher reads is fabricated, so the record's own timestamp is set to
            # match it rather than to whatever the filesystem would have written.
            os.utime(ready_state, (seed_ready_at, seed_ready_at))
        if isinstance(seed_asked, bytes):
            (Path(directory) / "asked").write_bytes(seed_asked)
        elif seed_asked is not None:
            (Path(directory) / "asked").write_text(f"{int(seed_asked)}\n", encoding="utf-8")
        with fabricated_readings({1: fabricate(1)} if states is None else states) as stack:
            for name in ("READY_STATE", "HEARTBEAT", "LOCK", "ASKED"):
                # `create` for the reason `poll` gives over the same redirect.
                stack.enter_context(mock.patch.object(settle.watcher_state, name,
                                                      Path(directory) / name.lower(), create=True))
            if asked is not None:
                stack.enter_context(mock.patch.object(settle.watcher_state, "ASKED", asked,
                                                      create=True))
            if not listing_answers:
                stack.enter_context(mock.patch.object(settle, "open_pull_requests",
                                                     refuse_to_answer))
            stack.enter_context(mock.patch.object(settle, "hold_the_watch",
                                                  keeping_the_lock(handles)))
            stack.enter_context(mock.patch.object(settle.time, "sleep", clock.sleep))
            stack.enter_context(mock.patch.object(settle.time, "time", clock.time))
            stack.enter_context(contextlib.redirect_stdout(printed))
            try:
                code = settle.watch(Path("."), "main")
            except Polled:
                code = None
            # Read before this closes it: what a case asks is whether the code let the lock go, so
            # the handle is held until the reading rather than left to fall out of scope.
            released = handles[0].closed
            handles[0].close()
        recorded = (ready_state.read_text(encoding="utf-8", errors="replace")
                    if ready_state.exists() else "")
    return Watched(code, clock.polls, printed.getvalue(), released, recorded)


class RetirementTests(unittest.TestCase):
    """When a watcher stops.

    One polled for seven days past the session that started it, on the order of ten thousand polls
    nobody read. `watcher_state.ASKED` owns what decides when one stops.
    """

    def test_Given_OneRunReadAndOneNot_When_BothPassTheRetirementInterval_Then_OnlyTheUnreadStops(self):
        # Arrange — either arm alone settles nothing: a watcher retiring on its own elapsed time
        # would pass the unread one, and one that never retires passes the read one.
        ended = (watch_until().code,
                 watch_until(between_polls=settle.watcher_state.alive).code)

        # Act / Assert — None is what a watcher still polling at the clock's bound leaves.
        self.assertEqual(ended, (0, None))

    def test_Given_ARetiringWatcher_When_ItStops_Then_ItSaysTheCauseTheCostAndTheCure(self):
        # Arrange — a guard starts refusing the moment it goes, so a message carrying one of the
        # three leaves whoever reads it to work out the rest.
        said = watch_until().output

        # Act / Assert — "since anything last stamped" rather than "nothing stamped": the file is
        # normally there and normally old, and a reader who has been editing all session reads the
        # second as the stamping being broken when what happened is that they stopped.
        self.assertEqual(("since anything last stamped" in said,
                          "blocks a Stop again" in said,
                          "python3 scripts/pr/settle.py watch" in said), (True, True, True))

    def test_Given_NoStampAtAll_When_ItRetires_Then_ItNamesTheReaderThatCouldNotRecord(self):
        # Arrange — a guard resolves the stamping code from its own checkout, so one at a commit
        # predating it reads the watcher every turn and cannot record having done it, and the file
        # it would have written stays absent however long the reading goes on.
        said = watch_until().output

        # Act / Assert
        self.assertIn("carries no stamp at all", said)

    def test_Given_AStampThatWentStale_When_ItRetires_Then_ItReportsTheIntervalAndNothingMore(self):
        # Arrange — a file that exists and is old is the reader having stopped, which the sentence
        # for the third state would misdescribe as a checkout that cannot record.
        said = watch_until(seed_asked=0).output

        # Act / Assert — paired, because the absence alone is also what a message that never gained
        # the sentence looks like.
        self.assertEqual(("since anything last stamped" in said,
                          "carries no stamp at all" in said), (True, False))

    def test_Given_ARetiringWatcher_When_ItStops_Then_ItLetsGoOfTheLock(self):
        # Act
        watched = watch_until()

        # Assert
        self.assertTrue(watched.lock_released)

    def test_Given_NothingReadingItsState_When_ItRetires_Then_ItPolledTheIntervalOutFirst(self):
        # Arrange — the count rather than the fact of stopping, because both directions are defects:
        # too late is the seven days this branch is about, and too early takes the watcher out from
        # under a session that is still working and hard-refuses its next edit.
        watched = watch_until()

        # Act / Assert
        self.assertEqual(watched.polls, 120)

    def test_Given_ARecordFromLongBeforeThisWatcher_When_ItStarts_Then_ThatIsNotWhatRetiresIt(self):
        # Arrange — the recovery a guard prints is `settle.py watch`, and a Bash call that runs it
        # stamps nothing, so the record a replacement finds can be hours old.
        watched = watch_until(seed_asked=CLOCK_START - 14400)

        # Act / Assert
        self.assertEqual(watched.polls, 120)

    def test_Given_ReadingsThatNeverAnswer_When_TheIntervalPasses_Then_ItStillRetires(self):
        # Arrange — the wedge that reads to a guard as nothing watching: each poll fails, prints,
        # sleeps and comes back, so the retirement is reached on that path or not at all.
        watched = watch_until(listing_answers=False)

        # Act / Assert
        self.assertEqual(watched.code, 0)

    def test_Given_AReadyRecordAnEarlierWatcherLeft_When_OneStarts_Then_OnlyTheUsableAgeIsCarried(self):
        # Arrange — two pull requests recorded ready before this run started, one of them stamped
        # ahead of the clock. Retirement makes restarts ordinary, and a run that began its record
        # empty would date every green pull request to its own start, which is the age
        # `refuse/edit_while_a_ready_pr_sits.py` refuses on; carrying the second forward would date
        # one of them permanently ahead of that age instead.
        watched = watch_until(
            seed_ready=f"1 {CLOCK_START - 900}\n2 {CLOCK_START + 9000}\n",
            seed_ready_at=CLOCK_START, states={1: fabricate(1), 2: fabricate(2)})

        # Act / Assert — a dropped age is redated to the run's own start, and a carried one is not.
        self.assertEqual(watched.ready.split(),
                         ["1", str(CLOCK_START - 900), "2", str(CLOCK_START)])

    def test_Given_AReadyRecordWrittenOutsideTheWindow_When_OneStarts_Then_ThatAgeIsNotCarried(self):
        # Arrange — a record older than the staleness window is one nothing polled through, so the
        # pull request may have left the ready set and returned inside it; one dated ahead of the
        # clock would sit inside the window for as long as it stayed ahead. The two arms a second
        # apart are the window's own edge, and the inner one is what goes red on a watcher that
        # carried nothing at all.
        seeded = f"1 {CLOCK_START - 900}\n"
        carried = (watch_until(seed_ready=seeded,
                               seed_ready_at=CLOCK_START - STALE_SECONDS + 1).ready.split(),
                   watch_until(seed_ready=seeded,
                               seed_ready_at=CLOCK_START - STALE_SECONDS).ready.split(),
                   watch_until(seed_ready=seeded, seed_ready_at=CLOCK_START + 9000).ready.split())

        # Act / Assert — a dropped age is redated to the run's own start.
        self.assertEqual(carried, (["1", str(CLOCK_START - 900)],
                                   ["1", str(CLOCK_START)],
                                   ["1", str(CLOCK_START)]))

    def test_Given_ARecordStampedAheadOfThisWatcher_When_ItStarts_Then_ItStillRetires(self):
        # Arrange — a millisecond epoch, a forward clock step or a hand-written stamp. Read as an
        # age it is negative, which is younger than any interval, so a watcher that believed it
        # would poll for as long as the stamp stays ahead — the shape this branch exists to remove.
        watched = watch_until(seed_asked=CLOCK_START + 14400)

        # Act / Assert
        self.assertEqual(watched.code, 0)

    def test_Given_ARecordOfTheLastReadThatIsNotText_When_OneStarts_Then_ItStartsAnyway(self):
        # Arrange — a record the poll cannot decode; `watcher_state.asked_ago` owns why that must
        # not raise. The sibling case covers the ready record, one `try` block away.
        watched = watch_until(seed_asked=b"\xff\xfe1787240000\n")

        # Act / Assert
        self.assertEqual(watched.code, 0)

    def test_Given_AReadyRecordThatIsNotText_When_OneStarts_Then_ItStartsAnyway(self):
        # Arrange — until the record was read back at all, a file like this was simply overwritten on
        # the first poll. Reading it must not be what stops a watcher starting, every time, until
        # somebody deletes the file.
        watched = watch_until(seed_ready=b"\xff\xfe700 1\n", seed_ready_at=CLOCK_START)

        # Act / Assert — that it reached its own ending rather than the clock's is the whole claim,
        # so this reads the exit and leaves the interval to the case that pins it.
        self.assertEqual(watched.code, 0)

    def test_Given_AGuardAskingEachPoll_When_TheRecordCannotBeWritten_Then_ItStillGetsItsAnswer(self):
        # Arrange — a directory the write cannot reach. The retirement is folded in beside the
        # answers because with the record landing this watcher would still be polling, so the pair
        # is what says the guard was answered while nothing was being written down.
        answers = []
        with tempfile.TemporaryDirectory(prefix="settle-nowrite-") as directory:
            unwritable = Path(directory) / "home"
            unwritable.mkdir()
            os.chmod(unwritable, 0o500)
            try:
                # Act
                watched = watch_until(
                    between_polls=lambda now: answers.append(settle.watcher_state.alive(now)),
                    asked=unwritable / "asked")
            finally:
                os.chmod(unwritable, 0o700)

        # Assert
        self.assertEqual((set(answers), watched.code), ({True}, 0))


class ForkMergeTests(unittest.TestCase):
    """What `settle.py merge` does with a head this checkout has no ref for."""

    def test_Given_AForkPullRequestOntoARedBase_When_TheMergeIsDecided_Then_ItIsRefusedRatherThanRaising(self):
        # Arrange — `contains_commit` is what would run on `origin/<a branch on the fork>`, and it
        # exits 128 rather than answering, so a merge decided without the fork reading raises out of
        # a command whose whole job is to report what blocks.
        printed = io.StringIO()
        with fabricated_readings({8: fabricate(8, fork=True, base=RED)}):
            with contextlib.redirect_stderr(printed):
                # Act
                code = settle.merge(Path("."), 8, None, dry_run=True)

        # Assert
        self.assertEqual((code, "head is on another repository" in printed.getvalue()), (1, True))


LONG_LIVED_REASON = ("its head {} is a long-lived branch: squashed, it drops out of the base's "
                     "ancestry, and deleted, it is gone. Land it as a merge commit that keeps the "
                     "branch (the web interface's \"Create a merge commit\"), as CONTRIBUTING.md's "
                     "maintenance-line section says")


def merge_reasons(branch):
    """What `settle.py merge` would refuse a pull request for whose head is `branch`, green otherwise."""
    with fabricated_readings({7: fabricate(7, branch=branch)}):
        return settle.blocking_reasons(Path("."), 7).reasons


class LongLivedHeadTests(unittest.TestCase):
    """A head that has to outlive its merge, which `merge` would squash and then delete."""

    def test_Given_ALongLivedHead_When_Decided_Then_ItIsRefusedWithHowToLandIt(self):
        # Act
        decided = reasons(long_lived_head=True)

        # Assert
        self.assertEqual(decided, [LONG_LIVED_REASON.format("topic")])

    def test_Given_ALongLivedHeadThatMoved_When_Decided_Then_ItIsStillReported(self):
        # Act — the branch name is what makes it long-lived, and a force-push does not change it.
        decided = reasons(after=MOVED, long_lived_head=True)

        # Assert
        self.assertIn(LONG_LIVED_REASON.format("topic"), decided)

    def test_Given_AMaintenanceLineMergedForward_When_TheMergeIsDecided_Then_ItIsRefused(self):
        # Act
        decided = merge_reasons("2.x")

        # Assert
        self.assertEqual(decided, [LONG_LIVED_REASON.format("2.x")])

    def test_Given_TheMirrorBranchAsHead_When_TheMergeIsDecided_Then_ItIsRefused(self):
        # Act
        decided = merge_reasons("upm")

        # Assert
        self.assertEqual(decided, [LONG_LIVED_REASON.format("upm")])

    # GREEN_ON_BASE(characterization): the base holds no long-lived rule, so this holds there too.
    # What it pins is that a line's name is matched whole: reading the pattern with `search` reddens it.
    def test_Given_ABranchNamedLikeALineButLonger_When_TheMergeIsDecided_Then_NothingBlocksIt(self):
        # Act — `fix/2.x` names a line without being one.
        decided = merge_reasons("fix/2.x")

        # Assert
        self.assertEqual(decided, [])


class ReadyStateTests(unittest.TestCase):
    def test_Given_AReadyPullRequestThatStoppedBeingReady_When_ItIsRecorded_Then_ItsAgeIsDropped(self):
        # Arrange
        since = {}
        with tempfile.TemporaryDirectory(prefix="settle-ready-") as directory:
            with mock.patch.object(settle.watcher_state, "READY_STATE", Path(directory) / "ready"):
                settle.write_ready_state({7}, since)

                # Act
                settle.write_ready_state(set(), since)

        # Assert
        self.assertNotIn(7, since)


# Holds the watcher lock and says so, then waits to be killed. A separate process because a lock is
# a claim between processes, and what one makes of its own is the platform's business rather than
# this decision's.
HOLDER = """
import fcntl, os, sys, time
handle = open(sys.argv[1], "a+")
fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
handle.seek(0)
handle.truncate()
handle.write(str(os.getpid()))
handle.flush()
print("held", flush=True)
time.sleep(120)
"""


def refuse_to_answer(*_):
    """Stands in for a reading in a case whose whole claim is that no reading is taken."""
    raise RuntimeError("the watcher asked the API in a case that must not reach it")


def exited_pid():
    """The id of a process that has finished, which is what a killed watcher leaves in a heartbeat."""
    done = subprocess.Popen([sys.executable, "-c", "pass"])
    done.wait()
    return done.pid


@contextlib.contextmanager
def another_watcher_holding(lock):
    holder = subprocess.Popen([sys.executable, "-c", HOLDER, str(lock)],
                              stdout=subprocess.PIPE, text=True)
    try:
        holder.stdout.readline()
        yield holder
    finally:
        holder.kill()
        holder.wait()
        holder.stdout.close()


class WatcherLockTests(unittest.TestCase):
    """One watcher at a time: a second polls the same API on its own cycle against the same quota,
    and writes the same heartbeat, so neither of them says which one is alive."""

    def test_Given_AWatcherAlreadyHoldingTheLock_When_AnotherAsksForIt_Then_ItIsRefused(self):
        # Arrange
        with tempfile.TemporaryDirectory(prefix="settle-lock-") as directory:
            lock = Path(directory) / "lock"
            with mock.patch.object(settle.watcher_state, "LOCK", lock):
                with another_watcher_holding(lock):
                    # Act
                    handle, _ = settle.hold_the_watch()

                    # Assert
                    self.assertIsNone(handle)

    def test_Given_AWatcherAlreadyHoldingTheLock_When_AnotherAsksForIt_Then_ItIsToldWhichPid(self):
        # Arrange
        with tempfile.TemporaryDirectory(prefix="settle-lock-") as directory:
            lock = Path(directory) / "lock"
            with mock.patch.object(settle.watcher_state, "LOCK", lock):
                with another_watcher_holding(lock) as holder:
                    # Act
                    _, reported = settle.hold_the_watch()

                    # Assert
                    self.assertEqual(reported, str(holder.pid))

    def test_Given_ALockFileTheHolderDiedUnder_When_AWatcherAsksForIt_Then_ItTakesIt(self):
        # Arrange — the file survives the kill carrying the dead pid, which is what a pidfile would
        # have to decide about. Both are asked at once, since a file that never got written would
        # also be taken and would say nothing about staleness.
        with tempfile.TemporaryDirectory(prefix="settle-lock-") as directory:
            lock = Path(directory) / "lock"
            with mock.patch.object(settle.watcher_state, "LOCK", lock):
                with another_watcher_holding(lock):
                    pass
                left_behind = lock.read_text().strip()

                # Act
                handle, _ = settle.hold_the_watch()
                if handle is not None:
                    handle.close()

                # Assert
                self.assertEqual((left_behind.isdigit(), handle is not None), (True, True))

    def test_Given_AWatcherHoldingTheLock_When_ItRecordsItself_Then_TheFileNamesItsPid(self):
        # Arrange
        with tempfile.TemporaryDirectory(prefix="settle-lock-") as directory:
            lock = Path(directory) / "lock"
            with mock.patch.object(settle.watcher_state, "LOCK", lock):
                # Act
                handle, _ = settle.hold_the_watch()
                handle.close()

            # Assert
            self.assertEqual(lock.read_text().strip(), str(os.getpid()))


class HeartbeatTests(unittest.TestCase):
    """What a reader may conclude from the file, which is what both guards conclude from it, and
    what the reading leaves behind for the watcher."""

    def test_Given_AHeartbeatFromALiveProcess_When_ItIsFresh_Then_TheWatcherReadsAsAlive(self):
        # Arrange
        with self.heartbeat(settle.watcher_state.beat(os.getpid(), now=1000)):
            # Act / Assert
            self.assertTrue(settle.watcher_state.alive(now=1060))

    def test_Given_AFreshHeartbeatFromAProcessThatIsGone_When_ItIsRead_Then_TheWatcherIsNotAlive(self):
        # Arrange — a watcher killed between two polls leaves a stamp still inside the window, which
        # is the whole of what the stamp alone could ever say.
        with self.heartbeat(settle.watcher_state.beat(exited_pid(), now=1000)):
            # Act / Assert
            self.assertFalse(settle.watcher_state.alive(now=1060))

    def test_Given_AHeartbeatNamingNoProcess_When_ItIsRead_Then_TheWatcherIsNotAlive(self):
        # Arrange — the format the watcher wrote before it had to name itself.
        with self.heartbeat("1000\n"):
            # Act / Assert
            self.assertFalse(settle.watcher_state.alive(now=1060))

    def test_Given_AHeartbeatStampedInTheFuture_When_ItIsRead_Then_TheWatcherIsNotAlive(self):
        # Arrange — a millisecond epoch or a backward clock step vouched for a watcher permanently.
        with self.heartbeat(settle.watcher_state.beat(os.getpid(), now=9000)):
            # Act / Assert
            self.assertFalse(settle.watcher_state.alive(now=1060))

    def test_Given_AHeartbeatOlderThanThePollWindow_When_ItIsRead_Then_TheWatcherIsNotAlive(self):
        # Arrange
        with self.heartbeat(settle.watcher_state.beat(os.getpid(), now=1000)):
            # Act / Assert
            self.assertFalse(
                settle.watcher_state.alive(now=1000 + settle.watcher_state.STALE_AFTER))

    def test_Given_AFreshHeartbeatNamingNoProcess_When_AWatcherStarts_Then_SomebodyElseIsWatching(self):
        # Arrange — what a watcher launched from a checkout older than the lock leaves, which is the
        # one kind the lock cannot see.
        with self.heartbeat("1000\n"):
            # Act / Assert
            self.assertTrue(settle.watcher_state.beating_elsewhere(os.getpid(), now=1060))

    def test_Given_AFreshHeartbeatFromAWatcherThatDied_When_AnotherStarts_Then_NobodyElseIsWatching(self):
        # Arrange — restarting inside the window would otherwise refuse itself.
        with self.heartbeat(settle.watcher_state.beat(exited_pid(), now=1000)):
            # Act / Assert
            self.assertFalse(settle.watcher_state.beating_elsewhere(os.getpid(), now=1060))

    def test_Given_ANamelessHeartbeatOlderThanTheWindow_When_AWatcherStarts_Then_NobodyElseIsWatching(self):
        # Arrange — a file left behind by a watcher that stopped is not one still being written.
        with self.heartbeat("1000\n"):
            # Act / Assert
            self.assertFalse(settle.watcher_state.beating_elsewhere(
                os.getpid(), now=1000 + settle.watcher_state.STALE_AFTER))

    def test_Given_SomethingElseStillBeating_When_TheWatcherIsAsked_Then_ItDeclinesToPollAsWell(self):
        # Arrange — the lock is free, so the heartbeat is the only thing saying anyone else is there.
        # The readings raise rather than answer, so a watcher that starts anyway ends this case
        # instead of polling in a loop nothing here would stop.
        with tempfile.TemporaryDirectory(prefix="settle-lock-") as directory:
            lock = Path(directory) / "lock"
            with contextlib.ExitStack() as stack:
                stack.enter_context(mock.patch.object(settle.watcher_state, "LOCK", lock))
                stack.enter_context(self.heartbeat(f"{int(time.time())}\n"))
                stack.enter_context(mock.patch.object(settle, "open_pull_requests", refuse_to_answer))
                stack.enter_context(mock.patch.object(settle.time, "sleep", side_effect=Polled))
                stack.enter_context(contextlib.redirect_stderr(io.StringIO()))
                stack.enter_context(contextlib.redirect_stdout(io.StringIO()))

                # Act / Assert
                self.assertEqual(settle.watch(Path("."), "main"), 1)

    # A truncated write, a disk fault, a file written by something else. The three readers answer
    # different questions, so each gets its own case rather than one standing in for the others.
    CORRUPT = b"\xff\xfe1000 1\n"

    def answered(self, reading):
        """What the reading returned, or the name of what it raised instead.

        The claim is that it answers, and an answer and a raise are the two outcomes to tell apart —
        so the raise is turned into a value rather than left to end the case, which reports the same
        way whether the reading raised or the file was never written.
        """
        with self.heartbeat(self.CORRUPT):
            try:
                return reading()
            except Exception as raised:  # noqa: BLE001
                return type(raised).__name__

    def test_Given_AHeartbeatThatIsNotUtf8_When_LivenessIsRead_Then_ItAnswersRatherThanRaising(self):
        # Arrange — a UnicodeDecodeError is a ValueError, so it goes past an OSError catch and out of
        # whichever PreToolUse or Stop hook was asking.
        # Act / Assert
        self.assertIs(self.answered(lambda: settle.watcher_state.alive(now=1060)), False)

    def test_Given_AHeartbeatThatIsNotUtf8_When_TheUnreadableReadingIsAsked_Then_ItAnswersRatherThanRaising(self):
        # Arrange — this reading exists to separate "nothing is watching" from "the reading failed",
        # and raising is neither.
        # Act / Assert
        self.assertIs(self.answered(lambda: settle.watcher_state.unreadable_beat(now=1060)), False)

    def test_Given_AHeartbeatThatIsNotUtf8_When_AnotherWatcherIsLookedFor_Then_ItAnswersRatherThanRaising(self):
        # Arrange — this one decides whether a second watcher may start, so a raise here refuses the
        # recovery the other two name.
        # Act / Assert
        self.assertIs(
            self.answered(lambda: settle.watcher_state.beating_elsewhere(os.getpid(), now=1060)),
            False)

    @contextlib.contextmanager
    def heartbeat(self, text):
        """Both files a read touches: reading the first is what writes the second.

        `text` may be bytes, which is how a heartbeat corrupted outside this process arrives.
        """
        with tempfile.TemporaryDirectory(prefix="settle-beat-") as directory:
            path = Path(directory) / "beat"
            if isinstance(text, bytes):
                path.write_bytes(text)
            else:
                path.write_text(text)
            with contextlib.ExitStack() as stack:
                stack.enter_context(mock.patch.object(settle.watcher_state, "HEARTBEAT", path))
                stack.enter_context(mock.patch.object(settle.watcher_state, "ASKED",
                                                     Path(directory) / "asked"))
                yield


class RedBaseMergeTests(unittest.TestCase):
    """What a base whose required workflows last failed on push does to the merge decision."""

    def test_Given_MainsLastPushRunFailed_When_AHeadWithoutThatCommitIsDecided_Then_ItIsRefused(self):
        # Arrange
        states = {1: fabricate(1)}

        # Act
        with fabricated_readings(states, red=("main",)):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [RED_REASON])

    def test_Given_MainsLastPushRunFailed_When_AHeadContainingThatCommitIsDecided_Then_NothingBlocksIt(self):
        # Arrange — the fix or the revert, whose own suites ran over the failing commit.
        states = {1: fabricate(1, holds=(BROKE,), results=SUITES_RAN)}

        # Act
        with fabricated_readings(states, red=("main",)):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    def test_Given_MainsLastPushRunFailed_When_AHeadContainingItSkippedItsSuites_Then_ItIsRefused(self):
        # Arrange — the aggregate check passed with the suite jobs skipped, so nothing ran over it.
        states = {1: fabricate(1, holds=(BROKE,), results=SUITES_SKIPPED)}

        # Act
        with fabricated_readings(states, red=("main",)):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [RED_REASON])

    # GREEN_ON_BASE(characterization): a branch behind a green base merges on the base as well.
    # Its readings gained the head's workflow runs, answered as none, which the base does not read.
    def test_Given_ABranchBehindAGreenBase_When_TheMergeIsDecided_Then_NothingBlocksIt(self):
        # Arrange — `gh_git` answers that the branch is behind: the merge-base is not the base's tip.
        # That is what `contains_base` reads, so a decision asking it is told the branch is behind.
        state = types.SimpleNamespace(sha=GREEN, branch="topic", base="main", draft=False,
                                      merge_state="clean", fork=False, state="open")
        answers = {"merge-base": MOVED, "rev-parse": BROKE}
        with contextlib.ExitStack() as stack:
            for name, answer in (
                ("repository", lambda *_: "owner/name"),
                ("pull_request", lambda *_: state),
                ("checks", lambda *_: PASSING),
                ("head_runs", lambda *_: []),
                ("run_jobs", lambda *_: {}),
                ("head_sha", lambda *_: GREEN),
                ("project_state", lambda _project, base: base_state(set(), base)),
                ("gh_git", lambda _project, command, *_: answers[command]),
            ):
                stack.enter_context(mock.patch.object(settle, name, answer, create=True))

            # Act
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    # GREEN_ON_BASE(characterization): the base keeps the one failing workflow as well.
    # Its listing now answers the rules reading too, which the base does not take.
    def test_Given_OneRequiredWorkflowFailedOnTheBase_When_ItsStateIsRead_Then_ThatOneIsKept(self):
        # Arrange — a base other than main, so a reading that asks about main answers wrong here.
        payloads = {"test.yml": {"workflow_runs": [workflow_run(1, "success", GREEN)]},
                    "generators.yml": {"workflow_runs": [workflow_run(1, "failure", BROKE)]}}
        expected = ([failing("generators.yml")],
                    ["repos/owner/name/" + settle.red_base.runs_path(workflow, "2.x")
                     for workflow in ("test.yml", "generators.yml")])
        asked = []

        def listing(path):
            if "/rules/branches/" in path:
                return []
            asked.append(path)
            return payloads[path.split("/actions/workflows/")[1].split("/")[0]]

        with contextlib.ExitStack() as stack:
            for name, answer in (("gh_git", lambda *_: ""),
                                 ("repository", lambda *_: "owner/name"),
                                 ("worktree_branches", lambda *_: set()),
                                 ("rest_json", listing)):
                stack.enter_context(mock.patch.object(settle, name, answer))
            stack.enter_context(mock.patch.object(settle.published_check, "unpublished_reason",
                                                  lambda *_, **__: None))
            stack.enter_context(mock.patch.object(settle, "release_commit", lambda *_: None))

            # Act
            red = settle.project_state(Path("."), "2.x").red

        # Assert
        self.assertEqual((red, asked), expected)


@contextlib.contextmanager
def project_readings(release_commit):
    """`project_state` with every reading but the release commit answered as nothing to report."""
    with contextlib.ExitStack() as stack:
        for name, answer in (("gh_git", lambda *_: ""),
                             ("repository", lambda *_: "owner/name"),
                             ("worktree_branches", lambda *_: set()),
                             ("rest_json", lambda path: [] if "/rules/branches/" in path
                              else {"workflow_runs": []})):
            stack.enter_context(mock.patch.object(settle, name, answer))
        stack.enter_context(mock.patch.object(settle.published_check, "unpublished_reason",
                                              lambda *_, **__: None))
        stack.enter_context(mock.patch.object(settle, "release_commit", release_commit))
        yield


class ReleaseCommitMergeTests(unittest.TestCase):
    """What the base's newest release commit does to the merge decision."""

    def test_Given_AHeadLackingTheBasesNewestReleaseCommit_When_Decided_Then_ItIsRefused(self):
        # Arrange
        states = {1: fabricate(1)}

        # Act
        with fabricated_readings(states, releasing=("main",)):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [RELEASE_REASON])

    def test_Given_AHeadHoldingTheNewestReleaseCommitAndBehindLaterOnes_When_Decided_Then_NothingBlocksIt(self):
        # Arrange — `holds` names the release commit alone, so the head lacks everything after it.
        states = {1: fabricate(1, holds=(RELEASED,))}

        # Act
        with fabricated_readings(states, releasing=("main",)):
            decided = settle.blocking_reasons(Path("."), 1).reasons

        # Assert
        self.assertEqual(decided, [])

    def test_Given_ABase_When_ItsStateIsRead_Then_ItsReleaseCommitIsWhatItHolds(self):
        # Arrange
        asked = []

        def release_commit(_project, base):
            asked.append(base)
            return (RELEASED, "2.1.0")

        with project_readings(release_commit):
            # Act
            release = settle.project_state(Path("."), "2.x").release

        # Assert
        self.assertEqual((release, asked), ((RELEASED, "2.1.0"), ["2.x"]))

    def test_Given_ABase_When_ItsReleaseCommitIsRead_Then_ItIsAskedOfThatBaseOnOrigin(self):
        # Arrange
        read, asked = settle.release_commit, []

        def dated(_project, rev, **_):
            asked.append(rev)
            return (RELEASED, "2.1.0")

        with mock.patch.object(settle.published_check, "release_commit", dated):
            # Act
            read(Path("."), "2.x")

        # Assert
        self.assertEqual(asked, ["origin/2.x"])

    def test_Given_AReleaseCommitGitCannotRead_When_ItIsRead_Then_ItRaisesWhatTheWatcherCatches(self):
        # Arrange — `watch` catches RuntimeError per pull request; anything else stops the watcher.
        read = settle.release_commit

        def unreadable(*_, **__):
            raise subprocess.CalledProcessError(128, ["git", "log"])

        with mock.patch.object(settle.published_check, "release_commit", unreadable):
            # Act / Assert
            with self.assertRaises(RuntimeError):
                read(Path("."), "2.x")


def workflow_run(number, conclusion, sha=BROKE, status="completed", attempt=1):
    """One entry of the runs listing, carrying the fields `failing_run` reads."""
    return {"run_number": number, "status": status, "conclusion": conclusion, "head_sha": sha,
            "run_attempt": attempt}


class RedBaseRunTests(unittest.TestCase):
    """Which run of a base's push runs `red_base.failing_run` takes as the verdict."""

    def verdict(self, *entries):
        return settle.red_base.failing_run("test.yml", {"workflow_runs": list(entries)})

    def test_Given_TheNewestRunFailed_When_Read_Then_ItIsTheVerdict(self):
        # Act / Assert
        self.assertEqual(self.verdict(workflow_run(2, "failure"), workflow_run(1, "success", GREEN)),
                         failing())

    def test_Given_TheNewestRunPassedOverAnOlderFailure_When_Read_Then_NothingIsFailing(self):
        # Act / Assert
        self.assertIsNone(self.verdict(workflow_run(2, "success", GREEN), workflow_run(1, "failure")))

    def test_Given_ARunStillGoingOverAPass_When_Read_Then_NothingIsFailing(self):
        # Act / Assert — a pending base refuses nothing.
        self.assertIsNone(self.verdict(workflow_run(2, None, status="in_progress"),
                                       workflow_run(1, "success", GREEN)))

    def test_Given_ARunStillGoingOverAFailure_When_Read_Then_TheFailureStands(self):
        # Act / Assert
        self.assertEqual(self.verdict(workflow_run(2, None, MOVED, status="in_progress"),
                                      workflow_run(1, "failure")),
                         failing())

    def test_Given_AReRunStillGoingOverAPass_When_Read_Then_ItIsTheFailureItReRuns(self):
        # Act / Assert — the listing shows the re-run's attempt, so the failure it replaced is gone
        # from the page and the pass before it would otherwise be the verdict.
        self.assertEqual(self.verdict(workflow_run(2, None, status="in_progress", attempt=2),
                                      workflow_run(1, "success", GREEN)),
                         failing())

    def test_Given_ACancelledRunOverAFailure_When_Read_Then_TheFailureStands(self):
        # Act / Assert — a newer push supersedes a run without passing it.
        self.assertEqual(self.verdict(workflow_run(2, "cancelled", MOVED), workflow_run(1, "failure")),
                         failing())

    def test_Given_AConclusionNobodyClassified_When_Read_Then_ItCountsAsAFailure(self):
        # Act / Assert
        self.assertEqual(self.verdict(workflow_run(1, "something_new")), failing())

    def test_Given_RunsListedOldestFirst_When_Read_Then_TheNewestNumberIsTheVerdict(self):
        # Act / Assert
        self.assertIsNone(self.verdict(workflow_run(1, "failure"), workflow_run(2, "success", GREEN)))

    def test_Given_NoRunAtAll_When_Read_Then_NothingIsFailing(self):
        # Act / Assert
        self.assertIsNone(self.verdict())


class UnityRanTests(unittest.TestCase):
    """Whether a head's own suites ran, which the red-base exemption asks."""

    def test_Given_BothSuiteJobsSucceeded_When_Read_Then_TheyRan(self):
        # Act / Assert
        self.assertTrue(settle.red_base.unity_ran(
            [("Required checks (Unity)", "success"), ("Unity tests (EditMode)", "success"),
             ("Unity tests (PlayMode)", "success")]))

    def test_Given_SuiteJobsSkippedUnderAPassingAggregate_When_Read_Then_TheyDidNotRun(self):
        # Act / Assert
        self.assertFalse(settle.red_base.unity_ran(
            [("Required checks (Unity)", "success"), ("Unity tests (EditMode)", "skipped"),
             ("Unity tests (PlayMode)", "skipped")]))

    def test_Given_NoSuiteJobAtAll_When_Read_Then_TheyDidNotRun(self):
        # Act / Assert
        self.assertFalse(settle.red_base.unity_ran([("Required checks (Unity)", "success")]))


class RequiredWorkflowTests(unittest.TestCase):
    def test_Given_TheWorkflowDirectory_When_RequiredChecksAreFound_Then_TheyAreTheWorkflowsRead(self):
        # Arrange
        declaring = re.compile(r"^\s+name: Required checks \(", re.M)
        workflows = Path(__file__).resolve().parents[2] / ".github" / "workflows"

        # Act
        found = sorted(path.name for path in workflows.glob("*.yml")
                       if declaring.search(path.read_text(encoding="utf-8")))

        # Assert
        self.assertEqual(found, sorted(settle.red_base.REQUIRED_WORKFLOWS))

    def test_Given_TestYml_When_ItsSuiteJobIsRead_Then_ItsNameCarriesTheUnityPrefix(self):
        # Arrange
        prefix = settle.red_base.UNITY_TESTS
        workflow = Path(__file__).resolve().parents[2] / ".github" / "workflows" / "test.yml"

        # Act
        names = re.findall(r"^  unity-tests:\n    name: (.*)$",
                           workflow.read_text(encoding="utf-8"), re.M)

        # Assert
        self.assertEqual([name.startswith(prefix + " (") for name in names], [True])


class PullRequestBaseTests(unittest.TestCase):
    """Which branch the decision is taken against, once more than one of them takes pull requests.

    Every case above poses a pull request based on `main`, which is why nothing noticed that the
    base was a constant until a maintenance branch was cut and its release could not be merged.
    """

    def test_Given_APullRequestBasedOnAMaintenanceBranch_When_ItsReasonsAreRead_Then_TheyNameIt(self):
        # Arrange
        states = {1: fabricate(1, base=RED)}

        # Act
        with fabricated_readings(states):
            decided = settle.blocking_reasons(Path("."), 1, None).reasons

        # Assert
        self.assertEqual(decided, [RED_REASON.replace("main", RED)])

    def test_Given_PullRequestsOnTwoBases_When_OnePollReadsThem_Then_EachBaseIsAskedOnce(self):
        # Arrange — the dict a poll carries, which is what keeps N pull requests at one fetch per
        # base rather than one per pull request.
        states = {1: fabricate(1), 2: fabricate(2, base="2.x"), 3: fabricate(3)}
        asked, shared = [], {}

        # Act
        with fabricated_readings(states) as stack:
            stack.enter_context(mock.patch.object(
                settle, "project_state",
                lambda _project, base: (asked.append(base), base_state(set(), base))[1]))
            for number in sorted(states):
                settle.blocking_reasons(Path("."), number, None, states=shared)

        # Assert
        self.assertEqual(asked, ["main", "2.x"])

    def test_Given_TwoPollsOverOneBase_When_TheyRun_Then_TheSecondReadsItAgain(self):
        # Arrange — the cache is a poll's, not a watcher's: a base whose release is dispatched
        # between two polls has to stop blocking at the next poll rather than at the next watcher.
        states = {1: fabricate(1)}
        asked = []
        with tempfile.TemporaryDirectory(prefix="settle-poll-") as directory:
            with fabricated_readings(states) as stack:
                for name, path in (("READY_STATE", Path(directory) / "ready"),
                                   ("HEARTBEAT", Path(directory) / "beat"),
                                   ("LOCK", Path(directory) / "lock")):
                    stack.enter_context(mock.patch.object(settle.watcher_state, name, path))
                stack.enter_context(mock.patch.object(
                    settle, "project_state",
                    lambda _project, base: (asked.append(base), base_state(set(), base))[1]))
                stack.enter_context(mock.patch.object(settle.time, "sleep",
                                                      side_effect=[None, Polled]))
                stack.enter_context(contextlib.redirect_stdout(io.StringIO()))

                # Act
                try:
                    settle.watch(Path("."), None)
                except Polled:
                    pass

        # Assert
        self.assertEqual(asked, ["main", "main"])

    def test_Given_AnUpdateOfAPullRequestOnAMaintenanceBranch_When_Refused_Then_ItNamesThatBranch(self):
        # Arrange
        printed = io.StringIO()
        refs = {".head.ref": "release/2.1.1", ".base.ref": "2.x"}
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "rest", lambda _path, jq: refs[jq]))
            stack.enter_context(mock.patch.object(settle, "gh_git", lambda *_: ""))
            stack.enter_context(mock.patch.object(settle, "contains_base", lambda *_: True))
            stack.enter_context(mock.patch.object(settle, "worktree_branches", lambda *_: set()))
            stack.enter_context(contextlib.redirect_stderr(printed))

            # Act
            settle.update(Path("."), 733, None)

        # Assert
        self.assertIn("already contains origin/2.x", printed.getvalue())


class UpdateReasonsTests(unittest.TestCase):
    """The update side of the same decision: what makes bringing the base in the wrong move."""

    def test_Given_ABranchBehindTheBase_When_Decided_Then_NothingBlocksTheUpdate(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=False)

        # Assert
        self.assertEqual(reasons, [])

    def test_Given_ABranchAlreadyHoldingTheBase_When_Decided_Then_ItIsRefused(self):
        # Act — an update that merges nothing still pushes, and a push re-runs every check.
        reasons = settle.update_reasons("feat/x", "main", holds_base=True, held_by_worktree=False)

        # Assert
        self.assertEqual(len(reasons), 1)

    def test_Given_ABranchHeldByAWorktree_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=True)

        # Assert
        self.assertEqual(len(reasons), 1)

    def test_Given_BothConditions_When_Decided_Then_EachIsReportedOnce(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=True, held_by_worktree=True)

        # Assert
        self.assertEqual(len(reasons), 2)

    def test_Given_ARefusal_When_ItsTextIsRead_Then_ItNamesTheBranch(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=True)

        # Assert
        self.assertIn("feat/x", reasons[0])


class RepositorySlugTests(unittest.TestCase):
    """The remote forms that have to yield owner/name, since a wrong slug 404s only at merge time.

    Five of the eight were measured wrong before this: both trailing-slash forms, both host-alias
    forms, and the one naming no owner. A bare `alias:` is what a `Host` entry in ~/.ssh/config puts
    in remote.origin.url.
    """

    def test_Given_AnScpStyleRemote_When_Parsed_Then_ItIsOwnerAndName(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("git@github.com:s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_AnHttpsRemote_When_Parsed_Then_ItIsOwnerAndName(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet"),
                         "s4k10503/velvet")

    def test_Given_AnHttpsRemoteWithATrailingSlash_When_Parsed_Then_TheOwnerIsNotDropped(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet/"),
                         "s4k10503/velvet")

    def test_Given_ADotGitRemoteWithATrailingSlash_When_Parsed_Then_TheSuffixIsStillRemoved(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet.git/"),
                         "s4k10503/velvet")

    def test_Given_AnSshUrlRemote_When_Parsed_Then_TheHostIsNotCountedAsTheOwner(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("ssh://git@github.com/s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_AnSshHostAlias_When_Parsed_Then_TheAliasIsNotKeptInTheSlug(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("gh:s4k10503/velvet.git"), "s4k10503/velvet")

    def test_Given_AnSshHostAliasCarryingDashes_When_Parsed_Then_TheAliasIsNotKeptInTheSlug(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("velvet-alias:s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_ARemoteNamingNoOwner_When_Parsed_Then_ItRaisesRatherThanReturningHalfASlug(self):
        # Act / Assert
        self.assertRaises(RuntimeError, settle.repository_slug, "https://github.com/velvet")


def runs(*entries):
    """A check-runs payload carrying one entry per (name, status, conclusion) triple."""
    listed = [{"name": name, "status": status, "conclusion": conclusion}
              for name, status, conclusion in entries]
    return {"total_count": len(listed), "check_runs": listed}


# What the commit-status endpoint answered for a commit carrying no status at all: a rollup state of
# "pending" beside an empty list. Reading the rollup rather than the entries blocks such a commit.
NO_STATUSES = {"state": "pending", "total_count": 0, "statuses": []}


class CheckResultTests(unittest.TestCase):
    """The buckets the decision is made from, built out of the two payloads that carry them."""

    def test_Given_ACompletedSuccess_When_Bucketed_Then_NothingBlocks(self):
        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), NO_STATUSES)

        # Assert
        self.assertEqual(reasons(results=results), [])

    def test_Given_AFailingConclusion_When_Decided_Then_TheMergeIsRefused(self):
        # Arrange — the whole point of the table: a conclusion that must never reach TERMINAL_PASS.
        results = settle.check_results(runs(("Unity", "completed", "failure")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(reasons(results=results), ["failing at aaaaaaa: Unity=fail"])

    def test_Given_EveryMappedConclusion_When_ComparedToTheTerminalSets_Then_OnlyThreeLetAMergeThrough(self):
        # Act
        passing = sorted(name for name, bucket in settle._BUCKET.items()
                         if bucket in settle.TERMINAL_PASS)

        # Assert
        self.assertEqual(passing, ["neutral", "skipped", "success"])

    def test_Given_AConclusionTheTableDoesNotCarry_When_Bucketed_Then_ItBlocks(self):
        # Arrange — GitHub adding a conclusion must not merge unclassified.
        results = settle.check_results(runs(("Unity", "completed", "invented_by_github")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results, [{"name": "Unity", "bucket": "fail"}])

    def test_Given_ARunStillQueued_When_Bucketed_Then_ItIsPendingRatherThanFailing(self):
        # Arrange — a run carrying no conclusion yet is unfinished, not one that concluded badly.
        results = settle.check_results(runs(("Unity", "queued", None)), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results, [{"name": "Unity", "bucket": "pending"}])

    def test_Given_ANameCarryingATab_When_Bucketed_Then_ItArrivesWhole(self):
        # Arrange — the name is read as a field, not split out of one line of text.
        results = settle.check_results(runs(("Unity\ttests", "completed", "success")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results[0]["name"], "Unity\ttests")

    def test_Given_ACommitCarryingNoStatusAtAll_When_Bucketed_Then_ThePendingRollupIsNotRead(self):
        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), NO_STATUSES)

        # Assert
        self.assertEqual(len(results), 1)

    def test_Given_AFailingLegacyCommitStatus_When_Decided_Then_ItBlocksLikeACheckRun(self):
        # Arrange — a required context can be a commit status instead of an Actions check run.
        statuses = {"state": "failure", "total_count": 1,
                    "statuses": [{"context": "external/ci", "state": "failure"}]}

        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), statuses)

        # Assert
        self.assertEqual(reasons(results=results), ["failing at aaaaaaa: external/ci=fail"])

    def test_Given_APageThatDidNotCarryEveryRun_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — the run that arrived passes, so nothing else in the decision would block.
        truncated = {"total_count": 2, "check_runs": [{"name": "Unity", "status": "completed",
                                                       "conclusion": "success"}]}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results, truncated, NO_STATUSES)

    def test_Given_APageThatDidNotCarryEveryCommitStatus_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — every entry that did arrive is passing, so `whole_page` is the only thing
        # between this payload and a green merge; its docstring owns why.
        truncated = {"state": "failure", "total_count": 31,
                     "statuses": [{"context": f"external/ci-{index}", "state": "success"}
                                  for index in range(30)]}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results,
                          runs(("Unity", "completed", "success")), truncated)

    def test_Given_APageThatCarriedNoRunAtAll_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — a page that dropped every entry it claims, which is the one truncation whose
        # buckets are also what a head with no workflow leaves. `whole_page` is what separates them,
        # so an empty list must not be short-circuited past it.
        truncated = {"total_count": 3, "check_runs": []}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results, truncated, NO_STATUSES)


class CheckReadTests(unittest.TestCase):
    """The paths the two check surfaces are read from: neither leaves its page size to the API."""

    def test_Given_BothCheckSurfaces_When_Read_Then_EachPathAsksForAPageSize(self):
        # Arrange
        asked = []
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(
                settle, "rest_json", lambda path: (asked.append(path), NO_STATUSES)[1]))

            # Act
            settle.checks(Path("."), GREEN)

        # Assert
        self.assertEqual(asked, [f"repos/owner/name/commits/{GREEN}/check-runs?per_page=100",
                                 f"repos/owner/name/commits/{GREEN}/status?per_page=100"])


class ForkReadingTests(unittest.TestCase):
    """Which two names decide a fork, since getting it wrong makes every pull request one."""

    PAYLOAD = {"head": {"sha": GREEN, "ref": "topic", "repo": {"full_name": "Owner/Velvet"}},
               "base": {"ref": "main", "repo": {"full_name": "Owner/Velvet"}},
               "draft": False, "mergeable_state": "clean"}

    def read(self, payload, slug):
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: slug))
            stack.enter_context(mock.patch.object(settle, "rest_json", lambda *_: payload))
            return settle.pull_request(Path("."), 1)

    def test_Given_AHeadOnTheSameRepository_When_TheSlugIsCasedDifferently_Then_ItIsNoFork(self):
        # Arrange — a clone made with different capitals answers everywhere and reads back canonical,
        # so a comparison against the remote URL's spelling calls every pull request a fork and the
        # ready state empties for good.
        read = self.read(self.PAYLOAD, "owner/velvet")

        # Act / Assert
        self.assertFalse(read.fork)

    def test_Given_AHeadOnAnotherRepository_When_ItIsRead_Then_ItIsAFork(self):
        # Arrange — the control: a comparison that never says fork is not a comparison.
        payload = dict(self.PAYLOAD, head=dict(self.PAYLOAD["head"],
                                               repo={"full_name": "somebody/velvet"}))

        # Act / Assert
        self.assertTrue(self.read(payload, "Owner/Velvet").fork)


class RepositoryReadTests(unittest.TestCase):
    """Which checkout the slug is read from, since --project points this at one that is not the cwd."""

    def test_Given_AProjectThatIsNotTheCwd_When_TheSlugIsRead_Then_ItComesFromThatCheckout(self):
        # Arrange
        with repository_holding("topic") as project:
            subprocess.run(["git", "-C", str(project), "remote", "add", "origin",
                            "https://github.com/elsewhere/other.git"], capture_output=True, check=True)

            # Act / Assert
            self.assertEqual(settle.repository(project), "elsewhere/other")


def stubbed_readings(head=GREEN, branch="topic", title="A title", body="A body"):
    """Every reading settle.merge takes, answered without a network — `repository` included.

    Left real, `repository` shells out to git in whatever directory the tests were started from. In
    a copy of scripts/ outside a checkout it raised, and it raised inside the merge call before any
    assertion about that call — reporting as an error rather than a failure, so a run that stubbed
    only the other readings looked like it had proved something and had not.
    """
    return [mock.patch.object(settle, "repository", lambda *_: "owner/name"),
            mock.patch.object(settle, "blocking_reasons",
                              lambda *_: settle.Blocking([], head, branch, [], "main")),
            mock.patch.object(settle, "pull_request_text", lambda *_: (title, body))]


def merge_request(**readings):
    """The arguments settle.merge hands `gh`, with the cleanup stubbed out too."""
    sent = []
    with contextlib.ExitStack() as stack:
        for patch in stubbed_readings(**readings):
            stack.enter_context(patch)
        stack.enter_context(mock.patch.object(settle, "delete_merged_branch", lambda *_: []))
        stack.enter_context(mock.patch.object(settle, "gh",
                                              lambda *args: (sent.append(args), "")[1]))
        stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        settle.merge(Path("."), 592, "main", dry_run=False)
    return sent[0]


class MergeRequestTests(unittest.TestCase):
    """What the merge request itself carries, which no reading of the decision would catch."""

    def test_Given_AMergeNothingBlocks_When_Sent_Then_ItCarriesTheHeadTheChecksWereReadAt(self):
        # Arrange — a push landing after the last reading has to lose, not win by arriving late.
        sent = merge_request()

        # Act / Assert
        self.assertIn("sha={}".format(GREEN), sent)

    def test_Given_AMergeNothingBlocks_When_Sent_Then_TheBodyIsThePullRequestsOwnDescription(self):
        # Arrange — left out, GitHub composes one from the branch commits and repeats their trailers.
        sent = merge_request(body="What changed and why")

        # Act / Assert
        self.assertIn("commit_message=What changed and why", sent)

    def test_Given_AMergeThatWentThrough_When_ItReturns_Then_TheBranchIsDeleted(self):
        # Arrange
        deleted = []
        with contextlib.ExitStack() as stack:
            for patch in stubbed_readings():
                stack.enter_context(patch)
            stack.enter_context(mock.patch.object(settle, "gh", lambda *args: ""))
            stack.enter_context(mock.patch.object(
                settle, "delete_merged_branch", lambda project, branch: (deleted.append(branch),
                                                                         [])[1]))
            stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
            settle.merge(Path("."), 592, "main", dry_run=False)

        # Act / Assert
        self.assertEqual(deleted, ["topic"])

    def test_Given_AMergeOntoAMaintenanceBranch_When_ItReturns_Then_ItNamesTheBaseItLandedOn(self):
        # Arrange — the decision runs rather than being stubbed, so the base printed is the one the
        # pull request named rather than one this case handed in.
        printed = io.StringIO()
        with fabricated_readings({592: fabricate(592, base="2.x")}) as stack:
            stack.enter_context(mock.patch.object(settle, "pull_request_text",
                                                  lambda *_: ("A title", "A body")))
            stack.enter_context(mock.patch.object(settle, "gh", lambda *args: ""))
            stack.enter_context(mock.patch.object(settle, "delete_merged_branch", lambda *_: []))
            stack.enter_context(contextlib.redirect_stdout(printed))

            # Act
            settle.merge(Path("."), 592, None, dry_run=False)

        # Assert
        self.assertIn("squashed onto 2.x", printed.getvalue())

    def test_Given_AMergeThatWentThrough_When_ItReturns_Then_ItSaysTheMergeHappened(self):
        # Arrange — a dry run that decided nothing and a merge that landed both exit 0.
        printed = io.StringIO()
        with contextlib.ExitStack() as stack:
            for patch in stubbed_readings():
                stack.enter_context(patch)
            stack.enter_context(mock.patch.object(settle, "gh", lambda *args: ""))
            stack.enter_context(mock.patch.object(settle, "delete_merged_branch", lambda *_: []))
            stack.enter_context(contextlib.redirect_stdout(printed))
            settle.merge(Path("."), 592, "main", dry_run=False)

        # Act / Assert
        self.assertIn("PR#592 merged:", printed.getvalue())


@contextlib.contextmanager
def repository_holding(branch):
    """A throwaway repository on `main` with `branch` also present, since deletion needs a real one."""
    with tempfile.TemporaryDirectory(prefix="settle-test-") as directory:
        project = Path(directory)
        git = ["git", "-C", str(project), "-c", "user.email=t@example.com", "-c", "user.name=t"]
        subprocess.run(["git", "init", "-b", "main", str(project)], capture_output=True, check=True)
        subprocess.run(git + ["commit", "--allow-empty", "-m", "init"],
                       capture_output=True, check=True)
        subprocess.run(git + ["branch", branch], capture_output=True, check=True)
        yield project


def local_branches(project):
    listing = subprocess.run(["git", "-C", str(project), "for-each-ref", "--format=%(refname:short)",
                              "refs/heads"], capture_output=True, text=True, check=True)
    return sorted(listing.stdout.split())


class LocalBranchDeletionTests(unittest.TestCase):
    """The half `gh pr merge --delete-branch` did and a REST ref delete cannot reach."""

    def test_Given_AMergedBranchPresentLocally_When_Deleted_Then_ItIsGoneFromTheCheckout(self):
        # Arrange
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: ""):
                settle.delete_merged_branch(project, "topic")

            # Assert
            self.assertEqual(local_branches(project), ["main"])

    def test_Given_ABranchNeverCheckedOutLocally_When_Deleted_Then_NothingIsReported(self):
        # Arrange — work done from a detached worktree leaves no local ref, which is not a failure.
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: ""):
                failures = settle.delete_merged_branch(project, "never-existed")

            # Assert
            self.assertEqual(failures, [])

    def test_Given_ARemoteDeleteThatFailed_When_ItReturns_Then_TheFailureIsReportedNotSwallowed(self):
        # Arrange
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: "HTTP 403"):
                failures = settle.delete_merged_branch(project, "topic")

            # Assert
            self.assertEqual(failures, ["the remote branch topic survived: HTTP 403"])


class BranchDeletionTests(unittest.TestCase):
    def test_Given_ARefDeleteThatFoundNothing_When_Read_Then_ItIsNotReportedAsAFailure(self):
        # Arrange — the repository deletes the head on merge, so this DELETE usually arrives second.
        stderr = "gh: Reference does not exist (HTTP 422)"

        # Act / Assert
        self.assertTrue(settle.reference_already_gone(stderr))

    def test_Given_ARefDeleteRefusedForAnyOtherReason_When_Read_Then_ItIsReported(self):
        # Act / Assert
        self.assertFalse(settle.reference_already_gone("gh: Resource not accessible (HTTP 403)"))


class TerminalStateTests(unittest.TestCase):
    def test_Given_TheTerminalSets_When_Compared_Then_NoBucketIsInBoth(self):
        # Arrange — a bucket in both would make a failing check merge or a passing one block.
        overlap = settle.TERMINAL_PASS & settle.TERMINAL_FAIL

        # Act / Assert
        self.assertEqual((len(settle.TERMINAL_PASS) > 0, overlap), (True, set()))

    def test_Given_APendingBucket_When_ClassifiedAgainstBothSets_Then_ItIsInNeither(self):
        # Act / Assert
        self.assertNotIn("pending", settle.TERMINAL_PASS | settle.TERMINAL_FAIL)


if __name__ == "__main__":
    unittest.main(verbosity=2)

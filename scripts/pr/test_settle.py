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
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path
from unittest import mock

GREEN = "a" * 40
MOVED = "b" * 40


def load_module():
    """Imports settle by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("settle", Path(__file__).with_name("settle.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


settle = load_module()


def reasons(before=GREEN, after=GREEN, results=None, branch="topic", base="main",
            holds_base=True, held_by_worktree=False, unpublished_release=None, draft=False,
            merge_state="clean", fork=False):
    if results is None:
        results = [{"name": "Required checks (Unity)", "bucket": "pass"}]
    return settle.reasons_from(before, after, results, branch, base, holds_base, held_by_worktree,
                               unpublished_release, draft, merge_state, fork)


class MergeDecisionTests(unittest.TestCase):
    def test_Given_EveryCheckPassedAndNothingElseBlocks_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertEqual(reasons(), [])

    def test_Given_TheHeadMovedWhileChecksWereRead_When_Decided_Then_NothingElseIsReported(self):
        # Arrange — the readings straddle a force-push, so they are not about one commit.
        results = [{"name": "Unity", "bucket": "fail"}]

        # Act
        decided = reasons(after=MOVED, results=results, holds_base=False, held_by_worktree=True)

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

    def test_Given_ABranchBehindItsBase_When_Decided_Then_ItBlocksThoughEveryCheckPassed(self):
        # Arrange — GitHub reports BEHIND only where the base requires up-to-date heads, which this
        # repository deliberately does not, so mergeStateStatus reads CLEAN here.
        decided = reasons(holds_base=False)

        # Act / Assert
        self.assertEqual(decided, ["does not contain origin/main: merge it in and let the checks run again"])

    def test_Given_AWorktreeHoldingTheBranch_When_Decided_Then_ItBlocksBeforeTheMergeHappens(self):
        # Arrange — the local delete would otherwise fail once the merge had already happened.
        decided = reasons(held_by_worktree=True)

        # Act / Assert
        self.assertTrue(any("worktree holds topic" in reason for reason in decided))

    def test_Given_SeveralIndependentProblems_When_Decided_Then_EachIsReported(self):
        # Arrange — reporting one at a time costs a round of CI per reason.
        results = [{"name": "Unity", "bucket": "pending"}]

        # Act
        decided = reasons(results=results, holds_base=False, held_by_worktree=True)

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

    def test_Given_APullRequestConflictingWithTheBase_When_Decided_Then_TheConflictIsNamedBesideIt(self):
        # Arrange — a conflicting branch does not contain the base either, so both are asked at once:
        # the conflict alone would be reported by a branch that is merely behind.
        decided = reasons(merge_state="dirty", holds_base=False)

        # Act / Assert
        self.assertEqual(decided, [
            "it conflicts with main: resolve the conflict in the branch, which `settle.py update` "
            "declines to do",
            "does not contain origin/main: merge it in and let the checks run again"])

    def test_Given_AHeadOnAFork_When_Decided_Then_ItBlocksWithoutClaimingAContainmentReading(self):
        # Arrange — `holds_base` is the default nothing computed for a fork, so the case asks that
        # the reason naming it is absent as well as that the fork reason is there.
        decided = reasons(fork=True, holds_base=False)

        # Act / Assert
        self.assertEqual(decided, [
            "its head is on another repository: this settles branches on origin, so nothing here "
            "read whether it contains origin/main"])

    def test_Given_ADirtyStateBesideAContainmentThatHolds_When_Decided_Then_ItIsWhatBlocks(self):
        # Arrange — the state the predicate earns its keep in, and the reason it is read rather than
        # left to the message: the containment reading is of the tip the cycle's fetch saw, GitHub's
        # is of a newer one, so a branch can hold the first and conflict with the second. Posed
        # beside `unknown` to keep the absence of a reading from becoming a reason.
        counted = (len(reasons(merge_state="dirty", holds_base=True)),
                   len(reasons(merge_state="unknown", holds_base=True)))

        # Act / Assert
        self.assertEqual(counted, (1, 0))


# One pull request's whole state, so `watch` and `merge` can be posed the same table.
Fabricated = collections.namedtuple(
    "Fabricated", "sha after branch draft merge_state results holds_base held fork")

PASSING = [{"name": "Required checks (Unity)", "bucket": "pass"}]


def fabricate(number, results=PASSING, draft=False, merge_state="clean", holds_base=True,
              held=False, moved=False, fork=False):
    sha = str(number).rjust(40, "0")
    return Fabricated(sha=sha, after=MOVED if moved else sha, branch=f"topic-{number}", draft=draft,
                      merge_state=merge_state, results=results, holds_base=holds_base, held=held,
                      fork=fork)


@contextlib.contextmanager
def fabricated_readings(states):
    """Every reading a poll takes, answered from a table of pull request states.

    Patched at the readings rather than at `blocking_reasons`, so the decision itself is what runs:
    stubbing the verdict would make the agreement below true by construction.
    """
    by_sha = {state.sha: state for state in states.values()}
    by_branch = {state.branch: state for state in states.values()}
    with contextlib.ExitStack() as stack:
        for name, answer in (
            ("repository", lambda *_: "owner/name"),
            ("open_pull_requests", lambda *_: sorted(states)),
            ("pull_request", lambda _project, number: settle.PullRequest(
                states[number].sha, states[number].branch, states[number].draft,
                states[number].merge_state, states[number].fork)),
            ("checks", lambda _project, sha: by_sha[sha].results),
            ("head_sha", lambda _project, number: states[number].after),
            ("contains_base", lambda _project, branch, _base: (
                _refuse_for_a_fork(by_branch[branch]) if by_branch[branch].fork
                else by_branch[branch].holds_base)),
            ("project_state", lambda *_: settle.ProjectState(
                {state.branch for state in states.values() if state.held}, None)),
        ):
            stack.enter_context(mock.patch.object(settle, name, answer))
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
                               ("LOCK", Path(directory) / "lock")):
                stack.enter_context(mock.patch.object(settle.watcher_state, name, path))
            stack.enter_context(mock.patch.object(settle.time, "sleep", side_effect=Polled))
            stack.enter_context(contextlib.redirect_stdout(printed))
            try:
                settle.watch(Path("."), "main")
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
                if not settle.blocking_reasons(Path("."), number, "main").reasons}


class ReadinessTests(unittest.TestCase):
    """What the watcher records as ready, against what the merge would take.

    Two readings of one question is what this is here to stop. Asked separately they disagreed on a
    draft with conflicts, which sat in the ready state while `refuse/edit_while_a_ready_pr_sits.py`
    refused every Edit and Write in every session and named a merge that could not take it.
    """

    # Every state the two readings could differ on, plus the ordinary green one so the table is not
    # made of exceptions alone.
    TABLE = {
        1: fabricate(1),
        2: fabricate(2, draft=True),
        3: fabricate(3, merge_state="dirty", holds_base=False),
        4: fabricate(4, holds_base=False),
        5: fabricate(5, held=True),
        6: fabricate(6, results=[{"name": "Unity", "bucket": "pending"}]),
        7: fabricate(7, results=[{"name": "Unity", "bucket": "fail"}]),
        8: fabricate(8, results=[]),
        9: fabricate(9, moved=True),
        10: fabricate(10, draft=True, merge_state="dirty", holds_base=False),
        11: fabricate(11, fork=True),
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


class ForkMergeTests(unittest.TestCase):
    """What `settle.py merge` does with a head this checkout has no ref for."""

    def test_Given_AForkPullRequest_When_TheMergeIsDecided_Then_ItIsRefusedRatherThanRaising(self):
        # Arrange — `contains_base` is what would run on `origin/<a branch on the fork>`, and it
        # exits 128 rather than answering, so a merge decided without the fork reading raises out of
        # a command whose whole job is to report what blocks.
        printed = io.StringIO()
        with fabricated_readings({8: fabricate(8, fork=True)}):
            with contextlib.redirect_stderr(printed):
                # Act
                code = settle.merge(Path("."), 8, "main", dry_run=True)

        # Assert
        self.assertEqual((code, "head is on another repository" in printed.getvalue()), (1, True))


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
    """What a reader may conclude from the file, which is what both guards conclude from it."""

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

    @contextlib.contextmanager
    def heartbeat(self, text):
        with tempfile.TemporaryDirectory(prefix="settle-beat-") as directory:
            path = Path(directory) / "beat"
            path.write_text(text)
            with mock.patch.object(settle.watcher_state, "HEARTBEAT", path):
                yield


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
               "base": {"repo": {"full_name": "Owner/Velvet"}},
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
                              lambda *_: settle.Blocking([], head, branch, [])),
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

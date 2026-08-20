#!/usr/bin/env python3
"""Holds the hand-rolled-poller guard to both directions of its verdict.

A refusing guard fails silently in two directions. Refusing nothing looks like a session that wrote
no poller; refusing everything looks like a guard working, until a legitimate wait becomes
impossible. So the allowances below are cases rather than commentary: the
one-shot merge the guard must not touch, a wait on a file the watcher does not write, and the same
poller under every way `watcher_state.alive` says nothing is watching.

The guard is run rather than imported. Its verdict is an exit code a `PreToolUse` event reads, and
the two readings that decide it — the heartbeat under `HOME`, and the backgrounding flag on the tool
input — are environment rather than argument.

Run: python3 scripts/hooks/test_hand_rolled_pr_poller.py
"""

import json
import os
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/hand_rolled_pr_poller.py"

sys.path.insert(0, str(REPO_ROOT / "scripts" / "pr"))
from watcher_state import HEARTBEAT, LOCK, READY_STATE, STALE_AFTER, beat  # noqa: E402

# A verdict is the exit code paired with the guard being there to give it. python3 exits 2 for a
# script it cannot open, which is the code a PreToolUse refusal exits, so the code alone reads a
# deleted guard as a refusing one — and every refusal case below passed against a tree without it.
REFUSED, ALLOWED = (True, 2), (True, 0)

SETTLE = "python3 /w/scripts/pr/settle.py"

# One pull request poll, in the shape a re-derived watcher takes: ask whether the merge would go
# through, and ask again in a minute.
POLL = (f'until {SETTLE} merge 702 --dry-run 2>&1 | grep -q "would merge"; do sleep 60; done')

# A process id no process has. Chosen by asking rather than by picking a number, because a live one
# would make the reading below say "a watcher is running" for the opposite reason.
def unused_pid():
    for candidate in range(60000, 70000):
        try:
            os.kill(candidate, 0)
        except ProcessLookupError:
            return candidate
        except PermissionError:
            continue
    raise RuntimeError("no unused process id in the scanned range, so a dead watcher cannot be posed")


class HookRun:
    """A HOME the heartbeat is written into, and the guard's verdict on one command posed against it."""

    def __init__(self, heartbeat=None):
        self.heartbeat = heartbeat

    def __enter__(self):
        self.home = tempfile.mkdtemp(prefix="velvet-poller-")
        if self.heartbeat is not None:
            (Path(self.home) / HEARTBEAT.name).write_text(self.heartbeat, encoding="utf-8")
        return self

    def __exit__(self, *_):
        for entry in Path(self.home).iterdir():
            entry.unlink()
        os.rmdir(self.home)
        return False

    def verdict(self, command, background=False, tool="Bash"):
        payload = {"tool_name": tool, "cwd": str(REPO_ROOT),
                   "tool_input": {"command": command, "run_in_background": background}}
        environment = dict(os.environ, HOME=self.home)
        run = subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(payload),
                             capture_output=True, text=True, env=environment, timeout=60)
        return HOOK.exists(), run.returncode


def watching():
    """A heartbeat a live watcher would have just written."""
    return beat(os.getpid())


def stale():
    return beat(os.getpid(), now=time.time() - STALE_AFTER - 60)


class BackgroundedPollerTests(unittest.TestCase):
    """The shapes a session wrote beside the committed watcher, one case per way of spelling it."""

    def test_Given_ALiveWatcher_When_APollIsBackgroundedByTheToolFlag_Then_ItIsRefused(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(POLL, background=True)

        # Assert
        self.assertEqual(verdict, REFUSED, "a poll detached by the tool's own flag runs unattended")

    def test_Given_ALiveWatcher_When_APollIsBackgroundedByAnAmpersand_Then_ItIsRefused(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(f"( {POLL} ) &")

        # Assert
        self.assertEqual(verdict, REFUSED, "a subshell ampersand detaches without the tool's flag")

    def test_Given_ALiveWatcher_When_PollsAreFannedOutAcrossPullRequests_Then_ItIsRefused(self):
        # Arrange
        fanned = (f'for n in 701 702; do ( until {SETTLE} merge $n --dry-run | grep -q "would merge"; '
                  "do sleep 60; done ) & done; wait")

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(fanned)

        # Assert
        self.assertEqual(verdict, REFUSED, "the counter is unexpanded and the wait inside it is not")

    def test_Given_ALiveWatcher_When_APollLoopsOnATrueCondition_Then_ItIsRefused(self):
        # Arrange
        forever = (f'while true; do {SETTLE} merge 702 --dry-run | grep -q "would merge" && break; '
                   "sleep 60; done &")

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(forever)

        # Assert
        self.assertEqual(verdict, REFUSED, "a break inside it still leaves the wait unattended")

    def test_Given_ALiveWatcher_When_APollAsksGhDirectly_Then_ItIsRefused(self):
        # Arrange — the quota is what the two share, so a poll that never loads settle.py draws on
        # the watcher's as much as one that does.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict("until gh pr checks 702 | grep -q pass; do sleep 60; done &")

        # Assert
        self.assertEqual(verdict, REFUSED, "gh is the other program that spends the shared rate limit")

    def test_Given_ALiveWatcher_When_AWaitBusyLoopsWithoutSleeping_Then_ItIsRefused(self):
        # Arrange — no sleep anywhere, so the loop keyword is the only term saying this repeats.
        busy = f'until {SETTLE} merge 702 --dry-run | grep -q "would merge"; do :; done &'

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(busy)

        # Assert
        self.assertEqual(verdict, REFUSED, "a busy wait repeats without a sleep to say so")

    def test_Given_ALiveWatcher_When_APollNamesGhOnlyInsideASubstitution_Then_ItIsRefused(self):
        # Arrange — the program is inside a `$( )`, which reaches a tokeniser as one token rather
        # than as a command word.
        hidden = ('while [ "$(gh pr checks 702 2>&1 | grep -ci pending)" != "0" ]; do sleep 30; done &')

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(hidden)

        # Assert
        self.assertEqual(verdict, REFUSED, "a quoted substitution still runs the program inside it")

    def test_Given_ALiveWatcher_When_AWaitSleepsWithoutALoopKeyword_Then_ItIsRefused(self):
        # Arrange — the mirror of the case above: a wait spelled without while or until.
        spaced = f"( {SETTLE} merge 702 --dry-run; sleep 60; {SETTLE} merge 702 ) &"

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(spaced)

        # Assert
        self.assertEqual(verdict, REFUSED, "a sleep between two asks is the poll interval")


class WatcherFileWaitTests(unittest.TestCase):
    """Waits on the files the watcher writes rather than on the programs that write them."""

    def test_Given_ALiveWatcher_When_AWaitPollsTheReadyFile_Then_ItIsRefused(self):
        # Arrange
        waiting = (f"until [ -s {READY_STATE} ] && grep -qE '^[0-9]+ ' {READY_STATE}; "
                   "do sleep 45; done")

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(waiting, background=True)

        # Assert
        self.assertEqual(verdict, REFUSED, "the ready file is the watcher's output, not a second source")

    def test_Given_ALiveWatcher_When_AWaitPollsTheHeartbeatThroughHome_Then_ItIsRefused(self):
        # Arrange — spelled through $HOME, which the shell has not expanded when the guard reads it.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(f"while [ -f $HOME/{HEARTBEAT.name} ]; do sleep 30; done &")

        # Assert
        self.assertEqual(verdict, REFUSED, "the file name survives a home the guard cannot resolve")

    def test_Given_ALiveWatcher_When_AWaitPollsTheLockFile_Then_ItIsRefused(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(f"until [ ! -f {LOCK} ]; do sleep 30; done &")

        # Assert
        self.assertEqual(verdict, REFUSED, "waiting for the lock to clear is waiting on the watcher")


class SanctionedCommandTests(unittest.TestCase):
    """What a session legitimately runs, which a guard that refused it would make impossible."""

    def test_Given_ALiveWatcher_When_AOneShotMergeIsBackgrounded_Then_ItIsAllowed(self):
        # Arrange — the command this guard's own refusal names as the way out.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(f"{SETTLE} merge 702", background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "merging once is the action the refusal asks for")

    def test_Given_ALiveWatcher_When_TheReadyFileIsReadOnce_Then_ItIsAllowed(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(f"cat {READY_STATE}", background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "reading the watcher's output is not polling it")

    def test_Given_ALiveWatcher_When_GhIsFannedOutWithoutAWait_Then_ItIsAllowed(self):
        # Arrange — a list this walks to its end, rather than a condition it waits on.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict("for n in 700 701 702; do ( gh pr view $n ) & done; wait")

        # Assert
        self.assertEqual(verdict, ALLOWED, "a bounded fan-out ends without anyone retiring it")

    def test_Given_ALiveWatcher_When_GhIsWalkedOverInputLines_Then_ItIsAllowed(self):
        # Arrange — a batch edit reading its work from a file. The loop keyword heads it, and it
        # ends with the input rather than with a condition somebody else has to satisfy.
        batch = 'while read n label; do gh pr edit "$n" --add-label "$label"; done < labels.txt &'

        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(batch)

        # Assert
        self.assertEqual(verdict, ALLOWED, "reading lines to their end is a walk rather than a wait")

    def test_Given_ALiveWatcher_When_ThePollRunsInTheForeground_Then_ItIsAllowed(self):
        # Arrange — it blocks the call that started it, so it ends when that call does.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(POLL)

        # Assert
        self.assertEqual(verdict, ALLOWED, "a foreground wait blocks the call that posed it")

    def test_Given_ALiveWatcher_When_AWaitPollsAFileTheWatcherDoesNotWrite_Then_ItIsAllowed(self):
        # Arrange — the boundary a wider subject would cross: a wait on somebody else's lock.
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict("until [ ! -f /tmp/build.lock ]; do sleep 5; done &")

        # Assert
        self.assertEqual(verdict, ALLOWED, "a lock the watcher does not write is another program's")

    def test_Given_ALiveWatcher_When_AWaitPollsATestResultsFile_Then_ItIsAllowed(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict("until [ -s Logs/results.xml ]; do sleep 45; done", background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "waiting on a suite is the ordinary shape of a long run")


class NoWatcherTests(unittest.TestCase):
    """Three ways the heartbeat says nothing is watching, where nothing is being duplicated."""

    def test_Given_NoHeartbeatAtAll_When_ThePollIsPosed_Then_ItIsAllowed(self):
        # Arrange
        with HookRun() as run:
            # Act
            verdict = run.verdict(POLL, background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "with nothing watching, this poll duplicates nothing")

    def test_Given_AHeartbeatOlderThanTheWindow_When_ThePollIsPosed_Then_ItIsAllowed(self):
        # Arrange
        with HookRun(stale()) as run:
            # Act
            verdict = run.verdict(POLL, background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "a watcher that stopped writing stopped watching")

    def test_Given_AFreshHeartbeatFromADeadProcess_When_ThePollIsPosed_Then_ItIsAllowed(self):
        # Arrange — a watcher killed inside the window leaves a stamp that is still fresh.
        with HookRun(beat(unused_pid())) as run:
            # Act
            verdict = run.verdict(POLL, background=True)

        # Assert
        self.assertEqual(verdict, ALLOWED, "the stamp outlives the process that vouched for it")


class ToolGateTests(unittest.TestCase):
    def test_Given_ALiveWatcherAndThePoll_When_ItArrivesUnderAnUnroutedToolName_Then_ItIsAllowed(self):
        # Arrange
        with HookRun(watching()) as run:
            # Act
            verdict = run.verdict(POLL, background=True, tool="VelvetNoToolIsCalledThis")

        # Assert
        self.assertEqual(verdict, ALLOWED, "a gate reading something other than the tool name answers here")


if __name__ == "__main__":
    unittest.main(verbosity=2)

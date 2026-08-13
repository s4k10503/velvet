#!/usr/bin/env python3
"""Refuse to write code while a pull request has sat green and unmerged.

Detection was never the problem. The watcher printed every check as it finished, `stop/unsettled_pr.py`
blocked on the same state, and both were right — the pull requests still sat, because a notification
competes with whatever is in hand and loses. What this removes is the choice.

**Age, not existence.** Several branches are normally in flight here and one of them is normally
green, so refusing whenever a ready pull request exists would stop the ordinary case. The defect is a
green pull request nobody returned to, so only one older than GRACE blocks. Below that, parallel work
is exactly what should be happening.

**Unknown blocks too.** The state is written by the watching process, so a dead watcher means an
unread file rather than an empty one — and a guard that reads "I cannot tell" as "nothing to report"
goes quiet precisely when its subject is unobserved. A stale or missing file therefore refuses and
says to start the watcher, which is the one action that can clear it.

Only the editing tools are held. Every command that moves a pull request toward merging is a Bash call,
so the way out stays open while the way deeper in does not.
"""

import json
import sys
import time
from pathlib import Path

HOOK_DIRECTORY = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOK_DIRECTORY / "lib"))
sys.path.insert(0, str(HOOK_DIRECTORY.parent.parent / "scripts" / "pr"))
from deferrals import DEFERRALS, deferred, unusable
from watcher_state import READY_STATE, STALE_AFTER, alive, unreadable_beat

# Held on the editing tools, which carry a file path rather than a shell command, so there is no operand
# for the shell to expand and nothing here reads one.
UNEXPANDED_POLICY = "n/a"

UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"file_path": "CHANGELOG.md", "old_string": "a", "new_string": "b"}

# Long enough that a pull request going green mid-task is not an interruption, short enough that
# "I will get to it" cannot outlive the task that said it.
GRACE = 900

# Its own deferral key. A pull request number holds one pull request; this holds the reading itself,
# and the two are not the same claim.
WATCHER_KEY = "watcher"

HOOK_TOOLS = {"Edit", "Write", "NotebookEdit"}


def sitting(now):
    """(number, seconds) for each ready pull request past the grace period and not deferred."""
    try:
        lines = READY_STATE.read_text(encoding="utf-8").splitlines()
    except OSError:
        return []

    found = []
    for line in lines:
        parts = line.split()
        if len(parts) != 2 or not parts[1].isdigit():
            continue
        number, since = parts[0], int(parts[1])
        broken = unusable(number, now)
        if broken is not None:
            print(f"A deferral was written for PR #{number}, and {broken} — so it is being ignored.",
                  file=sys.stderr)
        if now - since < GRACE or deferred(number, now):
            continue
        found.append((number, int(now - since)))
    return found


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    now = time.time()
    if not alive():
        # Both refusals here hold every editing tool in every session; this is the one that had no
        # deferral, and the command it names can itself refuse — a watcher wedged mid-poll holds the
        # lock while its heartbeat goes stale, and starting a replacement is what `hold_the_watch`
        # declines. So the way out of this branch cannot go through the thing that is stuck.
        held = deferred(WATCHER_KEY, now)
        if held is not None:
            reason, minutes = held
            sys.stderr.write(f"Nothing is watching the open pull requests, and {WATCHER_KEY} is held "
                             f"{minutes}m ago because: {reason}\n")
            return 0
        broken = unusable(WATCHER_KEY, now)
        if broken is not None:
            sys.stderr.write(f"A deferral was written for {WATCHER_KEY}, and {broken} — so it is "
                             "being ignored.\n")
        # Two states reach here and they want opposite actions: start a watcher, or end one. Saying
        # "nothing is watching" of the second is this guard's blindness written as a fact about the
        # watcher, and the command it would name refuses while that watcher runs.
        if unreadable_beat(now):
            sys.stderr.write(
                "Refusing to write: a watcher is writing the heartbeat in a form this cannot read, "
                "so whether a pull request is sitting green cannot be read.\n\n"
                "Something IS watching — what failed is the reading. A watcher started before the "
                "heartbeat named its own process writes the older form, and starting a second one "
                "is refused while it runs. End it and start one from this checkout:\n\n"
                "  ps -Ao pid=,command= | grep 'settle[.]py watch'\n"
                "  kill <pid>\n"
                f"  # its last heartbeat ages out within {STALE_AFTER}s, and until it does\n"
                "  # `settle.py watch` refuses to start\n"
                "  python3 scripts/pr/settle.py watch\n\n"
                "If the pause is deliberate, arm the deferral for what the WORK is waiting on; the "
                "reason expires, so it gets re-read rather than forgotten:\n\n"
                f'  echo "{WATCHER_KEY} <what the work is waiting on> {int(now)}" >> {DEFERRALS}\n')
            return 2
        sys.stderr.write(
            "Refusing to write: nothing is watching the open pull requests, so whether one is sitting "
            "green cannot be read.\n\n"
            "An unwatched pull request and none at all look identical from here, which is the state "
            "this guard exists to stop being invisible.\n\n"
            "  python3 scripts/pr/settle.py watch\n\n"
            "That reports what stops it starting, if anything does — a watcher already holding the "
            "lock is named there with the command to end it. If the pause is deliberate, arm the "
            "deferral for what the WORK is waiting on rather than for the watcher being off; the "
            "reason expires, so it gets re-read rather than forgotten:\n\n"
            f'  echo "{WATCHER_KEY} <what the work is waiting on> {int(now)}" >> {DEFERRALS}\n')
        return 2

    found = sitting(now)
    if not found:
        return 0

    lines = "\n".join(f"  #{number} — green and unmerged for {seconds // 60}m" for number, seconds in found)
    sys.stderr.write(
        "Refusing to write: a pull request has been green and unmerged long enough to have been "
        "forgotten.\n\n"
        f"{lines}\n\n"
        "Every check passed and nothing is waiting on it but a decision. Merging is the shorter path "
        "than anything being written now, and it is what a green pull request is for:\n\n"
        "  python3 scripts/pr/settle.py merge <pr>\n\n"
        "That reports what still blocks it, if anything does. If one is held on purpose, say what "
        f"clears it — the reason expires, so it gets re-read rather than forgotten:\n\n"
        f'  echo "<pr> <what clears it> {int(now)}" >> {DEFERRALS}\n')
    return 2


if __name__ == "__main__":
    sys.exit(main())

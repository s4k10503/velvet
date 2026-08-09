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

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from deferrals import DEFERRALS, deferred

# Held on the editing tools, which carry a file path rather than a shell command, so there is no operand
# for the shell to expand and nothing here reads one.
UNEXPANDED_POLICY = "n/a"

READY_STATE = Path.home() / ".velvet-pr-ready"
HEARTBEAT = Path.home() / ".velvet-pr-watch.heartbeat"

# Long enough that a pull request going green mid-task is not an interruption, short enough that
# "I will get to it" cannot outlive the task that said it.
GRACE = 900

# Two polls plus a margin: one missed poll is a slow API call, two is a watcher that stopped.
HEARTBEAT_TTL = 180

HOOK_TOOLS = {"Edit", "Write", "NotebookEdit"}


def watcher_age():
    """Seconds since the watching process last wrote, or None when it never has."""
    try:
        return time.time() - int(HEARTBEAT.read_text(encoding="utf-8").strip())
    except (OSError, ValueError):
        return None


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

    age = watcher_age()
    if age is None or age > HEARTBEAT_TTL:
        sys.stderr.write(
            "Refusing to write: nothing is watching the open pull requests, so whether one is sitting "
            "green cannot be read.\n\n"
            "An unwatched pull request and none at all look identical from here, which is the state "
            "this guard exists to stop being invisible.\n\n"
            "  python3 scripts/pr/settle.py watch\n\n"
            "Run it in the background and this clears within a poll.\n")
        return 2

    now = time.time()
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

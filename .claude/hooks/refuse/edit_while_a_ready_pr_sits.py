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

**A shell write is the same edit.** Held on the editing tools alone this stopped nothing: a session
that makes its changes with `sed`, a heredoc or a short script is outside every one of them by
construction, and the refusal it never met is the one being routed around. So a Bash command that
writes a file this repository tracks is held too — `lib/tracked_writes.py` owns which shapes are read
and how narrow that is, and the refusal below says so rather than implying it saw the rest.

The way out stays open: none of the commands these refusals name writes a tracked file. The
`settle.py` calls and the process reading beside them carry no write operand at all, and the
deferral line appends to `deferrals.DEFERRALS`, which is under HOME.
"""

import json
import sys
import time
from pathlib import Path

HOOK_DIRECTORY = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOK_DIRECTORY / "lib"))
sys.path.insert(0, str(HOOK_DIRECTORY.parent.parent / "scripts" / "pr"))
import tracked_writes
from deferrals import DEFERRALS, deferred, disowned, unusable
from watcher_state import READY_STATE, STALE_AFTER, alive, unreadable_beat

# A shell operand this cannot place is not a file it can say is tracked, so it drops out of the
# reading and the command runs. That is the under-approximation `LIMITS` states.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = "sed -i '' -e s/a/b/ \"$CHANGELOG\""

# Which files a command writes is git's answer, and one it cannot give leaves a shell write
# indistinguishable from a scratch one. It is counted as a repository write rather than let through,
# so an unreadable git hands the verdict to the pull-request reading rather than deciding it.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "sed -i '' -e s/a/b/ CONTRIBUTING.md"}

# Long enough that a pull request going green mid-task is not an interruption, short enough that
# "I will get to it" cannot outlive the task that said it.
GRACE = 900

# Its own deferral key. A pull request number holds one pull request; this holds the reading itself,
# and the two are not the same claim.
WATCHER_KEY = "watcher"

HOOK_TOOLS = {"Bash", "Edit", "Write", "NotebookEdit"}


def written_here(event):
    """The tracked files a Bash event writes, or None when the event is an editing tool.

    None rather than the whole tree, because an editing tool's file path is not read at all: the
    guard holds every one of them, and a Bash call only where it lands on the repository.
    """
    if event.get("tool_name") != "Bash":
        return None
    command = (event.get("tool_input") or {}).get("command", "")
    return tracked_writes.tracked_writes(command, event.get("cwd"))


def coverage(written):
    """What a refusal has to add when a shell command is what reached it."""
    if written is None:
        return ""
    return ("\nThis command writes " + ", ".join(sorted(written)) + ", which this repository "
            "tracks, so it is the edit the Edit and Write tools are held for.\n\n"
            + tracked_writes.LIMITS + "\n")


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
        for reason, whose in disowned(number, now):
            print(f"Session {whose} deferred PR #{number} as \"{reason}\", which does not suppress "
                  "here.", file=sys.stderr)
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
    written = written_here(event)
    # Before the watcher is read, so that a shell command landing nowhere near the repository is the
    # same event as one this guard is not routed at all — which is what lets its stated answer to an
    # unexpanded operand be one answer rather than whatever the pull requests happen to be doing.
    if written is not None and not written:
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
        for reason, whose in disowned(WATCHER_KEY, now):
            sys.stderr.write(f"Session {whose} deferred {WATCHER_KEY} as \"{reason}\", which does "
                             "not suppress here.\n")
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
                f'  echo "{WATCHER_KEY} <what the work is waiting on> {int(now)} $CLAUDE_CODE_SESSION_ID" >> {DEFERRALS}\n'
                + coverage(written))
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
            f'  echo "{WATCHER_KEY} <what the work is waiting on> {int(now)} $CLAUDE_CODE_SESSION_ID" >> {DEFERRALS}\n'
            + coverage(written))
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
        f'  echo "<pr> <what clears it> {int(now)} $CLAUDE_CODE_SESSION_ID" >> {DEFERRALS}\n\n'
        "A deferral claims somebody read the hold and judged it deliberate, so a cause invented for a "
        "pull request you have never opened is the one shape of it that is false. Not owning it is "
        "itself a reason, and it is one you can state truthfully — with the telling done rather than "
        "intended, since that is the only part that moves the pull request:\n\n"
        f'  echo "<pr> held by <owner>, who has been asked to settle it {int(now)} $CLAUDE_CODE_SESSION_ID" >> {DEFERRALS}\n'
        + coverage(written))
    return 2


if __name__ == "__main__":
    sys.exit(main())

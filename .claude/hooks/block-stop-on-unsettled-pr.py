#!/usr/bin/env python3
"""Block Stop while an open PR still has a check that has not reached a terminal state.

A run that is already green is not the failure this guards. The failure is the assistant reading a
check list once, seeing "pending", turning to something else, and never looking again — twice in one
session, for 7h45m and for 33m, the second time after writing the rule that was supposed to prevent
it. An intention to re-check is not a mechanism; a Stop that refuses to return is.

The judgement is two-stage on purpose. An empty check list is NOT "still running": it means no
workflow was ever triggered for that SHA, which is what a cancelled run followed by a force-push
leaves behind, and reading it as pending is how the 7h45m gap started. So a PR with zero checks
blocks too, with a different reason.

A PR whose checks have all passed and that nobody has merged is the state this exists for as much as
a pending one. It reads as "settled", and settled is what an assistant stops on — eight green PRs
sat unmerged for nine hours behind a watcher that was alive, emitting nothing, because it only
reported checks CHANGING state and every check had finished changing. A live watcher with nothing
left to say is indistinguishable from progress.

A pending check is forgiven while a watcher is demonstrably alive. The heartbeat is written by the
watching process itself on each poll, never by the assistant, so it cannot be satisfied by intending
to watch — if the watcher dies the file goes stale within one poll and this blocks again. The
watcher must re-enumerate open PRs every cycle, or a PR opened after it started would be unwatched
while the heartbeat still looked fresh. Zero checks is judged without the heartbeat: there, nothing
is running for a watcher to observe.

Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".
"""

import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

from deferrals import DEFERRALS, deferred  # noqa: E402

HEARTBEAT = Path.home() / ".velvet-pr-watch.heartbeat"
# Three polls of the 60s cycle, so one slow `gh` call does not read as a dead watcher.
STALE_AFTER = 180

# Merge states that explain an absent check list without anything being wrong with it.
EXPECTED_WITHOUT_CHECKS = {"UNSTABLE", "BEHIND", "BLOCKED", ""}


def gh(args):
    """Return (stdout, combined output, exit code) for a gh call."""
    try:
        result = subprocess.run(["gh", *args], capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.SubprocessError) as error:
        return "", str(error), 1
    return result.stdout.strip(), (result.stdout + result.stderr).strip(), result.returncode


def watcher_is_alive():
    """True while a watching process is still writing its heartbeat.

    Same two bounds as the deferral expiry, and for the same reason: a malformed stamp must not
    vouch for a watcher, and one in the future must not vouch for it permanently.
    """
    try:
        beat = HEARTBEAT.read_text(encoding="utf-8").strip()
    except OSError:
        return False
    if not beat.isdigit():
        return False
    age = time.time() - int(beat)
    return 0 <= age < STALE_AFTER


def merge_state(pr):
    state, _, code = gh(["pr", "view", pr, "--json", "mergeStateStatus",
                         "--jq", ".mergeStateStatus"])
    return state if code == 0 else ""


def checks_of(pr):
    out, _, code = gh(["pr", "checks", pr, "--json", "name,bucket"])
    if code != 0:
        return []
    try:
        return json.loads(out or "[]")
    except ValueError:
        return []


def unreadable(output, code):
    print(f"""Do not stop: the open pull requests could not be read, so nothing here says they are settled.

  gh pr list exited {code}
{output}

If gh is unauthenticated or the network is down, say so and arm the deferral rather than treating
an unanswered question as a settled one:

  echo "backlog <what clears it> $(date +%s)" >> {DEFERRALS}""", file=sys.stderr)
    return 2


def judge(pr):
    """The reason this PR blocks a Stop, or None when it does not."""
    checks = checks_of(pr)

    if not checks:
        # Zero checks is legitimate when nothing the PR touches matches a workflow's path filter —
        # a docs-only or .claude/-only change reports none and is ready to merge. It is a problem
        # when the head never got a run it should have, and the tell is the merge state: a
        # conflicting PR reports DIRTY (or UNKNOWN while GitHub is still computing) and never starts
        # CI at all, which is the shape that went unwatched for seven hours.
        state = merge_state(pr)
        if state == "CLEAN":
            # Ready, and ready is the state that reads as finished. A docs-only or .claude/-only
            # change reports no checks at all and so never reached the merge reminder below, which
            # is the same hole one level down from the one that left eight green PRs sitting.
            return (f"  PR #{pr} — no checks apply to it and it is unmerged. Merge it, or say what "
                    "it is waiting on and arm\n    something that brings you back when that arrives.")
        if state in EXPECTED_WITHOUT_CHECKS:
            return None
        return (f"  PR #{pr} — no checks reported and merge state is {state}. A conflicting PR never "
                "starts CI, so\n    this is not 'still running'. Rebase it, or check the head SHA "
                "against\n    'gh run list --branch <b> --json headSha' if you expected a run.")

    pending = [check for check in checks if check.get("bucket") == "pending"]
    if not pending:
        # Nothing is running, so a watcher has nothing to observe and its heartbeat vouches for
        # nothing.
        state = merge_state(pr)
        # A cancelled check is counted here rather than nowhere. In gh's buckets it is neither
        # `pass` nor `fail`, so a run that was cancelled outright used to read as every check
        # having passed.
        fails = [c for c in checks if c.get("bucket") in ("fail", "cancel")]
        if fails:
            return (f"  PR #{pr} — {len(fails)} check(s) failed or were cancelled. Read the run, fix "
                    "or say why it is not yours\n    to fix. A cancelled run does not restart on "
                    "its own.")
        if state == "CLEAN":
            return (f"  PR #{pr} — every check passed and it is unmerged. Merge it, or say what it "
                    "is waiting on and arm\n    something that brings you back when that arrives.")
        # Every other merge state, rather than the ones seen so far. Listing them made an unlisted
        # state mean "settled", which is how a green DIRTY or DRAFT pull request passed both guards.
        return (f"  PR #{pr} — checks are settled but the merge state is {state or 'unknown'}, so it "
                "cannot go green on\n    its own. Rebase it, take it out of draft, or say what it is "
                "waiting on.")

    if watcher_is_alive():
        return None

    names = ", ".join(check.get("name", "") for check in pending)
    return f"  PR #{pr} — {len(pending)} check(s) still pending: {names}"


def main():
    if shutil.which("gh") is None:
        return 0

    listing, combined, code = gh(["pr", "list", "--state", "open", "--json", "number",
                                  "--jq", ".[].number"])
    if code != 0:
        return unreadable(combined, code)
    prs = listing.split()
    if not prs:
        return 0

    blocked, held = [], []
    for pr in prs:
        holding = deferred(pr)
        if holding is not None:
            reason, minutes = holding
            held.append(f"  PR #{pr} — held {minutes}m ago because: {reason}")
            continue
        reason = judge(pr)
        if reason is not None:
            blocked.append(reason)

    if not blocked:
        # A held PR still gets said out loud on the way past, so the claim is re-read rather than
        # trusted.
        if held:
            print("Held, not settled — check each reason is still true:", file=sys.stderr)
            print("\n" + "\n".join(held), file=sys.stderr)
        return 0

    held_block = ("\nHeld on purpose, and worth re-reading:\n" + "\n".join(held)) if held else ""
    print(f"""Do not stop: an open PR has not settled.

{chr(10).join(blocked)}
{held_block}

Holding one on purpose is allowed and expires after 45 minutes, so the reason gets re-examined
rather than forgotten:

  echo "<pr> <what clears it> {int(time.time())}" >> {DEFERRALS}

Otherwise: wait for it with a Monitor that emits on both pass and fail, or keep working on
something that is itself on the critical path. Work that is off the critical path satisfies
"do not idle" while the thing you are actually waiting on goes unwatched.

A pending check stops blocking once a watcher writes {HEARTBEAT} on each
poll. It must re-enumerate open PRs every cycle — a watcher pinned to one PR number leaves the
next one unwatched behind a fresh heartbeat:

  while true; do
    date +%s > "$HOME/.velvet-pr-watch.heartbeat"
    for pr in $(gh pr list --state open --json number --jq '.[].number'); do
      : # emit each check that has reached a terminal state, once
    done
    sleep 60
  done""", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

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
to watch — if the watcher dies the file goes stale within one poll and this blocks again, and
`scripts/pr/watcher_state.py` owns what else a reader has to establish before believing it. The
watcher must re-enumerate open PRs every cycle, or a PR opened after it started would be unwatched
while the heartbeat still looked fresh. Zero checks is judged without the heartbeat: there, nothing
is running for a watcher to observe.

Exit 2 with output on stderr is what Stop reads as "do not stop, here is why".
"""

import json
import shutil
import subprocess
import sys
from pathlib import Path

HOOK_DIRECTORY = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(HOOK_DIRECTORY / "lib"))
sys.path.insert(0, str(HOOK_DIRECTORY.parent.parent / "scripts" / "pr"))

from deferrals import DEFERRALS, deferred, unusable  # noqa: E402
from repository import SELF_REPORT, open_pull_requests, unreadable_report  # noqa: E402
from watcher_state import HEARTBEAT, alive  # noqa: E402

UNREADABLE_POLICY = "refuse"

# Merge states that explain an absent check list without anything being wrong with it. The empty
# string is NOT one of them: it used to stand for both "GitHub answered nothing" and "the read
# failed", and the second is what put a pull request nothing was read about into this set.
EXPECTED_WITHOUT_CHECKS = {"UNSTABLE", "BEHIND", "BLOCKED"}


def gh(args):
    """Return (stdout, combined output, exit code) for a gh call."""
    try:
        result = subprocess.run(["gh", *args], capture_output=True, text=True, timeout=60)
    except (OSError, subprocess.SubprocessError) as error:
        return "", str(error), 1
    return result.stdout.strip(), (result.stdout + result.stderr).strip(), result.returncode


# Both of these go through GraphQL while the listing above can fall back to REST, so the state this
# guard has to survive is not only "nothing answered": the listing can answer and these not. A pull
# request read that far and no further is one this guard knows nothing about, and None is how it says
# so — an empty answer from either is a different claim and keeps its own path.
def merge_state(pr):
    """The pull request's merge state, or None when the read did not answer."""
    state, _, code = gh(["pr", "view", pr, "--json", "mergeStateStatus",
                         "--jq", ".mergeStateStatus"])
    return state if code == 0 else None


def carries_no_check(pr):
    """Whether the head really carries no check, asked a way that answers instead of erroring.

    The rollup is read for whether it is an empty list and nothing else — the buckets stay
    `gh pr checks`'s to name, so this adds no second copy of what a conclusion means.

    Read whole rather than through `--jq '.statusCheckRollup | length'`: jq answers 0 for a field
    that is absent or null exactly as it does for one that is an empty list, so the shorter form
    turns a payload this could not understand into "there are none" — which is the reading that put
    an unread pull request into the settled set in the first place. Only an empty list says so.
    """
    out, _, code = gh(["pr", "view", pr, "--json", "statusCheckRollup"])
    if code != 0:
        return False
    try:
        rollup = json.loads(out or "{}").get("statusCheckRollup")
    except ValueError:
        return False
    return isinstance(rollup, list) and not rollup


def checks_of(pr):
    """The pull request's check list, or None when the read did not answer.

    `gh pr checks` says "there are none" by failing, and it says "I could not read" two ways: by
    failing, and — the shape `scripts/pr/settle.py` records for an exhausted quota — by succeeding
    and printing nothing. So neither an empty answer nor a failed one is decided here. Both go to the
    rollup, which answers where this errors, and only an empty list from there makes this empty.

    Reading a failure as "there are none" is what let a pull request nobody could see into the
    settled set; reading "there are none" as a failure blocks one whose head has no check yet.
    """
    out, _, code = gh(["pr", "checks", pr, "--json", "name,bucket"])
    if code == 0 and out:
        try:
            listed = json.loads(out)
        except ValueError:
            return None
        if not isinstance(listed, list):
            return None
        if listed:
            return listed
    # Every way of arriving at "there are none" ends here — the call failing, printing nothing, or
    # printing an empty list — because none of the three is something this can tell from a failure.
    return [] if carries_no_check(pr) else None


def unreadable(attempts):
    """Every way of asking failed, so this blocks on the guard's own blindness and says which."""
    # Its own key, not "backlog": that one holds the backlog guard, and one line silencing both
    # guards is the unqualified exemption the expiry exists to prevent.
    holding = deferred("pr-list")
    if holding is not None:
        reason, minutes = holding
        print(f"The pull requests could not be read, and pr-list is held {minutes}m ago "
              f"because: {reason}", file=sys.stderr)
        return 0
    broken = unusable("pr-list")
    if broken is not None:
        print(f"A deferral was written for pr-list, and {broken} — so it is being ignored.",
              file=sys.stderr)
    print(unreadable_report("the open pull requests", attempts, "pr-list",
                            '`gh pr view <n>`, `gh run list --branch <b>`, or the output of the watcher `scripts/pr/settle.py watch` writes'), file=sys.stderr)
    return 2


def unread(pr, what):
    """A pull request this guard learned nothing about, said as a fact about the guard."""
    return (f"  PR #{pr} — {what} could not be read. {SELF_REPORT} PR #{pr}, and nothing here says "
            "it is\n    settled. Read it another way and say what you found.")


def judge(pr):
    """The reason this PR blocks a Stop, or None when it does not."""
    checks = checks_of(pr)
    if checks is None:
        return unread(pr, "its checks")

    if not checks:
        # Zero checks is legitimate when nothing the PR touches matches a workflow's path filter —
        # a docs-only or .claude/-only change reports none and is ready to merge. It is a problem
        # when the head never got a run it should have, and the tell is the merge state: a
        # conflicting PR reports DIRTY (or UNKNOWN while GitHub is still computing) and never starts
        # CI at all, which is the shape that went unwatched for seven hours.
        state = merge_state(pr)
        if state is None:
            return unread(pr, "its merge state")
        if state == "CLEAN":
            # Ready, and ready is the state that reads as finished. A docs-only or .claude/-only
            # change reports no checks at all and so never reached the merge reminder below, which
            # is the same hole one level down from the one that left eight green PRs sitting.
            return (f"  PR #{pr} — no checks apply to it and it is unmerged. Merge it, or say what "
                    "it is waiting on and arm\n    something that brings you back when that arrives.")
        if state in EXPECTED_WITHOUT_CHECKS:
            return None
        return (f"  PR #{pr} — no checks reported and merge state is {state or 'unnamed'}. A "
                "conflicting PR never starts CI, so\n    this is not 'still running'. Rebase it, or "
                "check the head SHA against\n    'gh run list --branch <b> --json headSha' if you "
                "expected a run.")

    pending = [check for check in checks if check.get("bucket") == "pending"]
    if not pending:
        # Nothing is running, so a watcher has nothing to observe and its heartbeat vouches for
        # nothing.
        state = merge_state(pr)
        if state is None:
            return unread(pr, "its merge state")
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
        return (f"  PR #{pr} — checks are settled but the merge state is {state or 'unnamed'}, so it "
                "cannot go green on\n    its own. Rebase it, take it out of draft, or say what it is "
                "waiting on.")

    if alive():
        return None

    names = ", ".join(check.get("name", "") for check in pending)
    return f"  PR #{pr} — {len(pending)} check(s) still pending: {names}"


def main():
    if shutil.which("gh") is None:
        return 0

    reading = open_pull_requests()
    if reading.numbers is None:
        return unreadable(reading.attempts)
    prs = reading.numbers
    if not prs:
        return 0

    blocked, held, ignored = [], [], []
    for pr in prs:
        broken = unusable(pr)
        if broken is not None:
            ignored.append(f"  PR #{pr} — a deferral was written for it, and {broken}.")
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
            # A held pull request is skipped above before `judge`, so every one of them held means
            # no pull request was read this time. Under the ordinary heading that list reads as
            # "each was checked and each is held", which is a different claim — and the count
            # separates the two without any reading of its own.
            print("Every open pull request is held, so none of them was read this time. What "
                  "follows is what was claimed, not what is true of them now:"
                  if len(held) == len(prs) else
                  "Held, not settled — check each reason is still true:", file=sys.stderr)
            print("\n" + "\n".join(held), file=sys.stderr)
        if ignored:
            print("\nDeferrals that were ignored:", file=sys.stderr)
            print("\n".join(ignored), file=sys.stderr)
        return 0

    held_block = ("\nHeld on purpose, and worth re-reading:\n" + "\n".join(held)) if held else ""
    ignored_block = ("\nDeferrals that were ignored:\n" + "\n".join(ignored)) if ignored else ""
    print(f"""Do not stop: an open PR has not settled.

{chr(10).join(blocked)}
{held_block}
{ignored_block}

Holding one on purpose is allowed and expires after 45 minutes, so the reason gets re-examined
rather than forgotten:

  echo "<pr> <what clears it> $(date +%s)" >> {DEFERRALS}

Otherwise: wait for it with a Monitor that emits on both pass and fail, or keep working on
something that is itself on the critical path. Work that is off the critical path satisfies
"do not idle" while the thing you are actually waiting on goes unwatched.

A pending check stops blocking once a watcher writes {HEARTBEAT} on each
poll. Run the committed one rather than writing another — the hand-written ones have been pinned to
a single PR number, which leaves the next one unwatched behind a fresh heartbeat:

  python3 scripts/pr/settle.py watch

And merge through the same script. It reads the head SHA on both sides of the check list, so a
force-push between them voids the answer instead of merging a SHA nothing tested, and it declines
while the branch is behind main or held by a worktree:

  python3 scripts/pr/settle.py merge <pr> --dry-run""", file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Watch open pull requests, and merge one only when every precondition holds.

Both halves existed as instructions rather than as code: `stop/unsettled_pr.py` printed a watcher for
the reader to reimplement, and the merge was typed by hand. An instruction is re-derived each time and
re-derived wrong — the watcher has been written pinned to one pull request number, leaving the next
one unwatched behind a fresh heartbeat, which is the exact failure the hook's own text warns about.

**Every precondition below is also a refuse hook**, one apiece, so a merge typed without this script
is held to the same four. What this adds is reporting them together: one run names everything wrong
rather than costing a round of CI per reason. If a precondition is worth having here it belongs in a
hook, because a script nobody is obliged to run guards nothing.

Four preconditions, each from a merge that went wrong rather than from a list of good practice:

- **Checks are bound to the head SHA they were read at.** `gh pr checks` answers about whatever the
  API last recorded, which after a force-push is the previous commit's run. A green read was once
  carried into a merge decision for a SHA that had never been tested. So the head is read, then the
  checks, then the head again, and a change between the two readings voids the answer.
- **The branch must contain the current base.** `mergeStateStatus` reports CLEAN for a branch whose
  tests never saw a commit that is now on main: GitHub reports BEHIND only where the base requires
  up-to-date heads, which this repository deliberately does not. So the merge-base is compared
  directly.
- **No worktree may hold the branch.** `gh pr merge --delete-branch` deletes the remote branch, then
  fails to delete the local one, and reports that failure as a line of output after the merge has
  already happened. Nothing is left to retry and the leftover looks like an unmerged branch.
- **An empty check list is not "still running".** It means no workflow was ever triggered for that
  SHA, which is what a cancelled run followed by a force-push leaves behind.

Run: python3 scripts/pr/settle.py watch
     python3 scripts/pr/settle.py merge <number>
"""

import argparse
import json
import subprocess
import sys
import time
from pathlib import Path

HEARTBEAT = Path.home() / ".velvet-pr-watch.heartbeat"

# When each pull request first read as ready, so a guard can ask how long one has sat rather than
# whether one exists. Several are usually in flight here and one of them is usually green, so the
# existence of a ready pull request is the ordinary state and only its age is a defect.
READY_STATE = Path.home() / ".velvet-pr-ready"

POLL_SECONDS = 60

# What the checks API calls a state that will not change again. "skipping" is terminal and passing:
# the Unity jobs are skipped wholesale on a fork with no licence, which is what lets one merge at all.
TERMINAL_PASS = frozenset({"pass", "skipping"})
TERMINAL_FAIL = frozenset({"fail", "cancel"})


def gh(*args, check=True):
    result = subprocess.run(["gh", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise RuntimeError("gh {} failed: {}".format(" ".join(args), result.stderr.strip()))
    return result.stdout


def open_pull_requests():
    return json.loads(gh("pr", "list", "--state", "open", "--json", "number,headRefName") or "[]")


def head_sha(number):
    return json.loads(gh("pr", "view", str(number), "--json", "headRefOid"))["headRefOid"]


def checks(number):
    """Check results, or an empty list when no workflow ever ran for this head."""
    result = subprocess.run(["gh", "pr", "checks", str(number), "--json", "name,bucket"],
                            capture_output=True, text=True)
    # A pull request with no checks at all exits non-zero with nothing on stdout, which is a state
    # this reports rather than an error it raises.
    return json.loads(result.stdout) if result.stdout.strip() else []


def worktree_branches(project):
    """Branch names currently checked out in a worktree, which cannot be deleted while they are."""
    held = set()
    for line in gh_git(project, "worktree", "list", "--porcelain").splitlines():
        if line.startswith("branch "):
            held.add(line.split(" ", 1)[1].strip().removeprefix("refs/heads/"))
    return held


def gh_git(project, *args):
    result = subprocess.run(["git", "-C", str(project), *args], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(args), result.stderr.strip()))
    return result.stdout


def contains_base(project, branch, base):
    """Whether the branch already holds every commit on the base.

    Asked of the remote refs rather than of local ones, because a local base can be behind what the
    merge will actually happen against and would report a stale answer as a clean one.
    """
    gh_git(project, "fetch", "origin", base, "--quiet")
    merge_base = gh_git(project, "merge-base", f"origin/{base}", f"origin/{branch}").strip()
    base_head = gh_git(project, "rev-parse", f"origin/{base}").strip()
    return merge_base == base_head


def reasons_from(before, after, results, branch, base, holds_base, held_by_worktree):
    """Every reason not to merge, decided from plain data so the decision is testable without a network.

    A moved head returns alone: with the readings straddling a force-push, nothing else read here is
    known to be about the same commit, so reporting the rest would be reporting about two SHAs at once.
    """
    if before != after:
        return [f"head moved from {before[:7]} to {after[:7]} while its checks were being read"]

    reasons = []
    if not results:
        reasons.append(f"no check has run for {after[:7]}: a workflow was never triggered for this head")

    unfinished = [entry["name"] for entry in results
                  if entry["bucket"] not in TERMINAL_PASS and entry["bucket"] not in TERMINAL_FAIL]
    failed = [f"{entry['name']}={entry['bucket']}" for entry in results
              if entry["bucket"] in TERMINAL_FAIL]
    if unfinished:
        reasons.append("still pending at {}: {}".format(after[:7], ", ".join(sorted(unfinished))))
    if failed:
        reasons.append("failing at {}: {}".format(after[:7], ", ".join(sorted(failed))))

    if not holds_base:
        reasons.append(f"does not contain origin/{base}: merge it in and let the checks run again")

    if held_by_worktree:
        reasons.append(f"a worktree holds {branch}: remove it first, or --delete-branch half-fails "
                       f"after the merge has already happened")

    return reasons


def blocking_reasons(project, number, base):
    """reasons_from, with every reading taken from the repository and the API."""
    before = head_sha(number)
    results = checks(number)
    after = head_sha(number)
    branch = json.loads(gh("pr", "view", str(number), "--json", "headRefName"))["headRefName"]
    return reasons_from(before, after, results, branch, base,
                        holds_base=contains_base(project, branch, base),
                        held_by_worktree=branch in worktree_branches(project))


def write_ready_state(ready, since):
    """Record each ready pull request beside the time it first read that way.

    Rewritten whole every poll, so a merged one leaves and a newly ready one arrives without the file
    accumulating. `since` carries entries across polls; without it every poll would reset the clock a
    guard reads and nothing would ever look stale.
    """
    for number in list(since):
        if number not in ready:
            del since[number]
    for number in ready:
        since.setdefault(number, int(time.time()))
    READY_STATE.write_text("".join(f"{number} {since[number]}\n" for number in sorted(since)))


def watch(project, base):
    """Emit each check that reaches a terminal state, once, and hold the heartbeat open meanwhile."""
    seen = set()
    ready_since = {}
    while True:
        HEARTBEAT.write_text(f"{int(time.time())}\n")
        try:
            pull_requests = open_pull_requests()
        except RuntimeError as error:
            print(f"! {error}", flush=True)
            time.sleep(POLL_SECONDS)
            continue

        ready = set()
        for entry in pull_requests:
            number = entry["number"]
            try:
                sha = head_sha(number)
                results = checks(number)
            except RuntimeError as error:
                print(f"! PR#{number}: {error}", flush=True)
                continue

            for result in results:
                if result["bucket"] == "pending":
                    continue
                line = "PR#{} {} {} => {}".format(number, sha[:7], result["name"], result["bucket"])
                if line not in seen:
                    seen.add(line)
                    print(line, flush=True)

            if results and all(r["bucket"] in TERMINAL_PASS for r in results):
                # A pull request that finished green and sits unmerged is what this exists for as much
                # as a pending one: a watcher reporting only state CHANGES goes silent on it forever.
                ready.add(number)
                line = f"PR#{number} {sha[:7]} READY: every check terminal and passing"
                if line not in seen:
                    seen.add(line)
                    print(line, flush=True)

        write_ready_state(ready, ready_since)
        time.sleep(POLL_SECONDS)


def merge(project, number, base, dry_run):
    reasons = blocking_reasons(project, number, base)
    if reasons:
        print(f"Refusing to merge PR#{number}:", file=sys.stderr)
        for reason in reasons:
            print(f"  - {reason}", file=sys.stderr)
        return 1

    if dry_run:
        print(f"PR#{number} would merge: no blocking reason")
        return 0

    subprocess.run(["gh", "pr", "merge", str(number), "--squash", "--delete-branch"], check=True)
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--base", default="main", help="branch merged into (default: main)")
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("watch", help="emit terminal check results for every open pull request")
    merge_parser = sub.add_parser("merge", help="merge one pull request if nothing blocks it")
    merge_parser.add_argument("number", type=int)
    merge_parser.add_argument("--dry-run", action="store_true",
                              help="report what blocks it and merge nothing")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    if args.command == "watch":
        watch(project, args.base)
        return 0
    return merge(project, args.number, args.base, args.dry_run)


if __name__ == "__main__":
    sys.exit(main())

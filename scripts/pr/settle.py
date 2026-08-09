#!/usr/bin/env python3
"""Watch open pull requests, and merge one only when every precondition holds.

Both halves existed as instructions rather than as code: `stop/unsettled_pr.py` printed a watcher for
the reader to reimplement, and the merge was typed by hand. An instruction is re-derived each time and
re-derived wrong — the watcher has been written pinned to one pull request number, leaving the next
one unwatched behind a fresh heartbeat, which is the exact failure the hook's own text warns about.

**Every precondition below is also a refuse hook**, one apiece, so a merge typed without this script
is held to the same five. What this adds is reporting them together: one run names everything wrong
rather than costing a round of CI per reason. If a precondition is worth having here it belongs in a
hook, because a script nobody is obliged to run guards nothing.

Five preconditions, each from a merge that went wrong rather than from a list of good practice:

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
- **The base must not hold an unpublished release.** A closed CHANGELOG section reaches main as an
  ordinary commit, and the dispatch that publishes it is separate; v2.0.1 sat there through twelve
  merges, each of which the eventual release carried and its note described none of.
  `scripts/release/published_check.py` owns that decision.

Run: python3 scripts/pr/settle.py watch
     python3 scripts/pr/settle.py merge <number>
"""

import argparse
import importlib.util
import json
import shutil
import subprocess
import tempfile
import sys
import time
from pathlib import Path


def load_published_check():
    """Imports the release guard by path, since scripts/ holds no packages.

    Its own directory goes on the path first: the module reads the CHANGELOG heading grammar out of
    release_notes.py rather than restating it, and a by-path import gives a module no siblings.
    """
    release = Path(__file__).resolve().parent.parent / "release"
    sys.path.insert(0, str(release))
    spec = importlib.util.spec_from_file_location("published_check", release / "published_check.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


published_check = load_published_check()

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


def reasons_from(before, after, results, branch, base, holds_base, held_by_worktree,
                 unpublished_release):
    """Every reason not to merge, decided from plain data so the decision is testable without a network.

    `unpublished_release` takes no default on purpose: a caller that stops supplying it would otherwise
    read as a clean base, and the only production caller is held by no test.

    A moved head returns with the publication reason and nothing else: with the readings straddling a
    force-push, nothing else read here is known to be about the same commit, so reporting the rest
    would be reporting about two SHAs at once. The publication reason is about the base, which the
    force-push did not touch.
    """
    reasons = [unpublished_release] if unpublished_release else []
    if before != after:
        return reasons + [f"head moved from {before[:7]} to {after[:7]} while its checks were being read"]

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
                        held_by_worktree=branch in worktree_branches(project),
                        unpublished_release=published_check.unpublished_reason(
                            project, f"origin/{base}", fetch=True))


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


def update_reasons(branch, base, holds_base, held_by_worktree):
    """Every reason not to bring the base into this branch, decided from plain data.

    A branch already holding the base is refused rather than no-opped: an update that merges nothing
    still pushes, and a push re-triggers every check for a head that already passed them.
    """
    reasons = []
    if holds_base:
        reasons.append(f"already contains origin/{base}: there is nothing to bring in")
    if held_by_worktree:
        reasons.append(f"a worktree holds {branch}: its own state would decide the merge, "
                       f"not the pushed head")
    return reasons


def update(project, number, base):
    """Merges the base into a pull request's branch and pushes it.

    Done in a throwaway worktree off the REMOTE head rather than in the checkout: the branch may be
    checked out somewhere with work in progress, and a local ref may be behind what the merge has to
    happen against. Refuses a conflict rather than leaving a half-merged worktree behind — a branch
    whose files really do conflict needs a person, and the conflicting one this exists for
    (a long-held branch that re-conflicts on every base move) is exactly the case not to automate.
    """
    branch = json.loads(gh("pr", "view", str(number), "--json", "headRefName"))["headRefName"]
    gh_git(project, "fetch", "origin", "--quiet")
    reasons = update_reasons(branch, base, contains_base(project, branch, base),
                             branch in worktree_branches(project))
    if reasons:
        print(f"Refusing to update PR#{number}:", file=sys.stderr)
        for reason in reasons:
            print(f"  - {reason}", file=sys.stderr)
        return 1

    scratch = Path(tempfile.mkdtemp(prefix="velvet-settle-"))
    work = scratch / "worktree"
    temp_branch = f"settle-update-{number}"
    try:
        gh_git(project, "worktree", "add", str(work), "-b", temp_branch,
               f"origin/{branch}", "--quiet")
        try:
            gh_git(work, "merge", f"origin/{base}", "-m",
                   f"Merge {base} into {branch}")
        except RuntimeError as exc:
            print(f"Refusing to update PR#{number}: the merge did not apply cleanly", file=sys.stderr)
            print(f"  {exc}", file=sys.stderr)
            return 1
        gh_git(work, "push", "origin", f"HEAD:{branch}")
    finally:
        subprocess.run(["git", "-C", str(project), "worktree", "remove", str(work), "--force"],
                       capture_output=True, text=True)
        subprocess.run(["git", "-C", str(project), "branch", "-D", temp_branch],
                       capture_output=True, text=True)
        shutil.rmtree(scratch, ignore_errors=True)
    print(f"PR#{number}: {base} merged into {branch} and pushed; its checks run again")
    return 0


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
    update_parser = sub.add_parser(
        "update", help="merge the base into one pull request's branch so its checks run against it")
    update_parser.add_argument("number", type=int)
    args = parser.parse_args()

    project = Path(args.project).resolve()
    if args.command == "watch":
        watch(project, args.base)
        return 0
    if args.command == "update":
        return update(project, args.number, args.base)
    return merge(project, args.number, args.base, args.dry_run)


if __name__ == "__main__":
    sys.exit(main())

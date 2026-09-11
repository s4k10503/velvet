#!/usr/bin/env python3
"""Report when local main lags origin/main, or the checked-out branch lags what it is based on.

Verification here reads the working copy. A checkout ten commits behind origin/main answered a guard
question with the file absent — the guard had merged — and the near-miss was caught only because the
same tree disagreed with a second fact already known to be true. Pull requests merge through the API
and origin/main advances. `scripts/pr/settle.py merge` then fast-forwards the local base, or says
why it could not; after any other merge, nothing here does.

`lib/merge_target.py` owns where a branch's base comes from.

Exit 0 always.
"""

import shlex
import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))

from merge_target import refs_of  # noqa: E402
from repository import git, printable, project_tree, worktrees  # noqa: E402

# The hook is registered for 15 s, and a report the harness kills prints nothing at all — including
# the local-main half, which no base takes part in. So the pull-request read, both fetches and the
# worktree listing sum inside that, and what is left is git plumbing against local state already on
# disk.
FETCH_TIMEOUT = 5
BASE_TIMEOUT = 3
LISTING_TIMEOUT = 1

DEFAULT_BASE = "main"

# Every ref a fetch failed to bring, named. A fetch that ran out of its bound leaves the ref that was
# already on disk, and that ref can read as up to date — which takes the whole report for that branch
# with it, so the fetch is the only thing left worth saying.
FETCH_NOTE = """
Not fetched: {}."""

# What goes where a remedy would, when nothing here said what the branch is based on. The remedy is
# a rebase and a force-push, and against the wrong branch it rewrites the commits other work sits
# on: while this said `main` unconditionally, the 2.1.1 release branch was told to rebase onto main
# and force-push.
UNREAD_BASE_NOTE = """
Nothing here named a base for this branch — either no pull request names one, or gh could not be
read — so the distance above is against origin/main and no remedy follows it."""


def fetch(tree, branch):
    """True when origin/<branch> was refreshed; a timeout or failure is reported, not fatal.

    One branch per call rather than both in one: a ref the remote has not got makes the whole fetch
    fatal, and asking for the base alongside main would take main's own reading down with it.
    """
    try:
        result = subprocess.run(
            ["git", "fetch", "-q", "origin", branch],
            cwd=tree, capture_output=True, text=True, timeout=FETCH_TIMEOUT,
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return result.returncode == 0


def base_of(tree):
    """The branch this checkout's pull request targets, or None when nothing here named one.

    Read from the pull request because git can say what a branch contains and not what it was cut
    from. None is not a synonym for main: a failed reading and a branch no pull request of its own
    names a base for both arrive here, and neither is a branch to print a rebase against.
    """
    target = refs_of(tree, "", timeout=BASE_TIMEOUT)
    return target.base if target else None


def holder_of(tree, branch):
    """The worktree that has `branch` checked out, or None when none of them has.

    Which remedy the local-main half prints turns on this: the refspec fetch and the fast-forward
    are not runnable in the same state, and where a worktree holds main the fast-forward has to be
    asked of that checkout. scripts/hooks/test_guard_remedies.py poses both spellings to git rather
    than arguing either here.
    """
    listing = worktrees(tree, timeout=LISTING_TIMEOUT)
    return next((entry.path for entry in listing or [] if entry.branch == branch), None)


def behind(tree, ref, base):
    counted = git(["rev-list", "--count", f"{ref}..origin/{base}"], tree)
    return int(counted.strip()) if counted and counted.strip().isdigit() else 0


def commits(count):
    return "commit" if count == 1 else "commits"


def main():
    if shutil.which("git") is None:
        return 0
    tree = project_tree()
    if tree is None or git(["remote", "get-url", "origin"], tree) is None:
        return 0

    branch = (git(["symbolic-ref", "--quiet", "--short", "HEAD"], tree) or "").strip()
    # Ahead of the fetches because which ref the second one asks for depends on it. A detached HEAD
    # names no branch, so no pull request names a base for it either.
    base = base_of(tree) if branch and branch != "main" else None

    # A failed fetch is not a reason to say nothing. main only ever advances here, so the ref already
    # on disk is a lower bound on how far behind the checkout is — the answer it gives is incomplete
    # in one direction only, and reporting it beats reporting silence over an unreachable remote.
    stale = [] if fetch(tree, DEFAULT_BASE) else [DEFAULT_BASE]
    if base and base != DEFAULT_BASE and not fetch(tree, base):
        stale.append(base)
    if git(["rev-parse", "--verify", "refs/remotes/origin/main"], tree) is None:
        return 0

    # None of the remedies below is a `git checkout`, for the reason
    # refuse/branch_from_unmerged.py's `from_origin_main` gives.
    main_report = ""
    if git(["rev-parse", "--verify", "refs/heads/main"], tree) is not None:
        count = behind(tree, "main", DEFAULT_BASE)
        if count > 0:
            holder = holder_of(tree, DEFAULT_BASE)
            main_report = (
                f"Local main is {count} {commits(count)} behind origin/main"
                + (f", and {holder} has it checked out.\n\n"
                   "git fetch origin main\n"
                   f"git -C {shlex.quote(holder)} merge --ff-only origin/main" if holder else
                   ".\n\ngit fetch origin main:main")
            )

    # A detached HEAD has no branch name, and skipping it on that basis left the one checkout shape
    # whose staleness nothing else reports — local main can be current while HEAD is arbitrarily
    # behind.
    branch_report = ""
    if branch != "main":
        against = base or DEFAULT_BASE
        merged = subprocess.run(
            ["git", "merge-base", "--is-ancestor", f"origin/{against}", "HEAD"],
            cwd=tree, capture_output=True, text=True,
        ).returncode == 0
        if not merged:
            count = behind(tree, "HEAD", against)
            if count > 0 and branch:
                remedy = (
                    "git fetch origin\n"
                    f"git rebase origin/{against}\n"
                    f"git push origin {branch} --force-with-lease"
                ) if base else UNREAD_BASE_NOTE.strip()
                branch_report = (
                    f"Branch {branch} is {count} {commits(count)} behind origin/{against}.\n\n"
                    + remedy
                )
            elif count > 0:
                head = (git(["rev-parse", "--short", "HEAD"], tree) or "").strip()
                branch_report = (
                    f"HEAD is detached at {head} and is {count} {commits(count)} behind "
                    "origin/main.\n\n"
                    "git fetch origin main\n"
                    "git worktree add <path> -b <name> origin/main"
                )

    note = FETCH_NOTE.format(", ".join("origin/" + ref for ref in stale)) if stale else ""
    if not main_report and not branch_report and not note:
        return 0

    if main_report and branch_report:
        print(printable(f"This checkout may not match what it is based on.\n\n{main_report}\n"))
        print(printable(f"{branch_report}\n{note}"))
    else:
        print(printable(f"This checkout may not match what it is based on.\n\n"
                        f"{main_report or branch_report}\n{note}"))
    return 0


if __name__ == "__main__":
    sys.exit(main())

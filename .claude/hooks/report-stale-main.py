#!/usr/bin/env python3
"""Report when local main or the checked-out branch lags origin/main.

Verification here reads the working copy. A checkout ten commits behind origin/main answered a guard
question with the file absent — the guard had merged — and the near-miss was caught only because the
same tree disagreed with a second fact already known to be true. Pull requests merge through the API,
origin/main advances, and nothing pulls afterwards.

Exit 0 always.
"""

import shutil
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

from repository import git, project_tree  # noqa: E402

FETCH_TIMEOUT = 15

FETCH_NOTE = """
origin/main could not be fetched, so the counts above are against the ref already on disk and the
real distance may be greater."""


def fetch_origin_main(tree):
    """True when origin/main was refreshed; a timeout or failure is reported, not fatal."""
    try:
        result = subprocess.run(
            ["git", "fetch", "-q", "origin", "main"],
            cwd=tree, capture_output=True, text=True, timeout=FETCH_TIMEOUT,
        )
    except (OSError, subprocess.SubprocessError):
        return False
    return result.returncode == 0


def behind(tree, ref):
    counted = git(["rev-list", "--count", f"{ref}..origin/main"], tree)
    return int(counted.strip()) if counted and counted.strip().isdigit() else 0


def commits(count):
    return "commit" if count == 1 else "commits"


def main():
    if shutil.which("git") is None:
        return 0
    tree = project_tree()
    if tree is None or git(["remote", "get-url", "origin"], tree) is None:
        return 0

    # A failed fetch is not a reason to say nothing. main only ever advances here, so the ref already
    # on disk is a lower bound on how far behind the checkout is — the answer it gives is incomplete
    # in one direction only, and reporting it beats reporting silence over an unreachable remote.
    fetch_failed = not fetch_origin_main(tree)
    if git(["rev-parse", "--verify", "refs/remotes/origin/main"], tree) is None:
        return 0

    branch = (git(["symbolic-ref", "--quiet", "--short", "HEAD"], tree) or "").strip()

    main_report = ""
    if git(["rev-parse", "--verify", "refs/heads/main"], tree) is not None:
        count = behind(tree, "main")
        if count > 0:
            main_report = (
                f"Local main is {count} {commits(count)} behind origin/main.\n\n"
                "git fetch origin main\n"
                "git checkout main\n"
                "git merge --ff-only origin/main"
            )

    # A detached HEAD has no branch name, and skipping it on that basis left the one checkout shape
    # whose staleness nothing else reports — local main can be current while HEAD is arbitrarily
    # behind.
    branch_report = ""
    if branch != "main":
        merged = subprocess.run(
            ["git", "merge-base", "--is-ancestor", "origin/main", "HEAD"],
            cwd=tree, capture_output=True, text=True,
        ).returncode == 0
        if not merged:
            count = behind(tree, "HEAD")
            if count > 0 and branch:
                branch_report = (
                    f"Branch {branch} is {count} {commits(count)} behind origin/main.\n\n"
                    "git fetch origin\n"
                    "git rebase origin/main\n"
                    f"git push origin {branch} --force-with-lease"
                )
            elif count > 0:
                head = (git(["rev-parse", "--short", "HEAD"], tree) or "").strip()
                branch_report = (
                    f"HEAD is detached at {head} and is {count} {commits(count)} behind "
                    "origin/main.\n\n"
                    "git fetch origin\n"
                    "git checkout main\n"
                    "git merge --ff-only origin/main"
                )

    if not main_report and not branch_report:
        return 0

    note = FETCH_NOTE if fetch_failed else ""
    if main_report and branch_report:
        print(f"This checkout may not match origin/main.\n\n{main_report}\n")
        print(f"{branch_report}\n{note}")
    else:
        print(f"This checkout may not match origin/main.\n\n{main_report or branch_report}\n{note}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

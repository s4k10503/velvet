#!/usr/bin/env python3
"""Refuse `gh pr merge` for a branch that does not contain the current main.

Two branches green against a main neither contained broke this repository once: one deleted a
helper, the other added callers of it, and merging them in either order left main not compiling.
CI does ask the right question — it tests the merge result — but the answer expires when main moves
and nothing looks again.

The staleness is computed here rather than read from mergeStateStatus. GitHub reports BEHIND only
when the base branch requires the head to be up to date, and this repository sets no required
status checks, so that field answers CLEAN for a branch eight commits behind. A guard keyed on it
would never fire.
"""
import json
import re
import subprocess
import sys


def git(*args):
    return subprocess.run(
        ["git", *args], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30
    )


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0

    command = (payload.get("tool_input") or {}).get("command") or ""
    match = re.search(r"gh\s+pr\s+merge\s+(\d+)", command)
    if not match:
        return 0
    pr = match.group(1)

    try:
        head = subprocess.run(
            ["gh", "pr", "view", pr, "--json", "headRefName", "--jq", ".headRefName"],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=20,
        ).stdout.decode().strip()
    except Exception:
        return 0
    if not head:
        return 0

    git("fetch", "-q", "origin", "main", head)
    if git("merge-base", "--is-ancestor", "origin/main", "origin/" + head).returncode == 0:
        return 0

    behind = git("rev-list", "--count", "origin/{}..origin/main".format(head)).stdout.decode().strip()
    sys.stderr.write(
        "PR #{} does not contain the current main — it is {} commit(s) behind.\n\n"
        "Its checks passed against a main this merge does not produce. That is how main was broken "
        "here: two branches, each green, neither containing the other's change.\n\n"
        "Merge origin/main into {}, let the checks re-run, then merge.\n\n"
        "Note mergeStateStatus says CLEAN for this PR. GitHub only reports BEHIND when the base "
        "requires the head to be up to date, which protect-main does not — so that field cannot be "
        "the thing you check.\n".format(pr, behind or "?", head)
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

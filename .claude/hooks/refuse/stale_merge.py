#!/usr/bin/env python3
"""Refuse `gh pr merge` for a branch that does not contain the base its pull request names.

Two branches green against a base neither contained broke this repository once: one deleted a
helper, the other added callers of it, and merging them in either order left the base not compiling.
CI does ask the right question — it tests the merge result — but the answer expires when the base
moves and nothing looks again.

The staleness is computed here rather than read from mergeStateStatus. GitHub reports BEHIND only
when the base branch requires the head to be up to date, which `protect-main` deliberately does not
require, so that field answers CLEAN for a branch eight commits behind.

`lib/merge_target.py` owns which pull request a command would land and what it targets.
"""
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from merge_target import UNRESOLVED, merge_targets, refs_of
from velvet_hooks import BRANCH_BASES


HOOK_TOOLS = {"Bash"}

# What an operand the shell has not expanded yet resolves to, which is nothing this can read. A merge
# guard errs toward refusing: allowing means the branch is merged with the check it exists for never
# having run, and the merge is what cannot be taken back.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}


def git(cwd, *args):
    """A finished `git`, or None when it could not be run at all.

    Same reason `lib/repository.py` answers None rather than raising: a hook that raises exits 1,
    and 1 lets the tool through.
    """
    try:
        return subprocess.run(
            ["git", "-C", cwd, *args],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30,
        )
    except (OSError, subprocess.SubprocessError):
        return None


UNREADABLE_REFUSAL = (
    "Refusing `gh pr merge`: whether the branch contains its base could not be read.\n\n"
    "  {}\n\n"
    "An unread answer and a branch that is up to date leave this guard looking the same from "
    "outside, and the merge is what cannot be taken back. Retry when the reading works, or see "
    "every merge precondition at once:\n"
    "  python3 scripts/pr/settle.py merge <pr> --dry-run\n"
)


def branch_base(name):
    try:
        with open(BRANCH_BASES, encoding="utf-8") as bases:
            matches = [line for line in bases if line.startswith(f"{name} ")]
    except OSError:
        return None
    if not matches:
        return None
    parts = matches[-1].split(None, 1)
    return parts[1].strip() if len(parts) == 2 else None


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0
    if payload.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = (payload.get("tool_input") or {}).get("command") or ""
    targets = merge_targets(command)
    if not targets:
        return 0
    if UNRESOLVED in targets:
        sys.stderr.write(
            "Refusing `gh pr merge`: the pull request is named by an operand the shell has not "
            "expanded yet, so neither its base nor whether its branch contains it can be read.\n\n"
            "Resolving the literal would fail and read as a pass, which is the check silently not "
            "happening. Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2
    pr = targets[0]
    cwd = payload.get("cwd") or "."

    target = refs_of(cwd, pr)
    if target is None:
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "the head and base of " + ("PR #" + pr if pr else "the current branch's pull request")
            + " came back empty"))
        return 2
    head, base = target.head, target.base

    fetched = git(cwd, "fetch", "-q", "origin", base, head)
    if fetched is None or fetched.returncode != 0:
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "origin/{} and origin/{} could not be fetched".format(base, head)))
        return 2

    # 0 is contained and 1 is behind; any other code is git declining to answer. Both of the shorter
    # readings are wrong about it: `== 0` lets a git that never answered stand for a branch that is
    # up to date, and `!= 0` refuses naming a commit count nothing produced.
    ancestry = git(cwd, "merge-base", "--is-ancestor", "origin/" + base, "origin/" + head)
    if ancestry is None or ancestry.returncode not in (0, 1):
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "origin/{}'s ancestry against origin/{} could not be read".format(base, head)))
        return 2
    if ancestry.returncode == 0:
        return 0

    counted = git(cwd, "rev-list", "--count", "origin/{}..origin/{}".format(head, base))
    behind = counted.stdout.decode().strip() if counted else ""
    parent = branch_base(head)
    # gh merges the current branch's pull request when no number is given, and naming it "#" reads
    # as a number nobody typed.
    label = "PR #" + pr if pr else "The pull request for " + head
    preamble = (
        "{} does not contain the current {} — it is {} commit(s) behind.\n\n"
        "Its checks passed against a {} this merge does not produce. That is how main was broken "
        "here: two branches, each green, neither containing the other's change.\n\n"
    ).format(label, base, behind or "?", base)
    coda = (
        "\n\nNote mergeStateStatus says CLEAN for this PR. GitHub only reports BEHIND when the base "
        "requires the head to be up to date, which is a setting rather than a fact about the branch "
        "— so that field cannot be the thing you check.\n"
    )
    if parent:
        sys.stderr.write(
            preamble
            + "This branch was created on top of unmerged work. After the parent merges, replay only "
              "this branch's commits with:\n"
              "git rebase --onto origin/{} {}".format(base, parent)
            + coda)
    else:
        sys.stderr.write(
            preamble
            + "Merge origin/{} into {}, let the checks re-run, then merge.".format(base, head)
            + coda)
    return 2


if __name__ == "__main__":
    sys.exit(main())

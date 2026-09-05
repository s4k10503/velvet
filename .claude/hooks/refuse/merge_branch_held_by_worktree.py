#!/usr/bin/env python3
"""Refuse `gh pr merge --delete-branch` while a worktree still holds the branch.

The two halves of `--delete-branch` do not fail together. The remote head goes, the merge lands, and
then the local delete fails because git will not remove a branch a worktree has checked out — and gh
reports that as a line of output on an otherwise successful command:

    failed to delete local branch tooling/x: cannot delete branch 'tooling/x' used by worktree at ...

Nothing is left to retry: the merge already happened. What remains is a worktree on a branch whose
pull request is closed, which from inside the checkout is indistinguishable from one holding work
that never landed — the same ambiguity `merge_without_branch_deletion.py` exists to avoid, arrived at
from the other side.

Ordering, not prohibition: remove the worktree, then merge.
"""

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from pr_body import merges_nothing  # noqa: E402
from shell_commands import (NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory,
                            program_invocations, unexpanded)
import repository


HOOK_TOOLS = {"Bash"}


# An operand the shell has not expanded yet resolves to nothing readable, and a merge guard errs
# toward refusing: allowing means --delete-branch half-fails after the merge has already landed.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}


# What a reading that did not answer resolves to. An empty answer and "nothing holds this branch"
# arrive as the same falsy value otherwise, and only one of them means the merge is safe.
UNREADABLE = object()


def held_branches(cwd):
    """Branch names a worktree has checked out, or None when the list could not be read."""
    try:
        result = subprocess.run(["git", "-C", cwd, "worktree", "list", "--porcelain"],
                                capture_output=True, text=True, timeout=20)
    except (OSError, subprocess.SubprocessError):
        return None
    if result.returncode != 0:
        return None
    held, path = {}, None
    for line in result.stdout.splitlines():
        if line.startswith("worktree "):
            path = line.split(" ", 1)[1].strip()
        elif line.startswith("branch ") and path:
            held[line.split(" ", 1)[1].strip().removeprefix("refs/heads/")] = path
    return held


def branch_of(cwd, operands):
    """The branch a `gh pr merge` invocation would merge, or UNREADABLE when it did not answer.

    Read over REST for the reason `scripts/pr/settle.py` states, and read from git when no number
    is given: the head of the current branch's pull request is that branch, which needs no API.
    """
    number = next((token for token in operands if token.isdigit()), None)
    if number is None:
        branch = (repository.git(["rev-parse", "--abbrev-ref", "HEAD"], cwd=cwd) or "").strip()
        return branch if branch and branch != "HEAD" else UNREADABLE
    ref = repository.gh(["api", "repos/{owner}/{repo}/pulls/" + number, "--jq", ".head.ref"],
                        cwd=cwd)
    return (ref or "").strip() or UNREADABLE


def blocked(command, cwd):
    """(branch, why this merge cannot clear it) for each merge whose branch is not clear.

    The unexpanded operand is answered before the worktree list is consulted. Returning early on an
    empty list put the refusal behind "some worktree exists", so the policy held on a checkout that
    happened to have one and lapsed on a runner that did not — which is the guard being exercised
    only in the states its environment happens to be in.
    """
    held, read = None, False
    found = []
    for operands in program_invocations(command, "gh", ("pr", "merge")):
        if merges_nothing(operands):
            continue
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            found.append(("the branch named by an unexpanded operand",
                          "unreadable — resolve it, or name the pull request"))
            continue
        if not read:
            held, read = held_branches(cwd), True
        if held is None:
            found.append(("the branch this merge would delete",
                          "unreadable — git did not list the worktrees"))
            continue
        branch = branch_of(cwd, operands)
        if branch is UNREADABLE:
            found.append(("the branch this merge would delete",
                          "unreadable — its name did not come back"))
        elif branch in held:
            found.append((branch, "held by " + held[branch]))
    return found


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = event.get("tool_input", {}).get("command", "")
    cwd = command_directory(command, event.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `gh pr merge`: which checkout holds the worktree list could "
                         f"not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2
    found = blocked(command, cwd)
    if not found:
        return 0

    lines = "\n".join(f"  {branch}  {why}" for branch, why in found)
    sys.stderr.write(
        "Refusing `gh pr merge`: the branch it would delete is not clear.\n\n"
        f"{lines}\n\n"
        "The delete is not atomic with the merge. The remote head goes and the merge lands, then the "
        "local delete fails because git will not remove a branch a worktree has checked out, and gh "
        "prints that failure after having already merged. Nothing is left to retry — which is also "
        "why an unread answer is refused rather than taken for an empty one.\n\n"
        "Remove the worktree first, then merge:\n"
        "  git worktree remove --force <path>\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

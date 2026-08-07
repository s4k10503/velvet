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
from shell_commands import program_invocations, unexpanded


# An operand the shell has not expanded yet resolves to nothing readable, and a merge guard errs
# toward refusing: allowing means --delete-branch half-fails after the merge has already landed.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'


def held_branches(cwd):
    """Branch names a worktree has checked out, which git refuses to delete while it does."""
    result = subprocess.run(["git", "-C", cwd, "worktree", "list", "--porcelain"],
                            capture_output=True, text=True)
    if result.returncode != 0:
        return {}
    held, path = {}, None
    for line in result.stdout.splitlines():
        if line.startswith("worktree "):
            path = line.split(" ", 1)[1].strip()
        elif line.startswith("branch ") and path:
            held[line.split(" ", 1)[1].strip().removeprefix("refs/heads/")] = path
    return held


def branch_of(cwd, operands):
    """The branch a `gh pr merge` invocation would merge, or None when it cannot be read offline."""
    number = next((token for token in operands if token.isdigit()), None)
    args = ["gh", "pr", "view", "--json", "headRefName"]
    if number:
        args.insert(3, number)
    result = subprocess.run(args, capture_output=True, text=True, cwd=cwd)
    if result.returncode != 0:
        return None
    try:
        return json.loads(result.stdout)["headRefName"]
    except Exception:
        return None


def blocked(command, cwd):
    """(branch, worktree path) for each merge whose branch this cannot clear.

    The unexpanded operand is answered before the worktree list is consulted. Returning early on an
    empty list put the refusal behind "some worktree exists", so the policy held on a checkout that
    happened to have one and lapsed on a runner that did not — which is the guard being exercised
    only in the states its environment happens to be in.
    """
    held = None
    found = []
    for operands in program_invocations(command, "gh", ("pr", "merge")):
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            found.append(("the branch named by an unexpanded operand",
                          "unreadable — resolve it, or name the pull request"))
            continue
        if held is None:
            held = held_branches(cwd)
        branch = branch_of(cwd, operands)
        if branch and branch in held:
            found.append((branch, held[branch]))
    return found


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0

    cwd = event.get("cwd") or "."
    found = blocked(event.get("tool_input", {}).get("command", ""), cwd)
    if not found:
        return 0

    lines = "\n".join(f"  {branch}  held by {path}" for branch, path in found)
    sys.stderr.write(
        "Refusing `gh pr merge`: a worktree holds the branch it would delete.\n\n"
        f"{lines}\n\n"
        "The delete is not atomic with the merge. The remote head goes and the merge lands, then the "
        "local delete fails because git will not remove a branch a worktree has checked out, and gh "
        "prints that failure after having already merged. Nothing is left to retry.\n\n"
        "Remove the worktree first, then merge:\n"
        "  git worktree remove --force <path>\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

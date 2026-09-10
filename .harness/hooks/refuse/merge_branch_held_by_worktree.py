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

Ordering, not prohibition: free the branch, then merge. What frees it is not one command — a linked
worktree is removed, and the main working tree returns to the base branch instead — so the refusal
reads the list rather than printing one sentence for both, and `freeing` says why.
"""

import json
import shlex
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
    """Each branch a worktree has checked out, mapped to that worktree, or None when unread.

    The list comes from `repository.worktrees`, which owns both the decode and what marks the main
    working tree; reading it here a second way is what would let this guard exit 0 on a listing the
    other reader refused.
    """
    listing = repository.worktrees(cwd)
    if listing is None:
        return None
    return {tree.branch: tree for tree in listing if tree.branch}


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


def merges(command):
    """The operands of each `gh pr merge` in the command that would merge something."""
    return [operands for operands in program_invocations(command, "gh", ("pr", "merge"))
            if not merges_nothing(operands)]


def blocked(asked, cwd):
    """(branch, why this merge cannot clear it, the worktree holding it) for each unclear merge.

    The unexpanded operand is answered before the worktree list is consulted. Returning early on an
    empty list put the refusal behind "some worktree exists", so the policy held on a checkout that
    happened to have one and lapsed on a runner that did not — which is the guard being exercised
    only in the states its environment happens to be in.

    The worktree rides along because the two kinds take different remedies, and this reading is
    what tells them apart; an entry that names no worktree is one no remedy here fits.
    """
    held, read = None, False
    found = []
    for operands in asked:
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            found.append(("the branch named by an unexpanded operand",
                          "unreadable — resolve it, or name the pull request", None))
            continue
        if not read:
            held, read = held_branches(cwd), True
        if held is None:
            found.append(("the branch this merge would delete",
                          "unreadable — git did not list the worktrees", None))
            continue
        branch = branch_of(cwd, operands)
        if branch is UNREADABLE:
            found.append(("the branch this merge would delete",
                          "unreadable — its name did not come back", None))
        elif branch in held:
            tree = held[branch]
            where = "held by " + tree.path
            found.append((branch, where + ", the main working tree" if tree.primary else where,
                          tree))
    return found


def freeing(trees):
    """The command that frees each held branch, in the order the entries were found.

    A worktree the caller can drop takes `worktree remove`; the main working tree takes a return to
    the base branch instead. scripts/hooks/test_merge_branch_held_by_worktree.py poses to git which
    kind a removal reaches, so the split is pinned rather than argued here.

    Printing the path git listed rather than a placeholder is the difference between a remedy a
    reader runs and one they have to reconstruct.
    """
    seen, lines = set(), []
    for tree in trees:
        where = shlex.quote(tree.path)
        line = (f"git -C {where} checkout main" if tree.primary
                else f"git worktree remove --force {where}")
        if line not in seen:
            seen.add(line)
            lines.append(line)
    return lines


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = event.get("tool_input", {}).get("command", "")
    asked = merges(command)
    if not asked:
        return 0
    cwd = command_directory(command, event.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `gh pr merge`: which checkout holds the worktree list could "
                         f"not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2
    found = blocked(asked, cwd)
    if not found:
        return 0

    lines = "\n".join(f"  {branch}  {why}" for branch, why, _ in found)
    trees = [tree for _, _, tree in found if tree is not None]
    remedy = ""
    if trees:
        commands = "\n".join("  " + line for line in freeing(trees))
        base = ("\nA return to the base branch is what frees the main working tree: git declines to "
                "remove it, so no ordering of the merge and a removal exists for it.\n"
                if any(tree.primary for tree in trees) else "")
        remedy = f"\nFree the branch first, then merge:\n{commands}\n{base}"
    sys.stderr.write(
        "Refusing `gh pr merge`: the branch it would delete is not clear.\n\n"
        f"{lines}\n\n"
        "The delete is not atomic with the merge. The remote head goes and the merge lands, then the "
        "local delete fails because git will not remove a branch a worktree has checked out, and gh "
        "prints that failure after having already merged. Nothing is left to retry — which is also "
        f"why an unread answer is refused rather than taken for an empty one.\n{remedy}"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Refuse `gh pr merge` whose head has no passing check of its own.

Branch protection cannot cover this here. It requires the two aggregate contexts, but with
`strict_required_status_checks_policy` deliberately false — requiring every head to be up to date
would serialise every merge behind a 21-25 minute Unity matrix — so GitHub will merge a head whose
own run never happened, as long as some run for the pull request passed.

`gh pr checks` makes that easy to walk into. It answers about whatever the API last recorded, which
after a push is the previous commit's result, and a green read for a superseded SHA has already been
carried to the edge of a merge here. So the head is read, then the checks, then the head again: a
change between the two readings means the answers are about two commits and neither is trusted.

An empty check list is refused rather than forgiven. It means no workflow was ever triggered for
that head — what a cancelled run followed by a push leaves behind — and reading it as "still
running" is how a pull request sat unnoticed for 7h45m.

The other four merge preconditions have their own hooks: `stale_merge.py` for a branch behind its
base, `merge_without_branch_deletion.py` for the flag, `merge_branch_held_by_worktree.py` for a
branch git will refuse to delete, `merge_onto_unpublished_release.py` for a base holding a release
nobody dispatched. `scripts/pr/settle.py` reports these five together with two more it
holds alone — a draft head and one on another repository, which its own docstring says why of — and
that reporting is the convenience; these are what hold when nobody runs it.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import (NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory,
                            program_invocations, unexpanded)
import repository


HOOK_TOOLS = {"Bash"}

TERMINAL_PASS = frozenset({"pass", "skipping"})

# An operand the shell has not expanded yet resolves to nothing readable, and a merge guard errs
# toward refusing: allowing means the branch lands with this check never having run.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

# gh answers None both for a check list it could not read and for a number that is not a pull
# request, and nothing here separates them. The merge is still refused — same backing rule as
# merge_onto_unpublished_release.py states.
UNREADABLE_POLICY = "allow"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}


def gh_json(cwd, args):
    out = repository.gh(args, cwd=cwd)
    if not out or not out.strip():
        return None
    try:
        return json.loads(out)
    except Exception:
        return None


def head_sha(cwd, number):
    payload = gh_json(cwd, ["pr", "view", *( [number] if number else [] ), "--json", "headRefOid"])
    return payload["headRefOid"] if payload else None


def merges(command):
    """The operands of each `gh pr merge` in the command."""
    return program_invocations(command, "gh", ("pr", "merge"))


def unproven(asked, cwd):
    """(pull request, reason) for each merge whose head is not covered by its own passing checks."""
    found = []
    for operands in asked:
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            found.append(("the pull request named",
                          "an operand the shell has not expanded, so its checks cannot be read"))
            continue
        number = next((token for token in operands if token.isdigit()), None)
        label = "#" + number if number else "the current branch"

        before = head_sha(cwd, number)
        if before is None:
            # gh is unreachable or this is not a pull request; the other guards still apply and this
            # one declines to invent an answer.
            continue

        results = gh_json(cwd, ["pr", "checks", *([number] if number else []), "--json", "name,bucket"])
        after = head_sha(cwd, number)

        if after != before:
            found.append((label, f"head moved from {before[:7]} to {after[:7]} while its checks were read"))
        elif not results:
            found.append((label, f"no check ran for {before[:7]}: a workflow was never triggered for it"))
        else:
            unfinished = sorted(entry["name"] for entry in results
                                if entry["bucket"] not in TERMINAL_PASS)
            if unfinished:
                found.append((label, "not passing at {}: {}".format(before[:7], ", ".join(unfinished))))
    return found


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
        sys.stderr.write("Refusing `gh pr merge`: which checkout gh would resolve the pull request "
                         f"from could not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2
    found = unproven(asked, cwd)
    if not found:
        return 0

    lines = "\n".join(f"  {label}: {reason}" for label, reason in found)
    sys.stderr.write(
        "Refusing `gh pr merge`: the head being merged is not covered by its own passing checks.\n\n"
        f"{lines}\n\n"
        "Branch protection does not catch this. It requires the aggregate contexts without requiring "
        "the head to be up to date, which is deliberate — the alternative serialises every merge "
        "behind the Unity matrix — so a head whose own run never happened can satisfy it.\n\n"
        "See all five merge preconditions at once:\n"
        "  python3 scripts/pr/settle.py merge <pr> --dry-run\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

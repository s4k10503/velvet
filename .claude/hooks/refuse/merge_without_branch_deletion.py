#!/usr/bin/env python3
"""Refuse `gh pr merge` that does not also delete the branch.

Ninety-six local branches accumulated before anyone counted them, along with the remote heads and
the pack they held; the checkout's `.git` was 24 MB and became 6.2 MB once they were swept. Every
one of them was a branch whose pull request had merged, and every one could have gone at the moment
it merged.

Cleaning up afterwards is the expensive half. A branch whose pull request squash-merged and one
holding work that never landed are indistinguishable from inside the checkout — ancestry does not
separate them, because a squash leaves the tip unreachable — so a later sweep has to ask the pull
requests one by one, and a sweep that guesses destroys work. At merge time there is no ambiguity:
the branch merged, which is why it is being deleted.

`--delete-branch` is gh's own, and it removes the remote head as well. Nothing here deletes
anything; it declines a merge that leaves the litter behind.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations

DELETE_FLAGS = ("--delete-branch", "-d")


def merges_without_deletion(command):
    """The pull requests this command merges while leaving their branches behind."""
    left = []
    for operands in program_invocations(command, "gh", ("pr", "merge")):
        if any(token.partition("=")[0] in DELETE_FLAGS for token in operands):
            continue
        left.append(next((token for token in operands if token.isdigit()), ""))
    return left


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0

    left = merges_without_deletion(event.get("tool_input", {}).get("command", ""))
    if not left:
        return 0

    named = ", ".join("#" + pr if pr else "the current branch's" for pr in left)
    sys.stderr.write(
        f"Refusing `gh pr merge`: {named} would merge and leave the branch behind.\n\n"
        "A branch is only unambiguously spent at the moment it merges. Later, a squash leaves its "
        "tip unreachable from main, so nothing in the checkout can tell it from a branch whose work "
        "never landed — and a sweep that guesses deletes the second kind.\n\n"
        "Add the flag, which removes the remote head too:\n"
        "  --delete-branch\n\n"
        "If this branch is meant to outlive the merge, say why and merge it from the web interface.\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

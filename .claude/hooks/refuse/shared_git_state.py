#!/usr/bin/env python3
"""Refuse git commands that move state shared across worktrees.

Several agents hold worktrees of this repository at once. `checkout`, `switch` and `stash` act on
state they share, so one agent running them silently retargets another's branch — this has cost
recovery work twice. Reading the rule in a prompt did not prevent it; refusing the command does.

Read off tokens rather than matched as text. The version this replaces exempted any argument
containing a slash, which is the shape of every branch in this repository, so `git checkout feat/x`
passed while only a slash-free ref such as `main` was refused. It also required `git` to be followed
immediately by the subcommand, so `git -C <other worktree> checkout` — the reach-into-another-tree
case the rule exists for — was invisible to it.
"""

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import git_invocations

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# run git, which would leave every other session unguarded. `HookWiringCoverageTests` reads this
# declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# `git stash list` and `git stash show` read; every other form moves the shared stash.
STASH_READS = {"list", "show"}

SWITCH_REFUSAL = (
    "Refused: `git switch` and `git stash` move state other worktrees share. Work in the worktree "
    "you were given; if you need a different base, say so and stop.\n"
)
CHECKOUT_REFUSAL = (
    "Refused: `git checkout` of a branch moves state other worktrees share. Restoring a file "
    "(`git checkout -- <path>`) is allowed; changing branch is not.\n"
)


def restores_paths(directory, operands, cwd):
    """Whether this checkout restores files rather than moving HEAD.

    `--` says so outright. Otherwise every non-flag operand has to name something that exists,
    which is the same question git resolves — and the only one answerable without moving anything.
    """
    if "--" in operands:
        return True
    root = directory or cwd
    named = [token for token in operands if not token.startswith("-")]
    if not named:
        return False
    return all(os.path.exists(os.path.join(root, token)) for token in named)


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    command = (event.get("tool_input") or {}).get("command", "")
    if not isinstance(command, str) or not command:
        return 0
    cwd = event.get("cwd") or "."

    for directory, subcommand, operands in git_invocations(command, {"switch", "stash", "checkout"}):
        if subcommand == "stash":
            first = next((token for token in operands if not token.startswith("-")), "")
            if first in STASH_READS:
                continue
            sys.stderr.write(SWITCH_REFUSAL)
            return 2
        if subcommand == "switch":
            sys.stderr.write(SWITCH_REFUSAL)
            return 2
        if not restores_paths(directory, operands, cwd):
            sys.stderr.write(CHECKOUT_REFUSAL)
            return 2

    return 0


if __name__ == "__main__":
    sys.exit(main())

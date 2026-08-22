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

import glob
import json
import os
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import git_invocations, unexpanded


HOOK_TOOLS = {"Bash"}

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# run git, which would leave every other session unguarded. `HookWiringCoverageTests` reads this
# declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# `git stash list` and `git stash show` read; every other form moves the shared stash.
STASH_READS = {"list", "show"}

# Flags a path-restoring checkout may carry. Anything else — `-b`, `-B`, `--detach`, `--orphan`,
# or a flag added to git after this line was written — takes the refusal, because the operand it
# governs is then not a pathspec and the question below is being asked about the wrong thing.
RESTORE_FLAGS = {
    "--",
    "-f", "--force",
    "-q", "--quiet",
    "-m", "--merge",
    "-p", "--patch",
    "--ours", "--theirs",
    "--overlay", "--no-overlay",
}

# Resolving an operand the shell has not expanded would answer about the literal and answer no, which
# is the pass — so it takes the refusal without being resolved at all. Recognising the case is shared
# with every other guard that resolves an operand; which way to err is each guard's own.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git checkout $BRANCH'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git checkout main"}

# A glob is the other operand the shell rewrites, and it cannot take the same refusal: `git checkout
# '*.cs'` is an ordinary restore, and refusing it is the over-refusal this guard was rewritten to
# remove. It is expanded instead, so the question is asked about the operand git receives.
GLOB = re.compile(r"[*?\[]")

SWITCH_REFUSAL = (
    "Refused: `git switch` and `git stash` move state other worktrees share. Work in the worktree "
    "you were given; if you need a different base, say so and stop.\n"
)
CHECKOUT_REFUSAL = (
    "Refused: `git checkout` of a branch moves state other worktrees share. Restoring a file "
    "(`git checkout -- <path>`) is allowed; changing branch is not.\n"
)


def names_a_commit(root, token):
    """Whether git resolves `token` to a commit, answering yes when git cannot be asked.

    The alternative that spends no subprocess is `os.path.exists`, and it reads a path the working
    tree no longer has — restoring a file you just deleted, the ordinary reason to run this command
    — as a branch. Reading `.git/refs` and `packed-refs` from here would also answer without git,
    and was rejected as a second implementation of ref resolution that drifts against the first.

    Answering yes is the refusal, and a refusal leaves the caller `git checkout -- <path>`, which
    this guard allows; the other direction retargets a branch another worktree is on.
    `GuardCommandCoverageTests` poses a command for each of the three answers git gives.
    """
    try:
        completed = subprocess.run(
            ["git", "-C", root, "rev-parse", "--verify", "--quiet", "--end-of-options",
             token + "^{commit}"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            timeout=10,
        )
    except Exception:
        return True
    return completed.returncode != 1


def sole_expansions(named, cwd):
    """The single name each glob operand expands to, skipping every glob that matches otherwise.

    A wider expansion is skipped rather than resolved because it cannot reach the branch-switching
    form of this command, which `GuardCommandCoverageTests` poses to git rather than asserting here.
    Skipping it is also what keeps the cost flat: `git checkout *.cs` resolves nothing per matched
    file.

    Expanded against `cwd` — the shell's directory — while the resolution below runs against the
    repository `-C` names, which is a different directory whenever the command carries one.
    """
    for token in named:
        if not GLOB.search(token):
            continue
        matches = glob.glob(os.path.join(cwd, token))
        if len(matches) == 1:
            yield os.path.relpath(matches[0], cwd)


def restores_paths(directory, operands, cwd):
    """Whether this checkout restores files rather than moving HEAD.

    `--` says so outright, and settles it before any operand is resolved, so the escape hatch the
    refusal text offers stays open even where git is unreachable — and it is the answer for a glob
    the expansion below reads differently from the caller's intent.
    """
    if any(token.startswith("-") and token not in RESTORE_FLAGS for token in operands):
        return False
    if "--" in operands:
        return True
    named = [token for token in operands if not token.startswith("-")]
    if not named or any(unexpanded(token) for token in named):
        return False
    root = directory or cwd
    if directory and not os.path.isabs(directory):
        root = os.path.join(cwd, directory)
    resolved = named + list(sole_expansions(named, cwd))
    return not any(names_a_commit(root, token) for token in resolved)


def refusals(command, cwd):
    """The subcommand of every invocation in `command` this guard refuses, in the order they run.

    Split from `main` so a command table can pose one without a hook payload around it, and left
    lazy so a caller wanting only the first refusal resolves no operand belonging to a later one.
    """
    for directory, subcommand, operands in git_invocations(command, {"switch", "stash", "checkout"}):
        if subcommand == "stash":
            first = next((token for token in operands if not token.startswith("-")), "")
            if first not in STASH_READS:
                yield subcommand
        elif subcommand == "switch":
            yield subcommand
        elif not restores_paths(directory, operands, cwd):
            yield subcommand


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    command = (event.get("tool_input") or {}).get("command", "")
    if not isinstance(command, str) or not command:
        return 0
    cwd = event.get("cwd") or "."

    refused = next(refusals(command, cwd), None)
    if refused is None:
        return 0
    sys.stderr.write(CHECKOUT_REFUSAL if refused == "checkout" else SWITCH_REFUSAL)
    return 2


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Refuse creating a branch from HEAD when HEAD is not main and local main is not current.

A branch cut from another change's tip carried three commits from an unmerged pull request;
after that pull request was squash-merged, rebasing the new branch replayed content already on
main and conflicted, and the pull request had to be abandoned and reopened at a new number.
Branching from a stale main produces a branch the merge guard refuses later, when the fix is a
rebase rather than a different starting point.

The command is split into segments and tokenised rather than matched as text. A regex over the
masked command missed eleven spellings of a creation, quoting the branch name among them, and
each miss is silent: exit 0 and no output is what a guard with nothing to say looks like.
`BranchGuardParsingTests` holds the table of what is and is not a creation.
"""

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from deferrals import deferred, unusable
from shell_commands import command_segments, git_invocation, tokens_of, without_redirections
from velvet_hooks import BRANCH_BASES


HOOK_TOOLS = {"Bash"}

CHECKOUT_CREATE = ("-b", "-B")
SWITCH_CREATE = ("-c", "-C", "--create", "--force-create")

# `git branch` creates only when it is not doing one of these instead. Copy and move are excluded
# because both name their source, which is the explicit-start-point case.
BRANCH_NOT_CREATING = {
    "-d", "-D", "--delete", "-m", "-M", "--move", "-c", "-C", "--copy",
    "-l", "--list", "-a", "--all", "-r", "--remotes", "-v", "-vv", "--verbose",
    "--show-current", "--merged", "--no-merged", "--contains", "--points-at",
    "-u", "--set-upstream-to", "--unset-upstream", "--edit-description", "--format",
    "--sort", "-h", "--help",
}
BRANCH_VALUE_FLAGS = {
    "--contains", "--no-contains", "--points-at", "--merged", "--no-merged",
    "--set-upstream-to", "-u", "--format", "--sort",
}


def flag_value(token, following, flags):
    """The branch name a creation flag carries, in any of its three spellings."""
    for flag in flags:
        if token == flag:
            return following, 2
        if token.startswith(flag + "="):
            return token[len(flag) + 1:], 1
        if len(flag) == 2 and len(token) > 2 and token.startswith(flag):
            return token[2:], 1
    return None, 0


def created_by_flag(operands, flags):
    index = 0
    while index < len(operands):
        following = operands[index + 1] if index + 1 < len(operands) else None
        name, consumed = flag_value(operands[index], following, flags)
        if name:
            rest = operands[index + consumed:]
            start_point = next((t for t in rest if not t.startswith("-")), None)
            return name, start_point
        index += 1
    return None


def created_by_branch(operands):
    index = 0
    while index < len(operands):
        token = operands[index]
        if token.startswith("-"):
            flag = token.partition("=")[0]
            if flag in BRANCH_NOT_CREATING:
                return None
            if flag in BRANCH_VALUE_FLAGS and "=" not in token:
                index += 2
                continue
            index += 1
            continue
        following = operands[index + 1] if index + 1 < len(operands) else None
        start_point = following if following and not following.startswith("-") else None
        return token, start_point
    return None


def creations(command):
    """Every branch this command would create, as (name, start point, -C directory)."""
    found = []
    for segment in command_segments(command):
        invocation = git_invocation(without_redirections(tokens_of(segment)))
        if not invocation:
            continue
        directory, subcommand, operands = invocation
        if subcommand == "checkout":
            made = created_by_flag(operands, CHECKOUT_CREATE)
        elif subcommand == "switch":
            made = created_by_flag(operands, SWITCH_CREATE)
        elif subcommand == "branch":
            made = created_by_branch(operands)
        else:
            made = None
        if made:
            found.append((made[0], made[1], directory))
    return found


# Resolving an unexpanded operand fails, and this already refuses on that failure — the right
# direction for a guard over branch creation. What it used to print was the failure itself, as a
# detached HEAD at an empty SHA, so the refusal named a repository state that was never true.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git -C "$D" branch feat/x'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git branch tooling/probe"}


def git(cwd, *args):
    return subprocess.run(
        ["git", "-C", cwd, *args],
        capture_output=True, text=True, timeout=30,
    )


def head_description(cwd):
    ref = git(cwd, "rev-parse", "--abbrev-ref", "HEAD")
    branch = ref.stdout.strip()
    if branch and branch != "HEAD":
        return branch
    sha = git(cwd, "rev-parse", "--short", "HEAD")
    return f"detached at {sha.stdout.strip()}"


def record_branch_base(name, sha):
    try:
        with open(BRANCH_BASES, "a", encoding="utf-8") as bases:
            bases.write(f"{name} {sha}\n")
        return True
    except OSError as err:
        sys.stderr.write(f"Could not record branch base in {BRANCH_BASES}: {err}\n")
        return False


def refusal(cwd, name, start_point):
    """The refusal text for one creation, or None when it is allowed."""
    # A start point named on purpose is the sanctioned way to stack, and origin/main is current by
    # definition. `main` is the exception: it is what the refusal below recommends, and it is stale
    # exactly when the guard's second arm exists to say so.
    explicit_elsewhere = start_point is not None and start_point != "main"
    if explicit_elsewhere:
        return None

    head_not_main = False
    if start_point is None:
        head_ref = git(cwd, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()
        if head_ref != "main":
            on_main = git(cwd, "merge-base", "--is-ancestor", "HEAD", "main")
            if on_main.returncode != 0:
                head_not_main = True

    behind_count = None
    origin_main = git(cwd, "rev-parse", "--verify", "origin/main")
    origin_missing = origin_main.returncode != 0
    main_behind = False
    if not origin_missing:
        count = git(cwd, "rev-list", "--count", "main..origin/main")
        behind_count = count.stdout.strip()
        if behind_count and behind_count != "0":
            main_behind = True

    if not head_not_main and not main_behind:
        return None

    head = head_description(cwd)
    lines = []

    if head_not_main:
        lines.append(
            f"Refusing to create `{name}`: HEAD is {head}, not main and not a commit main contains."
        )
        lines += [
            "",
            "Branching from another change's tip carries its commits into the new branch. Here that "
            "produced a rebase against squash-merged content already on main, conflicts, and a pull "
            "request that had to be abandoned and reopened.",
            "",
            "git checkout main",
            "git pull",
            f"git checkout -b {name}",
            "",
            f"To stack on unmerged work on purpose, record intent and retry:",
            f'  echo "{name} <why> $(date +%s)" >> ~/.velvet-pr-deferrals',
        ]

    if main_behind:
        if lines:
            lines.append("")
        lines.append(
            f"Refusing to create `{name}`: local main is {behind_count} commit(s) behind origin/main."
        )
        lines += [
            "",
            "Branching from a stale main produces a branch the merge guard refuses later; the fix "
            "then is a rebase rather than a different starting point.",
            "",
            "git checkout main",
            "git pull",
            f"git checkout -b {name}",
        ]

    if origin_missing and not main_behind:
        lines += [
            "",
            "origin/main is not present locally; staleness against it was not checked.",
        ]

    return "\n".join(lines)


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    made = creations(event.get("tool_input", {}).get("command", ""))
    if not made:
        return 0

    cwd = event.get("cwd") or "."
    refusals = []
    # Every creation in the command is evaluated. Consulting only the first let an undeferred
    # creation chained after a deferred one through on the deferral meant for the other name.
    for name, start_point, directory in made:
        target = directory or cwd
        broken = unusable(name)
        if broken is not None:
            print(f"A deferral was written for {name}, and {broken} — so it is being ignored.",
                  file=sys.stderr)
        if deferred(name):
            # Parent tip at branch creation is gone after squash-merge; rebase --onto needs it now.
            head_sha = git(target, "rev-parse", "HEAD").stdout.strip()
            record_branch_base(name, head_sha)
            sys.stderr.write(
                f"Recorded base {head_sha} for `{name}`. "
                f"After parent merges (assumes origin/main is current — fetch first if unsure):\n"
                f"git fetch origin main\n"
                f"git rebase --onto origin/main {head_sha}\n"
            )
            continue
        text = refusal(target, name, start_point)
        if text:
            refusals.append(text)

    if not refusals:
        return 0
    sys.stderr.write("\n\n".join(refusals) + "\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())

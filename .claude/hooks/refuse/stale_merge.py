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
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations, unexpanded
from velvet_hooks import BRANCH_BASES


HOOK_TOOLS = {"Bash"}

# What an operand the shell has not expanded yet resolves to, which is nothing this can read. A merge
# guard errs toward refusing: allowing means the branch is merged with the check it exists for never
# having run, and the merge is what cannot be taken back.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'
UNRESOLVED = object()


def merge_targets(command):
    """The pull requests a merge in this command would land, "" meaning the current branch's.

    Read off tokens. The pattern this replaces required the number to sit immediately after the
    subcommand, so putting a flag first — this repository's own squash convention does — matched
    nothing and the guard returned 0 without spawning anything. It also carried no command-position
    anchor, so naming the command inside an argument spent a `gh pr view` and a `git fetch` on a
    refusal; that happened while this fix was being tested.
    """
    targets = []
    for operands in program_invocations(command, "gh", ("pr", "merge")):
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            targets.append(UNRESOLVED)
            continue
        targets.append(next((token for token in operands if token.isdigit()), ""))
    return targets


def git(*args):
    return subprocess.run(
        ["git", *args], stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=30
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
            "expanded yet, so whether its branch contains main cannot be read.\n\n"
            "Resolving the literal would fail and read as a pass, which is the check silently not "
            "happening. Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2
    pr = targets[0]

    try:
        view = ["gh", "pr", "view"]
        if pr:
            view.append(pr)
        view += ["--json", "headRefName", "--jq", ".headRefName"]
        head = subprocess.run(
            view,
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
    base = branch_base(head)
    # gh merges the current branch's pull request when no number is given, and naming it "#" reads
    # as a number nobody typed.
    label = "PR #" + pr if pr else "The pull request for " + head
    if base:
        sys.stderr.write(
            "{} does not contain the current main — it is {} commit(s) behind.\n\n"
            "Its checks passed against a main this merge does not produce. That is how main was broken "
            "here: two branches, each green, neither containing the other's change.\n\n"
            "This branch was created on top of unmerged work. After the parent merges, replay only "
            "this branch's commits with:\n"
            "git rebase --onto origin/main {}\n\n"
            "Note mergeStateStatus says CLEAN for this PR. GitHub only reports BEHIND when the base "
            "requires the head to be up to date, which protect-main does not — so that field cannot be "
            "the thing you check.\n".format(label, behind or "?", base)
        )
    else:
        sys.stderr.write(
            "{} does not contain the current main — it is {} commit(s) behind.\n\n"
            "Its checks passed against a main this merge does not produce. That is how main was broken "
            "here: two branches, each green, neither containing the other's change.\n\n"
            "Merge origin/main into {}, let the checks re-run, then merge.\n\n"
            "Note mergeStateStatus says CLEAN for this PR. GitHub only reports BEHIND when the base "
            "requires the head to be up to date, which protect-main does not — so that field cannot be "
            "the thing you check.\n".format(label, behind or "?", head)
        )
    return 2


if __name__ == "__main__":
    sys.exit(main())

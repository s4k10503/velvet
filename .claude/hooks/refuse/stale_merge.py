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

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}

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


def head_of(cwd, pr):
    """The branch a pull request would merge, or None when that could not be read.

    Read over REST rather than through `gh pr view --json`, which is GraphQL. The two endpoints
    meter separately, and an exhausted GraphQL quota emptied this reading here while `gh` went on
    working everywhere else. The braces are gh's own placeholders, not a format string.

    With no number the pull request is the current branch's, and its head ref is that branch, so git
    answers it without an API at all.
    """
    if not pr:
        finished = git(cwd, "rev-parse", "--abbrev-ref", "HEAD")
        if finished is None or finished.returncode != 0:
            return None
        branch = finished.stdout.decode().strip()
        return branch if branch and branch != "HEAD" else None
    try:
        finished = subprocess.run(
            ["gh", "api", "repos/{owner}/{repo}/pulls/" + pr, "--jq", ".head.ref"],
            cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=20,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    return finished.stdout.decode().strip() or None if finished.returncode == 0 else None


UNREADABLE_REFUSAL = (
    "Refusing `gh pr merge`: whether the branch contains the current main could not be read.\n\n"
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
            "expanded yet, so whether its branch contains main cannot be read.\n\n"
            "Resolving the literal would fail and read as a pass, which is the check silently not "
            "happening. Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2
    pr = targets[0]
    cwd = payload.get("cwd") or "."

    head = head_of(cwd, pr)
    if head is None:
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "the head branch of " + ("PR #" + pr if pr else "the current branch's pull request")
            + " came back empty"))
        return 2

    fetched = git(cwd, "fetch", "-q", "origin", "main", head)
    if fetched is None or fetched.returncode != 0:
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "origin/main and origin/{} could not be fetched".format(head)))
        return 2

    # 0 is contained and 1 is behind; any other code is git declining to answer. Both of the shorter
    # readings are wrong about it: `== 0` lets a git that never answered stand for a branch that is
    # up to date, and `!= 0` refuses naming a commit count nothing produced.
    ancestry = git(cwd, "merge-base", "--is-ancestor", "origin/main", "origin/" + head)
    if ancestry is None or ancestry.returncode not in (0, 1):
        sys.stderr.write(UNREADABLE_REFUSAL.format(
            "origin/main's ancestry against origin/{} could not be read".format(head)))
        return 2
    if ancestry.returncode == 0:
        return 0

    counted = git(cwd, "rev-list", "--count", "origin/{}..origin/main".format(head))
    behind = counted.stdout.decode().strip() if counted else ""
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

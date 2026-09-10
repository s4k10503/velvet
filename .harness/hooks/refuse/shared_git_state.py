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

One move out is allowed: onto the branch git records as the default. Without it two rules of this
repository contradict each other — `scripts/pr/settle.py` will not merge a pull request while a
worktree holds its branch, and the command that moves a worktree off a branch is the one refused
here.
"""

import glob
import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import (GLOB, NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD,
                            command_directory, git_invocations, unexpanded)


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

SWITCH_REFUSAL = (
    "Refused: `git switch` and `git stash` move state other worktrees share. Work in the worktree "
    "you were given; if you need a different base, say so and stop.\n"
)
CHECKOUT_REFUSAL = (
    "Refused: `git checkout` of a branch moves state other worktrees share. Restoring a file "
    "(`git checkout -- <path>`) is allowed; changing branch is not.\n"
)
RETURN_TO_BASE = (
    "The one move out is back to `{base}`, which git records here as the default branch: "
    "`git checkout {base}` or `git switch {base}` in the tree you are standing in, or either of "
    "them under a `-C` naming the primary checkout. Nothing may follow the subcommand but that "
    "branch name.\n"
)
NO_RECORDED_BASE = (
    "No default branch is recorded here — `git symbolic-ref refs/remotes/origin/HEAD` answers "
    "nothing — so there is no branch to offer a move out to. `git remote set-head origin -a` "
    "records it.\n"
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


def base_branch(root):
    """The default branch as git records it, or None where git records none.

    Read off git's own record rather than spelled out here. A literal would be a second copy of what
    git already holds, and a copy is what goes stale when the record it copies changes — the same
    objection `names_a_commit` states against resolving refs without asking git.

    Where git records none, `returns_to_base` exempts nothing — the direction `UNREADABLE_POLICY`
    declares.
    """
    prefix = "refs/remotes/origin/"
    try:
        completed = subprocess.run(
            ["git", "-C", root, "symbolic-ref", "--quiet", prefix + "HEAD"],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            timeout=10,
        )
    except Exception:
        return None
    if completed.returncode != 0:
        return None
    named = completed.stdout.decode("utf-8", "replace").strip()
    return named[len(prefix):] if named.startswith(prefix) else None


def primary_worktree(root):
    """The repository's primary working tree, which is the record git lists first, or None where git
    did not answer.

    `GitOwnPrimaryWorktreeTests` in `scripts/hooks/test_shared_git_state.py` poses that ordering to
    git against a linked worktree whose path sorts ahead of it, alongside the removal git declines
    for a primary — the pair the exemption in `returns_to_base` rests on.

    Read under `-z` so the record ends on a byte a path cannot hold; `NewlineNamedPathTests` is what
    fails if a line-delimited reading comes back.
    """
    try:
        completed = subprocess.run(
            ["git", "-C", root, "worktree", "list", "--porcelain", "-z"],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            timeout=10,
        )
    except Exception:
        return None
    if completed.returncode != 0:
        return None
    first = os.fsdecode(completed.stdout).split("\0", 1)[0]
    return first[len("worktree "):] if first.startswith("worktree ") else None


def aims_at_primary(directory, cwd):
    """Whether a `-C` names the primary checkout — of the repository the caller stands in, not of
    whichever repository stands at the far end of it.

    So a `-C` onto a tree git does not list here keeps the refusal, another repository's primary
    included: the exemption exists to unblock a merge of this repository, and that turns on this
    repository's own worktrees. A path the shell has yet to rewrite keeps it too, for the reason
    `returns_to_base` gives about the operand.
    """
    if unexpanded(directory):
        return False
    primary = primary_worktree(cwd)
    if primary is None:
        return False
    named = directory if os.path.isabs(directory) else os.path.join(cwd, directory)
    return os.path.realpath(primary) == os.path.realpath(named)


def returns_to_base(context, operands, cwd):
    """Whether this moves the tree the caller stands in, or the primary checkout, onto the default
    branch.

    Nothing but the base name may follow the subcommand: any flag, and any second operand, is an
    operand that is not the base, so it keeps the refusal without this having to know what it does.
    An operand the shell has yet to rewrite keeps it as well, since the text compared below is not
    the text git would receive — the same policy `UNEXPANDED_POLICY` declares for `restores_paths`.

    A `-C` is exempt only onto the primary checkout. Linked worktrees are what this repository hands
    to agents — `.claude/agents/velvet-implementer.md` says so — so one is likely to have somebody
    standing in it, and pulling that tree out from under them is the harm the rest of this guard
    exists to stop. The primary is nobody's assigned workspace under that convention, so its sitting
    on a feature branch is the anomaly, and the return to the base is what repairs one rather than
    what causes one.

    A `--git-dir` or a `GIT_DIR=` keeps the refusal whatever it names. With no `--work-tree` beside
    it such a command moves the named repository's HEAD and index and leaves its files where they
    were — `GitOwnGitDirectoryTests` poses that — so it returns no working tree to the base, and
    returning one is the move this exempts.

    Cleanliness is not asked about at all, because git decides it and decides it later:
    `GitOwnCheckoutRefusalTests` in `scripts/hooks/test_shared_git_state.py` poses both a checkout
    git declines and the contest — two worktrees on one branch — that would otherwise want a rule
    here, and fails when either stops holding.
    """
    if cwd is UNRESOLVED_CD:
        return False
    if context.git_directory is not None:
        return False
    if any(unexpanded(token) for token in operands):
        return False
    base = base_branch(cwd)
    if base is None or operands != [base]:
        return False
    named = context.working_directory
    return named is None or aims_at_primary(named, cwd)


def sole_expansions(named, cwd):
    """The single name each glob operand expands to, skipping every glob that matches otherwise.

    A glob does not take the refusal an unexpanded operand takes: `git checkout '*.cs'` is an
    ordinary restore, and refusing it is the over-refusal this guard was rewritten to remove. It is
    expanded instead, so the question is asked about the operand git receives.

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
    if cwd is UNRESOLVED_CD:
        # The glob below is expanded against the shell's directory, so an unplaced one leaves the
        # operands unresolved and nothing left to put to git.
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
    for context, subcommand, operands in git_invocations(
            command, {"switch", "stash", "checkout"}, git_directory=True):
        if subcommand == "stash":
            first = next((token for token in operands if not token.startswith("-")), "")
            if first not in STASH_READS:
                yield subcommand
        elif returns_to_base(context, operands, cwd):
            continue
        elif subcommand == "switch" or not restores_paths(context.working_directory, operands, cwd):
            yield subcommand


def move_out(cwd):
    """What a refusal offers instead, given where the command was going to run.

    An unplaced tree gets nothing rather than `NO_RECORDED_BASE`: a base nobody could look for is
    not a base git failed to record, and saying so would send a reader to `git remote` over a
    reading that never happened.
    """
    if cwd is UNRESOLVED_CD:
        return ""
    base = base_branch(cwd)
    return NO_RECORDED_BASE if base is None else RETURN_TO_BASE.format(base=base)


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
    cwd = command_directory(command, event.get("cwd") or ".")

    refused = next(refusals(command, cwd), None)
    if refused is None:
        return 0
    if cwd is UNRESOLVED_CD and refused == "checkout":
        # Apart from the two refusals below, which each state what the command does. Which of them a
        # checkout is turns on operands resolved against the tree, so an unplaced tree leaves no such
        # statement to make — while `git switch` and `git stash` move branch state whichever tree
        # they run in.
        sys.stderr.write("Refusing `git checkout`: whether it restores a file or moves shared "
                         f"branch state could not be read.\n\n{UNPLACEABLE_MOVE}\n\n"
                         f"{NAME_THE_TREE}\n")
        return 2
    text = CHECKOUT_REFUSAL if refused == "checkout" else SWITCH_REFUSAL
    sys.stderr.write(text if refused == "stash" else text + move_out(cwd))
    return 2


if __name__ == "__main__":
    sys.exit(main())

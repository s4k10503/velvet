#!/usr/bin/env python3
"""Refuse `gh pr create` where no mutation campaign has measured the change being proposed.

A green suite says nothing about whether the tests would have noticed the change, and the campaign
that asks is the one instrument here that nothing runs: it needs an editor per mutant, which is
around 25 minutes for a median branch and 14 sequential Unity jobs on the runner, so no workflow can
carry it. Its verdicts were therefore reported in pull-request bodies and never gated on, which is
how a branch shipped "two surviving mutants, same hole" as a declaration.

The campaign leaves a receipt naming what it measured. This asks for one covering the change in the
checkout the command runs in, and mutation_check.py owns both what that means and what a passing
verdict is — including that a change no operator reaches counts, since such a branch cannot earn a
passing run at all.

## Why `gh pr create` and not `gh pr merge`

Everything below is about the checkout `cwd` names. At `gh pr create` that checkout *is* the change
being proposed, so the question and the reading are the same thing. At `gh pr merge <n>` they are
not: the pull request is named by an operand, the merge is normally run from `main` after a pull, and
a guard reading the local tree there answers about a tree with no change in it at all — passing every
merge of every pull request while printing a positive verdict about a change it never read. That was
measured on this guard and is the shape this whole branch exists to remove, so the merge half is
gone rather than narrowed: it cannot be made honest from a local receipt store, because the receipt
is keyed on file content this machine holds and the branch being merged may never have been on it.

Merge-time therefore remains ungated, and `scripts/pr/settle.py` merges through `gh api -X PUT`,
which no hook matcher sees. The effective contract is one campaign at pull-request-open time.

A branch that changes no mutable package source is owed nothing and is not asked. An operand naming
a repository or a head other than this checkout means the change is not the one here, and that is
refused rather than answered.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations, unexpanded  # noqa: E402

HOOK_TOOLS = {"Bash"}

# Read for one thing only: whether an operand says the change is somewhere other than this checkout.
# An operand the shell has not expanded could be one, so this refuses on the same rule and over the
# same operands as `merge_unproven_head.py`.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr create --head $BRANCH'

# Both readings here are git's — the checkout the command runs in, and the merge base the receipt is
# keyed on. A git that cannot answer has not established that no receipt covers the change; it has
# established nothing, and nothing it established says a receipt is not owed.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr create --fill"}

SCRIPT = os.path.join("scripts", "test_quality", "mutation_check.py")

# What mutation_check.py exits when a receipt is owed and absent, as against any other non-zero
# status, which means it could not take the reading at all. ReceiptRefusalStatusTests pins the pair.
RECEIPT_REFUSAL = 3

# The whole hook, against the 25 s it is registered with: this budget and `repo_root`'s own have to
# fit inside that together, or the harness kills the hook and the refusal goes with it.
ROOT_TIMEOUT = 5
TIMEOUT = 15

# Operands saying the change is not this checkout's. `--head` names another branch; the repository
# flags name another repository entirely.
ELSEWHERE = ("-R", "--repo", "-H", "--head")

OWED = "no mutation campaign covers this branch's change."
UNREAD = "this guard could not read the state it decides from."


def repo_root(cwd):
    try:
        found = subprocess.run(["git", "-C", cwd, "rev-parse", "--show-toplevel"],
                               capture_output=True, text=True, timeout=ROOT_TIMEOUT)
    except (OSError, subprocess.SubprocessError):
        return None
    return found.stdout.strip() if found.returncode == 0 else None


def elsewhere(operands):
    """The operand saying this command is about a change other than the one in this checkout."""
    for token in operands:
        head = token.split("=", 1)[0]
        if head in ELSEWHERE:
            return token
    return None


def refuse(headline, detail):
    """A refusal names which of the two it is. One found a receipt owed and absent; the other found
    nothing at all, and saying "no campaign covers this" of it would be a claim about the change."""
    sys.stderr.write(
        "Refusing `gh pr create`: {}\n\n{}\n\n"
        "The campaign is what asks whether any test would have noticed the change. Nothing else "
        "does:\nthe suite is green either way.\n".format(headline, detail.rstrip()))
    return 2


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
        return 0
    command = event.get("tool_input", {}).get("command", "")
    invocations = program_invocations(command, "gh", ("pr", "create"))
    if not invocations:
        return 0

    for operands in invocations:
        loose = [token for token in operands if not token.startswith("-") and unexpanded(token)]
        if loose:
            return refuse(UNREAD,
                          "`{}` is an operand the shell has not expanded, so which change this "
                          "pull\nrequest would be about cannot be read here.".format(loose[0]))
        named = elsewhere(operands)
        if named:
            return refuse(UNREAD,
                          "`{}` says this pull request is not about the change in this checkout, "
                          "and\nthe receipt store here is keyed on this checkout's files. Nothing "
                          "read the change\nthat would be proposed.".format(named))

    cwd = event.get("cwd") or "."
    root = repo_root(cwd)
    if root is None:
        return refuse(UNREAD,
                      "git could not say which checkout {} is in, so nothing here read a "
                      "receipt at all.\nA reading that did not happen is not a reading that "
                      "found nothing owed.".format(cwd))
    # A repository that carries no campaign harness is a definite reading rather than a failed one:
    # there is no receipt to owe.
    script = os.path.join(root, SCRIPT)
    if not os.path.exists(script):
        return 0

    try:
        proc = subprocess.run(["python3", "-B", script, "--project", root, "--receipt"],
                              capture_output=True, text=True, timeout=TIMEOUT, cwd=root)
    except (OSError, subprocess.SubprocessError) as failure:
        return refuse(UNREAD,
                      "{} could not be run, so nothing read the receipt: {}".format(script, failure))
    if proc.returncode == 0:
        # Said rather than kept quiet. A check that ran and found nothing owed and a check that never
        # ran are the same silence, which is the confusion this whole guard exists inside — and it is
        # what lets the wiring fixture see that the gate decides at all rather than always returning.
        sys.stderr.write("Mutation receipt: {}\n".format(
            (proc.stdout or proc.stderr).strip() or "nothing owed"))
        return 0
    if proc.returncode == RECEIPT_REFUSAL:
        return refuse(OWED, proc.stdout or proc.stderr)
    return refuse(UNREAD,
                  "{} exited {} rather than answering whether a receipt covers this "
                  "change:\n{}".format(script, proc.returncode, (proc.stderr or proc.stdout).rstrip()))


if __name__ == "__main__":
    sys.exit(main())

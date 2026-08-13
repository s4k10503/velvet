#!/usr/bin/env python3
"""Refuse `gh pr create` and `gh pr merge` where no mutation campaign has measured the change.

A green suite says nothing about whether the tests would have noticed the change, and the campaign
that asks is the one instrument here that nothing runs: it needs an editor per mutant, which is
around 25 minutes for a median branch and 14 sequential Unity jobs on the runner, so no workflow can
carry it. Its verdicts were therefore reported in pull-request bodies and never gated on, which is
how a branch shipped "two surviving mutants, same hole" as a declaration.

The campaign leaves a receipt naming what it measured. This asks for one covering the change in front
of it, and mutation_check.py owns both what that means and what a passing verdict is — including that
a change no operator reaches counts, since such a branch cannot earn a passing run at all.

A branch that changes no mutable package source is owed nothing and is not asked.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations  # noqa: E402

HOOK_TOOLS = {"Bash"}

# This reads no operand of the command: which pull request, which body file and which flags are all
# irrelevant to it, and the tree it asks about is the same whatever they expand to.
UNEXPANDED_POLICY = "n/a"

# Both readings here are git's — the checkout the command runs in, and the merge base the receipt is
# keyed on. A git that cannot answer has not established that no receipt covers the change; it has
# established nothing, and "no receipt is owed" is the only reading that lets a command through.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr create --fill"}

SCRIPT = os.path.join("scripts", "test_quality", "mutation_check.py")

# Under the timeout this hook is registered with, so the harness does not kill it mid-check and take
# the refusal with it. Reading a receipt is a file read; the base resolution is one `git merge-base`.
TIMEOUT = 20


def repo_root(cwd):
    try:
        found = subprocess.run(["git", "-C", cwd, "rev-parse", "--show-toplevel"],
                               capture_output=True, text=True, timeout=15)
    except (OSError, subprocess.SubprocessError):
        return None
    return found.stdout.strip() if found.returncode == 0 else None


OWED = "no mutation campaign covers this branch's change."
UNREAD = "this guard could not read the state it decides from."


def refuse(what, headline, detail):
    """A refusal names which of the two it is. One found a receipt owed and absent; the other found
    nothing at all, and saying "no campaign covers this" of it would be a claim about the change."""
    sys.stderr.write(
        "Refusing `{}`: {}\n\n{}\n\n"
        "The campaign is what asks whether any test would have noticed the change. Nothing else "
        "does:\nthe suite is green either way.\n".format(what, headline, detail.rstrip()))
    return 2


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
        return 0
    command = event.get("tool_input", {}).get("command", "")
    wanted = [words for words in (("pr", "create"), ("pr", "merge"))
              if program_invocations(command, "gh", words)]
    if not wanted:
        return 0

    cwd = event.get("cwd") or "."
    root = repo_root(cwd)
    if root is None:
        return refuse("gh " + " ".join(wanted[0]), UNREAD,
                      "git could not say which checkout {} is in, so nothing here read a "
                      "receipt at all.\nA reading that did not happen is not a reading that "
                      "found nothing owed.".format(cwd))
    # A repository that carries no campaign harness is a definite reading rather than a failed one:
    # there is no receipt to owe.
    script = os.path.join(root, SCRIPT)
    if not os.path.exists(script):
        return 0

    base = os.environ.get("VELVET_MUTATION_BASE", "origin/main")
    try:
        proc = subprocess.run(["python3", "-B", script, "--project", root, "--base", base,
                               "--receipt"],
                              capture_output=True, text=True, timeout=TIMEOUT, cwd=root)
    except (OSError, subprocess.SubprocessError) as failure:
        return refuse("gh " + " ".join(wanted[0]), UNREAD,
                      "{} could not be run, so nothing read the receipt: {}".format(script, failure))
    if proc.returncode == 0:
        # Said rather than kept quiet. A check that ran and found nothing owed and a check that never
        # ran are the same silence, which is the confusion this whole guard exists inside — and it is
        # what lets the wiring fixture see that the gate decides at all rather than always returning.
        sys.stderr.write("Mutation receipt: {}\n".format(
            (proc.stdout or proc.stderr).strip() or "nothing owed"))
        return 0
    return refuse("gh " + " ".join(wanted[0]), OWED, proc.stdout or proc.stderr)


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Refuse a pull-request body that was not written for this branch.

A pull request was opened here whose whole description belonged to a different one, and it stayed
that way until a person read it. The body file was named `pr-body.md`, a previous pull request had
left a file of that name in the same scratch directory, and the write meant to replace it never ran:
it sat in the same `&&` chain as a `gh pr create` that a hook refused, and a refusal stops the whole
chain. Nothing about the retry looked wrong — the path existed, `gh` read it, the pull request opened
and went green.

The body is the one artefact nothing else here checks. CI reads the diff and so does review; the
description is whatever the file held.

Two questions, because "is this body about this branch" has exactly two a machine can answer:

- **The file predates the branch.** A body last written before the branch's first commit cannot
  describe what that branch does, whatever left it there. A missing file is the same failure one
  step earlier, which is the shape the incident took.
- **It names no issue.** A pull request that closes one says so and the issue closes itself on merge;
  one that closes nothing says `No issue: <where this came from>`, which is a decision rather than a
  silence. CONTRIBUTING.md owns that rule — this refuses, it does not define.

It does not read the prose: nothing can look at a description and tell whose branch it is.

**Both halves fail closed.** The first version resolved the branch from the session's directory,
which is the primary checkout while the branch under review sits in a worktree — the configuration
this is used from. `origin/main..HEAD` was then empty, the start came back unknown, and the check
that could not be made was skipped rather than refused. What a guard cannot determine is what it must
refuse; the alternative is silence exactly where the input is unusual.
"""

import json
import os
import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations, unexpanded
from velvet_hooks import BRANCH_BASES

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# open pull requests, which would leave every other session unguarded. `HookWiringCoverageTests`
# reads this declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# Only the body operand is resolved, so only that one going unexpanded leaves the question
# unanswered. A rewritten --title or --label costs nothing here, and refusing over one is how the
# first version blocked bodies it could read perfectly well.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr create --title t --body-file $BODY --label bug'

BODY_FILE_FLAGS = ("--body-file", "-F")
BODY_FLAGS = ("--body", "-b")
HEAD_FLAGS = ("--head", "-H")
BASE_FLAGS = ("--base", "-B")
WEB_FLAGS = {"--web", "-w"}

ISSUE_REFERENCE = re.compile(r"#\d+")
# A line rather than a flag, so the answer lands in the published body where a reader looking for
# where a change came from finds it. It has to carry a reason: the bare token would be a way of
# saying nothing in a form that satisfies the check, which is the silence this asks about.
NO_ISSUE_LINE = re.compile(r"^[^\S\n]*No issue[:.][^\S\n]*\S", re.MULTILINE | re.IGNORECASE)

# A body is often written before the commit it describes. This is how much of that ordering is
# allowed — our decision, not a measurement of anything.
DRAFTING_WINDOW = 900


def valued(operands, flags):
    """The value given to one of `flags`, or None. Handles both `--flag v` and `--flag=v`."""
    for index, token in enumerate(operands):
        name, sep, inline = token.partition("=")
        if name not in flags:
            continue
        if sep:
            return inline
        if index + 1 < len(operands):
            return operands[index + 1]
    return None


def git(cwd, *args):
    """stdout of a git command, or None when it cannot be run or fails."""
    try:
        done = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return None
    return done.stdout.strip() if done.returncode == 0 else None


def recorded_base(name):
    """The start point `branch_from_unmerged.py` recorded for this branch, or None."""
    try:
        with open(BRANCH_BASES, encoding="utf-8") as bases:
            matches = [line for line in bases if line.startswith(f"{name} ")]
    except OSError:
        return None
    if not matches:
        return None
    parts = matches[-1].split(None, 1)
    return parts[1].strip() if len(parts) == 2 else None


def branch_start(cwd, head, base):
    """When the branch's own first commit was authored, or None when that cannot be determined.

    Author date, not committer date: a rebase — which the sibling guards here prescribe by name —
    rewrites the committer date to now and would turn every correct body into a refusal.

    The head is taken from the command when it names one, because the directory the command runs in
    is routinely not the branch's: a session coordinating worktrees runs from the primary checkout.
    """
    ref = head or "HEAD"
    start = recorded_base(head) if head else None
    if start is None:
        start = git(cwd, "merge-base", base, ref)
    if start is None:
        return None
    stamps = git(cwd, "log", "--format=%at", f"{start}..{ref}")
    if stamps is None:
        return None
    authored = [line for line in stamps.split() if line.isdigit()]
    return int(authored[-1]) if authored else None


def refuse(message):
    print(message, file=sys.stderr)
    return 2


def check(operands, cwd):
    """0, or 2 with the reason written to stderr."""
    if WEB_FLAGS & set(operands):
        return 0

    path = valued(operands, BODY_FILE_FLAGS)
    text = valued(operands, BODY_FLAGS)
    if path is None and text is None:
        # --fill, --fill-first, --template: the body comes from commits or a template, which are not
        # a file this can date, and which the author did not write here to be checked.
        return 0
    if any(unexpanded(operand) for operand in (path, text) if operand is not None):
        return refuse(
            "Refusing `gh pr create`: the body operand is still unexpanded, so neither the file's\n"
            "provenance nor the issue it names can be read.\n\n"
            "Run it with the body spelled out.")

    if path == "-":
        return refuse(
            "Refusing `gh pr create`: the body comes from stdin, which this cannot read.\n\n"
            "Write it to a file and pass that, so the question of whose branch it describes has\n"
            "an answer.")
    if path is not None:
        resolved = Path(path) if os.path.isabs(path) else Path(cwd) / path
        if not resolved.exists():
            return refuse(
                f"Refusing `gh pr create`: {path} does not exist.\n\n"
                "A body file that is not there is usually one whose write did not run — a refused\n"
                "hook stops the whole `&&` chain it was in, including the write.")

        head = valued(operands, HEAD_FLAGS)
        base = valued(operands, BASE_FLAGS) or "origin/main"
        started = branch_start(cwd, head, base)
        if started is None:
            return refuse(
                "Refusing `gh pr create`: this cannot tell when the branch started, so it cannot\n"
                f"tell whether {path} was written for it.\n\n"
                f"Run it from the branch's own directory, or pass `--head <branch>`.")
        try:
            written = resolved.stat().st_mtime
        except OSError:
            return refuse(f"Refusing `gh pr create`: {path} cannot be read.")
        if written < started - DRAFTING_WINDOW:
            older = int((started - written) // 60)
            return refuse(
                f"Refusing `gh pr create`: {path} was last written {older} minutes before this\n"
                "branch's first commit, so it was written for something else.\n\n"
                "A pull request opened this way carries whatever that path held. On the occasion\n"
                "this guard was written for, that was another branch's description, from creation\n"
                "until a person read it.")
        try:
            text = resolved.read_text(encoding="utf-8", errors="replace")
        except OSError:
            return refuse(f"Refusing `gh pr create`: {path} cannot be read.")

    if text is not None and not ISSUE_REFERENCE.search(text) and not NO_ISSUE_LINE.search(text):
        return refuse(
            "Refusing `gh pr create`: the body names no issue.\n\n"
            "CONTRIBUTING.md asks every pull request to say where it came from. If it closes an\n"
            "issue, the first line closes it on merge:\n\n"
            "  Closes #<n>.\n\n"
            "If it closes nothing — a tooling change, a release — say that instead, so a reader\n"
            "who wonders where it came from finds an answer rather than a silence:\n\n"
            "  No issue: <where this came from>")
    return 0


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") != "Bash":
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        cwd = event.get("cwd") or "."
        if not isinstance(command, str):
            return 0
        for operands in program_invocations(command, "gh", ("pr", "create")):
            verdict = check(operands, cwd)
            if verdict:
                return verdict
        return 0
    except Exception as err:
        # Exit 1 is not a refusal — PreToolUse runs the tool anyway — so an unforeseen shape here
        # would let through exactly what this exists to stop.
        print(f"Refusing `gh pr create`: this guard failed to reach a verdict ({err!r}).",
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

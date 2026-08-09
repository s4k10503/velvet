#!/usr/bin/env python3
"""Refuse a pull-request body that was not written for this branch.

A pull request was opened whose whole description belonged to a different one, and it stayed that
way until a person read it. The body file was named `pr-body.md`, a previous pull request had left
a file of that name in the same scratch directory, and the write that was meant to replace it never
ran: it sat in the same `&&` chain as a `gh pr create` that a hook refused, and a refusal stops the
whole chain. Nothing about the second attempt looked wrong — the path existed, `gh` read it, the
pull request opened green.

Two checks, because "is this body about this branch" has two answers a machine can give:

- **The file is older than the branch.** A body written before the branch's first commit cannot
  describe what that branch does. This is what catches the stale file, whatever left it there, and
  it needs no idea of what the body says.
- **It names no issue.** Every pull request here that closes one says so on its first line, and the
  one that forgot it also had the wrong body — the same omission twice over. A pull request that
  genuinely closes nothing says so in a line of its own, which is a decision rather than a silence.

Not a check on what the body says: nothing can read prose and tell whose branch it is. These two
are what remains once that is given up, and both would have fired here.
"""

import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import program_invocations, unexpanded

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# open pull requests, which would leave every other session unguarded. `HookWiringCoverageTests`
# reads this declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# An operand the shell rewrites is a path this cannot stat and a body it cannot read, so the
# question goes unanswered. A guard that reads "I cannot tell" as "nothing to report" goes quiet
# exactly when its subject is unusual, which is the shape the stale body already exploited.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr create --title t --body-file $BODY --label bug'

BODY_FILE_FLAGS = ("--body-file", "-F")
BODY_FLAGS = ("--body", "-b")

ISSUE_REFERENCE = re.compile(r"#\d+")
# Written as a line rather than a flag so it lands in the published body, where a reader who
# wonders which issue a change came from finds the answer instead of nothing.
NO_ISSUE_LINE = re.compile(r"^\s*No issue[:.]", re.MULTILINE)

# Long enough that a body drafted alongside the first commit is not caught by clock skew between
# the file system and the commit stamp, short enough that a body from a previous branch is.
DRIFT = 300


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


def branch_start(cwd):
    """When the oldest commit this branch does not share with its base was authored, or None."""
    try:
        base = subprocess.run(
            ["git", "merge-base", "origin/main", "HEAD"],
            cwd=cwd, capture_output=True, text=True, timeout=5)
        if base.returncode != 0:
            return None
        stamps = subprocess.run(
            ["git", "log", "--format=%ct", f"{base.stdout.strip()}..HEAD"],
            cwd=cwd, capture_output=True, text=True, timeout=5)
        if stamps.returncode != 0:
            return None
        lines = [line for line in stamps.stdout.split() if line.isdigit()]
        return int(lines[-1]) if lines else None
    except (OSError, subprocess.SubprocessError, ValueError):
        return None


def body_of(operands, cwd):
    """(text, path, problem) for the body this invocation carries."""
    path = valued(operands, BODY_FILE_FLAGS)
    if path is None:
        return valued(operands, BODY_FLAGS), None, None
    resolved = Path(path) if os.path.isabs(path) else Path(cwd) / path
    if not resolved.exists():
        return None, resolved, f"{path} does not exist"
    started = branch_start(cwd)
    if started is not None and resolved.stat().st_mtime < started - DRIFT:
        age = int((started - resolved.stat().st_mtime) // 60)
        return None, resolved, (
            f"{path} was last written {age} minutes before this branch's first commit, "
            "so it was written for something else")
    try:
        return resolved.read_text(encoding="utf-8", errors="replace"), resolved, None
    except OSError:
        return None, resolved, f"{path} cannot be read"


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") != "Bash":
        return 0
    command = event.get("tool_input", {}).get("command", "")
    cwd = event.get("cwd") or "."

    for operands in program_invocations(command, "gh", ("pr", "create")):
        if "--web" in operands:
            continue
        if any(unexpanded(token) for token in operands):
            print(
                "Refusing `gh pr create`: an operand is still unexpanded, so neither the body's\n"
                "provenance nor the issue it closes can be read here.\n\n"
                "Run it with the operands spelled out.", file=sys.stderr)
            return 2

        text, _, problem = body_of(operands, cwd)
        if problem:
            print(
                f"Refusing `gh pr create`: {problem}.\n\n"
                "A pull request opened this way carries whatever that path held — on the occasion\n"
                "this guard was written for, another branch's description, from creation until a\n"
                "person read it.\n\n"
                "Write the body for this branch and pass that file.", file=sys.stderr)
            return 2

        if text is not None and not ISSUE_REFERENCE.search(text) and not NO_ISSUE_LINE.search(text):
            print(
                "Refusing `gh pr create`: the body names no issue.\n\n"
                "A pull request that closes one says so on its first line, and the issue then closes\n"
                "itself on merge. Add:\n\n"
                "  Closes #<n>.\n\n"
                "If it closes nothing, say that instead — a line of its own, so a reader who wonders\n"
                "where the change came from finds an answer rather than a silence:\n\n"
                "  No issue: <where this came from>", file=sys.stderr)
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())

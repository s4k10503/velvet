#!/usr/bin/env python3
"""Refuse a commit message or pull-request body read from outside the worktree it describes.

An implementer wrote its commit message to the session scratchpad, another agent overwrote that path
between the write and the `git commit --amend -F`, and the commit landed carrying a message closing
two issues the branch does not touch. It was caught because the author read the message back; nothing
in the repository would have.

The failure is silent by construction. `-F` reports success, the tree is right, the diff is right,
and only the prose is another change's — which a squash merge then makes the permanent record.

Measured over one session's transcripts, of every `-F` and `--body-file` naming a path under the
session scratchpad: 163 total, 0 inside the worktree they describe, 163 at the shared root, with
`msg.txt` reused 21 times and `commitmsg.txt` 14. The collision was not bad luck; it is what a
generic path shared between concurrent agents produces.

A worktree is per-agent here, so a message inside the one it describes cannot be another agent's.
That is the whole rule, and every one of the 163 reaches it by changing a path.

`pr_body_of_another_branch.py` asks whether the body says anything; this asks where it lives. Kept
apart because the remedies differ and a guard that refuses two things names one of them first.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import json
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from pr_body import valued as gh_valued  # noqa: E402
from shell_commands import (  # noqa: E402
    NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory, git_invocations,
    program_invocations, unexpanded)

HOOK_TOOLS = {"Bash"}

# `git commit -F <path>` and its long spelling. `-m` holds the message itself and is not a path.
MESSAGE_FLAGS = ("-F", "--file")
BODY_FILE_FLAGS = ("-F", "--body-file")

# The commands that put prose somewhere it outlives the tree it was written in.
GH_WORDS = (("pr", "create"), ("pr", "edit"), ("pr", "new"),
            ("issue", "create"), ("issue", "edit"), ("issue", "comment"), ("pr", "comment"))

# An operand the shell has not expanded names no path this can resolve, and answering about the
# literal would pass every one of them. Refused, because the shape being refused is precisely a path
# written once and read later.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git commit -F "$MSG"'

# A git that will not name a worktree leaves this with no question to ask rather than an unanswered
# one, and it costs nothing: `git commit` and `gh pr create` both need a repository themselves, so a
# command this stands down over fails on its own a moment later. The unexpanded case above is the
# opposite, and refuses, because there the command is fine and only the reading is blind.
UNREADABLE_POLICY = "allow"
UNREADABLE_PROBE = {"command": "git commit -F /tmp/velvet-message-probe.txt"}


def valued(operands, flags):
    """The last value given to any of `flags`, or None. `--flag=value` and `-Fvalue` count.

    `git commit`'s reading, and only its. gh's goes through `pr_body`, which knows which options carry
    a value -- this one does not, and measured, `gh pr create -dF /tmp/x.md` reads no body file here
    while gh posts one.

    The union `pr_body` falls back to is gh's table rather than git's, and neither reading covers the
    other. Measured over the flags this looks for:

        --file <path>       here /tmp/m.txt      under the union None
        -F <path>           here /tmp/m.txt      under the union /tmp/m.txt
        -F<path>            here /tmp/m.txt      under the union /tmp/m.txt

    `--file` is not one of gh's options at all, which is why the two stay apart. The attached form was
    read by neither until now: measured, `git commit -F/tmp/m.txt` reads that file and this guard let
    it through.

    A cluster -- `-aF <path>` -- is still not read, and closing it would take git's own table of which
    short options carry a value. That is the parse this repository has reduced twice, so what is done
    instead is the unambiguous half: `-F` at the head of the token, where no other letter can have
    claimed the tail.
    """
    found = None
    index = 0
    short = tuple(flag for flag in flags if len(flag) == 2 and flag.startswith("-"))
    while index < len(operands):
        token = operands[index]
        flag, separator, attached = token.partition("=")
        if flag in flags:
            if separator:
                found = attached
                index += 1
            else:
                found = operands[index + 1] if index + 1 < len(operands) else None
                index += 2
            continue
        head = next((flag for flag in short if token.startswith(flag) and len(token) > 2), None)
        if head is not None:
            found = token[len(head):]
            index += 1
            continue
        index += 1
    return found


def worktree_of(directory):
    """The top of the worktree `directory` sits in, or None when git will not say."""
    try:
        done = subprocess.run(["git", "-C", str(directory), "rev-parse", "--show-toplevel"],
                              capture_output=True, text=True, timeout=10)
    except (OSError, subprocess.SubprocessError):
        return None
    if done.returncode != 0:
        return None
    top = done.stdout.strip()
    return Path(top).resolve() if top else None


def inside(path, root, cwd):
    """Whether `path` resolves under `root`. A relative path is read from `cwd`, as the shell will."""
    try:
        resolved = (Path(cwd) / path).resolve() if not os.path.isabs(path) else Path(path).resolve()
    except OSError:
        return False
    return resolved == root or root in resolved.parents


def refuse(what, path, root):
    sys.stderr.write(
        f"Refusing `{what}`: {path}\nis outside {root}, the worktree it describes.\n\n"
        "A path several agents share is one another agent can overwrite between the write and the "
        "read,\nand the failure is silent — the command succeeds, the tree is right, and only the "
        "prose is\nanother change's. A worktree is per-agent, so a file inside it is nobody else's "
        "to write.\n\n"
        f"Write it under {root} and pass that path.\n")
    return 2


def judge(command, cwd):
    """0, or 2 with the reason written to stderr."""
    asked = [("git commit", operands, MESSAGE_FLAGS, None)
             for _, _, operands in git_invocations(command, ("commit",))]
    for words in GH_WORDS:
        for operands in program_invocations(command, "gh", words):
            asked.append(("gh " + " ".join(words), operands, BODY_FILE_FLAGS, words))

    named = [(what, valued(operands, flags) if words is None else gh_valued(operands, flags, words))
             for what, operands, flags, words in asked]
    named = [(what, path) for what, path in named if path is not None]
    if not named:
        return 0

    # Asked before the worktree, because a path the shell has not expanded is unreadable in any tree
    # and the worktree reading stands down where git will not answer.
    for what, path in named:
        if unexpanded(path):
            sys.stderr.write(
                f"Refusing `{what}`: the message path is still unexpanded, so which worktree it\n"
                "belongs to cannot be read here.\n\n"
                "Spell the path out, inside the worktree the change is in.\n")
            return 2

    here = command_directory(command, cwd)
    if here is UNRESOLVED_CD:
        # The tree the command runs in cannot be read, so neither can the question.
        sys.stderr.write("Refusing this command: which worktree the message belongs to could not "
                         f"be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2

    root = worktree_of(here)
    if root is None:
        return 0
    for what, path in named:
        if not inside(path, root, here):
            return refuse(what, path, root)
    return 0


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        if not isinstance(command, str):
            return 0
        return judge(command, event.get("cwd") or ".")
    except Exception as failure:  # noqa: BLE001 - a raise here turns the guard off silently
        print(f"message_outside_its_worktree: {failure}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())

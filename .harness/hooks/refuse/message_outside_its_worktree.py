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

The rule is only where the file sits: inside the worktree it describes. A checkout can hold more than
one agent, so a fixed name even there can be another agent's; AGENTS.md's Conventions take it the
rest of the way, into a directory of the author's own. Every one of the 163 reaches both by changing
a path.

For `git commit` that worktree is the one git makes the commit in, asked of git with the command's
own `-C`, `--git-dir`, `--work-tree`, `GIT_DIR=` and `GIT_WORK_TREE=` replayed, as
`commit_failing_fast_checks.py` asks it. Read off the directory the command runs in instead, a
commit made in a worktree from the primary checkout was refused over a message inside that
worktree, and a commit aimed at a second worktree passed with a message from the one it was run in.
`gh` takes no option naming a tree, so for it the worktree is the one the command runs in.

`pr_body_of_another_branch.py` asks whether the body says anything; this asks where it lives. Kept
apart because the remedies differ and a guard that refuses two things names one of them first.

Exit 2 refuses; exit 1 lets the tool through, so nothing here may raise.
"""

import json
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "lib"))

from pr_body import valued as gh_valued  # noqa: E402
from repository import DECODING, git_bytes  # noqa: E402
from shell_commands import (  # noqa: E402
    NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory, git_invocations,
    program_invocations, tree_selectors, unexpanded)

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


def rev_parse(here, selectors, flag):
    """None when git did not answer, which an empty prefix is not. Only the terminator comes off,
    for the reason `repository.toplevel` gives."""
    answer = git_bytes([*selectors, "rev-parse", flag], cwd=here, timeout=10)
    return None if answer is None else answer.removesuffix(b"\n").decode(**DECODING)


def placed(here, selectors):
    """(the top of the worktree git acts on, the directory it reads a relative path from), or None
    when git will not name a worktree.

    The second is git's answer too. Derived here instead, as `here` joined to a `-C` or as the top
    of the tree, it fails a case in scripts/hooks/test_message_outside_its_worktree.py's
    `TreeTheCommitIsMadeIn` that commits a relative message and reads back which file git took.
    """
    top = rev_parse(here, selectors, "--show-toplevel")
    prefix = rev_parse(here, selectors, "--show-prefix") if top else None
    if prefix is None:
        return None
    return Path(top).resolve(), Path(top, prefix)


def inside(path, root, base):
    """Whether `path` resolves under `root`. A relative path is read from `base`."""
    try:
        resolved = (Path(base) / path).resolve() if not os.path.isabs(path) else Path(path).resolve()
    except OSError:
        return False
    return resolved == root or root in resolved.parents


def refuse(what, path, root):
    sys.stderr.write(
        f"Refusing `{what}`: {path}\nis outside {root}, the worktree it describes.\n\n"
        "A path several agents share is one another agent can overwrite between the write and the "
        "read,\nand the failure is silent — the command succeeds, the tree is right, and only the "
        "prose is\nanother change's.\n\n"
        f"Write it in a directory of your own under {root / 'Logs'},\n"
        "as AGENTS.md's Conventions say, and pass that path.\n")
    return 2


def judge(command, cwd):
    """0, or 2 with the reason written to stderr."""
    asked = [("git commit", operands, MESSAGE_FLAGS, None, tree_selectors(context))
             for context, _, operands in git_invocations(command, ("commit",), git_directory=True)]
    for words in GH_WORDS:
        for operands in program_invocations(command, "gh", words):
            asked.append(("gh " + " ".join(words), operands, BODY_FILE_FLAGS, words, []))

    named = [(what, valued(operands, flags) if words is None else gh_valued(operands, flags, words),
              selectors)
             for what, operands, flags, words, selectors in asked]
    named = [(what, path, selectors) for what, path, selectors in named if path is not None]
    if not named:
        return 0

    # Asked before the worktree, because a path the shell has not expanded is unreadable in any tree
    # and the worktree reading stands down where git will not answer.
    for what, path, _ in named:
        if unexpanded(path):
            sys.stderr.write(
                f"Refusing `{what}`: the message path is still unexpanded, so which worktree it\n"
                "belongs to cannot be read here.\n\n"
                "Spell the path out, inside the worktree the change is in.\n")
            return 2

    here = command_directory(command, cwd)
    if here is UNRESOLVED_CD:
        # Every reading below is taken from the directory the command runs in.
        sys.stderr.write("Refusing this command: which worktree the message belongs to could not "
                         f"be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2

    for what, path, selectors in named:
        # Asked before the reading, as the path is: whatever git makes of the literal, it is not the
        # tree the command names.
        unresolved = [value for _, value in selectors if unexpanded(value)]
        if unresolved:
            sys.stderr.write(
                f"Refusing `{what}`: the tree it is made in is named by an operand the shell has not\n"
                "expanded yet, so which worktree the message belongs to cannot be read here.\n\n"
                + "\n".join("  " + value for value in unresolved)
                + "\n\nSpell the directory out.\n")
            return 2
        tree = placed(here, [part for flag, value in selectors
                             for part in (flag, os.path.expanduser(value))])
        if tree is None:
            continue
        root, base = tree
        if not inside(path, root, base):
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

#!/usr/bin/env python3
"""Refuse `git commit --amend` when HEAD is already reachable from a remote-tracking ref.

A review round's findings cite the commit they were taken on. Amending replaces that commit, so the
round and the layer that answered it stop being separable, and the branch cannot land without a
force-push. It happened twice in one session, and both times what caught it was somebody checking
the parent and the file set by hand before rewriting the remote.

An amend of an unpushed commit is the ordinary case and stays allowed. That is what decides the
predicate: refusing every amend would cost more than the defect, so the question asked is whether
some `refs/remotes/*` reaches HEAD, and nothing else decides it.

`git for-each-ref --contains` and `git branch -r --contains` were measured against each other before
this picked one, since either would serve. `test_amend_of_published_commit.py` holds them to the
agreement that made the choice free.

Run: python3 scripts/hooks/test_amend_of_published_commit.py
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import COMMIT_VALUE_FLAGS, git_invocations
import repository


HOOK_TOOLS = {"Bash"}

# Registered on the event rather than on the agents expected to push, for the reason
# `shared_git_state.py` states of its own registration.
HOOK_SCOPE = "session"

AMEND = "--amend"

# `-C` names the tree the amend acts on, and it is the one operand of this command the shell
# rewrites. What refuses it is the unreadable answer below rather than a check of its own, which is
# a spelling `test_amend_of_published_commit.py` poses rather than a claim about what git does with
# a literal.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git -C $WORKTREE commit --amend'

# Refusing an amend git could not place costs a command retyped; allowing it rewrites a reviewed
# commit on a reading that did not happen.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git commit --amend"}

# What a reading that did not answer resolves to, kept apart from "no remote ref reaches HEAD" —
# which is the pass.
UNREADABLE = object()


def amends(command):
    """The `-C` directory of each `git commit --amend` in the command, in the order they run.

    `--amend` is read as a flag rather than found in the text, so a message that spells it
    (`git commit -m --amend`) is the message and not an amend. Which options take a value is git's
    grammar and lives in `lib/shell_commands.py`; which of them this guard cares about is the
    single flag above.
    """
    found = []
    for directory, _, operands in git_invocations(command, {"commit"}):
        index = 0
        amending = False
        while index < len(operands):
            token = operands[index]
            if token == "--":
                break
            if token == AMEND:
                amending = True
                index += 1
                continue
            flag = token.partition("=")[0]
            if token.startswith("--"):
                index += 2 if flag in COMMIT_VALUE_FLAGS and "=" not in token else 1
                continue
            if token.startswith("-") and len(token) > 1:
                # In a short group such as -am each letter is its own flag, and the first
                # value-taking one ends the group by taking either the rest of the token or the
                # next one.
                takes_next = False
                for position, letter in enumerate(token[1:]):
                    if "-" + letter in COMMIT_VALUE_FLAGS:
                        takes_next = position + 2 == len(token)
                        break
                index += 2 if takes_next else 1
                continue
            index += 1
        if amending:
            found.append(directory)
    return found


def publishing_refs(directory, cwd):
    """The remote-tracking refs that reach HEAD, or UNREADABLE when git did not answer.

    The namespace is written into the command rather than left to porcelain's idea of a remote
    branch, so what counts as published is readable here.
    """
    root = directory or cwd
    answer = repository.git(
        ["-C", root, "for-each-ref", "--contains", "HEAD", "--format=%(refname)", "refs/remotes/"],
        cwd=None, timeout=15)
    if answer is None:
        return UNREADABLE
    return [line.strip() for line in answer.splitlines() if line.strip()]


def named(refs):
    """A ref to name in the refusal, preferring a branch over the symbolic default it points at."""
    branches = [ref for ref in refs if not ref.endswith("/HEAD")] or refs
    return branches[0].replace("refs/remotes/", "", 1)


def head_sha(directory, cwd):
    answer = repository.git(["-C", directory or cwd, "rev-parse", "--short", "HEAD"],
                            cwd=None, timeout=15)
    return answer.strip() if answer else "HEAD"


PUBLISHED = "Refusing `git commit --amend`: this commit is already published."
# A blind guard saying what a seeing one says is the defect `lib/repository.py` owns the sentence
# for on the Stop side: the reader records the failed reading as a fact about the commit and goes
# looking for a push nobody made.
UNREAD = "Refusing `git commit --amend`: git could not say whether this commit is published."


def findings(command, cwd):
    """(headline, what to say about each tree) for the amends refused, or None when none is."""
    read, blind = [], []
    for directory in amends(command):
        refs = publishing_refs(directory, cwd)
        if refs is UNREADABLE:
            blind.append(f"{directory or cwd}: git did not answer")
        elif refs:
            read.append(f"{head_sha(directory, cwd)} is reachable from {named(refs)}")
    if not read and not blind:
        return None
    return (PUBLISHED if read else UNREAD), read + blind


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

    found = findings(command, event.get("cwd") or ".")
    if found is None:
        return 0

    headline, details = found
    lines = "\n".join(f"  {detail}" for detail in details)
    sys.stderr.write(
        f"{headline}\n\n"
        f"{lines}\n\n"
        "A review round's findings cite the SHA they were taken on. Amending replaces it, so the "
        "round and the layer that answered it stop being separable, and the branch needs a "
        "force-push to land.\n\n"
        "Commit the change as its own layer on top. Amending an unpushed commit is not refused, so "
        "if this one is genuinely unpublished, say what git is reporting and stop.\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())

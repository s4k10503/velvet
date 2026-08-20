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
SHORTEST_AMEND = "--am"

# The repository selectors are replayed together; `PublishedHeadTests` holds the cases where `-C`
# and `--git-dir` disagree. What refuses one the shell has yet to rewrite is the unreadable answer
# below rather than a check of its own, which is a spelling `test_amend_of_published_commit.py`
# poses rather than a claim about what git does with a literal.
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
    """The tree each `git commit --amend` in the command acts on, in the order they run.

    `--amend` is read as a flag rather than found in the text, so a message that spells it
    (`git commit -m --amend`) is the message and not an amend. Which options swallow the token
    after them lives in `lib/shell_commands.py`; which of them this guard cares about is the single
    flag above.

    A git directory is taken alongside `-C` because everything done with the answer here is to hand
    it back to git.
    """
    found = []
    for context, _, operands in git_invocations(command, {"commit"}, git_directory=True):
        index = 0
        amending = False
        while index < len(operands):
            token = operands[index]
            if token == "--":
                break
            if token.startswith(SHORTEST_AMEND) and AMEND.startswith(token):
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
            found.append(context)
    return found


def git_location_arguments(context, cwd):
    arguments = ["-C", context.working_directory or cwd]
    if context.git_directory:
        arguments.append(f"--git-dir={context.git_directory}")
    return arguments


def display_location(context, cwd):
    """The selectors the reading was taken with, spelled as they were handed to git.

    A failed reading does not say which of the two git could not resolve, so neither is named alone.
    """
    return " ".join(git_location_arguments(context, cwd))


def publishing_refs(context, cwd):
    """The remote-tracking refs that reach HEAD, or UNREADABLE when git did not answer.

    The namespace is written into the command rather than left to porcelain's idea of a remote
    branch, so what counts as published is readable here.
    """
    answer = repository.git(
        [*git_location_arguments(context, cwd), "for-each-ref", "--contains", "HEAD",
         "--format=%(refname)", "refs/remotes/"],
        cwd=None, timeout=15)
    if answer is None:
        return UNREADABLE
    return [line.strip() for line in answer.splitlines() if line.strip()]


def named(refs, context, cwd):
    """How to describe the refs reaching HEAD: one of them, and how many others there are.

    The branch's own upstream is preferred where it is one of them. Every ref here answers the
    question the guard asked, but a reader sent to a branch they are not on reads the refusal as
    being about somebody else's work — and on a commit merged long ago there are a dozen to pick
    from, so which one is named is not a detail.
    """
    upstream = repository.git(
        [*git_location_arguments(context, cwd), "rev-parse", "--abbrev-ref",
         "--symbolic-full-name", "@{upstream}"],
        cwd=None, timeout=15)
    tracked = "refs/remotes/" + upstream.strip() if upstream and upstream.strip() else None
    # `origin/HEAD` is the default branch under another name, so naming it says less than naming
    # what it points at, which is in this list whenever it is.
    branches = [ref for ref in refs if not ref.endswith("/HEAD")] or refs
    chosen = tracked if tracked in branches else branches[0]
    others = len(branches) - 1
    return (chosen.replace("refs/remotes/", "", 1)
            + (f" and {others} other remote branch{'es' if others > 1 else ''}" if others else ""))


def head_sha(context, cwd):
    answer = repository.git([*git_location_arguments(context, cwd), "rev-parse", "--short", "HEAD"],
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
    for context in amends(command):
        refs = publishing_refs(context, cwd)
        if refs is UNREADABLE:
            blind.append(f"{display_location(context, cwd)}: git did not answer")
        elif refs:
            read.append(f"{head_sha(context, cwd)} is reachable from "
                        f"{named(refs, context, cwd)}")
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
        "Where this is a review round being answered, the answer is a commit of its own on top. "
        "Amending an unpushed commit is not refused, so if this one is genuinely unpublished, say "
        "what git is reporting and stop.\n")
    return 2


if __name__ == "__main__":
    sys.exit(main())

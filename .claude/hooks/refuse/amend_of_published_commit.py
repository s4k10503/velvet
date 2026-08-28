#!/usr/bin/env python3
"""Refuse `git commit --amend` when HEAD is already reachable from a remote-tracking ref.

A review round's findings cite the commit they were taken on. Amending replaces that commit, so the
round and the layer that answered it stop being separable, and the branch cannot land without a
force-push.

An amend of an unpushed commit is the ordinary case and stays allowed, since refusing every amend
would cost more than the defect. What is asked of git is whether some `refs/remotes/*` reaches HEAD;
a reading that came back with no answer at all is the separate case `UNREADABLE_POLICY` below
decides, and it decides it the other way.

`git for-each-ref --contains` and `git branch -r --contains` were measured against each other before
this picked one, since either would serve. `test_amend_of_published_commit.py` holds them to the
agreement that made the choice free.

Run: python3 scripts/hooks/test_amend_of_published_commit.py
"""

import collections
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import (  # noqa: E402
    COMMIT_VALUE_FLAGS,
    UNRESOLVED_CD,
    git_invocations,
    leading_cd,
    unexpanded,
)
import repository


HOOK_TOOLS = {"Bash"}

# Registered on the event rather than on the agents expected to push, for the reason
# `shared_git_state.py` states of its own registration.
HOOK_SCOPE = "session"

AMEND = "--amend"
SHORTEST_AMEND = "--am"

# The repository selectors are replayed together; `PublishedHeadTests` holds the cases where `-C`
# and `--git-dir` disagree. What refuses one the shell has yet to rewrite is the unreadable answer
# below rather than a check of its own — the spelling is recognised only to say what to do about it,
# which `test_amend_of_published_commit.py` poses rather than a claim about what git does with a
# literal.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'git -C $WORKTREE commit --amend'

# Refusing an amend git could not place costs a command retyped; allowing it rewrites a reviewed
# commit on a reading that did not happen.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "git commit --amend"}

# What a reading that did not answer resolves to, kept apart from "no remote ref reaches HEAD" —
# which is the pass. It carries git's own message, which is what names the selector that failed.
Unreadable = collections.namedtuple("Unreadable", "message")


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


def repository_selectors(context):
    """Each repository selector to replay: the tokens git receives, and the path inside them.

    The event's own directory is not one of them. Every reading below runs there, so adding a `-C`
    for it would put a selector in the refusal that the contributor did not write.
    """
    selectors = []
    if context.working_directory:
        selectors.append((["-C", context.working_directory], context.working_directory))
    if context.git_directory:
        selectors.append(([f"--git-dir={context.git_directory}"], context.git_directory))
    return selectors


def git_location_arguments(context):
    return [token for tokens, _ in repository_selectors(context) for token in tokens]


def publishing_refs(context, cwd):
    """The remote-tracking refs that reach HEAD, or `Unreadable` when git did not answer.

    The namespace is written into the command rather than left to porcelain's idea of a remote
    branch, so what counts as published is readable here.

    Taken from the event's directory, which is where the tool call starts. Taken from this process's
    own instead, `git -C ../x` answered about whichever repository sat beside wherever the hook was
    started — measured allowing an amend of published history, and `PublishedHeadTests` is what
    fails if that comes back.
    """
    answer = repository.git_answer(
        [*git_location_arguments(context), "for-each-ref", "--contains", "HEAD",
         "--format=%(refname)", "refs/remotes/"],
        cwd=cwd, timeout=15)
    if answer.code != 0:
        return Unreadable(answer.stderr)
    return [line.strip() for line in answer.stdout.splitlines() if line.strip()]


def named(refs, context, cwd):
    """How to describe the refs reaching HEAD: one of them, and how many others there are.

    The branch's own upstream is preferred where it is one of them. Every ref here answers the
    question the guard asked, but a reader sent to a branch they are not on reads the refusal as
    being about somebody else's work — and on a commit merged long ago there are a dozen to pick
    from, so which one is named is not a detail.
    """
    upstream = repository.git(
        [*git_location_arguments(context), "rev-parse", "--abbrev-ref",
         "--symbolic-full-name", "@{upstream}"],
        cwd=cwd, timeout=15)
    tracked = "refs/remotes/" + upstream.strip() if upstream and upstream.strip() else None
    # `origin/HEAD` is the default branch under another name, so naming it says less than naming
    # what it points at, which is in this list whenever it is.
    branches = [ref for ref in refs if not ref.endswith("/HEAD")] or refs
    chosen = tracked if tracked in branches else branches[0]
    others = len(branches) - 1
    return (chosen.replace("refs/remotes/", "", 1)
            + (f" and {others} other remote branch{'es' if others > 1 else ''}" if others else ""))


def head_sha(context, cwd):
    answer = repository.git([*git_location_arguments(context), "rev-parse", "--short", "HEAD"],
                            cwd=cwd, timeout=15)
    return answer.strip() if answer else "HEAD"


def quoted_selectors(selectors, message):
    """The selectors the failed reading named back: git quotes the path it could not resolve.

    `UnreadableCauseTests` fails when it stops.
    """
    return [tokens for tokens, path in selectors if f"'{path}'" in message]


def blamed(selectors, message, cwd):
    """What to call the reading that failed: what its message names, or where it ran.

    Naming both selectors puts the one that resolved fine beside the one that did not, and a
    contributor who mistyped a git directory is then shown a `-C` they never wrote. Where the
    message names the reading's own directory and none of the selectors, naming them would blame a
    spelling nothing complained about.
    """
    named = quoted_selectors(selectors, message)
    if not named and f"'{cwd}'" in message:
        return cwd
    spelled = named or [tokens for tokens, _ in selectors]
    return " ".join(token for tokens in spelled for token in tokens) or cwd


# What the shell rewrites besides the substitutions `unexpanded` recognises. Widening that reading
# instead would make `shared_git_state.py` refuse `git checkout '*.cs'`. What is asked here is
# narrower, and only once a reading has already failed: whether git was handed the path the command
# names.
SHELL_REWRITES = re.compile(r"^~|[*?\[]")


def stands_for_a_path(path):
    return unexpanded(path) or bool(SHELL_REWRITES.search(path))


# What a failed reading leaves the contributor to do. The markers are git's own words rather than a
# classification made here, and `UnreadableCauseTests` fails when git stops writing them.
UNBORN_HEAD = "Commit first: that branch has nothing on it to amend."
NO_REPOSITORY = "Name a repository the amend is for, or pose it from inside one."
NOT_A_REPOSITORY = "Check the path: git found no repository where that selector points."
NO_DIRECTORY = "Check the path: git could not enter that directory."

# A selector that is not a repository and a reading from a directory in none arrive under the same
# marker, and leave the contributor in different positions: one named a path to check and the other
# named nothing. What separates them is whether the command named a selector at all, since that is
# what the two actions differ on; keying on the path git quoted back instead sent a `-C` naming a
# directory in no repository to the action that tells a contributor to name one, which
# `UnreadableTreeTests` poses.
NO_REPOSITORY_MARKER = "not a git repository"

UNREADABLE_ACTIONS = (
    ("malformed object name HEAD", UNBORN_HEAD),
    ("cannot change to", NO_DIRECTORY),
    (NO_REPOSITORY_MARKER, NO_REPOSITORY),
)
UNEXPANDED_SELECTOR = ("Write the path out: a hook is handed the command before the shell expands "
                       "it, so what git could not reach is the selector as spelled rather than the "
                       "path it stands for.")
UNCLASSIFIED = "Establish by hand whether the commit is published, and say what you found."


def unreadable_action(selectors, message):
    """What to do about a reading that failed.

    A selector the shell has yet to rewrite is answered ahead of the markers: git resolved the
    literal, so its message is about a directory the command was never going to name, and a table
    read first would send the contributor to check that one.
    """
    for _, path in selectors:
        if stands_for_a_path(path):
            return UNEXPANDED_SELECTOR
    for marker, action in UNREADABLE_ACTIONS:
        if marker not in message:
            continue
        if marker == NO_REPOSITORY_MARKER and selectors:
            return NOT_A_REPOSITORY
        return action
    return UNCLASSIFIED


PUBLISHED = "Refusing `git commit --amend`: this commit is already published."
# A blind guard saying what a seeing one says is the defect `lib/repository.py` owns the sentence
# for on the Stop side: the reader records the failed reading as a fact about the commit and goes
# looking for a push nobody made.
UNREAD = "Refusing `git commit --amend`: git could not say whether this commit is published."

REWRITES_THE_ROUND = (
    "A review round's findings cite the SHA they were taken on. Amending replaces it, so the round "
    "and the layer that answered it stop being separable, and the branch needs a force-push to "
    "land.")
ANSWER_ON_TOP = ("Where this is a review round being answered, the answer is a commit of its own "
                 "on top.")
# The sentence `lib/repository.py` owns, so that a guard reporting its own blindness cannot drift
# into a second way of saying it.
NOTHING_READ = f"{repository.SELF_REPORT} the commit. What failed is the reading:"


def findings(command, cwd):
    """(headline, the trees read, the trees that did not, what to do about those), or None."""
    read, blind, actions = [], [], []
    for context in amends(command):
        refs = publishing_refs(context, cwd)
        if isinstance(refs, Unreadable):
            selectors = repository_selectors(context)
            said = next((line for line in refs.message.splitlines() if line.strip()),
                        "git did not answer")
            blind.append(f"{blamed(selectors, refs.message, cwd)}: {said}")
            action = unreadable_action(selectors, refs.message)
            if action not in actions:
                actions.append(action)
        elif refs:
            read.append(f"{head_sha(context, cwd)} is reachable from "
                        f"{named(refs, context, cwd)}")
    if not read and not blind:
        return None
    return (PUBLISHED if read else UNREAD), read, blind, actions


def refusal(headline, read, blind, actions):
    """The whole refusal, in the order that keeps each paragraph next to what it is about.

    A command carrying two amends can read one tree and fail on the other, so both halves can be
    here at once, and `NOTHING_READ` opens on a pronoun: it has to sit against the readings that
    failed rather than against the ones that answered.
    """
    listed = lambda entries: "\n".join(f"  {entry}" for entry in entries)
    paragraphs = [headline]
    if read:
        paragraphs += [listed(read), REWRITES_THE_ROUND, ANSWER_ON_TOP]
    if blind:
        paragraphs += [listed(blind), NOTHING_READ, listed(actions)]
    return "\n\n".join(paragraphs) + "\n"


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

    where = leading_cd(command)
    if where is UNRESOLVED_CD:
        sys.stderr.write(
            "Refusing `git commit --amend`: the command changes into a directory the shell has not "
            "expanded yet, so which tree this amends cannot be read.\n\n"
            "Spell the path out, or run the amend from the worktree itself.\n")
        return 2
    found = findings(command, where or event.get("cwd") or ".")
    if found is None:
        return 0

    sys.stderr.write(refusal(*found))
    return 2


if __name__ == "__main__":
    sys.exit(main())

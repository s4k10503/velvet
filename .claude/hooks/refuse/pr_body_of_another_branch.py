#!/usr/bin/env python3
"""Refuse a pull-request body that names neither an issue nor a reason for having none.

A change that came straight out of an issue was merged without linking it, so the issue stayed open
with its work already shipped. The body is the one artefact nothing else here checks: CI reads the
diff and so does review, and the description is whatever the file held.

CONTRIBUTING.md owns the rule; this refuses, it does not define. An answer is a closing or referring
keyword against a number, or a full issue URL, or a `No issue: <where this came from>` line — a
decision rather than a silence. A bare `#` and digits is not one: a six-digit colour satisfies it,
and a number mentioned in prose closes nothing on merge, which is the half of the accident that hurt.

## The staleness check is gone, and nothing replaced it

Earlier rounds of this also dated the body file against the branch's first commit, to catch the
leftover of a previous pull request being reused by the next. It was posed the case it exists for,
built from two pull requests opened one after another here: a body file stamped at the moment the
first was opened, against the branch of the second, whose first commit lands sixteen seconds later.
The check allowed fifteen minutes, and allowed it. The comparison was one-sided besides, so a
leftover stamped after the branch's first commit was allowed with no bound at all.

No window fixes that, wider or narrower, because of when a PreToolUse hook runs. Where the body is
written by the very command that posts it — a heredoc into the path, then `gh pr create` reading it
— this runs before the write. The mtime it reads then belongs to whatever was already at that path,
which is the leftover itself, and the file it would date is the one the command is about to
overwrite. A narrower window refuses correct bodies; a wider one admits more leftovers; neither is
reading the description that will be posted.

So it does not judge what the body is about at all — only whether it says where the change came from.

## What it reads, and what it refuses for being unreadable

It reads the description that will be posted, so the body has to be there before the command runs.
What it cannot read it refuses rather than skips, because a guard that skips reports what a guard
that passed reports: an absent file, a path the shell has yet to expand, a body on stdin, a relative
path in a command that also changes directory, a file the filesystem will not hand over. Each
refusal carries its own remedy.

An inline `--body` is the description rather than a name for it, so it is searched as it stands and
its backticks and `$` cost nothing. Only when the search finds no answer does an expansion in it
matter, and then it is the same refusal for the same reason: the text posted is not the text here.

Nor does it see a `gh pr create` the shared parser does not claim — behind `sudo`, inside `bash -c`,
or with gh's own options before the subcommand. Four attempts at that reached in both wrong
directions: enumerating wrapper names refused `sudo gh auth status` and a `timeout gh run watch` CI
wait, matching the phrase refused prose including the message of the commit that added it, and
teaching the shared parser gh's options made every value-taking option it did not name walk off the
subcommand and match nothing — in six guards, not one. What is left is what has never been in doubt.
"""

import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
# GuardCommandCoverageTests poses its table to this module rather than to the lib, so
# `BODY_FILE_FLAGS` stays bound here although nothing below reads it.
from pr_body import (BODY_FILE_FLAGS, BODY_FLAGS, MISSING, RELATIVE_AFTER_MOVE, STDIN,
                     UNEXPANDED_PATH, UNREADABLE, effective_body, invocations, valued)
from shell_commands import unexpanded

# Registered on the event in .claude/settings.json rather than narrowed to the agents expected to
# open pull requests, which would leave every other session unguarded. `HookWiringCoverageTests`
# reads this declaration to check that the registration is still there.
HOOK_SCOPE = "session"

# The tools this acts on, gated on below rather than spelled a second time there: two statements of
# the same set drift.
HOOK_TOOLS = {"Bash"}

# The body file is opened, so a path the shell has not expanded leaves nothing to open. An inline
# body is searched rather than resolved, and is refused only where the search comes back empty.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr create --title t --body-file $BODY'

UNREADABLE_POLICY = "none"
UNREADABLE_PROBE = {"command": "gh pr create --title t --body-file velvet-no-such-body.md"}

# A keyword against a number, or the issue's own URL. The keyword is what distinguishes a statement
# of origin from a number that happens to appear: the bare form was satisfied by a six-digit colour,
# and it counted a cross-reference in prose, which leaves the issue open exactly as before.
ISSUE_REFERENCE = re.compile(
    r"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?|refs?)\b[^\S\n]*:?[^\S\n]*#\d+"
    r"|github\.com/[\w.-]+/[\w.-]+/issues/\d+",
    re.IGNORECASE)
# A line rather than a flag, so the answer lands in the published body where a reader looking for
# where a change came from finds it. It has to carry a reason: the bare token would be a way of
# saying nothing in a form that satisfies the check, which is the silence this asks about.
NO_ISSUE_LINE = re.compile(r"^[^\S\n]*No issue[:.][^\S\n]*\S", re.MULTILINE | re.IGNORECASE)

NO_ANSWER = (
    "the body names no issue.\n\n"
    "CONTRIBUTING.md asks every pull request to say where it came from. If it closes an\n"
    "issue, the first line closes it on merge:\n\n"
    "  Closes #<n>.\n\n"
    "If it closes nothing — a tooling change, a release — say that instead, so a reader\n"
    "who wonders where it came from finds an answer rather than a silence:\n\n"
    "  No issue: <where this came from>")


def refuse(message):
    print(message, file=sys.stderr)
    return 2


def refuse_command(command, message):
    return refuse(f"Refusing `{command}`: {message}")


def answered(text):
    """Whether the body says where the change came from, either way round."""
    return bool(ISSUE_REFERENCE.search(text) or NO_ISSUE_LINE.search(text))


def judge_inline(text, command):
    """0, or 2 with the reason written to stderr."""
    if answered(text):
        return 0
    if unexpanded(text):
        return refuse_command(
            command,
            "the body is assembled by the shell, so the description this\n"
            "can read is not the one that will be posted.\n\n"
            "Write it to a file in a step of its own and pass `--body-file <path>`.")
    return refuse_command(command, NO_ANSWER)


# One remedy per obstruction, because a body this cannot read is refused rather than skipped and a
# reader given the wrong remedy tries the wrong thing. `pr_body.read_body_file` names them; what to
# say about each is here.
UNREADABLE_BODY = {
    UNEXPANDED_PATH: (
        "the body file's path is still unexpanded, so this cannot open\n"
        "the description that will be posted.\n\n"
        "Run it with the path spelled out."),
    STDIN: (
        "the body comes from stdin, which this cannot read.\n\n"
        "Write it to a file and pass that, so the description that will be posted is one this\n"
        "can read too."),
    RELATIVE_AFTER_MOVE: (
        "the command changes directory, so a relative body path\n"
        "names one file here and another one to `gh`.\n\n"
        "Give the body an absolute path."),
    MISSING: (
        "{path} does not exist.\n\n"
        "The body has to be there before this command runs, so a body written by this same\n"
        "command is too late — write it in a step of its own and create the pull request in\n"
        "the next. A path that is missing for any other reason is usually one whose write did\n"
        "not run: a refused hook stops the whole `&&` chain it was in, including the write."),
    UNREADABLE: "{path} cannot be read.",
}


def check(operands, cwd, after_a_move, command="gh pr create"):
    """0, or 2 with the reason written to stderr."""
    text, obstruction, path = effective_body(operands, cwd, after_a_move)
    if obstruction is not None:
        return refuse_command(command, UNREADABLE_BODY[obstruction].replace("{path}", path))
    if text is None:
        # No body operand at all: --fill and its relatives, --template, --recover, --editor, and the
        # interactive form. The description comes from commits, from a file every branch reuses, or
        # from a prompt after this has run, and none of those is text held here to search — so the
        # question goes unasked, which CONTRIBUTING states without this exception.
        return 0
    if path is None:
        return judge_inline(text, command)
    if not answered(text):
        return refuse_command(command, NO_ANSWER)
    # Both are judged when both are given: which one gh posts is not something this holds, so an
    # answer carried only by the one it does not post would be an answer nobody reads.
    inline = valued(operands, BODY_FLAGS)
    return 0 if inline is None else judge_inline(inline, command)


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    try:
        if not isinstance(event, dict) or event.get("tool_name") not in HOOK_TOOLS:
            return 0
        command = event.get("tool_input", {}).get("command") or ""
        cwd = event.get("cwd") or "."
        if not isinstance(command, str):
            return 0
        for words, operands, moved in invocations(
                command, ("pr", "create"), ("pr", "new"), ("pr", "edit")):
            verdict = check(operands, cwd, moved, "gh pr " + words[1])
            if verdict:
                return verdict
        return 0
    except Exception as err:
        # Exit 1 is not a refusal — PreToolUse runs the tool anyway — so an unforeseen shape here
        # would let through exactly what this exists to stop.
        print(f"Refusing a pull-request body update: this guard failed to reach a verdict ({err!r}).",
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())

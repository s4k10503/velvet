"""Locating the tree a guard reports on, reading git and gh without raising, and saying so when
neither way of asking could answer.

A guard that cannot answer says nothing rather than failing: a hook writing a traceback into a
session start is noise a reader cannot act on, and every caller here already treats an unavailable
answer as "no report".

For a PreToolUse guard the stake is higher than noise. A hook that raises exits 1, which is not a
refusal — the tool proceeds — so a guard reaching for a program that is not installed is a guard
that has been deleted, silently, on every machine without it. Both readers here answer None instead.

A Stop guard blocks instead, and then has a second thing to get right: what it says. "I could not
establish whether anything is unsettled" and "something is unsettled" are different claims, both
block, and a reader who cannot tell them apart records the failed reading as what the work is waiting
on. `unreadable_report` is the first of those, written as a statement about the guard, and it is
shared so that two guards cannot drift into two ways of saying it.
"""

import collections
import os
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

from deferrals import DEFERRALS  # noqa: E402


GitAnswer = collections.namedtuple("GitAnswer", "stdout stderr code")


def git_answer(args, cwd, timeout=15):
    """git's stdout, what it wrote about a failure, and its exit code. A git that never started
    reports exit 1.

    Separate from `git` below for the reason `gh_answer` is separate from `gh`: a caller that has to
    say why a reading failed needs what git said, and one that only wants the answer must not have
    to decide what an empty string meant.
    """
    try:
        result = subprocess.run(
            ["git", *args], cwd=cwd, capture_output=True, text=True, timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError) as error:
        return GitAnswer("", str(error), 1)
    return GitAnswer(result.stdout, result.stderr.strip(), result.returncode)


def git(args, cwd, timeout=15):
    """Run git and return its stdout, or None when it could not answer."""
    answer = git_answer(args, cwd, timeout)
    return answer.stdout if answer.code == 0 else None


Answer = collections.namedtuple("Answer", "stdout combined code")


def gh_answer(args, cwd=None, timeout=7):
    """gh's stdout, its whole output and its exit code. A gh that never started reports exit 1.

    Separate from `gh` below because a caller that has to say why a reading failed needs the output
    and the code, and one that only wants the answer must not have to decide what an empty string
    meant.
    """
    try:
        result = subprocess.run(
            ["gh", *args], cwd=cwd, capture_output=True, text=True, timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError) as error:
        return Answer("", str(error), 1)
    return Answer(result.stdout, (result.stdout + result.stderr).strip(), result.returncode)


def gh(args, cwd=None, timeout=7):
    """Run gh and return its stdout, or None when it could not answer.

    A bound per call rather than per invocation, since a guard makes several: merge_unproven_head
    makes three. It is a judgement about how long a tool call may pause before the pause is the
    problem, and it is not derived from the timeout the settings register for these hooks — nothing
    here reads that number.
    """
    answer = gh_answer(args, cwd, timeout)
    return answer.stdout if answer.code == 0 else None


# Two ways of asking the same question, drawn in order. `gh pr list` goes through GraphQL and
# `gh api` through REST; scripts/pr/settle.py owns why the difference matters.
#
# What this buys is the LISTING surviving one exhausted quota, and nothing else a caller reads. A
# guard that reads the listing here and its subject through GraphQL is one whose listing answers
# while everything it wanted to say is unread — so it has to block per subject rather than report on
# one, which stop/unsettled_pr.py does and unreadable_state_check.py's gh-graphql-error mode holds it
# to. Two ways is a smaller blast radius, not an answer to what a guard says when it cannot read.
OPEN_PULL_REQUEST_READS = (
    ["pr", "list", "--state", "open", "--limit", "100", "--json", "number", "--jq", ".[].number"],
    ["api", "repos/{owner}/{repo}/pulls?state=open&per_page=100", "--jq", ".[].number"],
)

# What was asked and what came back, for a caller that has to report a failure rather than a subject.
PullRequests = collections.namedtuple("PullRequests", "numbers attempts")

# Bounded so that two ways of asking still fit inside a pause worth waiting through, and held to
# that by scripts/hooks/test_hook_repository.py — which says there what the comparison covers.
OPEN_PULL_REQUEST_TIMEOUT = 8


def open_pull_requests(cwd=None, timeout=OPEN_PULL_REQUEST_TIMEOUT):
    """The open pull request numbers, or None with what each way of asking answered.

    An empty list is an answer and takes the ordinary path; None is the absence of one.
    """
    attempts = []
    for args in OPEN_PULL_REQUEST_READS:
        answer = gh_answer(args, cwd, timeout)
        if answer.code == 0:
            return PullRequests(answer.stdout.split(), attempts)
        attempts.append(("gh " + " ".join(args), f"exited {answer.code}\n{answer.combined}"))
    return PullRequests(None, attempts)


# The sentence that separates a guard's blindness from its verdict. Owned here so two guards cannot
# drift into two ways of saying it, and asserted by scripts/hooks/unreadable_state_check.py against
# any Stop guard that blocks on an unreadable state.
SELF_REPORT = "That is a fact about this guard, not about"


def unreadable_report(subject, attempts, key, another_way):
    """What a Stop guard prints when a reading it needed did not answer.

    `another_way` is the caller's, not this module's: the remedy for an unread pull request is not
    the remedy for an unread issue list, and one report written with the first in mind told a caller
    with the second to run `gh pr view`. So is the count — one failed call is not "every way of
    asking failed", and a report that says so about a single call is wrong about its own evidence.

    The deferral it invites asks about the work rather than about the reading: a deferral naming the
    reading expires, is rewritten identically, and leaves on the record a reason nothing was waiting
    on.

    Both outcomes here exit 2, and the distinction is therefore the text alone. Putting it in the
    exit as well would mean blocking one of the two through a form this repository has never
    measured, and a refusal that bets on an unmeasured form and loses is a refusal that fails open —
    which is the defect this whole family exists to remove. So the second way stays unused until
    something here can measure it.
    """
    asked = "\n\n".join(f"  {call}\n{detail}" for call, detail in attempts)
    header = "Every way of asking failed:" if len(attempts) > 1 else "What was asked, and what came back:"
    return f"""Do not stop: this guard could not read {subject}.

{SELF_REPORT} {subject}. What follows is what was asked and what came back, and nothing more — no
part of it is a finding about {subject}.

{header}

{asked}

Establish it another way and say what you found: {another_way}. A reading that failed is not a
subject that is clear.

If the pause is deliberate, arm the deferral for what the WORK is waiting on. The failure above is
not that, and naming it there is how a deferral comes to record something nothing was waiting on:

  echo "{key} <what the work is waiting on> $(date +%s) $CLAUDE_CODE_SESSION_ID" >> {DEFERRALS}"""


def project_tree():
    """The checkout to report on, or None when there is no git repository to read.

    CLAUDE_PROJECT_DIR names the session's own project. Falling back to the working directory's
    toplevel keeps a guard useful when it is run by hand.
    """
    declared = os.environ.get("CLAUDE_PROJECT_DIR", "")
    tree = Path(declared) if declared else None
    if tree is None:
        toplevel = git(["rev-parse", "--show-toplevel"], cwd=Path.cwd())
        if toplevel is None:
            return None
        tree = Path(toplevel.strip())
    if not tree.is_dir() or git(["rev-parse", "--git-dir"], cwd=tree) is None:
        return None
    return tree

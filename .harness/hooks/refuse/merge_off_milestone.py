#!/usr/bin/env python3
"""Refuse `gh pr merge` of a pull request whose milestone does not hold.

`scripts/pr/milestone.py` owns the decision and CONTRIBUTING.md's merging section the rule; this is
the copy that fires for a merge typed without `settle.py`. `lib/merge_target.py` owns which pull
request a command would land.
"""

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from merge_target import UNRESOLVED, merge_targets
from repository import gh, git_answer
from shell_commands import NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "pr"))
import milestone


HOOK_TOOLS = {"Bash"}

# A merge guard errs toward refusing, for the reason merge_unchecked_against_base.py gives over the
# same probe.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}

# Repository state any session can move — the scope rule shared_git_state.py states.
HOOK_SCOPE = "session"

# The decision is `milestone.py`'s, and its suite has no reason to spell this hook.
DECISION_TESTS = ("scripts/pr/test_milestone.py",)

# What one merge reads at worst, each at its bound: the pull request's number where the command names
# none, the pull request, `MILESTONE_PAGES` pages of milestones and the tag listing at
# `READ_TIMEOUT`, one fetch at `GIT_TIMEOUT`, and `LOCAL_READS` git reads of the fetched trees at
# `LOCAL_TIMEOUT`. test_merge_off_milestone.py holds the sum under the timeout `.harness/hooks.json`
# registers, and the git reads one merge takes to the count. A command is held to one merge, which
# is what keeps it inside that sum.
READ_TIMEOUT = 8
MILESTONE_PAGES = 2
GIT_TIMEOUT = 20
LOCAL_READS = 17
LOCAL_TIMEOUT = 5

UNREADABLE_REFUSAL = (
    "Refusing `gh pr merge`: the milestone {} merges under could not be judged.\n\n"
    "  {}\n\n"
    "An unread milestone and one that holds look the same from outside, and the merge is what "
    "cannot be taken back. Retry when the reading works, or see every merge precondition at once:\n"
    "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")


def api(cwd):
    """`milestone.py`'s REST caller over gh in `cwd`."""
    def ask(path, method="GET"):
        answer = gh(["api", f"repos/{{owner}}/{{repo}}/{path}",
                     *([] if method == "GET" else ["-X", method])], cwd=cwd, timeout=READ_TIMEOUT)
        try:
            return json.loads(answer) if answer else _unread(f"gh api {path} answered nothing")
        except ValueError:
            return _unread(f"gh api {path} answered something other than JSON")
    return ask


def _unread(message):
    raise milestone.Unread(message)


def number_of(cwd, pr):
    """The pull request's number: the operand, or gh's answer for the current branch."""
    if pr:
        return int(pr)
    answer = gh(["pr", "view", "--json", "number"], cwd=cwd, timeout=READ_TIMEOUT)
    try:
        return int(json.loads(answer)["number"])
    except (TypeError, ValueError, KeyError):
        return _unread("the pull request for the current branch could not be read")


def refuse_one(cwd, pr):
    """What to say about one merge in the command, or None when its milestone holds."""
    label = "PR #" + pr if pr else "the pull request for the current branch"
    ask = api(cwd)
    try:
        number = number_of(cwd, pr)
        asked = milestone.pull_request(ask, number)
        # A fork's head is not a branch of origin, which carries it as the pull request's ref.
        refs = list(dict.fromkeys([milestone.maintenance_line.MAIN, asked.base,
                                   f"pull/{number}/head" if asked.fork else asked.head]))
        fetched = git_answer(["fetch", "-q", "origin", *refs], cwd=cwd, timeout=GIT_TIMEOUT)
        if fetched.code != 0:
            return UNREADABLE_REFUSAL.format(label, f"origin/{' '.join(refs)} could not be fetched")
        state = milestone.repository(cwd, ask, READ_TIMEOUT, pages=MILESTONE_PAGES)
        change = milestone.change(cwd, f"origin/{asked.base}", asked.sha, state.published)
    except milestone.Unread as failure:
        return UNREADABLE_REFUSAL.format(label, failure)
    found = milestone.merge_reasons(number, asked, change, state)
    if not found:
        return None
    lines = "".join(f"  - {reason}\n" for reason in found)
    return f"Refusing `gh pr merge`: {label} does not merge under the milestone it carries.\n\n{lines}"


def main():
    try:
        payload = json.load(sys.stdin)
    except ValueError:
        return 0
    if payload.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = (payload.get("tool_input") or {}).get("command") or ""
    targets = merge_targets(command)
    if not targets:
        return 0
    if UNRESOLVED in targets:
        sys.stderr.write(
            "Refusing `gh pr merge`: the pull request is named by an operand the shell has not "
            "expanded yet, so the milestone it merges under cannot be read.\n\n"
            "Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2
    if len(targets) > 1:
        sys.stderr.write(
            f"Refusing `gh pr merge`: this command merges {len(targets)} pull requests, and this "
            "guard judges one per command inside the time it is given.\n\n"
            "Merge them one command at a time, or with `python3 scripts/pr/settle.py merge <pr>`.\n")
        return 2
    cwd = command_directory(command, payload.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `gh pr merge`: which checkout the milestone would be read from "
                         f"could not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2
    refusal = refuse_one(cwd, targets[0])
    if refusal is not None:
        sys.stderr.write(refusal)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())

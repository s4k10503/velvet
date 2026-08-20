"""Which pull request a `gh pr merge` would land, and the two branches it names.

The two guards that judge a merge against a base were written when `main` was the only branch that
took a pull request, and each named it. Read here instead, the answer is the pull request's own
field on every call — nothing is remembered, so retargeting one cannot leave a guard deciding from
what it used to say.

A numbered pull request is read over REST rather than through `gh pr view`; `scripts/pr/settle.py`
owns why the difference matters. The braces are gh's own placeholders, not a format string.
"""

import collections
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from shell_commands import program_invocations, unexpanded  # noqa: E402

# What an operand the shell has not expanded resolves to. It is not a pull request number and it is
# not the absence of one, so a guard has to decide about it separately.
UNRESOLVED = object()

GH_TIMEOUT = 20

MergeTarget = collections.namedtuple("MergeTarget", "head base")


def merge_targets(command):
    """The pull requests a merge in this command would land, "" meaning the current branch's.

    Read off tokens rather than by position. The pattern this replaces required the number to sit
    immediately after the subcommand, so putting a flag first matched nothing and the guard returned
    0 without spawning anything. It also carried no command-position
    anchor, so naming the command inside an argument spent a `gh pr view` and a `git fetch` on a
    refusal; that happened while this fix was being tested.
    """
    targets = []
    for operands in program_invocations(command, "gh", ("pr", "merge")):
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            targets.append(UNRESOLVED)
            continue
        targets.append(next((token for token in operands if token.isdigit()), ""))
    return targets


def _gh_json(cwd, args, timeout):
    """One gh call whose stdout is JSON, decoded, or None when it could not answer."""
    try:
        finished = subprocess.run(
            ["gh", *args], cwd=cwd,
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=timeout,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    if finished.returncode != 0:
        return None
    try:
        return json.loads(finished.stdout.decode())
    except (ValueError, UnicodeDecodeError):
        return None


def refs_of(cwd, pr, timeout=GH_TIMEOUT):
    """The head and base of the pull request a merge would land, or None when that was unreadable.

    With no number gh resolves the pull request from the checked-out branch, and gh is asked to
    resolve it here too rather than matched against a listing of open ones: a rule invented here can
    disagree with the merge that follows, and then the verdict is about a different pull request.

    `timeout` is the caller's budget rather than this module's: a `PreToolUse` guard is registered
    for 25 s and a `SessionStart` report for 15, and the report shares that 15 with a fetch.
    """
    if pr:
        payload = _gh_json(cwd, ["api", "repos/{owner}/{repo}/pulls/" + pr], timeout)
        if not isinstance(payload, dict):
            return None
        head = ((payload.get("head") or {}).get("ref") or "")
        base = ((payload.get("base") or {}).get("ref") or "")
    else:
        payload = _gh_json(cwd, ["pr", "view", "--json", "headRefName,baseRefName"], timeout)
        if not isinstance(payload, dict):
            return None
        head = payload.get("headRefName") or ""
        base = payload.get("baseRefName") or ""
    return MergeTarget(head, base) if head and base else None

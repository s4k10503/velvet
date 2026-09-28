#!/usr/bin/env python3
"""Refuse `gh pr merge` where the head's checks are not known to cover what its base now holds.

A branch merges without containing its base, and three things about the base still need the head's
own checks to have seen them:

- a failed push verdict on a required workflow, which `scripts/pr/red_base.py` owns, as it owns
  what exempts a head from one;
- the base's newest release commit, which `published_check.release_commit` finds, and
  CONTRIBUTING.md's continuous-integration section owns why it is asked;
- which branch the head is at all: a head on another repository names a branch `origin/<it>` does
  not hold, or holds as a different branch of the same name, so neither containment can be read;
  and a long-lived head, which `scripts/pr/long_lived.py` names, is not to be squashed or deleted.

`lib/merge_target.py` owns which pull request a command would land.
"""
import collections
import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from merge_target import GH_TIMEOUT, UNRESOLVED, merge_targets
from repository import gh, git_answer
from shell_commands import NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory

SCRIPTS = Path(__file__).resolve().parents[3] / "scripts"
sys.path.insert(0, str(SCRIPTS / "pr"))
sys.path.insert(0, str(SCRIPTS / "release"))
import long_lived
import published_check
import red_base


HOOK_TOOLS = {"Bash"}

# What an operand the shell has not expanded yet resolves to, which is nothing this can read. A merge
# guard errs toward refusing: allowing means the branch is merged with the check it exists for never
# having run, and the merge is what cannot be taken back.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}

GIT_TIMEOUT = 30

Target = collections.namedtuple("Target", "head base commit fork")

UNREADABLE_REFUSAL = (
    "Refusing `gh pr merge`: what its base holds against the head could not be read.\n\n"
    "  {}\n\n"
    "An unread answer and a base with nothing against the head leave this guard looking the same "
    "from outside, and the merge is what cannot be taken back. Retry when the reading works, or see "
    "every merge precondition at once:\n"
    "  python3 scripts/pr/settle.py merge <pr> --dry-run\n"
)


def gh_json(cwd, args, timeout=7):
    listed = gh(args, cwd=cwd, timeout=timeout)
    try:
        return json.loads(listed) if listed else None
    except ValueError:
        return None


def target_of(cwd, pr):
    """The head, base, head SHA and whether the head is on another repository, or None unread.

    Under merge_target.py's bound, which merge_onto_unpublished_release.py relies on this sharing.
    """
    if pr:
        payload = gh_json(cwd, ["api", "repos/{owner}/{repo}/pulls/" + pr], GH_TIMEOUT)
        if not isinstance(payload, dict):
            return None
        head, base = payload.get("head") or {}, payload.get("base") or {}
        home = (head.get("repo") or {}).get("full_name") or ""
        fork = home != ((base.get("repo") or {}).get("full_name") or "")
        target = Target(head.get("ref"), base.get("ref"), head.get("sha"), fork)
    else:
        payload = gh_json(cwd, ["pr", "view", "--json",
                                "headRefName,baseRefName,headRefOid,isCrossRepository"], GH_TIMEOUT)
        if not isinstance(payload, dict):
            return None
        target = Target(payload.get("headRefName"), payload.get("baseRefName"),
                        payload.get("headRefOid"), payload.get("isCrossRepository") is not False)
    return target if target.head and target.base and target.commit else None


def targeted_as_base(cwd, branch):
    """Whether any pull request, in any state, is based on `branch`, or None when unread."""
    listed = gh_json(cwd, ["api", "repos/{owner}/{repo}/" + long_lived.targeted_path(branch)])
    return bool(listed) if isinstance(listed, list) else None


def failing_runs(cwd, base):
    """The base's required workflows whose last push verdict failed, or a string saying what went
    unread."""
    found = []
    for workflow in red_base.REQUIRED_WORKFLOWS:
        payload = gh_json(cwd, ["api", "repos/{owner}/{repo}/" + red_base.runs_path(workflow, base)])
        if not isinstance(payload, dict):
            return f"the push runs of {workflow} on {base} came back unreadable"
        failing = red_base.failing_run(workflow, payload)
        if failing:
            found.append(failing)
    return found


def suites_ran(cwd, sha):
    """`red_base.unity_ran` over the head's check runs, or None when they could not all be read."""
    payload = gh_json(cwd, ["api", "repos/{owner}/{repo}/commits/" + sha + "/check-runs?per_page=100"])
    if not isinstance(payload, dict):
        return None
    runs = payload.get("check_runs") or []
    if payload.get("total_count", len(runs)) > len(runs):
        return None
    return red_base.unity_ran([(run.get("name") or "", run.get("conclusion") or run.get("status"))
                               for run in runs])


def holds(cwd, head, sha):
    """Whether origin/<head> holds `sha`, or None when git gave neither answer."""
    held = git_answer(["merge-base", "--is-ancestor", sha, "origin/" + head], cwd=cwd,
                      timeout=GIT_TIMEOUT)
    return {0: True, 1: False}.get(held.code)


def refuse_one(cwd, pr):
    """What to say about one merge in the command, or None when nothing about it blocks.

    Every merge the command carries is decided, not the first: a compound command lands each of
    them, and a guard reading one operand covers one of the merges it was posed.
    """
    # gh merges the current branch's pull request when no number is given, and naming it "#" reads
    # as a number nobody typed.
    label = "PR #" + pr if pr else "The pull request for the current branch"
    target = target_of(cwd, pr)
    if target is None:
        return UNREADABLE_REFUSAL.format(f"the head and base of {label} came back empty")
    if target.fork:
        return (f"{label} has its head on another repository. What that head holds is read off "
                f"origin/<branch> here, which is not that branch, so nothing here can say whether "
                f"its checks cover its base; `settle.py merge` refuses it for the same reason.\n")
    lasting = long_lived.by_name(target.head) or targeted_as_base(cwd, target.head)
    if lasting is None:
        return UNREADABLE_REFUSAL.format(
            f"whether any pull request is based on {target.head} could not be read")
    if lasting:
        return f"{label}: {long_lived.reason(target.head)}; `settle.py merge` refuses it too.\n"

    failing = failing_runs(cwd, target.base)
    if isinstance(failing, str):
        return UNREADABLE_REFUSAL.format(failing)
    # Ahead of the release reading, which reads the fetched ref: an unfetched one can predate the
    # release and name the one before it.
    fetched = git_answer(["fetch", "-q", "origin", target.base, target.head], cwd=cwd,
                         timeout=GIT_TIMEOUT)
    if fetched.code != 0:
        return UNREADABLE_REFUSAL.format(
            f"origin/{target.base} and origin/{target.head} could not be fetched")
    try:
        release = published_check.release_commit(cwd, f"origin/{target.base}", timeout=GIT_TIMEOUT)
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError):
        return UNREADABLE_REFUSAL.format(
            f"the newest release commit on {target.base} could not be read")

    said = []
    if release:
        held = holds(cwd, target.head, release[0])
        if held is None:
            return UNREADABLE_REFUSAL.format(
                f"whether origin/{target.head} holds {release[0][:7]} could not be read")
        if not held:
            said.append(f"{label} does not contain {release[0][:7]}, which dated {release[1]} on "
                        f"{target.base}. Its checks ran before that commit closed the section a "
                        f"CHANGELOG entry files under, so take {target.base} in and let them run "
                        f"again:\n  python3 scripts/pr/settle.py update <pr>\n")

    uncovered, ran = [], None
    for workflow, commit in failing:
        held = holds(cwd, target.head, commit)
        if held is None:
            return UNREADABLE_REFUSAL.format(
                f"whether origin/{target.head} holds {commit[:7] or 'the failing commit'} could "
                f"not be read")
        if held and ran is None:
            ran = suites_ran(cwd, target.commit)
            if ran is None:
                return UNREADABLE_REFUSAL.format(
                    f"the check runs of {target.commit[:7]} could not all be read")
        if not held or not ran:
            uncovered.append((workflow, commit))
    if uncovered:
        lines = "".join(f"  {workflow} failed at {commit[:7]}\n" for workflow, commit in uncovered)
        said.append(
            f"{label} would merge onto {target.base}, whose last push verdict is a failure:\n\n"
            f"{lines}\nFix or revert it on {target.base} first. A head is exempt only where it "
            f"contains the failing commit and its own Unity tests ran and passed, which is how the "
            f"fix lands.\n")
    return "\n".join(said) or None


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
            "expanded yet, so neither its base nor what that base holds can be read.\n\n"
            "Resolving the literal would fail and read as a pass, which is the check silently not "
            "happening. Name the pull request, or see every merge precondition at once:\n"
            "  python3 scripts/pr/settle.py merge <pr> --dry-run\n")
        return 2
    cwd = command_directory(command, payload.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `gh pr merge`: which checkout the base and head would be read "
                         f"from could not be read itself.\n\n{UNPLACEABLE_MOVE}\n\n"
                         f"{NAME_THE_TREE}\n")
        return 2
    for pr in targets:
        refusal = refuse_one(cwd, pr)
        if refusal is not None:
            sys.stderr.write(refusal)
            return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())

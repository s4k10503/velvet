#!/usr/bin/env python3
"""Refuse `gh pr merge` whose head has no passing check of its own.

Branch protection cannot cover this here. It requires the two aggregate contexts, but with
`strict_required_status_checks_policy` deliberately false — requiring every head to be up to date
would serialise every merge behind a 21-25 minute Unity matrix — so GitHub will merge a head whose
own run never happened, as long as some run for the pull request passed.

`gh pr checks` makes that easy to walk into. It answers about whatever the API last recorded, which
after a push is the previous commit's result, and a green read for a superseded SHA has already been
carried to the edge of a merge here. So the head is read, then the checks, then the head again: a
change between the two readings means the answers are about two commits and neither is trusted.

An empty check list is refused rather than forgiven. It means no workflow was ever triggered for
that head — what a cancelled run followed by a push leaves behind — and reading it as "still
running" is how a pull request sat unnoticed for 7h45m.

A list whose every check passed is refused too while the head still has a workflow run to finish,
carries a workflow whose newest run failed, or lacks a context its base requires; `campaign.py`'s
rule holds the mutation campaign. All of it is read off the head's workflow runs and the base's
rules inside the same pair of head readings, and `scripts/pr/expected_checks.py` owns what each of
the first three is and why.

The other merge preconditions have their own hooks: `merge_unchecked_against_base.py` for a base
whose required workflows last failed on push, a head behind the base's newest release commit, a
head on another repository, and a long-lived head; `merge_without_branch_deletion.py` for the flag;
`merge_branch_held_by_worktree.py` for a branch git will refuse to delete;
`merge_onto_unpublished_release.py` for a base holding a release nobody dispatched.
`scripts/pr/settle.py` reports these together with one more it holds alone — a draft head — and that
reporting is the convenience; these are what hold when nobody runs it.
"""

import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
from shell_commands import (NAME_THE_TREE, UNPLACEABLE_MOVE, UNRESOLVED_CD, command_directory,
                            program_invocations, unexpanded)
import repository

sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "pr"))
import campaign
import expected_checks


HOOK_TOOLS = {"Bash"}

TERMINAL_PASS = frozenset({"pass", "skipping"})

# An operand the shell has not expanded yet resolves to nothing readable, and a merge guard errs
# toward refusing: allowing means the branch lands with this check never having run.
UNEXPANDED_POLICY = "refuse"
UNEXPANDED_PROBE = 'gh pr merge $PR --squash --delete-branch'

# gh answers None both for a check list it could not read and for a number that is not a pull
# request, and nothing here separates them. The merge is still refused — same backing rule as
# merge_onto_unpublished_release.py states.
UNREADABLE_POLICY = "allow"
UNREADABLE_PROBE = {"command": "gh pr merge 1 --squash --delete-branch"}


def gh_json(cwd, args):
    out = repository.gh(args, cwd=cwd)
    if not out or not out.strip():
        return None
    try:
        return json.loads(out)
    except Exception:
        return None


def head_sha(cwd, number):
    return pull_request(cwd, number, fields="headRefOid")[0]


def pull_request(cwd, number, fields="headRefOid,labels,baseRefName"):
    """(head, label names, base), each None where unread and the last two None where the head is."""
    payload = gh_json(cwd, ["pr", "view", *( [number] if number else [] ), "--json", fields])
    head = payload.get("headRefOid") if isinstance(payload, dict) else None
    if not (isinstance(head, str) and head):
        return None, None, None
    base = payload.get("baseRefName")
    base = base if isinstance(base, str) and base else None
    labels = payload.get("labels")
    if not isinstance(labels, list) or not all(
            isinstance(label, dict) and isinstance(label.get("name"), str) for label in labels):
        return head, None, base
    return head, frozenset(label["name"] for label in labels), base


def checks_of(cwd, number):
    """Keep a valid check list even when gh exits nonzero for pending or failed checks."""
    answer = repository.gh_answer(["pr", "checks", *([number] if number else []),
                                   "--json", "name,bucket"], cwd=cwd)
    try:
        listed = json.loads(answer.stdout)
    except ValueError:
        return None
    if not isinstance(listed, list) or not all(
            isinstance(entry, dict) and isinstance(entry.get("name"), str)
            and isinstance(entry.get("bucket"), str) for entry in listed):
        return None
    return listed


def head_runs(cwd, sha):
    """Every workflow run whose head is `sha`, or None where they could not all be read."""
    return expected_checks.listed_runs(
        gh_json(cwd, ["api", expected_checks.runs_path("{owner}/{repo}", sha)]))


def run_jobs(cwd, runs, now):
    """The jobs of each run in `expected_checks.open_runs` by run id, None for one left unread."""
    return {run.get("id"): expected_checks.listed_jobs(
                gh_json(cwd, ["api", expected_checks.jobs_path("{owner}/{repo}", run)]))
            for run in expected_checks.open_runs(runs, now)}


def required_contexts(cwd, base):
    """The contexts the rulesets over `base` require, None where `base` or the rules went unread."""
    if base is None:
        return None
    return expected_checks.listed_required(
        gh_json(cwd, ["api", expected_checks.rules_path("{owner}/{repo}", base)]))


def merges(command):
    """The operands of each `gh pr merge` in the command."""
    return program_invocations(command, "gh", ("pr", "merge"))


def unproven(asked, cwd):
    """(pull request, reason) for each merge whose head is not covered by its own passing checks."""
    found = []
    for operands in asked:
        named = [token for token in operands if not token.startswith("-")]
        if any(unexpanded(token) for token in named):
            found.append(("the pull request named",
                          "an operand the shell has not expanded, so its checks cannot be read"))
            continue
        number = next((token for token in operands if token.isdigit()), None)
        label = "#" + number if number else "the current branch"

        before, labels, base = pull_request(cwd, number)
        if before is None:
            # gh is unreachable or this is not a pull request; the other guards still apply and this
            # one declines to invent an answer.
            continue

        listed = checks_of(cwd, number)
        runs = head_runs(cwd, before)
        now = time.time()
        others = campaign.others(runs or [])
        jobs = run_jobs(cwd, others, now)
        required = required_contexts(cwd, base)
        after = head_sha(cwd, number)

        # Past the first reading a reading that failed is refused, where the arm above lets one
        # through: the first answer is what says this is a pull request gh can reach, and once it
        # has, a reading that comes back empty is this head going unchecked rather than a guard with
        # nothing to say. So the None arm has to sit ahead of the comparison that formats `after`.
        if after is None:
            found.append((label, f"the head could not be read again after its checks, so nothing "
                                 f"ties them to {before[:7]}"))
        elif after != before:
            found.append((label, f"head moved from {before[:7]} to {after[:7]} while its checks were read"))
        elif listed is None:
            found.append((label, f"the check list for {before[:7]} could not be read"))
        elif runs is None:
            found.append((label, f"the workflow runs of {before[:7]} could not all be read"))
        elif required is None:
            found.append((label, f"which checks {base or 'its base'} requires could not be read"))
        elif labels is None:
            found.append((label, f"its labels could not be read, so whether it owes a "
                                 f"{campaign.WORKFLOW} run is not known"))
        else:
            waiting = expected_checks.unfinished(others, jobs, now)
            failing = expected_checks.failed(others)
            absent = expected_checks.absent(required, (entry["name"] for entry in listed))
            unfinished = sorted(entry["name"] for entry in listed
                                if entry["bucket"] not in TERMINAL_PASS)
            if not listed and not waiting:
                found.append((label, f"no check ran for {before[:7]}: a workflow was never "
                                     f"triggered for it"))
            elif absent:
                found.append((label, "required by {} and not reported at {}: {}".format(
                    base, before[:7], ", ".join(absent))))
            if unfinished:
                found.append((label, "not passing at {}: {}".format(before[:7], ", ".join(unfinished))))
            if waiting:
                found.append((label, "workflow runs not finished at {}: {}".format(
                    before[:7], ", ".join(waiting))))
            if failing:
                found.append((label, "workflow runs failed at {}: {}".format(
                    before[:7], ", ".join(failing))))
            if owed := campaign.reason(labels, campaign.state(runs), before):
                found.append((label, owed))
    return found


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0

    command = event.get("tool_input", {}).get("command", "")
    asked = merges(command)
    if not asked:
        return 0
    cwd = command_directory(command, event.get("cwd") or ".")
    if cwd is UNRESOLVED_CD:
        sys.stderr.write("Refusing `gh pr merge`: which checkout gh would resolve the pull request "
                         f"from could not be read.\n\n{UNPLACEABLE_MOVE}\n\n{NAME_THE_TREE}\n")
        return 2
    found = unproven(asked, cwd)
    if not found:
        return 0

    lines = "\n".join(f"  {label}: {reason}" for label, reason in found)
    sys.stderr.write(
        "Refusing `gh pr merge`: the head being merged is not covered by its own passing checks.\n\n"
        f"{lines}\n\n"
        "Branch protection does not catch this. It requires the aggregate contexts without requiring "
        "the head to be up to date, which is deliberate — the alternative serialises every merge "
        "behind the Unity matrix — so a head whose own run never happened can satisfy it.\n\n"
        "See every merge precondition at once:\n"
        "  python3 scripts/pr/settle.py merge <pr> --dry-run\n"
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())

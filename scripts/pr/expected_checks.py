"""What a head is still owed beyond the checks listed on it.

A check list holds the checks that exist. A head whose `Test` run was still pending carried only
`Source generators`' checks, every one passing, and `settle.py` called it mergeable; the ruleset then
refused the merge because `Required checks (Unity)` had never reported. Two readings close that, and
each covers a case the other cannot:

- a workflow run for the head that has not finished, which catches the jobs no ruleset requires, and
  a workflow run that failed, which catches one that failed before reporting a check;
- a context the base's rulesets require that no check on the head carries, which catches a workflow
  whose run does not exist yet, so that no listing of runs can show it unfinished.

The first reading waits only on runs that exist, so a workflow a path filter stops is not a wall.
The second asks only for what the ruleset names: `generators.yml`'s repository-settings job holds
that to the two required-checks aggregates, and
`WorkflowTriggerCoverageTests` holds their workflows to subscribing to `pull_request` unfiltered.
`settle.py` and `refuse/merge_unproven_head.py` decide from here, and
`diagnostics/unsettled_pr.py` names what it finds.
"""

import datetime
import re

RUN_PAGE = 100

# A bound of this repository's own: the runs it was set for sat `queued` for weeks after every job of
# their latest attempt had completed, so no limit of GitHub's is relied on to end one.
STALE_AFTER = 24 * 60 * 60

FAILED = frozenset({"failure", "startup_failure"})

# The job conclusions a run built from its jobs reads as a pass.
PASSING = frozenset({"success", "skipped", "neutral"})

# The rules listing carries no total, so a page this full is read as one that may have dropped a rule.
RULE_PAGE = 100


def runs_path(slug, sha):
    """The listing, below the API root, of every workflow run whose head is `sha`."""
    return f"repos/{slug}/actions/runs?head_sha={sha}&per_page={RUN_PAGE}"


def rules_path(slug, base):
    """The listing, below the API root, of every rule the rulesets apply to `base`."""
    return f"repos/{slug}/rules/branches/{base}?per_page={RULE_PAGE}"


def jobs_path(slug, run):
    """The listing, below the API root, of the jobs of `run`'s latest attempt."""
    return (f"repos/{slug}/actions/runs/{run.get('id')}/attempts/{run.get('run_attempt') or 1}"
            f"/jobs?per_page={RUN_PAGE}")


def untouched_for(run, now):
    """Seconds between `run`'s last update and `now`, or 0 where the update time cannot be read."""
    try:
        return now - datetime.datetime.fromisoformat(
            run["updated_at"].replace("Z", "+00:00")).timestamp()
    except (KeyError, AttributeError, TypeError, ValueError):
        return 0


def open_runs(runs, now):
    """The runs whose jobs `conclusion` reads: not `completed`, and touched inside `STALE_AFTER`.

    Every status but `completed` counts, so one GitHub adds later holds the merge until somebody
    classifies it — the rule settle.py's `_BUCKET` follows.
    """
    return [run for run in runs
            if run.get("status") != "completed" and untouched_for(run, now) < STALE_AFTER]


def conclusion(run, jobs, now):
    """What `run` concluded, or None while it has not finished.

    `jobs` maps a run's id to its latest attempt's jobs; one missing from it, or mapped to None,
    counts as a job still to complete, and so does an attempt that lists no job yet. A run GitHub
    still calls open concludes from those jobs once every one has completed: `success` where each
    passed, `cancelled` where one was cancelled and none failed, `failure` otherwise. One untouched
    for `STALE_AFTER` measured nothing, and concludes `cancelled`.
    """
    if run.get("status") == "completed":
        return run.get("conclusion")
    listed = jobs.get(run.get("id"))
    if listed and all(job.get("status") == "completed" for job in listed):
        found = {job.get("conclusion") for job in listed}
        if found <= PASSING:
            return "success"
        return "cancelled" if found <= PASSING | {"cancelled"} else "failure"
    return "cancelled" if untouched_for(run, now) >= STALE_AFTER else None


def stuck_exit(run):
    """How to end `run` where GitHub never will, which a reason naming an open run carries."""
    return f"run {run.get('id')}: `gh run cancel {run.get('id')}` if it is stuck"


def unfinished(runs, jobs, now):
    """A reason for each run that has not concluded, as `conclusion` reads it over `jobs`.

    The reason names the run and how to end it, since a run GitHub never finishes holds its head
    until somebody ends it or `STALE_AFTER` passes.
    """
    return sorted(f"{name_of(run)} ({run.get('status')}, {stuck_exit(run)})"
                  for run in runs if conclusion(run, jobs, now) is None)


def read_duplicates(entries, runs, jobs, now, run_id):
    """`entries`, each a check carrying "name" and "bucket" whose run `run_id` names, with the checks
    of a run GitHub concluded as cancelled read against its group: the other runs of its workflow,
    event and pull request (or branch, for a run naming none) that `conclusion` does not read as
    cancelled. Such a check is left out where a run of the group reports it with a bucket other than
    `skipping`, reads as pending where none has and one has not finished, and is kept as it is
    otherwise. The run reporting it need not have finished, since one that has not holds the head
    by itself, as `unfinished` names it.

    No run stands in for another that ran, since a later run of a workflow can measure less than an
    earlier one; and a cancelled check gives way only to the same check in its group, since a run of
    another event or pull request, or one that skipped the job, measured something else. GitHub's own
    conclusion is read for the cancelled run rather than `conclusion`'s, which can read a run left
    open past `STALE_AFTER` as cancelled after its jobs failed. `CancelledDuplicateTests` poses each.
    """
    cancelled = {run.get("id"): run for run in runs
                 if run.get("status") == "completed" and run.get("conclusion") == "cancelled"}
    read = []
    for entry in entries:
        run = cancelled.get(run_id(entry)) if run_id(entry) is not None else None
        others = [] if run is None else [
            other for other in runs if other is not run and group_of(other) == group_of(run)
            and conclusion(other, jobs, now) != "cancelled"]
        if not others:
            read.append(entry)
        elif not any(reports(entries, run_id, other, entry) for other in others):
            read.append(dict(entry, bucket="pending")
                        if any(conclusion(other, jobs, now) is None for other in others) else entry)
    return read


def group_of(run):
    numbers = tuple(sorted(pull.get("number") for pull in run.get("pull_requests") or []))
    return workflow_of(run), run.get("event"), numbers or run.get("head_branch")


def reports(entries, run_id, run, entry):
    """Whether `run` reports the check `entry` is with a bucket other than `skipping`."""
    return any(run_id(other) == run.get("id") and other.get("bucket") != "skipping"
               and same_check(other.get("name"), entry.get("name")) for other in entries)


EXPRESSION = re.compile(r"\$\{\{.*?\}\}")


def same_check(reported, name):
    """Whether `reported` names the check `name` does, an expression left in `name` by a matrix that
    was never expanded standing for whatever the run that expanded it put there."""
    pattern = ".+".join(re.escape(piece) for piece in EXPRESSION.split(name or ""))
    return re.fullmatch(pattern, reported or "") is not None


def failed(runs, jobs, now):
    """A reason for each run on the head that concluded as a failure, whatever else its workflow
    ran there, for the reason `read_duplicates` gives.

    What it concluded is `conclusion`'s reading, so a run GitHub left open after its jobs failed
    counts.
    """
    return sorted(f"{name_of(run)} ({conclusion(run, jobs, now)}, run {run.get('id')})"
                  for run in runs if conclusion(run, jobs, now) in FAILED)


def workflow_of(run):
    return run.get("workflow_id") or run.get("path") or name_of(run)


ACTIONS_RUN = re.compile(r"/actions/runs/(\d+)/")


def run_of(entry):
    """The id of the workflow run a `gh pr checks` entry's link names, or None for one naming none,
    since that list carries no check suite to tell a run's checks by."""
    found = ACTIONS_RUN.search(str(entry.get("link") or ""))
    return int(found.group(1)) if found else None


def name_of(run):
    return run.get("name") or run.get("path") or str(run.get("id"))


def required(rules):
    """The status-check contexts a rules listing requires, or None where the page may be partial."""
    if len(rules) >= RULE_PAGE:
        return None
    return sorted({check["context"]
                   for rule in rules if rule.get("type") == "required_status_checks"
                   for check in (rule.get("parameters") or {}).get("required_status_checks") or []
                   if check.get("context")})


def listed_runs(payload):
    """The runs a runs listing carries, or None where it is not one or does not carry them all."""
    runs = payload.get("workflow_runs") if isinstance(payload, dict) else None
    if not isinstance(runs, list) or payload.get("total_count", len(runs)) > len(runs) or not all(
            isinstance(run, dict) for run in runs):
        return None
    return runs


def listed_jobs(payload):
    """The jobs a jobs listing carries, or None where it is not one or does not carry them all."""
    jobs = payload.get("jobs") if isinstance(payload, dict) else None
    if not isinstance(jobs, list) or payload.get("total_count", len(jobs)) > len(jobs) or not all(
            isinstance(job, dict) for job in jobs):
        return None
    return jobs


def listed_required(payload):
    """`required` over a rules listing, or None where the payload is not one."""
    if not isinstance(payload, list) or not all(isinstance(rule, dict) for rule in payload):
        return None
    return required(payload)


def absent(contexts, names):
    """The contexts in `contexts` that no check name in `names` carries."""
    present = set(names)
    return [context for context in contexts if context not in present]

"""What a head is still owed beyond the checks listed on it.

A check list holds the checks that exist. A head whose `Test` run was still pending carried only
`Source generators`' checks, every one passing, and `settle.py` called it mergeable; the ruleset then
refused the merge because `Required checks (Unity)` had never reported. Two readings close that, and
each covers a case the other cannot:

- a workflow run for the head that has not finished, which catches the jobs no ruleset requires, and
  the newest run of a workflow that failed, which catches one that failed before reporting a check;
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

RUN_PAGE = 100

# A bound of this repository's own: the runs it was set for sat `queued` for weeks after every job of
# their latest attempt had completed, so no limit of GitHub's is relied on to end one.
STALE_AFTER = 24 * 60 * 60

FAILED = frozenset({"failure", "startup_failure"})

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
    """The runs whose jobs `unfinished` reads: not `completed`, and touched inside `STALE_AFTER`.

    Every status but `completed` counts, so one GitHub adds later holds the merge until somebody
    classifies it — the rule settle.py's `_BUCKET` follows.
    """
    return [run for run in runs
            if run.get("status") != "completed" and untouched_for(run, now) < STALE_AFTER]


def unfinished(runs, jobs, now):
    """A reason for each run in `open_runs` whose latest attempt still has a job to complete.

    `jobs` maps a run's id to those jobs; one missing from it, or mapped to None, counts as a job
    still to complete, and so does an attempt that lists no job yet. The reason names the run and how
    to end it, since a run GitHub never finishes holds its head until somebody ends it or
    `STALE_AFTER` passes.
    """
    return sorted(f"{name_of(run)} ({run.get('status')}, run {run.get('id')}: "
                  f"`gh run cancel {run.get('id')}` if it is stuck)"
                  for run in open_runs(runs, now)
                  if not (jobs.get(run.get("id"))
                          and all(job.get("status") == "completed" for job in jobs[run.get("id")])))


def failed(runs):
    """A reason for each workflow whose newest run on the head completed as a failure.

    Newest by run number and then attempt: an older run of the same workflow is superseded, and a
    failure there would otherwise hold the head however often it ran again.
    """
    newest = {}
    for run in runs:
        workflow = run.get("workflow_id") or run.get("path") or name_of(run)
        order = (run.get("run_number") or 0, run.get("run_attempt") or 1)
        if workflow not in newest or order > newest[workflow][0]:
            newest[workflow] = (order, run)
    return sorted(f"{name_of(run)} ({run.get('conclusion')}, run {run.get('id')})"
                  for _, run in newest.values()
                  if run.get("status") == "completed" and run.get("conclusion") in FAILED)


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

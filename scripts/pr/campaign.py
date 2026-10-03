"""Whether a pull request's head owes the mutation campaign, and what the campaign on it concluded.

The campaign runs once per labelled head rather than on every push: `automerge.py`'s hand-off
dispatches `WORKFLOW` onto the head's branch once `AFTER` has passed on that head while the label is
on. CONTRIBUTING.md's continuous-integration section owns the label's meaning. `settle.py`,
`automerge.py` and `refuse/merge_unproven_head.py` all decide from here.

A campaign is a run of `WORKFLOW` whose head is the pull request's head, read off the Actions runs
API by its workflow file: a check of any other workflow carrying a campaign job's name is not one.
"""

LABEL = "automerge"

DISPLAY_CONTEXT = "Mutation campaign"

WORKFLOW = "mutation.yml"

PATH = ".github/workflows/" + WORKFLOW

# The workflow a head has to pass before its campaign is dispatched, by the file it runs.
# CONTRIBUTING.md's continuous-integration section owns why.
AFTER = ".github/workflows/test.yml"

# The job of `AFTER` that concludes over every other, by the name it declares.
AGGREGATE = "Required checks (Unity)"

RUNNING, PASSED, CANCELLED, FAILED = "running", "passed", "cancelled", "failed"


def newest(runs):
    """The newest run of `WORKFLOW` among an Actions runs listing's entries, or None.

    A head can carry more than one campaign — a dispatch onto a head whose campaign was cancelled
    runs another — and only the newest is a verdict about it.
    """
    ours = [run for run in runs if run.get("path") == PATH]
    return max(ours, key=lambda run: run.get("run_number") or 0, default=None)


def state(runs, concluded):
    """None where no campaign ran on the head, and otherwise what the newest one concluded.

    `concluded` answers what a run concluded, or None while it has not finished:
    `expected_checks.conclusion` over the head's jobs, so a campaign GitHub leaves open after its jobs
    completed, or one untouched for its bound, is not running forever. `success` alone is a pass, and
    a conclusion the table here does not name is a failure until somebody classifies it.
    """
    run = newest(runs)
    if run is None:
        return None
    found = concluded(run)
    if found is None:
        return RUNNING
    return {"success": PASSED, "cancelled": CANCELLED}.get(found, FAILED)


def after_order(run):
    """Where a run of `AFTER` sits among the head's, newest last: `expected_checks.failed`'s order."""
    return run.get("run_number") or 0, run.get("run_attempt") or 1


def newest_after(runs):
    """The newest `pull_request` run of `AFTER` among the head's runs, or None.

    A run of another event is left out, since `base-red` runs on a `pull_request` run alone.
    """
    ours = [run for run in runs if run.get("path") == AFTER and run.get("event") == "pull_request"]
    return max(ours, key=after_order, default=None)


def after_passed(runs, concluded, jobs):
    """Whether `newest_after` concluded `success`.

    `concluded` answers as it does for `state`, over `jobs`, each run's latest attempt's jobs by run
    id. A run still open passes only once `AGGREGATE` is listed among those and passed, rather than
    once every job listed so far has.
    """
    run = newest_after(runs)
    if run is None or concluded(run) != "success":
        return False
    return run.get("status") == "completed" or any(
        job.get("name") == AGGREGATE and job.get("status") == "completed"
        and job.get("conclusion") == "success" for job in jobs.get(run.get("id")) or [])


def others(runs):
    """The runs of every workflow but `WORKFLOW`: `reason` alone decides what a campaign owes, so a
    reading of what the head's other runs owe leaves it out."""
    return [run for run in runs if run.get("path") != PATH]


def superseded_suites(runs):
    """The check suites of every campaign on the head but the newest, whose checks decide nothing."""
    kept = newest(runs)
    return {run.get("check_suite_id") for run in runs
            if run.get("path") == PATH and run is not kept}


def reason(labels, found, head, stuck):
    """Why the campaign on a head blocks its merge, or None. `found` is `state`'s answer, and
    `stuck` is how to end the newest campaign where GitHub never will, which a running one names.

    One that ran has to have passed, and a head carrying the label has to have one that passed. A
    cancelled one measured nothing, so it holds a labelled head alone.
    """
    short = head[:7]
    if found == PASSED or (found in (None, CANCELLED) and LABEL not in labels):
        return None
    if found is None:
        return (f"it carries the {LABEL} label and no {WORKFLOW} run has measured {short}: a "
                f"hand-off dispatches one once {AFTER} passes on it, for a branch of this "
                f"repository whose base holds automerge.yml, and adding the label again asks again; "
                f"for any other branch the label comes off before a merge by hand")
    if found == RUNNING:
        return f"its {WORKFLOW} run on {short} has not finished ({stuck})"
    if found == CANCELLED:
        return (f"its {WORKFLOW} run on {short} was cancelled: adding the label again dispatches "
                f"another")
    return f"its {WORKFLOW} run on {short} failed"


def dispatch_owed(found):
    """Whether a hand-off starts a campaign on a head, from `state`'s answer for it.

    A running one is left to finish, and a failed one is a verdict, which a push or a re-run of its
    failed jobs asks again.
    """
    return found in (None, CANCELLED)

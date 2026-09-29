"""Whether a pull request's head owes the mutation campaign, and what the campaign on it concluded.

The campaign runs once per labelled head rather than on every push: `automerge.py`'s hand-off
dispatches `WORKFLOW` onto the head's branch when the label is added and on each push while it is
on. CONTRIBUTING.md's continuous-integration section owns the label's meaning. `settle.py`,
`automerge.py` and `refuse/merge_unproven_head.py` all decide from here.

A campaign is a run of `WORKFLOW` whose head is the pull request's head, read off the Actions runs
API by its workflow file: a check of any other workflow carrying a campaign job's name is not one.
"""

LABEL = "automerge"

WORKFLOW = "mutation.yml"

PATH = ".github/workflows/" + WORKFLOW

RUNNING, PASSED, CANCELLED, FAILED = "running", "passed", "cancelled", "failed"


def runs_path(slug, sha):
    """The listing, below the API root, of every workflow run whose head is `sha`."""
    return f"repos/{slug}/actions/runs?head_sha={sha}&per_page=100"


def newest(runs):
    """The newest run of `WORKFLOW` among an Actions runs listing's entries, or None.

    A head can carry more than one campaign — a dispatch onto a head whose campaign was cancelled
    runs another — and only the newest is a verdict about it.
    """
    ours = [run for run in runs if run.get("path") == PATH]
    return max(ours, key=lambda run: run.get("run_number") or 0, default=None)


def state(runs):
    """None where no campaign ran on the head, and otherwise what the newest one concluded.

    `success` alone is a pass, and a conclusion the table here does not name is a failure until
    somebody classifies it.
    """
    run = newest(runs)
    if run is None:
        return None
    if run.get("status") != "completed":
        return RUNNING
    return {"success": PASSED, "cancelled": CANCELLED}.get(run.get("conclusion"), FAILED)


def superseded_suites(runs):
    """The check suites of every campaign on the head but the newest, whose checks decide nothing."""
    kept = newest(runs)
    return {run.get("check_suite_id") for run in runs
            if run.get("path") == PATH and run is not kept}


def reason(labels, found, head):
    """Why the campaign on a head blocks its merge, or None. `found` is `state`'s answer.

    One that ran has to have passed, and a head carrying the label has to have one that passed. A
    cancelled one measured nothing, so it holds a labelled head alone.
    """
    short = head[:7]
    if found == PASSED or (found in (None, CANCELLED) and LABEL not in labels):
        return None
    if found is None:
        return (f"it carries the {LABEL} label and no {WORKFLOW} run has measured {short}: a "
                f"hand-off dispatches one for a branch of this repository whose base holds "
                f"automerge.yml, and for any other the label comes off before a merge by hand")
    if found == RUNNING:
        return f"its {WORKFLOW} run on {short} has not finished"
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

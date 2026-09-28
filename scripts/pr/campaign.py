"""Whether a pull request's head owes the mutation campaign, and whether it has one.

The campaign runs once per pull request rather than on every push: `automerge.py`'s hand-off
dispatches `WORKFLOW` onto the head's branch when the label is added and on each push while it is
on. CONTRIBUTING.md's continuous-integration section owns the label's meaning. `settle.py`,
`automerge.py` and `refuse/merge_unproven_head.py` all decide from here.
"""

LABEL = "automerge"

WORKFLOW = "mutation.yml"

# The conclusions settle.py's `_BUCKET` lets a merge through on, for a reader of the checks API that
# does not go through settle. test_settle.py's `CheckResultTests` fails when the two part.
PASSED = frozenset({"success", "neutral", "skipped"})

# The display name of WORKFLOW's aggregate job. test_automerge.py's `CampaignWorkflowTests` fails
# when that job stops carrying it.
CHECK = "Mutation campaign"


def newest(check_runs):
    """Every check run of the checks API with each campaign aggregate but the newest left out.

    A head can carry more than one campaign — a dispatch onto a head that already had one supersedes
    it — and the superseded one is not a verdict about that head.
    """
    ours = [run for run in check_runs if run.get("name") == CHECK]
    kept = max(ours, key=lambda run: run.get("id") or 0, default=None)
    return [run for run in check_runs if run.get("name") != CHECK or run is kept]


def missing(labels, names, head):
    """Why a head owes a campaign it has not got, or None. `names` are the head's check names."""
    if LABEL not in labels or CHECK in names:
        return None
    return (f"it carries the {LABEL} label and no {CHECK} check has run on {head[:7]}: one is "
            f"dispatched when the label is added and on each push while it is on")


def dispatch_owed(results):
    """Whether a hand-off starts a campaign on a head, from settle's buckets for its checks.

    A cancelled one is asked again, since cancelling measured nothing; a failed one is a verdict,
    which a push or a re-run of its failed jobs asks again.
    """
    return not any(entry["name"] == CHECK and entry["bucket"] != "cancel" for entry in results)

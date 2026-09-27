"""Which of a base branch's required workflows last failed on push, and what exempts a head from it.

A pull request merges without containing its base, so nothing re-runs its checks when the base
moves. What keeps a broken base from collecting merges on top of the break is asked here instead,
and `scripts/pr/settle.py` and `refuse/merge_unchecked_against_base.py` both decide from it.
"""

import collections

# The workflows carrying the two aggregate checks branch protection requires. test_settle.py's
# `RequiredWorkflowTests` fails when the workflows declaring one stop being these.
REQUIRED_WORKFLOWS = ("test.yml", "generators.yml")

RUN_PAGE = 20

PASSED = frozenset({"success"})

# Conclusions that say nothing about the tree, such as a run a newer push cancelled.
# A conclusion in neither set counts as a failure, so one GitHub adds later refuses until somebody
# classifies it — the rule settle.py's `_BUCKET` follows.
NO_VERDICT = frozenset({"cancelled", "skipped", "neutral", "stale"})

# How test.yml names its suite jobs. test_settle.py's `RequiredWorkflowTests` fails when it stops.
UNITY_TESTS = "Unity tests"

Failing = collections.namedtuple("Failing", "workflow sha")


def runs_path(workflow, base):
    """The listing, below `repos/<owner>/<name>/`, of one workflow's push runs on one base."""
    return f"actions/workflows/{workflow}/runs?branch={base}&event=push&per_page={RUN_PAGE}"


def failing_run(workflow, payload):
    """The newest run on the page that reached a verdict, when that verdict is not a pass.

    A first attempt still going is passed over rather than read as the answer, so a pending base
    refuses nothing new and the newest verdict on the page still stands. A re-run still going is read
    as the failure it re-runs instead: passing over it would hand the verdict to the run before the
    one being re-run. A re-run of a pass then reads red until it finishes, which errs toward refusing.
    A page holding no verdict at all answers None: nothing on it says the base is red.
    """
    runs = sorted(payload.get("workflow_runs") or [],
                  key=lambda run: run.get("run_number") or 0, reverse=True)
    for run in runs:
        if run.get("status") != "completed":
            if (run.get("run_attempt") or 1) > 1:
                return Failing(workflow, run.get("head_sha") or "")
            continue
        if run.get("conclusion") in NO_VERDICT:
            continue
        if run.get("conclusion") in PASSED:
            return None
        return Failing(workflow, run.get("head_sha") or "")
    return None


def unity_ran(checks):
    """Whether the head's Unity suite jobs concluded success, from (name, conclusion) pairs.

    A head containing a failing base commit is exempt from it only where its own suites ran over that
    commit. `Required checks (Unity)` passes a suite job that was skipped for want of a licence
    secret, so a skipped one, or none at all, is not a run.
    """
    unity = [conclusion for name, conclusion in checks if name.startswith(UNITY_TESTS)]
    return bool(unity) and all(conclusion == "success" for conclusion in unity)

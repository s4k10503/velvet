"""Which workflow runs are the mutation campaign's.

No pull request waits on the campaign, and CONTRIBUTING.md ▸ Checking that the tests can fail owns
why: the record on the default branch is what the release gate reads. No pull request starts the
campaign, but a dispatch onto a pull request's branch runs on its head, and a run of `WORKFLOW` there
is no evidence about the merge: `settle.py` and `refuse/merge_unproven_head.py` leave its runs, its checks and its old commit
status out of what they read (`others`, `suites`, `NAME`, `STATUS_CONTEXT`).
"""

WORKFLOW = "mutation.yml"

PATH = ".github/workflows/" + WORKFLOW

# The `name:` the workflow declares, which a check listing carries as the workflow a check belongs to.
NAME = "Mutation campaign"

# The commit-status context a publisher wrote on pull request heads while a campaign gated their
# merge. The publisher is gone and writes no new one, but a head can still carry one, pending
# included, so it is left out as the campaign's checks are.
STATUS_CONTEXT = "Mutation campaign"


def others(runs):
    """The runs of every workflow but `WORKFLOW`."""
    return [run for run in runs if run.get("path") != PATH]


def suites(runs):
    """The check suites of every campaign among `runs`, whose checks decide no merge."""
    return {run.get("check_suite_id") for run in runs if run.get("path") == PATH}

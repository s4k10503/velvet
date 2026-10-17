"""Which workflow runs are the mutation campaign's, and the records a campaign leaves for a later one.

The campaign runs nightly on the default branch and on a branch dispatched by hand; no pull request
waits on it, and CONTRIBUTING.md ▸ Checking that the tests can fail owns why. A run of `WORKFLOW` on a
pull request's head is therefore no evidence about its merge: `settle.py` and
`refuse/merge_unproven_head.py` leave its runs, its checks and its old commit status out of what they
read (`others`, `suites`, `NAME`, `STATUS_CONTEXT`).

A campaign that passes leaves its key in the name of an artifact (`key_artifact_name`), and a later
run on the same branch whose key is the same reuses that verdict instead of measuring (`reusable`).
Every campaign also leaves a ledger of its per-mutant verdicts (`LEDGER_ARTIFACT`), and
`mutation_check.py` reuses a kill whose file and killing fixtures are unchanged. That reuse is decided
inside the run, which measures every other mutant, so nothing here reads it.
"""

import re

WORKFLOW = "mutation.yml"

PATH = ".github/workflows/" + WORKFLOW

# The `name:` the workflow declares, which a check listing carries as the workflow a check belongs to.
NAME = "Mutation campaign"

# The commit-status context a publisher wrote on pull request heads while a campaign gated their
# merge. The publisher is gone and writes no new one, but a head can still carry one, pending
# included, so it is left out as the campaign's checks are.
STATUS_CONTEXT = "Mutation campaign"

# The artifact a passing campaign uploads: its key, and for a run that reused a verdict the run that
# measured it. A name rather than a file, so a REST listing of the run's artifacts carries it.
KEY_ARTIFACT = re.compile(r"Mutation campaign key ([0-9a-f]{64})(?: reused from run ([1-9][0-9]*))?")


# The artifact a campaign's verdict job uploads whatever the verdict was: what it measured per mutant,
# for a later campaign on the branch to reuse a kill from (`mutation_check.py --reuse-from`). A name
# rather than a file, so a REST listing of the run's artifacts carries it.
LEDGER_ARTIFACT = "Mutation campaign ledger"


def others(runs):
    """The runs of every workflow but `WORKFLOW`."""
    return [run for run in runs if run.get("path") != PATH]


def suites(runs):
    """The check suites of every campaign among `runs`, whose checks decide no merge."""
    return {run.get("check_suite_id") for run in runs if run.get("path") == PATH}


def key_artifact_name(key, reused_from=None):
    """The artifact name that records `key`, and the run it was measured by where a run reused it."""
    return f"Mutation campaign key {key}" + (f" reused from run {reused_from}" if reused_from else "")


def keyed(payload):
    """(key, the run that measured it or None) from a run's artifacts listing, or None.

    None where the listing is not a whole one, holds no unexpired key artifact, or holds artifacts of
    more than one key: a run answers for one key. Both names of one key read as measured.
    """
    listed = payload.get("artifacts") if isinstance(payload, dict) else None
    if not isinstance(listed, list) or payload.get("total_count", len(listed)) > len(listed):
        return None
    found = [KEY_ARTIFACT.fullmatch(item.get("name") or "") for item in listed
             if isinstance(item, dict) and not item.get("expired")]
    found = [match for match in found if match]
    keys = {match.group(1) for match in found}
    if len(keys) != 1:
        return None
    # A run that measured a key it had reused before, on a re-run, carries both names; the measuring
    # one is the later and stronger claim.
    chosen = next((match for match in found if not match.group(2)), None)
    if chosen is None:
        if len({match.group(2) for match in found}) != 1:
            return None
        chosen = found[0]
    reused = chosen.group(2)
    return chosen.group(1), int(reused) if reused else None


def ledger_artifact(payload):
    """The id of the one live ledger in a run's artifacts listing, or None.

    None where the listing is not a whole one, holds no unexpired ledger, or holds two: a run that
    cannot say which of two it wrote is read as having written neither, and its mutants are measured.
    """
    listed = payload.get("artifacts") if isinstance(payload, dict) else None
    if not isinstance(listed, list) or payload.get("total_count", len(listed)) > len(listed):
        return None
    live = [item for item in listed if isinstance(item, dict) and item.get("name") == LEDGER_ARTIFACT
            and not item.get("expired")]
    return live[0].get("id") if len(live) == 1 else None


def passed_run(run):
    return (isinstance(run, dict) and run.get("path") == PATH and run.get("status") == "completed"
            and run.get("conclusion") == "success")


def reusable(key, candidates):
    """The run that measured `key`, from `candidates` -- (run, artifacts listing) pairs, newest first.

    Only a run whose own key artifact says it measured the key is a source. One that reused the key
    is skipped, so a chain of reuses never forms and a verdict outlives only the artifacts of the
    run that measured it: once those lapse, nothing is found and the head is measured.
    """
    for run, payload in candidates:
        found = keyed(payload)
        if passed_run(run) and found is not None and found == (key, None):
            return run.get("id")
    return None


def artifacts_path(slug, run_id):
    """The listing, below the API root, of the artifacts of a run."""
    return f"repos/{slug}/actions/runs/{run_id}/artifacts?per_page=100"

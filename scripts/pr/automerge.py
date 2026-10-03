#!/usr/bin/env python3
"""Merge a pull request labelled `automerge` through `settle.py merge`, from a workflow.

`.github/workflows/automerge.yml` runs this from a checkout of the default branch, never of the
pull request. Settle decides the merge; what this adds is which pull request to ask it about, and,
handed a pull_request_target event or with `--hand-off` a passing run of `campaign.AFTER`, what the
labelled head is waiting on: `campaign.py`'s workflow where the head has passed `campaign.AFTER` and
has no campaign, and where its campaign ended, the merge run, which the hand-off dispatches for a
pull_request_target event alone. A refusal is an ordinary outcome
here — CONTRIBUTING.md's "Merging a pull request" section says what asks again — so it exits 0,
where a reading or a dispatch that failed, or a hand-off run that never finished, exits 1. A
pull_request_target hand-off whose reading or dispatch gh refuses exits 0 with a warning instead: its
job is a check on the head, and a failed one would have settle and the hook refuse that head, a merge
by hand without the label included.

Run: python3 scripts/pr/automerge.py [--hand-off]         (the event GitHub hands a job)
     python3 scripts/pr/automerge.py --number <n> [--after-run <run id>]
"""

import argparse
import importlib.util
import json
import os
import sys
import time
from pathlib import Path

# CONTRIBUTING.md's continuous-integration section owns why this is not the workflow's own token.
TOKEN = "GH_TOKEN"

# The workflow's own token, which the merge job and the sweep dispatch a campaign with, since the
# one above may read Actions and not write them.
DISPATCH_TOKEN = "DISPATCH_TOKEN"

AFTER_RUN_TIMEOUT = 600
AFTER_RUN_POLL = 10


def load_settle():
    """Imports settle by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("settle", Path(__file__).with_name("settle.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


settle = load_settle()

LABEL = settle.campaign.LABEL

MERGE_WORKFLOW = "automerge.yml"


# The workflows automerge.yml subscribes to, by the `name:` each declares. test_automerge.py fails
# when the subscription and these stop being the same names.
CHECK_WORKFLOWS = frozenset({"Test", "Source generators"})
PUBLISH_WORKFLOW = "UPM"

# What a completed run asks about: the pull requests it tested, or the labelled ones.
TESTED_PULLS = "tested"
SWEEP = "sweep"


def run_selection(run, default_branch):
    """(what a completed run asks about, None), or (None, why it asks about nothing).

    The sweeps are for the refusals about the base: a push run passing on the default branch is what
    turns a red base green, and a release dispatch of UPM is what publishes a release the base had
    closed.
    """
    name, event, conclusion = run.get("name"), run.get("event"), run.get("conclusion")
    if conclusion != "success":
        return None, f"the run concluded {conclusion or 'without a conclusion'}"
    if name in CHECK_WORKFLOWS and event == "pull_request":
        return TESTED_PULLS, None
    if name in CHECK_WORKFLOWS and event == "push" and run.get("head_branch") == default_branch:
        return SWEEP, None
    if name == PUBLISH_WORKFLOW and event == "workflow_dispatch":
        return SWEEP, None
    return None, (f"a {event or 'no named event'} run of {name or 'an unnamed workflow'} on "
                  f"{run.get('head_branch') or 'no branch'} clears no refusal")


def run_pull_requests(run, open_pulls):
    """The numbers of the pull requests a completed run tested."""
    named = {entry.get("number") for entry in run.get("pull_requests") or []}
    named.discard(None)
    if named:
        return sorted(named)
    tested = run.get("head_sha")
    return sorted(pull["number"] for pull in open_pulls
                  if tested and (pull.get("head") or {}).get("sha") == tested)


def pull_skip_reason(pull, tested=None):
    """Why this pull request is left alone, or None. `tested` is the head a completed run tested."""
    if pull.get("state") != "open":
        return "it is not open"
    if LABEL not in {label.get("name") for label in pull.get("labels") or []}:
        return f"it does not carry the {LABEL} label"
    current = (pull.get("head") or {}).get("sha") or ""
    if tested and current != tested:
        return (f"its head is {current[:7]}, not the {tested[:7]} this run tested: the newer head's "
                f"own runs ask again when they complete")
    return None


def wait_for_run(project, run_id, read=None, clock=time.monotonic, sleep=time.sleep,
                 timeout=AFTER_RUN_TIMEOUT):
    """Whether the named workflow run completed within `timeout` seconds.

    The hand-off in automerge.yml passes its own run, whose jobs are checks on the pull request's
    head: settle refuses a head with a check still running.
    """
    if read is None:
        slug = settle.repository(project)
        read = lambda: settle.rest(f"repos/{slug}/actions/runs/{run_id}", ".status")  # noqa: E731
    deadline = clock() + timeout
    while True:
        if read() == "completed":
            return True
        if clock() >= deadline:
            return False
        sleep(AFTER_RUN_POLL)


def settle_one(project, number, tested=None, dispatch_token=None):
    """Merges one pull request if nothing blocks it, and says what happened.

    Handed `dispatch_token`, a labelled head settle would refuse for want of a campaign, and whose
    `campaign.AFTER` has passed, gets the campaign instead: the hand-offs dispatch one only when
    their own events reach them, and this asks whatever event started the run.
    """
    pull = settle.rest_json(f"repos/{settle.repository(project)}/pulls/{number}")
    reason = pull_skip_reason(pull, tested)
    if reason:
        print(f"PR#{number} left alone: {reason}")
    elif dispatch_token and recover_campaign(project, number, pull, dispatch_token):
        return
    elif settle.merge(project, number, None, dry_run=False):
        print(f"PR#{number} not merged: settle refused it, above. CONTRIBUTING.md's \"Merging a pull "
              f"request\" section says what asks again.")


def recover_campaign(project, number, pull, token):
    if dispatch_skip_reason(project, pull, ask_base=True):
        return False
    found, ready = campaign_reading(project, pull["head"]["sha"])
    if not (settle.campaign.dispatch_owed(found) and ready):
        return False
    dispatch_campaign(project, number, pull, token)
    return True


def dispatch(project, workflow, ref, token=None, **inputs):
    """Starts `workflow` on `ref` with `inputs`, raising settle's RuntimeError when gh refuses.

    `token`, where given, stands in for $GH_TOKEN for this call alone.
    """
    fields = [argument for name, value in inputs.items() for argument in ("-f", f"{name}={value}")]
    held = os.environ.get(TOKEN)
    if token:
        os.environ[TOKEN] = token
    try:
        settle.gh("workflow", "run", workflow, "--repo", settle.repository(project), "--ref", ref,
                  *fields)
    finally:
        if token:
            if held is None:
                del os.environ[TOKEN]
            else:
                os.environ[TOKEN] = held


def dispatch_campaign(project, number, pull, token=None):
    head, base = pull["head"], pull["base"]
    try:
        dispatch(project, settle.campaign.WORKFLOW, head["ref"], token, base=base["ref"],
                 number=number)
    except RuntimeError as error:
        raise RuntimeError(f"{error}. If {head['ref']} predates {settle.campaign.WORKFLOW}, "
                           f"take the base in with `settle.py update`")
    print(f"PR#{number}: a campaign is dispatched onto {head['ref']}, whose run asks for the "
          f"merge once it passes")


def dispatch_skip_reason(project, pull, ask_base):
    """Why no campaign is dispatched onto `pull`'s head whatever its runs hold, or None.

    A head on another repository gets none: a dispatch names a branch of this repository, which that
    head's branch name does not address, and settle refuses that head anyway. `ask_base` is False
    only for a pull_request_target event, which ran the base's own automerge.yml; anything else runs
    the default branch's, so it asks whether the base holds one.
    """
    head, base = pull.get("head") or {}, pull.get("base") or {}
    if (head.get("repo") or {}).get("full_name") != (base.get("repo") or {}).get("full_name"):
        return "its head is on another repository, which no campaign is dispatched onto"
    if ask_base and not base_holds(project, base.get("ref"), MERGE_WORKFLOW):
        return (f"its base {base.get('ref')} holds no {MERGE_WORKFLOW}, so no hand-off reaches it "
                f"and the label comes off before a merge by hand")
    return None


def campaign_reading(project, sha, after_run=None):
    """(`campaign.state` over the head's runs, whether `campaign.AFTER` has passed there).

    `after_run` is a passing run of `campaign.AFTER` an event handed over, which passed unless the
    head's runs hold a newer one; without it, `campaign.after_passed` reads the head's runs.
    """
    runs = settle.campaign_runs(project, sha)
    now = time.time()
    jobs = settle.run_jobs(project, runs, now)
    found = settle.campaign_state(runs, jobs, now)
    if after_run is not None:
        newest = settle.campaign.newest_after(runs)
        return found, newest is None or (settle.campaign.after_order(newest)
                                         <= settle.campaign.after_order(after_run))
    return found, settle.campaign.after_passed(
        runs, lambda run: settle.expected_checks.conclusion(run, jobs, now), jobs)


def hand_off(project, number, run_id, after_run=None):
    """Dispatches what a labelled pull request's head waits on, and says which.

    `run_id` is this run's own, which the merge run waits out for the reason `wait_for_run` gives.
    `after_run` is the passing run of `campaign.AFTER` a completed-run event handed over, and None
    for a pull_request_target event. Handed one, this dispatches the campaign or nothing, since the
    merge job of the same run asks settle.
    """
    pull = settle.rest_json(f"repos/{settle.repository(project)}/pulls/{number}")
    reason = (pull_skip_reason(pull, (after_run or {}).get("head_sha"))
              or dispatch_skip_reason(project, pull, ask_base=after_run is not None))
    if reason:
        print(f"PR#{number} left alone: {reason}")
        return
    head, base = pull["head"], pull["base"]
    found, ready = campaign_reading(project, head["sha"], after_run)
    if found == settle.campaign.RUNNING:
        print(f"PR#{number}: the campaign on {head['sha'][:7]} is still running, and asks for the "
              f"merge once it passes")
    elif not settle.campaign.dispatch_owed(found):
        if after_run:
            print(f"PR#{number}: the campaign on {head['sha'][:7]} {found}, which this run's merge "
                  f"job asks settle about")
            return
        dispatch(project, MERGE_WORKFLOW, base["repo"]["default_branch"], number=number,
                 after_run=run_id)
        print(f"PR#{number}: the campaign on {head['sha'][:7]} {found}, so the merge run is "
              f"dispatched")
    elif ready:
        dispatch_campaign(project, number, pull)
    elif after_run:
        print(f"PR#{number}: a newer run of {settle.campaign.AFTER} on {head['sha'][:7]} than this "
              f"one decides, and its passing run dispatches the campaign")
    else:
        cancelled = cancel_superseded(project, head["ref"], head["sha"])
        print(f"PR#{number}: {settle.campaign.AFTER} has not passed on {head['sha'][:7]}, and its "
              f"passing run dispatches the campaign"
              + (f"; cancelled the campaign on an older head, run {', '.join(map(str, cancelled))}"
                 if cancelled else ""))


def base_holds(project, base, workflow):
    listed = settle.rest_json(f"repos/{settle.repository(project)}/contents/.github/workflows"
                              f"?ref={base}")
    return any(entry.get("name") == workflow for entry in listed)


def cancel_superseded(project, branch, head):
    """The ids of the campaigns open on `branch` over a head other than `head`, each cancelled.

    The campaign's concurrency group cancels the one before it only when another is dispatched,
    which for a pushed head waits on that head's `campaign.AFTER` run.
    """
    slug = settle.repository(project)
    payload = settle.rest_json(f"repos/{slug}/actions/workflows/{settle.campaign.WORKFLOW}/runs"
                               f"?branch={branch}&per_page=100")
    superseded = [run.get("id") for run in settle.expected_checks.open_runs(
        payload.get("workflow_runs") or [], time.time()) if run.get("head_sha") != head]
    for run_id in superseded:
        settle.gh("run", "cancel", str(run_id), "--repo", slug)
    return superseded


def passing_after_run(run):
    """Whether a completed run is a passing `pull_request` run of `campaign.AFTER` on a named head,
    which the hand-off job hands off and the merge job of the same run leaves to it."""
    return (run.get("conclusion") == "success" and run.get("event") == "pull_request"
            and run.get("path") == settle.campaign.AFTER and bool(run.get("head_sha")))


def hand_off_tested(project, event):
    """`hand_off` for each labelled pull request a passing run of `campaign.AFTER` tested.

    A failure exits 1, as the merge job's does: this run's jobs are checks on the default branch's
    commit rather than on the head.
    """
    run = event.get("workflow_run") or {}
    if not passing_after_run(run):
        print(f"Nothing to hand off: this is not a passing pull_request run of "
              f"{settle.campaign.AFTER}.")
        return 0
    failed = False
    open_pulls = [] if run.get("pull_requests") else list_open_pulls(project)
    for number in run_pull_requests(run, open_pulls):
        try:
            hand_off(project, number, None, run)
        except RuntimeError as error:
            print(f"::error::PR#{number}: {error}")
            failed = True
    return 1 if failed else 0


def list_open_pulls(project):
    return settle.rest_json(f"repos/{settle.repository(project)}/pulls?state=open&per_page=100")


def numbers_from_event(project, event):
    """(numbers, the head the run tested), the numbers empty after saying why nothing merges.

    A sweep tested no pull request's head, so it names none to compare against.
    """
    run = event.get("workflow_run") or {}
    selection, reason = run_selection(run, (event.get("repository") or {}).get("default_branch"))
    if reason:
        print(f"Nothing to merge: {reason}.")
        return [], None
    if selection == SWEEP:
        numbers = sorted(pull["number"] for pull in list_open_pulls(project)
                         if not pull_skip_reason(pull))
        if not numbers:
            print(f"Nothing to merge: no open pull request carries the {LABEL} label.")
        return numbers, None
    numbers = run_pull_requests(run, [] if run.get("pull_requests") else list_open_pulls(project))
    if not numbers:
        print(f"Nothing to merge: no open pull request has {(run.get('head_sha') or '')[:7]} as its head.")
    return numbers, run.get("head_sha")


def optional_int(text):
    """An argument a workflow passes whether or not its input was given, so empty means absent."""
    return int(text) if text.strip() else None


def main(argv=None, environ=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--number", type=optional_int, default=None,
                        help="the pull request to merge; without it, the workflow_run event is read")
    parser.add_argument("--after-run", type=optional_int, default=None,
                        help="a workflow run to wait for before reading anything about the head")
    parser.add_argument("--event", default=None, help="event payload (default: $GITHUB_EVENT_PATH)")
    parser.add_argument("--hand-off", action="store_true",
                        help="handed a workflow_run event, dispatch campaigns rather than merge")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ

    if not environ.get(TOKEN):
        if args.hand_off:
            print(f"::warning::Not handing off: {TOKEN} is empty. automerge.yml supplies "
                  "github.token for this invocation.")
            return 0
        print(f"::error::Not merging: {TOKEN} is empty. automerge.yml sets it from the "
              "AUTOMERGE_TOKEN secret; CONTRIBUTING.md's continuous-integration section says what "
              "that secret needs.")
        return 1

    project = Path(args.project).resolve()
    if args.after_run and not wait_for_run(project, args.after_run):
        print(f"::error::Run {args.after_run} did not complete within {AFTER_RUN_TIMEOUT}s, so its "
              f"jobs would read as pending checks on the head. Dispatch this workflow again for it.")
        return 1

    dispatch_token = environ.get(DISPATCH_TOKEN)
    if args.number is not None:
        numbers, tested = [args.number], None
    else:
        path = args.event or environ.get("GITHUB_EVENT_PATH")
        event = json.loads(Path(path).read_text())
        if "pull_request" in event:
            try:
                hand_off(project, event["pull_request"]["number"], environ.get("GITHUB_RUN_ID", ""))
            except RuntimeError as error:
                print(f"::warning::PR#{event['pull_request']['number']}: {error}")
            return 0
        if args.hand_off:
            return hand_off_tested(project, event)
        numbers, tested = numbers_from_event(project, event)
        if passing_after_run(event.get("workflow_run") or {}):
            dispatch_token = None

    failed = False
    for number in numbers:
        try:
            settle_one(project, number, tested, dispatch_token)
        except RuntimeError as error:
            print(f"::error::PR#{number}: {error}")
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

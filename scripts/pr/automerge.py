#!/usr/bin/env python3
"""Merge a pull request labelled `automerge` through `settle.py merge`, from a workflow.

`.github/workflows/automerge.yml` runs this from a checkout of the default branch, never of the
pull request. Settle decides the merge; what this adds is which pull request to ask it about. A
refusal is an ordinary outcome here — CONTRIBUTING.md's "Merging a pull request" section says what
asks again — so it exits 0, where a reading that failed or a hand-off run that never finished exits 1.

Run: python3 scripts/pr/automerge.py                      (the workflow_run event GitHub hands a job)
     python3 scripts/pr/automerge.py --number <n> [--after-run <run id>]
"""

import argparse
import importlib.util
import json
import os
import sys
import time
from pathlib import Path

LABEL = "automerge"

# CONTRIBUTING.md's continuous-integration section owns why this is not the workflow's own token.
TOKEN = "GH_TOKEN"

AFTER_RUN_TIMEOUT = 600
AFTER_RUN_POLL = 10


def load_settle():
    """Imports settle by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("settle", Path(__file__).with_name("settle.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


settle = load_settle()


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


def settle_one(project, number, tested=None):
    """Merges one pull request if nothing blocks it, and says what happened."""
    pull = settle.rest_json(f"repos/{settle.repository(project)}/pulls/{number}")
    reason = pull_skip_reason(pull, tested)
    if reason:
        print(f"PR#{number} left alone: {reason}")
    elif settle.merge(project, number, None, dry_run=False):
        print(f"PR#{number} not merged: settle refused it, above. CONTRIBUTING.md's \"Merging a pull "
              f"request\" section says what asks again.")


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
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ

    if not environ.get(TOKEN):
        print(f"::warning::Not merging: {TOKEN} is empty. automerge.yml sets it from the "
              f"AUTOMERGE_TOKEN secret; CONTRIBUTING.md's continuous-integration section says what "
              f"that secret needs.")
        return 0

    project = Path(args.project).resolve()
    if args.after_run and not wait_for_run(project, args.after_run):
        print(f"::error::Run {args.after_run} did not complete within {AFTER_RUN_TIMEOUT}s, so its "
              f"jobs would read as pending checks on the head. Dispatch this workflow again for it.")
        return 1

    if args.number is not None:
        numbers, tested = [args.number], None
    else:
        path = args.event or environ.get("GITHUB_EVENT_PATH")
        numbers, tested = numbers_from_event(project, json.loads(Path(path).read_text()))

    failed = False
    for number in numbers:
        try:
            settle_one(project, number, tested)
        except RuntimeError as error:
            print(f"::error::PR#{number}: {error}")
            failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

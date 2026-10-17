#!/usr/bin/env python3
"""Decide an aggregate job from the results of the jobs it needs.

test.yml's and generators.yml's `required-checks` and mutation.yml's `campaign` run this as their
step. It passes where every result is `success` or `skipped`, and fails otherwise, with one
exception: a `pull_request` or `workflow_dispatch` run whose only other results are `cancelled`, and
whose head is no longer the head its pull request or branch points at, cancels its own run instead.
A newer head's run is the one that answers for the pull request, and a red check on the old head
answers for nothing.

    RESULTS  the `needs.<job>.result` values, separated by spaces
    HEAD     the commit the run was started for: the pull request's head, not its merge commit
    NUMBER   the pull request, where the run has one
    GITHUB_EVENT_NAME, GITHUB_REF, GITHUB_REPOSITORY, GITHUB_RUN_ID   as Actions sets them
    GH_TOKEN for `gh`: actions: write to cancel the run, pull-requests: read to read the head

Cancelling rather than passing: branch protection counts a `success` or a `skipped` check as
satisfied, so either would let a head whose suites never finished read as tested, and a cancelled
check blocks a merge on the head it reports for. So a wrong reading of the head, or a head reverted
to the old commit, leaves a check that still refuses rather than one that passes.

A head that cannot be read, or a ref that is not a branch, is not superseded.
"""

import os
import subprocess
import sys
import time
import urllib.parse

PASSING = frozenset({"success", "skipped"})
CANCELLED = "cancelled"
SUPERSEDABLE = frozenset({"pull_request", "workflow_dispatch"})
BRANCH_PREFIX = "refs/heads/"

PASS, FAIL, ASK = "pass", "fail", "ask"

GH_TIMEOUT = 60
# How long the step waits for the cancellation it asked for before failing. The job's own bound is
# ten minutes.
CANCEL_WAIT = 300
POLL = 10


def decide(results, event):
    """PASS, FAIL, or ASK where the verdict turns on whether the run's head was superseded."""
    if not results:
        return FAIL
    others = [result for result in results if result not in PASSING]
    if not others:
        return PASS
    if any(result != CANCELLED for result in others) or event not in SUPERSEDABLE:
        return FAIL
    return ASK


def head_path(repository, number, ref):
    """The API path whose answer names the head the run is measured against, and its jq filter.

    None for a ref that is not a branch: an annotated tag's ref reads back the tag object rather than
    the commit the run measured.
    """
    if number:
        return "repos/{}/pulls/{}".format(repository, number), ".head.sha"
    if ref.startswith(BRANCH_PREFIX):
        branch = urllib.parse.quote(ref[len(BRANCH_PREFIX):], safe="/")
        return "repos/{}/git/ref/heads/{}".format(repository, branch), ".object.sha"
    return None


def superseded(head, current):
    """Whether a head read back as `current` has moved off `head`. An unread one has not."""
    return bool(head) and bool(current) and current != head


def gh(*arguments):
    """`gh`'s stdout, or None where it failed or did not answer within `GH_TIMEOUT`."""
    try:
        done = subprocess.run(["gh", *arguments], capture_output=True, text=True, timeout=GH_TIMEOUT)
    except (OSError, subprocess.TimeoutExpired) as failure:
        print("gh {} did not answer: {}".format(arguments[0], failure), file=sys.stderr)
        return None
    if done.returncode != 0:
        print("gh {} failed: {}".format(" ".join(arguments[:2]), done.stderr.strip()), file=sys.stderr)
        return None
    return done.stdout.strip()


def read_head(path, jq):
    return gh("api", path, "--jq", jq)


def cancel_run(repository, run_id):
    # force-cancel, which GitHub documents as overriding an `always()` condition; this job carries one.
    return gh("api", "--method", "POST",
              "repos/{}/actions/runs/{}/force-cancel".format(repository, run_id)) is not None


def main(environ=os.environ, read=read_head, cancel=cancel_run, sleep=time.sleep, wait=CANCEL_WAIT):
    results = environ.get("RESULTS", "").split()
    event = environ.get("GITHUB_EVENT_NAME", "")
    verdict = decide(results, event)
    if verdict == PASS:
        return 0
    if verdict == ASK:
        repository, head = environ.get("GITHUB_REPOSITORY", ""), environ.get("HEAD", "")
        asked = head_path(repository, environ.get("NUMBER", ""), environ.get("GITHUB_REF", ""))
        current = read(*asked) if asked else None
        if superseded(head, current):
            print("::notice::{} has moved from {} to {}, so this run is cancelled rather than "
                  "failed.".format(
                      "the pull request" if environ.get("NUMBER") else "the branch",
                      head[:7], current[:7]))
            if cancel(repository, environ.get("GITHUB_RUN_ID", "")):
                waited = 0
                while waited < wait:
                    sleep(POLL)
                    waited += POLL
                print("::error::the run was asked to cancel and was still running {} seconds later."
                      .format(wait))
            else:
                print("::error::the superseded run could not cancel itself.")
    if not results:
        print("::error::no dependency result was handed to this step")
    for result in results:
        if result not in PASSING:
            print("::error::dependency finished as '{}'".format(result))
    return 1


if __name__ == "__main__":
    sys.exit(main())

#!/usr/bin/env python3
"""Print the earlier campaign on this branch whose records a campaign on HEAD may carry kills from.

That is the newest run of the campaign workflow on the branch that completed without being cancelled,
measured a head other than HEAD that HEAD descends from, and left records to read. It is printed as the
`run=` and `head=` lines $GITHUB_OUTPUT takes, both empty where there is none.
`mutation_check.py --carry-to` decides which of its kills carry; CONTRIBUTING.md owns the rule.

Run, in a workflow step: python3 scripts/ci/previous_campaign.py >> "$GITHUB_OUTPUT"
"""

import argparse
import json
import os
import subprocess
import sys
import urllib.parse
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "pr"))
import campaign  # noqa: E402

# The artifacts a campaign records verdicts in, by the names mutation.yml uploads them under.
RECORDS = ("Mutation EditMode shard ", "Mutation PlayMode shard ")
CARRIED = "Mutation carried"


def holds_records(artifacts):
    """Whether an artifacts listing names one that is unexpired and holds verdict records."""
    return any(not artifact.get("expired") and (
        str(artifact.get("name", "")).startswith(RECORDS) or artifact.get("name") == CARRIED)
        for artifact in artifacts)


def previous(runs, head, current, descends, records):
    """The run `__doc__` describes among an Actions runs listing's entries, or None.

    `descends` answers whether HEAD descends from a commit, and `records` whether a run left records;
    both are asked only of a run every cheaper reading left standing, newest first.
    """
    standing = [run for run in runs
                if run.get("path") == campaign.PATH and run.get("status") == "completed"
                and run.get("conclusion") != "cancelled" and run.get("id") != current
                and run.get("head_sha") and run.get("head_sha") != head]
    for run in sorted(standing, key=lambda run: run.get("run_number") or 0, reverse=True):
        if descends(run["head_sha"]) and records(run):
            return run
    return None


def gh(path):
    done = subprocess.run(["gh", "api", path], capture_output=True, text=True)
    if done.returncode != 0:
        raise SystemExit("gh api {} failed: {}".format(path, done.stderr.strip()))
    return json.loads(done.stdout)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=os.environ.get("GITHUB_REPOSITORY", ""))
    parser.add_argument("--branch", default=os.environ.get("GITHUB_REF_NAME", ""))
    parser.add_argument("--run", default=os.environ.get("GITHUB_RUN_ID", ""))
    args = parser.parse_args(argv)
    head = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip()
    found = None
    if args.repo and args.branch and head:
        runs = gh("repos/{}/actions/workflows/{}/runs?branch={}&status=completed&per_page=100".format(
            args.repo, campaign.WORKFLOW, urllib.parse.quote(args.branch, safe=""))).get("workflow_runs") or []
        found = previous(
            runs, head, int(args.run) if args.run.isdigit() else None,
            lambda sha: subprocess.run(["git", "merge-base", "--is-ancestor", sha, "HEAD"],
                                       capture_output=True).returncode == 0,
            lambda run: holds_records(gh("repos/{}/actions/runs/{}/artifacts?per_page=100".format(
                args.repo, run["id"])).get("artifacts") or []))
    print("previous campaign: {}".format(
        "run {} at {}".format(found["id"], found["head_sha"]) if found else "none"), file=sys.stderr)
    print("run={}".format(found["id"] if found else ""))
    print("head={}".format(found["head_sha"] if found else ""))
    return 0


if __name__ == "__main__":
    sys.exit(main())

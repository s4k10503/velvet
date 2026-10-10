#!/usr/bin/env python3
"""Find the campaign that already measured a head's key, and name the artifact that records one.

mutation.yml's plan computes the head's campaign key (`mutation_check.py --key`). A head whose key
equals that of a campaign that passed owes no measurement: the verdict of the run that measured it
stands, and the run ends without an editor. `scripts/pr/campaign.py` owns what a run records and
which candidate counts; this reads the runs and prints the answer for $GITHUB_OUTPUT.

    python3 scripts/ci/reuse_verdict.py --key HEX               # reused=RUN or reused=
    python3 scripts/ci/reuse_verdict.py --name HEX [--from RUN] # name=THE ARTIFACT NAME

The first reads the newest `CANDIDATES` passing runs of the workflow on the head's branch. A branch
is where an earlier head with the same key sits; a key found nowhere there is measured.
"""

import argparse
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

CANDIDATES = 20


def load_campaign():
    path = Path(__file__).resolve().parents[1] / "pr" / "campaign.py"
    spec = importlib.util.spec_from_file_location("campaign", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


campaign = load_campaign()


def gh_json(path):
    done = subprocess.run(["gh", "api", path], capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("gh api {} failed: {}".format(path, done.stderr.strip()))
    return json.loads(done.stdout)


def candidates(slug, branch, current, read):
    """(run, artifacts listing) for the newest passing campaigns on `branch`, but not run `current`."""
    listing = read("repos/{}/actions/workflows/{}/runs?status=success&branch={}&per_page={}".format(
        slug, campaign.WORKFLOW, branch, CANDIDATES))
    runs = [run for run in listing.get("workflow_runs", []) if str(run.get("id")) != str(current)]
    runs.sort(key=lambda run: run.get("run_number") or 0, reverse=True)
    return [(run, read(campaign.artifacts_path(slug, run.get("id")))) for run in runs]


def main(argv, environ=os.environ, read=gh_json):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--key")
    parser.add_argument("--name")
    parser.add_argument("--from", dest="measured_by")
    args = parser.parse_args(argv)
    if args.name:
        print("name={}".format(campaign.key_artifact_name(args.name, args.measured_by)))
        return 0
    if not args.key:
        parser.error("one of --key and --name is required")
    try:
        found = campaign.reusable(args.key, candidates(
            environ["GITHUB_REPOSITORY"], environ["GITHUB_REF_NAME"], environ.get("GITHUB_RUN_ID", ""),
            read))
    except RuntimeError as failure:
        # Nothing is reused on a reading that failed: the campaign measures, which costs a run and
        # claims nothing.
        print("no verdict is reused: {}".format(failure), file=sys.stderr)
        found = None
    print("reused={}".format(found or ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

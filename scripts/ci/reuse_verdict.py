#!/usr/bin/env python3
"""Find the campaign that already measured a head's key, and name the artifact that records one.

mutation.yml's plan computes the head's campaign key (`mutation_check.py --key`). A head whose key
equals that of a campaign that passed owes no measurement: the verdict of the run that measured it
stands, and the run ends without an editor. `scripts/pr/campaign.py` owns what a run records and
which candidate counts; this reads the runs and prints the answer for $GITHUB_OUTPUT.

    python3 scripts/ci/reuse_verdict.py --key HEX               # reused=RUN or reused=
    python3 scripts/ci/reuse_verdict.py --name HEX [--from RUN] # name=THE ARTIFACT NAME
    python3 scripts/ci/reuse_verdict.py --ledgers DIR           # DIR/<run number>-<run>.json

The first reads the newest `CANDIDATES` passing runs of the workflow on the head's branch. A branch
is where an earlier head with the same key sits; a key found nowhere there is measured.

The last downloads the per-mutant ledger of each of the newest `CANDIDATES` finished runs on the
branch that left one, a failed run included: a campaign that failed on a survivor is the one whose
kills the next round needs. Which of a ledger's kills stand is `mutation_check.py --reuse-from`'s
reading of the tree, so a ledger that cannot be fetched or read costs a measurement and claims nothing.
"""

import argparse
import importlib.util
import json
import os
import subprocess
import sys
import tempfile
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


def gh_download(slug, run_id, directory):
    """Downloads the ledger artifact of a run into `directory`, raising where it cannot."""
    done = subprocess.run(["gh", "run", "download", str(run_id), "--repo", slug, "--name",
                           campaign.LEDGER_ARTIFACT, "--dir", str(directory)],
                          capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("gh run download {} failed: {}".format(run_id, done.stderr.strip()))


def fetch_ledgers(slug, branch, current, directory, read, download):
    """Writes each finished run's ledger to `directory` as `<run number>-<run>.json`; returns the
    runs it fetched from and a line for each run it could not use.

    A run whose listing, artifact or download fails is left out on its own: the others are still
    reusable, and a missing ledger is only a measurement.
    """
    listing = read("repos/{}/actions/workflows/{}/runs?status=completed&branch={}&per_page={}".format(
        slug, campaign.WORKFLOW, branch, CANDIDATES))
    runs = [run for run in listing.get("workflow_runs", [])
            if str(run.get("id")) != str(current) and run.get("path") == campaign.PATH
            and run.get("status") == "completed"]
    fetched, skipped = [], []
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    for run in sorted(runs, key=lambda run: run.get("run_number") or 0, reverse=True):
        run_id = run.get("id")
        try:
            if campaign.ledger_artifact(read(campaign.artifacts_path(slug, run_id))) is None:
                skipped.append("run {} left no single ledger that is still held".format(run_id))
                continue
            with tempfile.TemporaryDirectory(dir=str(directory)) as scratch:
                download(slug, run_id, scratch)
                held = Path(scratch) / "ledger.json"
                # Parsed here only to refuse a file that is not JSON; the reading that decides what
                # it holds is mutation_check.py's.
                json.loads(held.read_text())
                target = directory / "{:010d}-{}.json".format(run.get("run_number") or 0, run_id)
                held.replace(target)
            fetched.append(run_id)
        except Exception as failure:  # noqa: BLE001 -- see the docstring: any failure skips one run.
            skipped.append("run {} was skipped: {}: {}".format(run_id, type(failure).__name__, failure))
    return fetched, skipped


def main(argv, environ=os.environ, read=gh_json, download=gh_download):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--key")
    parser.add_argument("--name")
    parser.add_argument("--from", dest="measured_by")
    parser.add_argument("--ledgers", metavar="DIR")
    args = parser.parse_args(argv)
    if args.ledgers:
        try:
            fetched, skipped = fetch_ledgers(
                environ["GITHUB_REPOSITORY"], environ["GITHUB_REF_NAME"], environ.get("GITHUB_RUN_ID", ""),
                args.ledgers, read, download)
        except Exception as failure:  # noqa: BLE001 -- as below: no ledger is a measurement, not a failure.
            print("no ledger is fetched: {}: {}".format(type(failure).__name__, failure), file=sys.stderr)
            return 0
        for line in skipped:
            print(line, file=sys.stderr)
        print("ledgers fetched from runs: {}".format(", ".join(str(run) for run in fetched) or "none"))
        return 0
    if args.name:
        print("name={}".format(campaign.key_artifact_name(args.name, args.measured_by)))
        return 0
    if not args.key:
        parser.error("one of --key and --name is required")
    try:
        found = campaign.reusable(args.key, candidates(
            environ["GITHUB_REPOSITORY"], environ["GITHUB_REF_NAME"], environ.get("GITHUB_RUN_ID", ""),
            read))
    except Exception as failure:  # noqa: BLE001 -- a reading of any shape that fails, a malformed
        # answer or a missing variable included, reuses nothing: the campaign then measures, which
        # costs a run and claims nothing.
        print("no verdict is reused: {}: {}".format(type(failure).__name__, failure), file=sys.stderr)
        found = None
    print("reused={}".format(found or ""))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

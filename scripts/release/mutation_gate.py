#!/usr/bin/env python3
"""Refuse to release a commit the mutation record does not answer, or whose required CI did not pass.

`.github/workflows/upm.yml` runs this on a release dispatch, before anything is tagged, over the
commit the dispatch checked out, which is the commit the release promotes and the one `--release`
names. CONTRIBUTING.md's release
section owns the rule and the way out; this holds the decision:

- Where the commit is on the default branch, however the release was dispatched, the newest completed
  push run of each required workflow (`red_base.REQUIRED_WORKFLOWS`) at it concluded `success`.
- `mutation_check.py --gate` over the campaign's scope at the commit (`change_base.campaign_scope`),
  reading the record branch (`mutation_record.py`): every mutant holds a standing verdict, and every
  survivor among them is answered.

The mutation half reads git and the record alone: it runs in a worktree checked out from the release
commit's objects, so nothing uncommitted in the dispatch's checkout reaches it, and it refuses a
shallow repository, where the scope's tag and cutover are not in the history to be found. It runs that
commit's own copy of the campaign's scripts, the copy its record runs measured with, and refuses a
commit whose tree has none.

    python3 scripts/release/mutation_gate.py --release SHA [--skip REASON]
"""

import argparse
import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

# What the release commit's tree has to hold for the record to answer it.
TOOLING = ("scripts/ci/change_base.py", "scripts/ci/mutation_record.py",
           "scripts/test_quality/mutation_check.py")


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def git(project, *arguments):
    return subprocess.run(["git", "-C", str(project), *arguments], capture_output=True, text=True)


def gh_json(path):
    done = subprocess.run(["gh", "api", path], capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError("gh api {} failed: {}".format(path, done.stderr.strip()))
    return json.loads(done.stdout)


def ci_problems(slug, sha, workflows, read=gh_json):
    """Why the required CI at `sha` does not stand, one line per workflow, or [] where it does."""
    problems = []
    for workflow in workflows:
        listing = read("repos/{}/actions/workflows/{}/runs?head_sha={}&event=push&per_page=20".format(
            slug, workflow, sha)) or {}
        runs = [run for run in listing.get("workflow_runs") or [] if run.get("status") == "completed"]
        newest = max(runs, key=lambda run: (run.get("run_number") or 0, run.get("run_attempt") or 1),
                     default=None)
        if newest is None:
            problems.append("{} has no completed push run at {}".format(workflow, sha[:12]))
        elif newest.get("conclusion") != "success":
            problems.append("{}'s push run {} at {} concluded {}".format(
                workflow, newest.get("id"), sha[:12], newest.get("conclusion")))
    return problems


def on_default_branch(project, default_branch, release):
    """Whether origin's default branch holds `release`, fetched whole so the answer is not a cut's."""
    if git(project, "fetch", "--quiet", "origin", default_branch).returncode != 0:
        raise SystemExit("origin/{} could not be fetched, so whether it holds {} is unknown".format(
            default_branch, release[:12]))
    return git(project, "merge-base", "--is-ancestor", release, "FETCH_HEAD").returncode == 0


def mutation_gate(tree, project, record_dir=None):
    """(`mutation_check.py --gate`'s exit, what it printed) over the campaign's scope at `tree`'s HEAD,
    with the record fetched from `project`'s origin unless `record_dir` already holds it."""
    change_base = load(tree / "scripts/ci/change_base.py", "change_base")
    here = os.getcwd()
    os.chdir(tree)
    try:
        base = change_base.campaign_scope()
    finally:
        os.chdir(here)
    with tempfile.TemporaryDirectory(prefix="mutation-gate-") as scratch:
        if record_dir is None:
            record_dir = scratch
            load(tree / "scripts/ci/mutation_record.py", "mutation_record").fetch(project, scratch)
        done = subprocess.run([sys.executable, "scripts/test_quality/mutation_check.py",
                               "--project", ".", "--base", base, "--gate", "--reuse-from", str(record_dir)],
                              capture_output=True, text=True, cwd=str(tree))
    return done.returncode, "scope: {}\n{}{}".format(base, done.stdout, done.stderr)


def main(argv=None, environ=None, read=gh_json):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".")
    parser.add_argument("--release", required=True, help="the commit the release promotes")
    parser.add_argument("--record", help="a record directory to read instead of origin's branch")
    parser.add_argument("--skip", default="", help="release without the gate, for the reason given")
    args = parser.parse_args(argv)
    environ = os.environ if environ is None else environ
    if args.skip.strip():
        print("::warning::the release gate was skipped for {}: {}".format(args.release[:12], args.skip.strip()))
        return 0
    project = Path(args.project).resolve()
    if git(project, "rev-parse", "--is-shallow-repository").stdout.strip() != "false":
        print("::error::the repository is shallow, or git could not say: the scope's release tag and "
              "cutover may be cut off, so no scope read here can be trusted. Check out the whole history.")
        return 1
    failed = False
    default_branch = environ.get("DEFAULT_BRANCH", "")
    if not default_branch:
        print("DEFAULT_BRANCH is not set, so the required CI is not read")
    elif on_default_branch(project, default_branch, args.release):
        red_base = load(project / "scripts/pr/red_base.py", "red_base")
        for problem in ci_problems(environ.get("GITHUB_REPOSITORY", ""), args.release,
                                   red_base.REQUIRED_WORKFLOWS, read):
            print("::error::{}".format(problem))
            failed = True
    else:
        print("{} is not on {}, whose push runs alone the required CI is read from".format(
            args.release[:12], default_branch))
    scratch = Path(tempfile.mkdtemp(prefix="mutation-gate-tree-"))
    tree = scratch / "tree"
    try:
        if git(project, "worktree", "add", "--quiet", "--detach", str(tree), args.release).returncode != 0:
            print("::error::no worktree of {} could be checked out".format(args.release))
            return 1
        missing = [name for name in TOOLING if not (tree / name).is_file()]
        if missing:
            print("::error::{} is not in this commit's tree, so no campaign on it could have written the "
                  "record it would be gated on".format(missing[0]))
            return 1
        code, printed = mutation_gate(tree, project, args.record)
    finally:
        git(project, "worktree", "remove", "--force", str(tree))
        shutil.rmtree(scratch, ignore_errors=True)
    print(printed)
    if code != 0:
        print("::error::the mutation record does not answer every mutant of this release; what the gate "
              "printed above names each and how it is answered, and CONTRIBUTING.md's release section "
              "says how to release without the gate")
        failed = True
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

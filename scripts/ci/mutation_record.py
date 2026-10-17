#!/usr/bin/env python3
"""Read and write the mutation campaign's record: the `RECORD_BRANCH` branch, one file per verdict.

`mutation_check.py --reuse-from DIR` reads a directory of entries named by `record_key` and
`--emit-record DIR` writes one. This moves them between that directory and the branch, which is what
makes the record outlive any run.

    python3 scripts/ci/mutation_record.py fetch DIR     # DIR/<key>.json for every entry; empty if none
    python3 scripts/ci/mutation_record.py publish DIR   # commit DIR's entries onto the branch

Both talk to `$GITHUB_REPOSITORY` through `gh api`, as the token in `$GH_TOKEN`.

**Who wrote the record is checked, not assumed.** No ruleset can be set on the branch from here, so
the writer is enforced by what a commit carries. `publish` creates its commit through the Git Data
API with the workflow's `GITHUB_TOKEN`, giving no author, committer or signature of its own, and moves
the branch only forward. `fetch` walks the branch's first-parent history and refuses the whole record
where any commit is not one GitHub reports as verified with reason `valid` and committed by
`TRUSTED_COMMITTER`, naming it. A person with write access can still push to the branch, or an
administrator rewrite it; a commit GitHub did not sign as the bot is refused rather than read.

An entry's name is its content key, so two runs that wrote the same key wrote the same mutant of the
same file under the same mutator, and a later one replacing an earlier one is the newer verdict.
Entries under other names never touch, so an update that lost a race is retried over the newer
branch rather than merged.

This file also marks the campaign's cutover: `change_base.py` starts the range a run measures no
earlier than the first-parent commit on the default branch that added it, since everything merged
before that was measured by the per-pull-request campaign that preceded the record.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

RECORD_BRANCH = "mutation-record"

# Where the entries sit on the branch.
ENTRIES = "records"

PUSH_ATTEMPTS = 5

# The login GitHub reports as the committer of a commit the workflow's token created.
TRUSTED_COMMITTER = "github-actions[bot]"


class ApiError(RuntimeError):
    def __init__(self, status, message):
        super().__init__(message)
        self.status = status


def gh_api(method, path, body=None):
    """The JSON answer to one REST call through `gh api`, or ApiError carrying its HTTP status."""
    command = ["gh", "api", "--method", method, path]
    if body is not None:
        command += ["--input", "-"]
    done = subprocess.run(command, capture_output=True, text=True,
                          input=json.dumps(body) if body is not None else None)
    if done.returncode != 0:
        found = re.search(r"\(HTTP (\d{3})\)", done.stderr)
        raise ApiError(int(found.group(1)) if found else 0,
                       "gh api {} {} failed: {}".format(method, path, done.stderr.strip()))
    return json.loads(done.stdout) if done.stdout.strip() else None


def git(*arguments, cwd=None, check=True):
    done = subprocess.run(["git", *arguments], cwd=cwd, capture_output=True, text=True)
    if check and done.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(arguments), done.stderr.strip()))
    return done


def remote_branch(project):
    """Whether origin holds the record branch, fetched into `refs/remotes/origin/` where it does."""
    return git("fetch", "--quiet", "origin", "+refs/heads/{0}:refs/remotes/origin/{0}".format(RECORD_BRANCH),
               cwd=project, check=False).returncode == 0


def foreign_commits(project, slug, call=gh_api):
    """Each commit on the fetched branch's first-parent history that is not GitHub-signed by
    `TRUSTED_COMMITTER`, as "sha: why", oldest last. A first-parent history names each commit once,
    so each is asked about once."""
    history = git("rev-list", "--first-parent", "refs/remotes/origin/{}".format(RECORD_BRANCH),
                  cwd=project).stdout.split()
    problems = []
    for sha in history:
        held = call("GET", "repos/{}/commits/{}".format(slug, sha)) or {}
        verification = (held.get("commit") or {}).get("verification") or {}
        committer = (held.get("committer") or {}).get("login")
        if verification.get("verified") is not True or verification.get("reason") != "valid":
            problems.append("{}: GitHub does not report it signed (verified {}, reason {})".format(
                sha, verification.get("verified"), verification.get("reason")))
        elif committer != TRUSTED_COMMITTER:
            problems.append("{}: committed by {}, not {}".format(sha, committer, TRUSTED_COMMITTER))
    return problems


def fetch(project, directory, slug=None, call=gh_api):
    """Writes every entry the branch holds into `directory`, and returns how many; refuses a branch
    holding a commit `foreign_commits` names."""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    if not remote_branch(project):
        return 0
    slug = slug or os.environ.get("GITHUB_REPOSITORY", "")
    if not slug:
        raise SystemExit("$GITHUB_REPOSITORY is not set, so who wrote the {} branch cannot be asked"
                         .format(RECORD_BRANCH))
    problems = foreign_commits(project, slug, call)
    if problems:
        raise SystemExit("the {} branch holds commits its writer did not make, so none of it is read:\n  {}"
                         .format(RECORD_BRANCH, "\n  ".join(problems)))
    listed = [line.split("\t", 1) for line in git("ls-tree", "origin/{}".format(RECORD_BRANCH), ENTRIES + "/",
                                                    cwd=project).stdout.splitlines()]
    entries = [(header.split()[2], Path(name).name) for header, name in listed]
    done = subprocess.run(["git", "cat-file", "--batch"], cwd=project, capture_output=True,
                          input="".join(oid + "\n" for oid, _ in entries).encode())
    if done.returncode != 0:
        raise RuntimeError("git cat-file --batch failed: {}".format(done.stderr.decode().strip()))
    out, cursor = done.stdout, 0
    for _, name in entries:
        end = out.index(b"\n", cursor)
        size = int(out[cursor:end].split()[2])
        (directory / name).write_bytes(out[end + 1:end + 1 + size])
        cursor = end + 1 + size + 1
    return len(entries)


def publish(directory, message, slug=None, call=gh_api):
    """Commits `directory`'s entries onto the branch through the Git Data API, moving the branch only
    forward, and retries over a newer branch where another run moved it first."""
    slug = slug or os.environ.get("GITHUB_REPOSITORY", "")
    entries = sorted(Path(directory).glob("*.json"))
    if not entries:
        return 0
    files = [{"path": "{}/{}".format(ENTRIES, entry.name), "mode": "100644", "type": "blob",
              "content": entry.read_text()} for entry in entries]
    ref = "repos/{}/git/refs/heads/{}".format(slug, RECORD_BRANCH)
    for _ in range(PUSH_ATTEMPTS):
        try:
            parent = call("GET", "repos/{}/git/ref/heads/{}".format(slug, RECORD_BRANCH))["object"]["sha"]
        except ApiError as failure:
            if failure.status != 404:
                raise
            parent = None
        base = call("GET", "repos/{}/git/commits/{}".format(slug, parent))["tree"]["sha"] if parent else None
        tree = call("POST", "repos/{}/git/trees".format(slug),
                    dict({"tree": files}, **({"base_tree": base} if base else {})))["sha"]
        if tree == base:
            return 0
        # No author, committer or signature of its own: `foreign_commits` refuses a commit GitHub did not sign.
        commit = call("POST", "repos/{}/git/commits".format(slug),
                      {"message": message, "tree": tree, "parents": [parent] if parent else []})["sha"]
        try:
            if parent:
                call("PATCH", ref, {"sha": commit, "force": False})
            else:
                call("POST", "repos/{}/git/refs".format(slug),
                     {"ref": "refs/heads/{}".format(RECORD_BRANCH), "sha": commit})
            return len(entries)
        except ApiError as failure:
            if failure.status != 422:
                raise
    raise RuntimeError("the record was not written in {} attempts".format(PUSH_ATTEMPTS))


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".")
    parser.add_argument("command", choices=("fetch", "publish"))
    parser.add_argument("directory")
    parser.add_argument("--message", default="Record mutation verdicts")
    args = parser.parse_args(argv)
    if args.command == "fetch":
        print("{} record entries fetched".format(fetch(Path(args.project).resolve(), args.directory)))
    else:
        print("{} record entries published".format(publish(args.directory, args.message)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

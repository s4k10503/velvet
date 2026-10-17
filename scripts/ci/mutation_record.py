#!/usr/bin/env python3
"""Read and write the mutation campaign's record: the `RECORD_BRANCH` branch, one file per verdict.

`mutation_check.py --reuse-from DIR` reads a directory of entries named by `record_key` and
`--emit-record DIR` writes one. This moves them between that directory and the branch, which is what
makes the record outlive any run: every campaign reads it, and of the workflows only a campaign's `record` job writes it.

    python3 scripts/ci/mutation_record.py fetch DIR     # DIR/<key>.json for every entry; empty if none
    python3 scripts/ci/mutation_record.py publish DIR   # commit DIR's entries onto the branch and push

An entry's name is its content key, so two runs that wrote the same key wrote the same mutant of the
same file under the same tool, and a later one replacing an earlier one is the newer verdict. Entries
under other names never touch, so a push that lost a race is retried over the newer branch rather
than merged.

This file also marks the campaign's cutover: `change_base.py` starts the range a run measures no
earlier than the first-parent commit on the default branch that added it, since everything merged
before that was measured by the per-pull-request campaign that preceded the record.
"""

import argparse
import os
import subprocess
import sys
import tempfile
from pathlib import Path

RECORD_BRANCH = "mutation-record"

# Where the entries sit on the branch.
ENTRIES = "records"

PUSH_ATTEMPTS = 5


def git(*arguments, cwd=None, check=True):
    done = subprocess.run(["git", *arguments], cwd=cwd, capture_output=True, text=True)
    if check and done.returncode != 0:
        raise RuntimeError("git {} failed: {}".format(" ".join(arguments), done.stderr.strip()))
    return done


def remote_branch(project):
    """Whether origin holds the record branch, fetched into `refs/remotes/origin/` where it does."""
    return git("fetch", "--quiet", "origin", "+refs/heads/{0}:refs/remotes/origin/{0}".format(RECORD_BRANCH),
               cwd=project, check=False).returncode == 0


def fetch(project, directory):
    """Writes every entry the branch holds into `directory`, and returns how many."""
    directory = Path(directory)
    directory.mkdir(parents=True, exist_ok=True)
    if not remote_branch(project):
        return 0
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


def publish(project, directory, message):
    """Commits `directory`'s entries onto the branch and pushes, retrying over a newer branch.

    Built with an index of its own rather than a checkout, so the job's checkout is left as it was.
    """
    entries = sorted(Path(directory).glob("*.json"))
    if not entries:
        return 0
    blobs = {entry.name: git("hash-object", "-w", str(entry), cwd=project).stdout.strip() for entry in entries}
    with tempfile.TemporaryDirectory(prefix="mutation-record-") as scratch:
        environ = dict(os.environ, GIT_INDEX_FILE=str(Path(scratch) / "index"))
        for _ in range(PUSH_ATTEMPTS):
            parent = "origin/{}".format(RECORD_BRANCH) if remote_branch(project) else None
            Path(environ["GIT_INDEX_FILE"]).unlink(missing_ok=True)
            if parent:
                run(["git", "read-tree", parent], project, environ)
            else:
                run(["git", "read-tree", "--empty"], project, environ)
            for name, blob in blobs.items():
                run(["git", "update-index", "--add", "--cacheinfo",
                     "100644,{},{}/{}".format(blob, ENTRIES, name)], project, environ)
            tree = run(["git", "write-tree"], project, environ)
            if parent and tree == git("rev-parse", parent + "^{tree}", cwd=project).stdout.strip():
                return 0
            commit = run(["git", "-c", "user.name=github-actions[bot]",
                          "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
                          "commit-tree", tree, *(["-p", parent] if parent else []), "-m", message],
                         project, environ)
            if git("push", "--quiet", "origin", "{}:refs/heads/{}".format(commit, RECORD_BRANCH),
                   cwd=project, check=False).returncode == 0:
                return len(entries)
    raise RuntimeError("the record was not pushed in {} attempts".format(PUSH_ATTEMPTS))


def run(command, project, environ):
    done = subprocess.run(command, cwd=project, capture_output=True, text=True, env=environ)
    if done.returncode != 0:
        raise RuntimeError("{} failed: {}".format(" ".join(command), done.stderr.strip()))
    return done.stdout.strip()


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".")
    parser.add_argument("command", choices=("fetch", "publish"))
    parser.add_argument("directory")
    parser.add_argument("--message", default="Record mutation verdicts")
    args = parser.parse_args(argv)
    project = Path(args.project).resolve()
    if args.command == "fetch":
        print("{} record entries fetched".format(fetch(project, args.directory)))
    else:
        print("{} record entries published".format(publish(project, args.directory, args.message)))
    return 0


if __name__ == "__main__":
    sys.exit(main())

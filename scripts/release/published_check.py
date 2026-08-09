#!/usr/bin/env python3
"""Refuse a tree that closed a version in the CHANGELOG and never published it.

A release reaches main as an ordinary commit — the section dated, package.json bumped — and the
publish is a separate `workflow_dispatch` that nothing forces. In the window between the two, main
names a version that does not exist, and anything merged there becomes part of whatever that
version eventually ships: the dispatch builds the note from the CHANGELOG section, which was
written before those commits and does not describe them. Recovering means tagging the release
commit by hand and dispatching from the tag.

Read of the base rather than of the pull request, so the pull request that closes the section is
the one tree the state is allowed to be in.

Run: python3 scripts/release/published_check.py --rev origin/main
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from release_notes import DEFAULT_CHANGELOG, DEFAULT_PACKAGE_JSON, VERSION_HEADING

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

CHANGELOG_PATH = str(DEFAULT_CHANGELOG.relative_to(REPO_ROOT))
PACKAGE_JSON_PATH = str(DEFAULT_PACKAGE_JSON.relative_to(REPO_ROOT))

RELEASE_DATE = re.compile(r"\]\s*-\s*\d{4}-\d{2}-\d{2}\s*$")


def version_headings(changelog_text):
    """Every `## [version]` line, newest first, paired with the version it names."""
    headings = []
    for line in changelog_text.splitlines():
        match = VERSION_HEADING.match(line)
        if match:
            headings.append((match.group("version"), line))
    return headings


def publication_reason(changelog_text, package_json_text, tags):
    """Why nothing may be merged on top of this tree, or None.

    Ordered so the reader is told the first thing that is wrong rather than all of them: an absent
    section and an absent tag have the same repair, and naming the tag first would send someone to
    dispatch a version the note builder would refuse.
    """
    version = json.loads(package_json_text).get("version")
    if not version:
        return f"{PACKAGE_JSON_PATH} declares no version"

    headings = version_headings(changelog_text)
    own = next((line for named, line in headings if named == version), None)
    if own is None:
        return (f"package.json is at {version} and the CHANGELOG has no '## [{version}]' section, "
                f"so a dispatch of {version} would fail with nothing to build a note from")

    if not RELEASE_DATE.search(own):
        return (f"the CHANGELOG section for {version} carries no date: close it as "
                f"'## [{version}] - YYYY-MM-DD' before anything is merged on top of it")

    # package.json is what upm.yml verifies a dispatch against, so a section closed above the one it
    # names is a release the dispatch cannot reach and this check would otherwise read past.
    above = [named for named, line in headings[:headings.index((version, own))]
             if RELEASE_DATE.search(line)]
    if above:
        return (f"the CHANGELOG has closed {', '.join(above)} above the {version} package.json "
                f"names: bump package.json to {above[0]} so the dispatch can reach it")

    if f"v{version}" not in tags:
        return (f"v{version} is closed in the CHANGELOG and was never published. Dispatch it with "
                f"`gh workflow run upm.yml -f version={version}` — every commit merged before that "
                f"happens ships inside {version} with its note describing none of them")

    return None


def git(project, *args):
    result = subprocess.run(["git", "-C", str(project), *args],
                            capture_output=True, text=True, check=True)
    return result.stdout


def remote_tags(project, remote="origin"):
    """Tag names on the remote.

    Asked of the remote so a stale local tag list cannot report an unpublished version as published,
    and so the reading needs neither a tag fetch nor a checkout deep enough to carry one.
    """
    lines = git(project, "ls-remote", "--tags", remote).splitlines()
    return {line.split("refs/tags/", 1)[1].removesuffix("^{}")
            for line in lines if "refs/tags/" in line}


def read_at(project, rev, path):
    return git(project, "show", f"{rev}:{path}")


def unpublished_reason(project, rev="origin/main", remote="origin", fetch=False):
    """publication_reason for one revision of a repository, or None when it reads clean.

    Returns None on any git failure. Every caller but the workflow is a guard on a developer's
    machine, where a detached checkout or an absent remote is an ordinary state and refusing there
    would train the reader to work around the guard.
    """
    try:
        if fetch:
            git(project, "fetch", remote, rev.split("/", 1)[-1], "--quiet")
        return publication_reason(read_at(project, rev, CHANGELOG_PATH),
                                  read_at(project, rev, PACKAGE_JSON_PATH),
                                  remote_tags(project, remote))
    except (subprocess.CalledProcessError, json.JSONDecodeError, OSError):
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--rev", default="origin/main",
                        help="the revision to read the two files from (default: origin/main)")
    parser.add_argument("--remote", default="origin", help="remote to read tags from")
    args = parser.parse_args()

    project = Path(args.project).resolve()
    reason = publication_reason(read_at(project, args.rev, CHANGELOG_PATH),
                                read_at(project, args.rev, PACKAGE_JSON_PATH),
                                remote_tags(project, args.remote))
    if reason:
        print(f"{args.rev} holds an unpublished release: {reason}", file=sys.stderr)
        return 1

    print(f"{args.rev}: the version package.json names is published")
    return 0


if __name__ == "__main__":
    sys.exit(main())

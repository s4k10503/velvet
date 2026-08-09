#!/usr/bin/env python3
"""Hold a repository to two things about the version it names, each asked of a different tree.

CONTRIBUTING.md's release section owns what goes wrong when nothing asks. What is decided here is
which question is posed of which tree, and both answers follow from what repairs the state.

**Publication**, asked of the BASE: the version package.json names is tagged. A dispatch repairs it,
so refusing merges is pressure toward one, and reading the base leaves the pull request that closes a
section free to merge — the one tree the state is allowed to be in.

**Consistency**, asked of the TREE A MERGE WOULD PRODUCE: package.json names a CHANGELOG section that
exists, carries a date, and has no dated section above it. A commit repairs each of these, so they
must fail whoever would introduce them. Read of the base too, they would leave the repair itself
unmergeable, with no direct push to main to escape through.

Run: python3 scripts/release/published_check.py --base origin/main --result HEAD
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from release_notes import DEFAULT_CHANGELOG, DEFAULT_PACKAGE_JSON, VERSION_HEADING

REPO_ROOT = Path(__file__).resolve().parent.parent.parent

# Spelled for a git tree, which is not the filesystem: these are arguments to `git show`, not paths to
# open, so they take the separator git uses rather than the one this platform does.
CHANGELOG_PATH = DEFAULT_CHANGELOG.relative_to(REPO_ROOT).as_posix()
PACKAGE_JSON_PATH = DEFAULT_PACKAGE_JSON.relative_to(REPO_ROOT).as_posix()

RELEASE_DATE = re.compile(r"\]\s*-\s*\d{4}-\d{2}-\d{2}\s*$")
RELEASE_TAG = re.compile(r"^v\d")


def version_headings(changelog_text):
    """Every `## [version]` line, in file order, paired with the version it names."""
    return [(match.group("version"), line)
            for line in changelog_text.splitlines()
            if (match := VERSION_HEADING.match(line))]


def declared_version(package_json_text):
    return json.loads(package_json_text).get("version")


def consistency_reason(changelog_text, package_json_text):
    """Why this tree could not be released as it stands, or None.

    Ordered so the reader is told the first thing that is wrong rather than all of them.
    """
    version = declared_version(package_json_text)
    if not version:
        return f"{PACKAGE_JSON_PATH} declares no version"

    headings = version_headings(changelog_text)
    own = next((line for named, line in headings if named == version), None)
    if own is None:
        return (f"package.json is at {version} and the CHANGELOG has no '## [{version}]' section, "
                f"so a dispatch of {version} would fail with nothing to build a note from")

    if not RELEASE_DATE.search(own):
        return (f"the CHANGELOG section for {version} carries no date: close it as "
                f"'## [{version}] - YYYY-MM-DD', which is the spelling the note builder reads")

    # package.json is what upm.yml verifies a dispatch against, so a section closed above the one it
    # names is a release the dispatch cannot reach.
    above = [named for named, line in headings[:headings.index((version, own))]
             if RELEASE_DATE.search(line)]
    if above:
        return (f"the CHANGELOG has closed {', '.join(above)} above the {version} package.json "
                f"names: bump package.json to {above[0]} so the dispatch can reach it")

    return None


def publication_reason(changelog_text, package_json_text, tags):
    """Why nothing may be merged on top of this tree, or None.

    Answers None for a tree consistency_reason already refuses: the version to ask about is exactly
    what is in doubt there, and that question is posed of the merge result instead.

    A remote carrying no release tag at all answers None too: there is no release history to have
    left a version out of, and naming a dispatch would be an instruction with nothing behind it.
    """
    if consistency_reason(changelog_text, package_json_text):
        return None

    version = declared_version(package_json_text)
    if not any(RELEASE_TAG.match(tag) for tag in tags):
        return None

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

    A git failure answers None rather than refusing: an absent revision, an absent remote and an
    unreachable network are ordinary states on a developer's machine, and refusing in them would
    train the reader to work around the guard. It says so on stderr, because that answer is otherwise
    the same silence a published base gives. The workflow does not come through here — it lets a git
    error raise and go red.
    """
    try:
        if fetch:
            git(project, "fetch", remote, rev.split("/", 1)[-1], "--quiet")
        return publication_reason(read_at(project, rev, CHANGELOG_PATH),
                                  read_at(project, rev, PACKAGE_JSON_PATH),
                                  remote_tags(project, remote))
    except (subprocess.CalledProcessError, json.JSONDecodeError, OSError) as failure:
        print(f"could not read {rev} to check it against the published releases: {failure}",
              file=sys.stderr)
        return None


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--base", help="revision to ask the publication question of")
    parser.add_argument("--result", help="revision to ask the consistency question of")
    parser.add_argument("--remote", default="origin", help="remote to read tags from")
    args = parser.parse_args()
    if not args.base and not args.result:
        parser.error("name at least one of --base and --result")

    project = Path(args.project).resolve()
    failed = False

    def report(rev, reason, wrong, right):
        nonlocal failed
        if reason:
            failed = True
            print(f"{rev} {wrong}: {reason}", file=sys.stderr)
        else:
            print(f"{rev}: {right}")

    if args.base:
        report(args.base,
               publication_reason(read_at(project, args.base, CHANGELOG_PATH),
                                  read_at(project, args.base, PACKAGE_JSON_PATH),
                                  remote_tags(project, args.remote)),
               "holds an unpublished release", "the version package.json names is published")

    if args.result:
        report(args.result,
               consistency_reason(read_at(project, args.result, CHANGELOG_PATH),
                                  read_at(project, args.result, PACKAGE_JSON_PATH)),
               "could not be released as it stands", "package.json and the CHANGELOG agree")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

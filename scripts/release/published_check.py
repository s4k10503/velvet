#!/usr/bin/env python3
"""Hold a repository to three things about the version it names.

CONTRIBUTING.md's release section owns what goes wrong when nothing asks. What is decided here is
which question is posed of which tree.

**Publication**, asked of the BASE: every version the CHANGELOG has closed is tagged. A dispatch
repairs it, so refusing merges is pressure toward one, and reading the base leaves the pull request
that closes a section free to merge — the one tree the state is allowed to be in.

**Consistency**, asked of the TREE A MERGE WOULD PRODUCE: package.json names a CHANGELOG section that
exists, carries a date, and has no dated section above it. A commit repairs each of these, so they
must fail whoever would introduce them. Read of the base too, they would leave the repair itself
unmergeable, with no direct push to main to escape through.

**Drain**, asked of BOTH: a release leaves `## [Unreleased — breaking]` the way the version it closes
requires. Neither tree answers that, because one file is right or wrong depending on the change that
produced it — a section still holding entries under a freshly closed major is a release that forgot to
move them, and under an older major it is the ordinary state of collecting breaks for the next one. So
the question is posed of the edit rather than of either tree's contents, and only of an edit that
closes a version: an entry leaves that section reclassified, reworded, or dropped as untrue, and a
change closing nothing is free to do any of those.

Run: python3 scripts/release/published_check.py \
       --base "$(git merge-base origin/main HEAD)" --result HEAD

The merge base rather than origin/main: an origin/main that has moved on charges this change with
breaking entries it never saw. The workflow reads the same pair from the event — the base commit it
names, against the merge commit it checks out.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

from release_notes import (
    BREAKING_SECTION,
    DEFAULT_CHANGELOG,
    DEFAULT_PACKAGE_JSON,
    ReleaseNotesError,
    VERSION_HEADING,
    extract_version_section,
    normalize,
    split_entries,
)

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


def released_versions(changelog_text):
    """Every version the CHANGELOG has closed, in file order."""
    return [version for version, line in version_headings(changelog_text)
            if RELEASE_DATE.search(line)]


def is_major_bump(released, version):
    """Whether `version` opens a new major against the release below it in `released`.

    The last of them has nothing below it and is nobody's bump.
    """
    below = released.index(version) + 1
    return below < len(released) and version.split(".")[0] != released[below].split(".")[0]


def section_entries(changelog_text, version):
    """The top-level entries under `## [version]`, or none where the CHANGELOG has no such section."""
    try:
        return split_entries(extract_version_section(changelog_text, version))
    except ReleaseNotesError:
        return []


def entries_missing_from(entries, elsewhere):
    """Those of `entries` that `elsewhere` does not also list, in the order `entries` gives."""
    present = {normalize(entry) for entry in elsewhere}
    return [entry for entry in entries if normalize(entry) not in present]


def counted(entries, noun="entry", plural="entries"):
    return f"{len(entries)} {noun if len(entries) == 1 else plural}"


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
                f"'## [{version}] - YYYY-MM-DD', which is what marks it released rather than open")

    # package.json is what upm.yml verifies a dispatch against, so a section closed above the one it
    # names is a release the dispatch cannot reach.
    above = [named for named, line in headings[:headings.index((version, own))]
             if RELEASE_DATE.search(line)]
    if above:
        return (f"the CHANGELOG has closed {', '.join(above)} above the {version} package.json "
                f"names: bump package.json to {above[0]} so the dispatch can reach it")

    return None


def drain_reason(base_changelog, result_changelog):
    """Why this change may not close the version it closes, or None.

    Read of the two trees rather than one, for the reason the module docstring gives. A change that
    closes nothing is nobody's release and is asked nothing here — that is what leaves an entry free
    to be reclassified, reworded or dropped on its own.

    A major's entries are compared by text, so a drain that reworded one on the way is refused too.
    Counting them instead permits the reword and still catches a drop, until the release writes an
    entry of its own and the counts agree again; and a count reads two entries merged into one
    exactly as it reads one of the two dropped. The refusal is loud and repaired by making the
    wording change in a change that closes no version; the miss is a break published in a major with
    nothing describing it.
    """
    before = released_versions(base_changelog)
    after = released_versions(result_changelog)
    closing = [named for named in after if named not in before]
    waiting_before = section_entries(base_changelog, BREAKING_SECTION)
    waiting_after = section_entries(result_changelog, BREAKING_SECTION)

    majors = [version for version in closing if is_major_bump(after, version)]
    for version in majors:
        if waiting_after:
            return (f"{version} is a major and '## [{BREAKING_SECTION}]' still lists "
                    f"{counted(waiting_after)}, starting with:\n"
                    f"  {waiting_after[0].splitlines()[0]}\n"
                    f"A major moves them into the section it closes and leaves the heading standing "
                    f"with none. The note is built from that section alone, so a break left here "
                    f"ships in {version} with nothing describing it.")

        lost = entries_missing_from(waiting_before, section_entries(result_changelog, version))
        if lost:
            return (f"{counted(lost)} left '## [{BREAKING_SECTION}]' and no entry of {version} "
                    f"carries that text, starting with:\n"
                    f"  {lost[0].splitlines()[0]}\n"
                    f"A major carries the section into the version it closes. This reading compares "
                    f"the entry's text, so it cannot tell one reworded on the way from one dropped "
                    f"on the way, and a dropped one ships the break in {version} with nothing "
                    f"describing it. Carry the entry across as it stands, and make any wording "
                    f"change in a change that closes no version.")

    # A minor closing beside a major is answerable for what that drain left, not for the drain.
    carried = [entry for version in majors
               for entry in section_entries(result_changelog, version)]
    edited = entries_missing_from(waiting_before, waiting_after + carried)
    others = [named for named in closing if named not in majors]
    if edited and others:
        return (f"{others[0]} is not a major, and this change does not leave "
                f"'## [{BREAKING_SECTION}]' as it found it: {counted(edited)} changed or "
                f"gone, starting with:\n"
                f"  {edited[0].splitlines()[0]}\n"
                f"A minor or a patch leaves that section alone. Close this as a major if it "
                f"ships the break, or make the edit in a change that closes no version.")

    return None


def publication_reason(changelog_text, package_json_text, tags):
    """Why nothing may be merged on top of this tree, or None.

    Asked of EVERY closed section rather than only the one package.json names, so a version that was
    skipped past cannot be forgotten: bumping to the next one would otherwise take the question off
    the one before it for good.

    Answers None for a tree consistency_reason already refuses: the version to ask about is exactly
    what is in doubt there, and that question is posed of the merge result instead.

    A remote carrying no release tag at all answers None too: there is no release history to have
    left a version out of, and naming a dispatch would be an instruction with nothing behind it.
    """
    if consistency_reason(changelog_text, package_json_text):
        return None

    if not any(RELEASE_TAG.match(tag) for tag in tags):
        return None

    unpublished = [version for version, line in version_headings(changelog_text)
                   if RELEASE_DATE.search(line) and f"v{version}" not in tags]
    if not unpublished:
        return None

    # The oldest, since version_headings runs newest-first: publishing a later version before an earlier
    # one leaves the upm branch force-pushed to the older package and the next note's compare range
    # running backwards.
    first = unpublished[-1]
    return (f"{', '.join('v' + version for version in unpublished)} closed in the CHANGELOG and "
            f"never published. Dispatch from the release commit's own tag rather than from the branch, "
            f"because anything merged since would otherwise ship inside it with the note describing "
            f"none of it:\n"
            f"  git tag release/{first} <the release commit>\n"
            f"  git push origin release/{first}\n"
            f"  gh workflow run upm.yml --ref release/{first} -f version={first}\n"
            f"The push is not optional: --ref is resolved on the server, and a tag that exists only "
            f"locally answers 422.")


def git(project, *args, timeout=5):
    """Run git, raising on failure and on a read that never returns.

    A caller that kills this instead cannot report anything: a killed hook exits neither 0 nor 2, and
    the stderr note unpublished_reason promises is never written. The workflow pays the same bound
    although nothing there is waiting on it, which is the trade: a slow ls-remote reddens a required
    check rather than passing an unread answer along.
    """
    result = subprocess.run(["git", "-C", str(project), *args],
                            capture_output=True, text=True, check=True, timeout=timeout)
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
    except (subprocess.CalledProcessError, subprocess.TimeoutExpired,
            json.JSONDecodeError, OSError) as failure:
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
               "holds an unpublished release", "no unpublished release is in the way")

    if args.result:
        report(args.result,
               consistency_reason(read_at(project, args.result, CHANGELOG_PATH),
                                  read_at(project, args.result, PACKAGE_JSON_PATH)),
               "could not be released as it stands", "package.json and the CHANGELOG agree")

    if args.base and args.result:
        report(f"{args.base}..{args.result}",
               drain_reason(read_at(project, args.base, CHANGELOG_PATH),
                            read_at(project, args.result, CHANGELOG_PATH)),
               "leaves the breaking section wrong for the version it closes",
               "the breaking section suits whatever this closes")

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())

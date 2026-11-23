#!/usr/bin/env python3
"""Print the version the next release has, and the reason, instead of leaving it to be chosen.

CONTRIBUTING.md's release section owns the rule; this is what applies it. Against the previous
release, a version is

- a **major** when `## [Unreleased — breaking]` holds an entry: an API a caller has to edit around;
- a **minor** when the public API gains a member, which is a line `PublicAPI.txt` has that the
  previous release's tag did not, or when `## [Unreleased]` carries an `### Added` entry;
- a **patch** otherwise, behaviour changes that leave the API as it was included.

Both open sections are read with their fragments filed in, as `published_check.changelog_at` reads
them, so a change that has not been compiled into the file yet is counted. The previous release is the
newest `vX.Y.Z-main` tag the revision descends from, which is the tag the UPM dispatch leaves on each
release's commit whichever line it was cut from.

`published_check.py` asks the same derivation of a change that closes a version, and refuses a
version it does not give. It judges only a change that closes a version from here on; the releases
before the breaking section existed are not held to it.

    python3 scripts/release/next_version.py [--rev HEAD]
"""

import argparse
import re
import subprocess
import sys
from collections import namedtuple
from pathlib import Path

import release_notes

PUBLIC_API_PATH = "Packages/com.velvet.core/PublicAPI.txt"
TAG_PREFIX = "v"
TAG_SUFFIX = "-main"
SEMVER = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")

# A member is its line without the spellings that vary between dump formats and say nothing about
# the member: the assembly it is filed under, a nullability annotation (`?` or `~`), and the names a
# generic type lists for its own parameters (`Ref`1[T]` against `Ref`1`). Parameter and return types
# are kept, so a changed signature is a member gained. Where a later change to the dump format yields
# a gain that is not one, this is what takes the new spelling, and
# `NormalisationTests` holds a pair per format variant seen.
ASSEMBLY_PREFIX = re.compile(r"^\[[^\]]+\] ")
NULLABILITY = re.compile(r"[?~]")
TYPE_PARAMETER_NAMES = re.compile(r"(?<=`\d)\[[A-Z]\w*(?:, [A-Z]\w*)*\]")

Derivation = namedtuple("Derivation", "bump version reason")


def api_members(public_api_text):
    """The members `PublicAPI.txt` lists, without the assembly each is filed under."""
    return {member(line) for line in public_api_text.splitlines() if line.strip()}


def member(line):
    """One `PublicAPI.txt` line as the identity of the member it lists."""
    line = NULLABILITY.sub("", ASSEMBLY_PREFIX.sub("", line))
    return TYPE_PARAMETER_NAMES.sub("", line)


def api_gained(before_text, after_text):
    """The members `after_text` lists that `before_text` does not, sorted."""
    return sorted(api_members(after_text) - api_members(before_text))


def added_entries(changelog_text, section):
    """The entries under `### Added` of one `## [section]`, or none where there is no such section."""
    try:
        lines = release_notes.extract_version_section(changelog_text, section)
    except release_notes.ReleaseNotesError:
        return []
    under_added, found = False, []
    for line in lines:
        heading = release_notes.SUBSECTION_HEADING.match(line)
        if heading:
            under_added = heading.group("title") == "Added"
        elif under_added:
            found.append(line)
    return release_notes.split_entries(found)


def parse_version(version):
    match = SEMVER.match(version)
    if not match:
        raise ValueError(f"{version!r} is not a MAJOR.MINOR.PATCH version")
    return tuple(int(part) for part in match.groups())


def bumped(previous, bump):
    major, minor, patch = parse_version(previous)
    return {"major": f"{major + 1}.0.0",
            "minor": f"{major}.{minor + 1}.0",
            "patch": f"{major}.{minor}.{patch + 1}"}[bump]


BUMPS = ("patch", "minor", "major")


def successors(previous):
    """The versions a release after `previous` may take, in the order `BUMPS` names them."""
    return [bumped(previous, bump) for bump in BUMPS]


def skipped_from(published, version):
    """The newest of the `published` versions below `version` where `version` is not one of its
    `successors`, and None where it is or nothing below it was published."""
    below = [named for named in published if parse_version(named) < parse_version(version)]
    previous = max(below, key=parse_version, default=None)
    return previous if previous is not None and version not in successors(previous) else None


def first_line(entry):
    return entry.splitlines()[0]


def derive(previous, breaking, gained, added):
    """The bump `previous` takes given the breaking entries, the API members gained and the `### Added`
    entries, with the version it makes and why."""
    if breaking:
        bump = "major"
        reason = (f"'## [{release_notes.BREAKING_SECTION}]' holds {len(breaking)} "
                  f"{'entry' if len(breaking) == 1 else 'entries'}, an API a caller has to edit "
                  f"around, starting with: {first_line(breaking[0])}")
    elif gained or added:
        bump = "minor"
        parts = []
        if gained:
            parts.append(f"PublicAPI.txt gains {len(gained)} "
                         f"{'line' if len(gained) == 1 else 'lines'}, starting with: {gained[0]}")
        if added:
            parts.append(f"'## [{release_notes.OPEN_SECTION}]' carries {len(added)} "
                         f"`### Added` {'entry' if len(added) == 1 else 'entries'}, starting "
                         f"with: {first_line(added[0])}")
        reason = "; ".join(parts)
    else:
        bump = "patch"
        reason = (f"'## [{release_notes.BREAKING_SECTION}]' is empty, PublicAPI.txt gains no line, "
                  f"and '## [{release_notes.OPEN_SECTION}]' carries no `### Added` entry")
    return Derivation(bump, bumped(previous, bump), reason)


def tag_of(version):
    return f"{TAG_PREFIX}{version}{TAG_SUFFIX}"


class Unreadable(Exception):
    """What a derivation needs and cannot read: refused rather than read as nothing gained."""


def release_versions(listing):
    names = (name[len(TAG_PREFIX):-len(TAG_SUFFIX)] for name in listing.split())
    return [name for name in names if SEMVER.match(name)]


def newest_reachable_release(project, rev, run_git):
    """The newest version whose `vX.Y.Z-main` tag `rev` descends from, or None.

    Raises `Unreadable` where the line carries a newer release tag that `rev` does not descend from:
    the diff would then be taken against a release the line has already moved past.
    """
    pattern = f"{TAG_PREFIX}*{TAG_SUFFIX}"
    reachable = release_versions(run_git(project, "tag", "--merged", rev, "--list", pattern))
    newest = max(reachable, key=parse_version, default=None)
    if newest is None:
        return None
    line = [version for version in release_versions(run_git(project, "tag", "--list", pattern))
            if parse_version(version)[0] == parse_version(newest)[0]]
    latest = max(line, key=parse_version)
    if parse_version(latest) > parse_version(newest):
        raise Unreadable(f"{rev} descends from {tag_of(newest)} but the line's newest release tag is "
                         f"{tag_of(latest)}, which it does not descend from: derive from a revision "
                         f"that has it")
    return newest


def public_api_gained_since(project, version, rev, read_at):
    """What `PublicAPI.txt` at `rev` gained against release `version`'s tag.

    Raises `Unreadable` naming the revision it could not read, since a gain it cannot see would
    otherwise read as none and let an addition through as a patch.
    """
    texts = []
    for ref in (tag_of(version), rev):
        try:
            texts.append(read_at(project, ref, PUBLIC_API_PATH))
        except subprocess.CalledProcessError as failure:
            raise Unreadable(f"{PUBLIC_API_PATH} cannot be read at {ref} "
                             f"(is the tag fetched?): {(failure.stderr or '').strip()}") from failure
    return api_gained(*texts)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--rev", default="HEAD", help="revision to derive the next version of")
    args = parser.parse_args(argv)

    import published_check
    project = Path(args.project).resolve()
    try:
        previous = newest_reachable_release(project, args.rev, published_check.git)
        if previous is None:
            raise Unreadable(f"{args.rev} descends from no {TAG_PREFIX}X.Y.Z{TAG_SUFFIX} tag, so "
                             f"there is no release to derive a version from "
                             f"(is the clone missing its tags?)")
        gained = public_api_gained_since(project, previous, args.rev, published_check.read_at)
    except Unreadable as failure:
        print(failure, file=sys.stderr)
        return 1

    changelog = published_check.changelog_at(project, args.rev)
    derivation = derive(
        previous,
        published_check.section_entries(changelog, release_notes.BREAKING_SECTION),
        gained,
        added_entries(changelog, release_notes.OPEN_SECTION))
    print(f"previous release: {previous} ({tag_of(previous)})")
    print(f"bump: {derivation.bump}")
    print(f"next version: {derivation.version}")
    print(f"reason: {derivation.reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

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


def first_line(entry):
    return entry.splitlines()[0]


def derive(previous, breaking, gained, added):
    """The bump `previous` takes given the breaking entries, the API members gained and the `### Added`
    entries, with the version it makes and why.

    `gained` is None where the previous release's `PublicAPI.txt` could not be read, which leaves the
    minor to the `### Added` entries alone and says so.
    """
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
        reason = (f"'## [{release_notes.BREAKING_SECTION}]' is empty, "
                  + ("PublicAPI.txt gains no line, " if gained is not None
                     else "the previous release's PublicAPI.txt could not be read, ")
                  + f"and '## [{release_notes.OPEN_SECTION}]' carries no `### Added` entry")
    return Derivation(bump, bumped(previous, bump), reason)


def tag_of(version):
    return f"{TAG_PREFIX}{version}{TAG_SUFFIX}"


def newest_reachable_release(project, rev, git):
    """The newest version whose `vX.Y.Z-main` tag `rev` descends from, or None."""
    listed = git(project, "tag", "--merged", rev, "--list", f"{TAG_PREFIX}*{TAG_SUFFIX}").split()
    versions = [name[len(TAG_PREFIX):-len(TAG_SUFFIX)] for name in listed]
    versions = [version for version in versions if SEMVER.match(version)]
    return max(versions, key=parse_version, default=None)


def public_api_gained_since(project, version, rev, read_at):
    """What `PublicAPI.txt` at `rev` gained against release `version`'s tag, or None where either
    cannot be read."""
    try:
        return api_gained(read_at(project, tag_of(version), PUBLIC_API_PATH),
                          read_at(project, rev, PUBLIC_API_PATH))
    except subprocess.CalledProcessError:
        return None


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    parser.add_argument("--rev", default="HEAD", help="revision to derive the next version of")
    args = parser.parse_args(argv)

    import published_check
    project = Path(args.project).resolve()
    previous = newest_reachable_release(project, args.rev, published_check.git)
    if previous is None:
        print(f"{args.rev} descends from no {TAG_PREFIX}X.Y.Z{TAG_SUFFIX} tag, so there is no "
              f"release to derive a version from (is the clone missing its tags?)", file=sys.stderr)
        return 1

    changelog = published_check.changelog_at(project, args.rev)
    derivation = derive(
        previous,
        published_check.section_entries(changelog, release_notes.BREAKING_SECTION),
        public_api_gained_since(project, previous, args.rev, published_check.read_at),
        added_entries(changelog, release_notes.OPEN_SECTION))
    print(f"previous release: {previous} ({tag_of(previous)})")
    print(f"bump: {derivation.bump}")
    print(f"next version: {derivation.version}")
    print(f"reason: {derivation.reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

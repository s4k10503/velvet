---
name: release
description: Cut a Velvet release — close the CHANGELOG, bump the package version, dispatch the UPM workflow, and verify the published release. Use whenever a release, a version bump, or a tag is asked for, and when a published release needs its notes checked or repaired.
---

# Releasing Velvet

The `UPM` workflow does the tagging, the `upm` branch split and the GitHub release. What it cannot
do is decide what the release contains, so the whole job here is getting `CHANGELOG.md` right and
then dispatching.

`Packages/com.velvet.core/CHANGELOG.md` is the single source of truth for the release note.
`scripts/release/release_notes.py` turns one version's section into the published body — Highlights, install
instructions, the long-form entries collapsed, the compare link. Nothing about a release is written
twice, and nothing is written straight into the GitHub release.

## 1. Close the version in the CHANGELOG

Rename the working section to `## [X.Y.Z] - YYYY-MM-DD` and give it a `### Highlights` block above
its `### Added` / `### Changed` / `### Fixed` headings.

**Highlights is what the release note leads with, and the release fails without it.** Five to nine
bullets, one short paragraph each, ordered by what a user notices first — a fix for something that
was silently broken outranks a new utility. Lead the last bullet with `**Breaking:**` and list every
breaking change in it. Write them fresh: a bullet copied verbatim from a long-form entry below fails
`scripts/release/test_release_notes.py`, because the note would then say the same thing twice.

Bump `version` in `Packages/com.velvet.core/package.json` to match. SemVer against the previous
release: a `feat` on `main` makes it a minor, a breaking change makes it a major.

Check the note before opening the pull request:

```bash
python3 scripts/release/test_release_notes.py && python3 scripts/release/release_notes.py --version X.Y.Z --repo s4k10503/velvet
```

## 2. Land it on main

Branch, pull request, squash merge — `main` and `upm` both refuse a direct push, whatever the change
is. Merging to `main` re-runs the split into the `upm` branch on its own; never edit `upm` by hand,
it is a generated mirror.

From this merge until step 3 runs, every other pull request is red and every merge path refuses:
`scripts/release/published_check.py` reads a closed-and-untagged version off the base. That is
deliberate. v2.0.1 was closed, merged and left undispatched while five more pull requests landed on
top of it, and the release had to be built by tagging the release commit by hand and dispatching
from the tag, because dispatching from the branch would have shipped five undescribed changes.

## 3. Dispatch

```bash
gh workflow run upm.yml -f version=X.Y.Z
```

This is the only step that publishes. It verifies the version against `package.json`, builds the
note, splits the package to `upm`, tags `vX.Y.Z` on the split commit and `vX.Y.Z-main` on the main
commit, and creates the release.

**Do not run `gh release create` yourself** — the dispatch has already made the tag, so it fails with
`tag already exists`, and the note it would attach is the one the workflow builds anyway.

Two tags exist per release because the `vX.Y.Z` tags are one-off snapshots of an amended split with
no ancestor relation to each other, so they cannot anchor a "since the last release" compare. The
`-main` pair does that.

## 4. Verify

```bash
gh release view vX.Y.Z --json body -q .body | head -20
```

A body of a few dozen characters means only the compare link landed — which is what the release
notes used to be, before this was automated. If a published release needs repairing, rebuild it from
the CHANGELOG rather than writing prose into the release:

```bash
python3 scripts/release/release_notes.py --version X.Y.Z --repo s4k10503/velvet \
  --compare-tag vX.Y.Z-main --previous-compare-tag vA.B.C-main --output /tmp/notes.md
gh release edit vX.Y.Z --notes-file /tmp/notes.md
```

Omit `--previous-compare-tag` for the first release; the footer then links the whole history instead
of a compare.

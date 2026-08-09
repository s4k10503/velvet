#!/usr/bin/env python3
"""Refuse an edit that files a CHANGELOG entry into a version section already released.

A released section is one whose heading carries a date. Its text is the published release note:
`scripts/release/release_notes.py` rebuilds a note from it on demand, so an entry filed there
claims a version that shipped without it, and is missing from the version that will ship with it.
Both halves are wrong and neither is visible from the diff, which shows a `### Added` line gaining
a bullet and says nothing about which heading is forty lines above it.

`test_release_notes.py` cannot catch it: a released section that gained a stray entry still has its
Highlights block and still builds a complete note. The note is simply false, and no check reads
truth.

The insertion point is what decides, so the check is here rather than in CI — by the time a branch
is pushed the entry has been written, reviewed and cited as evidence.

The reading is a released section's BULLET COUNT, before against the proposed text:

- Only growth is refused, so deleting from a released section and moving an entry out of one both
  still run.
- Bullets rather than lines, because a line count calls a rewrap an addition and misses an addition
  paid for by a rewrap. Rewording an entry keeps its bullet.
- Only versions dated on BOTH sides are compared. Closing `## [Unreleased]` into `## [X.Y.Z] - DATE`
  newly dates a whole section, which under a first-seen-is-growth reading refused every release —
  including through the advice this hook prints, which left no in-band way to clear it. A heading
  that stops being released is refused separately, since that is the same edit by another route.
"""

import json
import os
import re
import subprocess
import sys
from pathlib import Path

CHANGELOG = "CHANGELOG.md"
# Keep A Changelog's heading form, tolerating the run of spaces a hand edit leaves behind. The
# grammar is `scripts/release/release_notes.py`'s; this is the loosest form that still finds every
# heading that one accepts, so a heading it would parse cannot slip past unmatched.
HEADING = re.compile(r"^##\s+\[(?P<version>[^\]]+)\](?P<tail>.*)$", re.MULTILINE)
DATED = re.compile(r"^\s*-\s*\d{4}-\d{2}-\d{2}")
BULLET = re.compile(r"^\s*[-*+]\s+\S")


def bullets(text):
    """Bullet count of each dated section, by version."""
    marks = [(m.start(), m.group("version"), bool(DATED.match(m.group("tail"))))
             for m in HEADING.finditer(text)]
    counts = {}
    for index, (start, version, dated) in enumerate(marks):
        end = marks[index + 1][0] if index + 1 < len(marks) else len(text)
        if dated:
            counts[version] = sum(1 for line in text[start:end].splitlines() if BULLET.match(line))
    return counts


def common_git_dir(start):
    """The repository a path belongs to, identified by the dir every worktree of it shares."""
    try:
        found = subprocess.run(
            ["git", "-C", str(start), "rev-parse", "--path-format=absolute", "--git-common-dir"],
            capture_output=True, text=True, timeout=5)
    except (OSError, subprocess.SubprocessError):
        return None
    return found.stdout.strip() or None if found.returncode == 0 else None


def in_this_repository(path, cwd):
    """Whether `path` belongs to the repository the hook is running for.

    Containment under the project root is the wrong test and turned the guard off everywhere the
    work actually happens: this repository does its branch work in `git worktree` trees outside the
    project directory, so every CHANGELOG edit in one silently passed. A worktree shares its
    repository's common git dir, and a genuinely foreign checkout does not.
    """
    target = Path(path)
    here = target.parent if target.parent.exists() else Path(cwd or ".")
    mine = common_git_dir(os.environ.get("CLAUDE_PROJECT_DIR") or cwd or ".")
    return mine is not None and common_git_dir(here) == mine


# The verdict reads a tool input, never a shell word, so no operand of this guard can arrive
# unexpanded.
UNEXPANDED_POLICY = "n/a"


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    tool = event.get("tool_name")
    if tool not in ("Edit", "Write"):
        return 0
    payload = event.get("tool_input", {})
    path = payload.get("file_path", "")
    if Path(path).name != CHANGELOG:
        return 0
    if not in_this_repository(path, event.get("cwd")):
        return 0
    try:
        text = Path(path).read_text()
    except OSError:
        return 0

    if tool == "Write":
        proposed = payload.get("content", "")
    else:
        old = payload.get("old_string", "")
        if old not in text:
            return 0
        count = text.count(old) if payload.get("replace_all") else 1
        proposed = text.replace(old, payload.get("new_string", ""), count)

    before, after = bullets(text), bullets(proposed)
    versions = sorted(v for v, count in after.items() if v in before and count > before[v])
    # A released heading that stops being one carries whatever is under it out of the comparison,
    # so renaming the version or stripping the date is the same edit by another route.
    unmade = sorted(v for v in before if v not in after)
    if not versions and not unmade:
        return 0
    if unmade and not versions:
        print(f"Refusing this {CHANGELOG} edit: it stops {', '.join(unmade)} being a released "
              "section, by renaming its version or removing its date.\n\n"
              "A released heading is what pins its entries to the version that shipped them.",
              file=sys.stderr)
        return 2

    print("\n".join([
        f"Refusing this {CHANGELOG} edit: it adds an entry inside {', '.join(versions)}, "
        "a section a release date has closed.",
        "",
        "That section is the published release note. An entry there claims a version that shipped",
        "without it, and is missing from the version that will ship with it.",
        "",
        "Put it under `## [Unreleased]` — opening one above the newest release if there is none.",
    ]), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

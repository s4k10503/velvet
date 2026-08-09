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
  including through the advice this hook prints, which left no in-band way to clear it.
"""

import json
import os
import re
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


def in_this_repository(path, cwd):
    root = Path(os.environ.get("CLAUDE_PROJECT_DIR") or cwd or ".").resolve()
    try:
        Path(path).resolve().relative_to(root)
    except (ValueError, OSError):
        return False
    return True


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
    if not versions:
        return 0

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

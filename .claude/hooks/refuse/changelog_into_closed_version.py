#!/usr/bin/env python3
"""Refuse an edit that adds a CHANGELOG entry inside a version section already closed.

A closed section is one whose heading carries a release date. Its text is the published release
note: `scripts/release/release_notes.py` rebuilds a note from it on demand, so an entry filed
there claims a version that shipped without it, and is missing from the version that will ship
with it. Both halves are wrong and neither is visible from the diff, which shows a `### Added`
line gaining a bullet and says nothing about which heading is above it.

`test_release_notes.py` cannot catch it: every closed section still has its Highlights block and
still builds a complete note. The note is simply false.

The insertion point is what decides, so the check is here rather than in CI — by the time a
branch is pushed the entry has been written, reviewed and pointed at as evidence.

The reading is a section's line count, not its text: only growth is refused, so rewording a closed
entry, moving one out, and deleting one — the three ways this mistake gets undone — all still run.
"""

import json
import re
import sys
from pathlib import Path

# The verdict reads a tool input, never a shell word, so no operand of this guard can arrive
# unexpanded.
UNEXPANDED_POLICY = "n/a"

CHANGELOG = "CHANGELOG.md"
# Keep A Changelog's heading form. A section is closed once a date follows the version.
HEADING = re.compile(r"^## \[(?P<version>[^\]]+)\](?P<tail>.*)$", re.MULTILINE)
DATED = re.compile(r"^\s*-\s*\d{4}-\d{2}-\d{2}")


def closed_spans(text):
    """(start, end, version) character spans of every dated section, end-exclusive."""
    marks = []
    for match in HEADING.finditer(text):
        marks.append((match.start(), match.group("version"), bool(DATED.match(match.group("tail")))))
    spans = []
    for index, (start, version, dated) in enumerate(marks):
        end = marks[index + 1][0] if index + 1 < len(marks) else len(text)
        if dated:
            spans.append((start, end, version))
    return spans


def owner(spans, offset):
    for start, end, version in spans:
        if start <= offset < end:
            return version
    return None


def body_size(text):
    """Non-blank line count of each dated section, by version."""
    marks = [(m.start(), m.group("version"), bool(DATED.match(m.group("tail"))))
             for m in HEADING.finditer(text)]
    sizes = {}
    for index, (start, version, dated) in enumerate(marks):
        end = marks[index + 1][0] if index + 1 < len(marks) else len(text)
        if dated:
            sizes[version] = sum(1 for line in text[start:end].splitlines() if line.strip())
    return sizes


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

    # A section growing is what files a new entry; a reword leaves the count where it was, and
    # correcting a closed section is how a mistake like this gets undone.
    before, after = body_size(text), body_size(proposed)
    versions = sorted(v for v, size in after.items() if size > before.get(v, 0))
    if not versions:
        return 0

    unreleased = "## [Unreleased]" in proposed
    print("\n".join([
        f"Refusing this {CHANGELOG} edit: it adds an entry inside {', '.join(versions)}, "
        "a section a release date has closed.",
        "",
        "That section is the published release note. An entry there claims a version that shipped",
        "without it, and is missing from the version that will ship with it.",
        "",
        ("Put it under `## [Unreleased]`." if unreleased else
         "There is no `## [Unreleased]` section — open one above the newest release."),
    ]), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

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

The reading is a released section's LIST ITEMS, before against the proposed text:

- An item the section did not carry before is refused, whether it arrived beside the existing ones
  or in place of one. A count could not separate a 1-for-1 substitution from the reword it looks
  like, and allowed the substitution; comparing the items themselves refuses the reword with it,
  which is the accepted cost — the refusal says what a genuine reword should do instead. Items
  rather than lines, each collapsed to a single line so a rewrap matches itself: the shape
  `test_release_notes.py` compares two spellings of a note with.
- Removal is not refused, so deleting an entry from a released section and moving one out of it
  both still run. Deleting the whole SECTION does not, because the heading goes with it — see
  below.
- Only versions dated on BOTH sides are compared. Closing `## [Unreleased]` into `## [X.Y.Z] - DATE`
  newly dates a whole section, which under a first-seen-is-growth reading refused every release —
  including through the advice this hook prints, which left no in-band way to clear it. A heading
  that stops being released is refused separately, since that is the same edit by another route.
- A version heading written a second time is refused on its own, without reading either section.
  `extract_version_section` stops at the first heading matching the version and ends at the next
  `## [`, so a second heading placed above the real one is the whole published note for that
  version, and it need carry no bullets at all to be one.
"""

import json
import os
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
import repository

HOOK_TOOLS = {"Edit", "Write"}

CHANGELOG = "CHANGELOG.md"
# Keep A Changelog's heading form, tolerating the run of spaces a hand edit leaves behind. The
# grammar is `scripts/release/release_notes.py`'s; this is the loosest form that still finds every
# heading that one accepts, so a heading it would parse cannot slip past unmatched.
HEADING = re.compile(r"^##\s+\[(?P<version>[^\]]+)\](?P<tail>.*)$", re.MULTILINE)
DATED = re.compile(r"^\s*-\s*\d{4}-\d{2}-\d{2}")
ITEM_BREAK = re.compile(r"\n(?=[-*+] )")
ITEM_START = re.compile(r"^[-*+]\s+\S")


def sections(text):
    """Each version heading in order, with whether it is dated and the list items under it."""
    marks = [(m.start(), m.group("version"), bool(DATED.match(m.group("tail"))))
             for m in HEADING.finditer(text)]
    for index, (start, version, dated) in enumerate(marks):
        end = marks[index + 1][0] if index + 1 < len(marks) else len(text)
        yield version, dated, items(text[start:end])


def items(block):
    """Top-level list items of one section, each collapsed to a line so a rewrap matches itself.

    A nested item stays inside its parent rather than counting on its own, so adding one changes
    the parent it was added under.
    """
    return [" ".join(item.split())
            for item in ITEM_BREAK.split(block) if ITEM_START.match(item)]


def released(text):
    """Items of each released version, counted by their collapsed text."""
    counted = {}
    for version, dated, listed in sections(text):
        if dated:
            counted.setdefault(version, Counter()).update(listed)
    return counted


def repeated(text):
    """Versions carrying more than one heading."""
    written = Counter(version for version, _, _ in sections(text))
    return {version for version, count in written.items() if count > 1}


def common_git_dir(start):
    """The repository a path belongs to, identified by the dir every worktree of it shares."""
    found = repository.git(
        ["-C", str(start), "rev-parse", "--path-format=absolute", "--git-common-dir"],
        cwd=None, timeout=5)
    return found.strip() or None if found is not None else None


def in_scope(path, cwd):
    """Whether the check runs for `path` — because it is in this repository, or because git is not
    answering and there is nothing left to place it with.

    Containment under the project root is the wrong test and turned the guard off everywhere the
    work actually happens: this repository does its branch work in `git worktree` trees outside the
    project directory, so every CHANGELOG edit in one silently passed. A worktree shares its
    repository's common git dir, and a genuinely foreign checkout does not.

    An unreadable project dir means the scoping question has no answer, not that the answer is no.
    `lib/repository` returns None for a git that is absent, one that timed out and one that failed,
    and for the stake it names there the check runs on its own reading rather than standing down: a
    guard that says nothing is indistinguishable from one that looked.

    Only that half. An unreadable TARGET stands down, because None is also git's ordinary answer for
    a path in no repository — the same value for a real "somewhere else" and for a reading that
    failed. Failing closed on it too would leave nothing out of scope but a path git positively
    names another repository for, and every CHANGELOG.md outside one would be this guard's to refuse.
    """
    mine = common_git_dir(os.environ.get("CLAUDE_PROJECT_DIR") or cwd or ".")
    if mine is None:
        return True
    target = Path(path)
    here = target.parent if target.parent.exists() else Path(cwd or ".")
    return common_git_dir(here) == mine


# The verdict reads a tool input, never a shell word, so no operand of this guard can arrive
# unexpanded.
UNEXPANDED_POLICY = "n/a"


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    tool = event["tool_name"]
    payload = event.get("tool_input", {})
    path = payload.get("file_path", "")
    if Path(path).name != CHANGELOG:
        return 0
    if not in_scope(path, event.get("cwd")):
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

    # Only a duplicate the edit introduces, so a file that already carries one can still be
    # repaired — the same in-band route the dated-on-both-sides reading exists to keep open.
    split = sorted(repeated(proposed) - repeated(text))
    if split:
        print(f"Refusing this {CHANGELOG} edit: it writes a second `## [{split[0]}]` heading.\n\n"
              "A note is rebuilt from the first heading matching its version down to the next "
              "heading, so only one of the two sections is ever published and their order is what "
              "decides which.\n\n"
              "One heading per version. Put the entry under `## [Unreleased]`.", file=sys.stderr)
        return 2

    before, after = released(text), released(proposed)
    versions = sorted(v for v, listed in after.items() if v in before and listed - before[v])
    # A released heading that stops being one carries whatever is under it out of the comparison,
    # so renaming the version, stripping the date and deleting the section outright are one edit
    # reached three ways.
    unmade = sorted(v for v in before if v not in after)
    if unmade:
        print(f"Refusing this {CHANGELOG} edit: {', '.join(unmade)} comes out of it with no "
              "released section.\n\n"
              "A date on the heading is what says the entries under it shipped, and this check "
              "reads for it: whatever sits under an undated heading is not compared at all.\n\n"
              "Leave the heading and its date where they are, and put the change under "
              "`## [Unreleased]`.", file=sys.stderr)
        return 2
    if not versions:
        return 0

    print("\n".join([
        f"Refusing this {CHANGELOG} edit: {', '.join(versions)} comes out of it carrying an entry "
        "it does not carry now, and a release date has closed that section.",
        "",
        "That section is the published release note. An entry there claims a version that shipped",
        "without it, and is missing from the version that will ship with it.",
        "",
        "Put it under `## [Unreleased]` — opening one above the newest release if there is none.",
        "A reword of what that section already says reads the same way here and is refused with it;",
        "it changes a published note, so ask for it rather than making it.",
    ]), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

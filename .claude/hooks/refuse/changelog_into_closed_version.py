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

The reading is a released section's PUBLISHED LINES, before against the proposed text:

- Every non-blank line of the section belongs to a unit — its own, or the one it is a soft wrap of
  — so the units cover the whole of what would be published. Counting only the top-level list items
  left the rest uncovered, and an indented bullet, or a whole `### …` block of them, filed into a
  released section was allowed. Whatever the reading does not cover, the guard allows, and nothing
  in a diff says which part that is.
- A line the section did not carry before is refused, whether it arrived beside the existing ones
  or in place of one. A count could not separate a 1-for-1 substitution from the reword it looks
  like, and allowed the substitution; comparing the text itself refuses the reword with it, which
  is the accepted cost — the refusal says what a genuine reword should do instead. A reflowed line
  matches itself, because the join that unwraps it is the publisher's own.
- A released section whose version the remote tags — `vX.Y.Z-main`, whichever line published it
  — is refused every change to it but two: putting back a line that copy has and the section is
  short of, where the copy has it and with nothing else lost or moved, and bringing the section in
  as that copy entire, that copy being the only text of it here and an addition to a note already
  published belonging in the release that follows it. Removal, reword, reorder and a changed date
  alike, since the note is the tag's and the file cannot tell a correction from a deletion. For a
  section the file already carries it is not equality with the tag's copy, because main's older
  sections already differ from theirs, each of those carrying a Highlights block the copy has not
  got. The remote's tags rather than the checkout's, for the reason
  `published_check.remote_tag_shas` gives. Where the remote tags no such version, or the remote or
  git does not answer, the reading is the one above: growth is refused and removal is not,
  so deleting an entry from such a section still runs. Deleting the whole SECTION does not either
  way, because the heading goes with it — see below.
- What the edit is measured against is the FILE, where the merge-time reading has the base commit,
  so undoing a put-back made in the editor is refused as the deletion it looks like here. Reverting
  it with git is the way back, as with the heading below; a base to compare against is what this
  has not got, and the merge-time reading is what decides the change either way.
- Only versions dated on BOTH sides are compared. Closing `## [Unreleased]` into `## [X.Y.Z] - DATE`
  newly dates a whole section, which under a first-seen-is-growth reading refused every release —
  including through the advice this hook prints, which left no in-band way to clear it. A heading
  that stops being released is refused separately, since that is the same edit by another route.
- A version heading written a second time is refused on its own, without reading either section.
  `extract_version_section` stops at the first heading matching the version and ends at the next
  `## [`, so a second heading placed above the real one is the whole published note for that
  version, and it need carry no bullets at all to be one.

A Bash command that writes the file is refused whatever it would have written, and that is the one
verdict here reached without a comparison. The reading above is before-against-proposed, and no
shell command reaching this presents proposed text: a `sed -i` states a substitution rather than a
result, and a `cp` source and a heredoc body are text this does not read — reading either would be a
second grammar beside the one below, and neither has been posed the cases the one below has. So a
reword of a released section and an entry added under `## [Unreleased]` arrive alike, and letting
both through is what left this guard registered on two tools while the work went round it. The
refusal names the editing tools instead, which present the text and get the reading.
`lib/tracked_writes.py` owns which shapes are read and how narrow that is.
"""

import json
import os
import subprocess
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))
import repository
import tracked_writes

# The section this guard compares is the one `release_notes.py` publishes, so it is delimited and
# unwrapped by that module -- through the reading `breaking_in_flight_check.py` shares with the
# merge-time check -- rather than parsed a second time here. A heading only one of two grammars
# recognises moves text across a version boundary for that one and not the other, and a disagreement
# in either direction can leave a released section's text uncompared and still published. Soundness
# would need the two to agree exactly, and nothing would hold them to it.
sys.path.insert(0, str(Path(__file__).resolve().parents[3] / "scripts" / "release"))
import breaking_in_flight_check as drain
import published_check

HOOK_TOOLS = {"Bash", "Edit", "Write"}

CHANGELOG = "CHANGELOG.md"


def released(text):
    """Published lines of each released version, heading included, counted by their collapsed text."""
    counted = {}
    for version, dated, heading, listed in drain.published_sections(text):
        if dated:
            counted.setdefault(version, Counter()).update([heading] + listed)
    return counted


def repeated(text):
    """Versions carrying more than one heading."""
    written = Counter(version for version, _, _, _ in drain.published_sections(text))
    return {version for version, count in written.items() if count > 1}


def published_copies(path, versions):
    """Those of `versions` the remote tags, each with the tag naming its release, the commit the
    remote has it at and that tag's copy of the section, as {version: (tag, sha, lines)}, and the
    path `git show` reads the file by.

    A version the remote does not tag is unpublished and absent here. So is one whose copy this
    checkout cannot show, and every one where the remote could not be listed: the guard then reads
    as it did before the repository had a release, and the merge-time check refuses what it let by.
    """
    where = Path(path).parent
    prefix, _ = drain.run(["git", "rev-parse", "--show-prefix"], cwd=where)
    if prefix is None or not versions:
        return {}, None
    tracked = prefix.strip() + Path(path).name
    try:
        tagged = published_check.remote_tag_shas(where)
    except (OSError, subprocess.SubprocessError):
        return {}, tracked
    copies = {}
    for version in versions:
        try:
            found = drain.published_copy(
                version, tagged,
                lambda revision: drain.run(["git", "show", f"{revision}:{tracked}"], cwd=where))
        except drain.UnreadableRelease:
            continue
        if found is not None:
            copies[version] = (found[0], tagged[found[0]], found[1])
    return copies, tracked


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


# A shell operand this cannot place is not a file it can name, so it drops out of the reading and the
# command runs. That is the under-approximation `tracked_writes.LIMITS` states.
UNEXPANDED_POLICY = "allow"
UNEXPANDED_PROBE = "sed -i '' -e s/a/b/ \"$CHANGELOG\""

# Which file a command writes is git's answer, and one it cannot give leaves this unable to tell a
# CHANGELOG write from any other, so it counts as one.
#
# One payload, posed once per tool this is routed. The Edit half's anchor is a released heading,
# which is the one line this guard's whole premise says nobody edits.
UNREADABLE_POLICY = "refuse"
UNREADABLE_PROBE = {
    "command": "sed -i '' -e s/a/b/ Packages/com.velvet.core/CHANGELOG.md",
    "file_path": "Packages/com.velvet.core/CHANGELOG.md",
    "old_string": "## [2.0.0] - 2026-08-02\n",
    "new_string": "## [2.0.0] - 2026-08-02\n\n- an entry nobody released\n",
}


def shell_write(command, cwd):
    """The verdict on a Bash command, which is a refusal wherever it writes a CHANGELOG at all."""
    written = [path for path in tracked_writes.tracked_writes(command, cwd)
               if Path(path).name == CHANGELOG and in_scope(path, cwd)]
    if not written:
        return 0
    print("\n".join([
        f"Refusing this command: it writes {', '.join(sorted(written))}, and what it would write is "
        "not something this reads.",
        "",
        "A released section of that file is a published release note. This check compares the "
        "section before against the section proposed, and a shell command presents no proposed text "
        "to it — a substitution is not a result, and a copied source and a heredoc body are text "
        "this does not read. So an entry filed into a released section and one filed under "
        "`## [Unreleased]` arrive alike.",
        "",
        "Make the change with Edit or Write, which present the text and get that comparison.",
        "",
        tracked_writes.LIMITS,
    ]), file=sys.stderr)
    return 2


def main():
    try:
        event = json.load(sys.stdin)
    except Exception:
        return 0
    if event.get("tool_name") not in HOOK_TOOLS:
        return 0
    tool = event["tool_name"]
    payload = event.get("tool_input", {})
    if tool == "Bash":
        return shell_write(payload.get("command", ""), event.get("cwd"))
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
              "One heading per version. Put the entry under `## [Unreleased]` or "
              "`## [Unreleased — breaking]`.", file=sys.stderr)
        return 2

    before, after = released(text), released(proposed)
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
              "`## [Unreleased]`, or `## [Unreleased — breaking]` where it has to wait for a "
              "major.\n\n"
              "Undoing a rename that closed a version too early arrives here as well, and nothing "
              "in the file tells that date from a published one. Revert it with git, which this "
              "check never reads.", file=sys.stderr)
        return 2

    was, becomes = drain.dated_sections(text), drain.dated_sections(proposed)
    moved = [v for v in becomes if becomes[v] != was.get(v)]
    copies, tracked = published_copies(path, moved)
    # A section the edit brings in -- a maintenance line's, carried forward -- arrives as its tag's
    # copy; one already here is held to what it was, but for a line of that copy put back in place.
    restored = {v for v, (tag, sha, copy) in copies.items()
                if (drain.only_put_back(becomes[v], was[v], copy) if v in was
                    else becomes[v] == copy)}
    held = [v for v in moved if v in copies and v not in restored]
    if held:
        tag, sha, _ = copies[held[0]]
        print(f"Refusing this {CHANGELOG} edit: it changes `## [{held[0]}]`, and {tag} on the "
              "remote is the release that shipped that section.\n\n"
              "A dated section is the note its release shipped. Nothing in the file separates a "
              "correction from a deletion, so neither is made past the tag; an edit that only puts "
              "back a line that copy has and the section is short of, where the copy has it — a "
              "heading in the same edit as an entry it heads, since the section may not come out "
              "of the edit with a heading newly standing over nothing, the put-back's own or one "
              "it empties — or brings the section in as that copy entire, is what this lets "
              "through — an addition to a note already published belongs in the release "
              "that follows it:\n\n"
              f"  git show {sha}:{tracked}   # {tag} on the remote\n\n"
              "Put what this change has to say under `## [Unreleased]`, or "
              "`## [Unreleased — breaking]` where it has to wait for a major.\n\n"
              "Undoing a put-back made in the editor arrives here as that deletion, because this "
              "reads the file rather than a base commit. Revert it with git, which this check "
              "never reads.", file=sys.stderr)
        return 2
    versions = sorted(v for v, listed in after.items()
                      if v in before and listed - before[v] and v not in restored)
    if not versions:
        return 0

    print("\n".join([
        f"Refusing this {CHANGELOG} edit: {', '.join(versions)} comes out of it carrying a line "
        "it does not carry now, and a release date has closed that section.",
        "",
        "That section is the published release note. An entry there claims a version that shipped",
        "without it, and is missing from the version that will ship with it.",
        "",
        "Put it under `## [Unreleased]`, or `## [Unreleased — breaking]` where it has to wait for a",
        "major — opening the one it belongs in above the newest release if there is none.",
        "A reword of what that section already says reads the same way here and is refused with it;",
        "it changes a published note, so ask for it rather than making it.",
        "",
        "Closing a release is the one case where the line does belong there, and then the rename ran",
        "too early. Undoing it in the editor runs into this check again, because a heading that stops",
        "being released is refused whatever route reaches it. Revert the rename with git, which this",
        "check never reads, then close the version again in the order CONTRIBUTING.md's release",
        "section gives: everything written into `## [Unreleased]` first, the rename last.",
    ]), file=sys.stderr)
    return 2


if __name__ == "__main__":
    sys.exit(main())

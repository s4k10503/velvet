#!/usr/bin/env python3
"""Report what is untracked in the session's own checkout, into the parent's context.

Two failures this guards. A read-only agent creating a probe fixture and not removing it —
self-reported cleanup has been wrong before, and the leftover reads as production code to the next
session. And a source file .gitignore excludes, which stays untracked while every local run passes
because the file is there.

Coverage stops at that one checkout: the repository holding the session's cwd. A tree a subagent was
handed some other way is read here only where the session's cwd sits inside it, so a leftover there
is not reported and a silence here says nothing about it. Scanning every tree `git worktree
list` names was rejected: that listing says which trees exist and not which one the stopping
subagent worked in, so a sibling's in-flight files would go to whichever agent stopped. That is the
loop this hook already caused once, reporting the MAIN checkout's untracked files to an agent in a
worktree: it was told about files it had not created, could not clear them without destroying
another agent's work, and was told again on every stop because nothing it could do changed the
condition. Three agents hit it in one session, and one was instructed to delete the three source
files of an open PR. The wording below carries the same lesson: this is a report, and whoever owns
a tree decides what is left in it.

The same loop remains for a file in the reported tree that predates the run, so the untracked
listing is narrowed by when this subagent started. The bound is a time, not an author — a sibling
writing into this tree while this agent runs falls inside it — and the session's start would not
serve, since a file written after a session began stays inside that bound for every subagent it
later spawns. What is read is the modification time, so a file that arrives already carrying an
older one is dated by where it came from and not by when it appeared here.

The gitignored-source listing is deliberately outside that narrowing. What makes it worth reporting
is that the file STAYS excluded while local runs stay green, and an age bound would name it while it
was new and never again. Exempting the untracked half the same way, for a source under `Assets/` or
`Packages/`, was tried and rejected: an open branch's uncommitted work is untracked source under
those roots too and the exemption has no reading that declines it, so it reached the loop above
through a second door.

What the bound withholds is a leftover this hook says nothing about, not a tree it reports clean.
"""

import datetime
import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))

from repository import git, toplevel  # noqa: E402

LISTED = 20
SOURCE_SUFFIXES = re.compile(r"\.(cs|uss|uxml|asmdef|csproj|sln|md)$")
BUILD_DIRECTORIES = re.compile(r"^(Library|Temp|obj|Logs)/")
ESCAPE = re.compile(rb"\\([abfnrtv\"\\]|[0-7]{3})")
CONTROLS = {b"a": b"\a", b"b": b"\b", b"f": b"\f", b"n": b"\n", b"r": b"\r", b"t": b"\t",
            b"v": b"\v"}


def payload():
    try:
        return json.loads(sys.stdin.read() or "{}")
    except (ValueError, OSError):
        return {}


def session_tree(record):
    """The repository root holding the session's cwd, or None where that is not a repository.

    Rooted rather than taken as handed, so the age bound below reaches the files the listing names.
    `Given_ACwdBelowTheRepositoryRoot_When_TheReportIsTaken_Then_TheBoundStillApplies` is what fails
    when it stops.
    """
    start = Path(record.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR", "."))
    return toplevel(start) if start.is_dir() else None


def agent_start(transcript):
    """When this subagent's own transcript opened, or None where the reading did not answer.

    None leaves the listing unnarrowed, which is the side this belongs on: a bound nobody could
    read is not a bound saying nothing was left.
    """
    if not isinstance(transcript, str) or not transcript:
        return None
    try:
        with open(transcript, encoding="utf-8") as handle:
            first = json.loads(handle.readline() or "{}")
        stamp = first.get("timestamp") or ""
        return datetime.datetime.fromisoformat(stamp.replace("Z", "+00:00")).timestamp()
    except (AttributeError, OSError, ValueError):
        return None


def written_since(path, moment):
    """Whether a path was last written no earlier than `moment`.

    No moment to compare against, and a path whose age will not read, both count as written. That
    is the side `agent_start` gives its own failures.
    """
    if moment is None:
        return True
    try:
        return path.lstat().st_mtime >= moment
    except OSError:
        return True


def unquoted(listed):
    """The file a porcelain listing's spelling names.

    Porcelain answers a path holding a space, a quote, a backslash or a control character with a
    C-quoted spelling, and that spelling is not the path's own: an age taken from it is not the
    file's, and a suffix read off its end is the closing quote. A non-ASCII byte is escaped into
    that spelling too, unless `core.quotePath` is off — then it arrives as itself, inside whatever
    quoting the rest of the name forced. So the unescaping runs over the encoded bytes rather than
    over the decoded characters;
    `Given_ARawNonASCIIByteInsideTheQuotes_When_TheReportIsTaken_Then_TheBoundReachesTheFile` is
    what fails when it stops.

    Asking git for `-z` output instead was rejected: it answers in NUL-separated fields rather than
    lines, and a rename's second field is a bare path a marker filter would take for an entry of its
    own. `Given_ARenameWhoseOldNameOpensWithTheMarker_When_TheReportIsTaken_Then_NoPhantomIsNamed`
    is what fails when the form changes.
    """
    if not (listed.startswith('"') and listed.endswith('"') and len(listed) > 1):
        return listed

    def byte(hit):
        found = hit.group(1)
        return bytes([int(found, 8)]) if len(found) == 3 else CONTROLS.get(found, found)

    return ESCAPE.sub(byte, listed[1:-1].encode("utf-8", "surrogateescape")).decode(
        "utf-8", "surrogateescape")


def entries(status, marker):
    """The files a porcelain status marked, with the marker dropped and the spelling read back."""
    return [unquoted(line[3:]) for line in (status or "").splitlines()
            if line.startswith(marker)]


def displayed(path):
    """How a path is spelled inside the block, whose entries a reader separates by line.

    A path carrying a line break of its own does not survive that separation, so it is spelled
    quoted and escaped instead.
    """
    return path if path.splitlines() == [path] else json.dumps(path)


def shown(listing, total):
    """The truncated listing, told how much it is hiding."""
    text = "\n".join(displayed(path) for path in listing)
    return text if total <= len(listing) else f"{text}\n... and {total - len(listing)} more"


def main():
    record = payload()
    tree = session_tree(record)
    if tree is None:
        return 0
    started = agent_start(record.get("agent_transcript_path"))

    # Counted before the truncation, not after: the headline read "20 untracked" for a tree holding
    # thirty-seven, and a reader who clears the twenty believes they are done.
    untracked = [path for path in
                 entries(git(["status", "--porcelain", "--untracked-files=all"], tree), "??")
                 if not path.startswith("Library/") and written_since(tree / path, started)]

    # An ignored source file is the dangerous case; build output is ignored on purpose.
    ignored = [path for path in
               entries(git(["status", "--porcelain", "--ignored=matching",
                            "--untracked-files=all"], tree), "!!")
               if SOURCE_SUFFIXES.search(path) and not BUILD_DIRECTORIES.match(path)]

    if not untracked and not ignored:
        return 0

    since = "" if started is None else " since this subagent started"
    scope = "" if started is None else (
        " The listing is bounded by when this subagent started; the tree may hold more.")

    headline, parts = [], []
    if untracked:
        headline.append(f"{len(untracked)} untracked{since}")
        parts.append(
            f"Untracked files remain in {tree}.{scope} Read each before deciding it is harmless — a "
            "probe fixture left behind reads as production code to the next session, and an agent's "
            "own report that it cleaned up has been wrong before. What stays is for whoever owns "
            "this tree to decide; nothing here asks for a deletion:\n"
            + shown(untracked[:LISTED], len(untracked))
        )
    if ignored:
        headline.append(f"{len(ignored)} gitignored source")
        parts.append(
            "Source files here are EXCLUDED by .gitignore, so they cannot reach CI while every "
            "local run stays green because the files are present locally:\n"
            + shown(ignored[:LISTED], len(ignored))
        )

    print(json.dumps({
        # A reader in another worktree of this repository has to be able to tell at a glance
        # whether the report is about the tree they are working in, so the headline names the one
        # read.
        "systemMessage": f"files remain in {tree} — " + ", ".join(headline),
        "hookSpecificOutput": {
            "hookEventName": "SubagentStop",
            "additionalContext": "\n\n".join(parts),
        },
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())

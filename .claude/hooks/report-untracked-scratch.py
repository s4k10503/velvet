#!/usr/bin/env python3
"""Report what a subagent left in the tree IT worked in, into the parent's context.

Two failures this guards. A read-only agent creating a probe fixture and not removing it —
self-reported cleanup has been wrong before, and the leftover reads as production code to the next
session. And a source file .gitignore excludes, which stays untracked while every local run passes
because the file is there.

The tree comes from the session's own cwd, not from CLAUDE_PROJECT_DIR. An agent running in a git
worktree has its own checkout, and reporting the MAIN checkout's untracked files to it produced a
non-terminating loop: it was told about files it had not created, could not clear them without
destroying another agent's in-flight work, and was told again on every stop because nothing it could
do changed the condition. Three agents hit it in one session, and one was instructed to delete the
three source files of an open PR. The wording below carries the same lesson: this is a report, and
whoever owns a tree decides what is left in it.
"""

import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent / "lib"))

from repository import git  # noqa: E402

LISTED = 20
SOURCE_SUFFIXES = re.compile(r"\.(cs|uss|uxml|asmdef|csproj|sln|md)$")
BUILD_DIRECTORIES = re.compile(r"^(Library|Temp|obj|Logs)/")


def session_tree():
    try:
        payload = json.loads(sys.stdin.read() or "{}")
        declared = payload.get("cwd") or ""
    except (ValueError, OSError):
        declared = ""
    return Path(declared or os.environ.get("CLAUDE_PROJECT_DIR", "."))


def entries(status, marker):
    """The paths a porcelain status marked, with the two-character marker and its space removed."""
    return [line[3:] for line in (status or "").splitlines() if line.startswith(marker)]


def shown(listing, total):
    """The truncated listing, told how much it is hiding."""
    text = "\n".join(listing)
    return text if total <= len(listing) else f"{text}\n... and {total - len(listing)} more"


def main():
    tree = session_tree()
    if not tree.is_dir() or git(["rev-parse", "--git-dir"], cwd=tree) is None:
        return 0

    # Counted before the truncation, not after: the headline read "20 untracked" for a tree holding
    # thirty-seven, and a reader who clears the twenty believes they are done.
    untracked = [path for path in
                 entries(git(["status", "--porcelain", "--untracked-files=all"], tree), "??")
                 if not path.startswith("Library/")]

    # An ignored source file is the dangerous case; build output is ignored on purpose.
    ignored = [path for path in
               entries(git(["status", "--porcelain", "--ignored=matching",
                            "--untracked-files=all"], tree), "!!")
               if SOURCE_SUFFIXES.search(path) and not BUILD_DIRECTORIES.match(path)]

    if not untracked and not ignored:
        return 0

    headline, parts = [], []
    if untracked:
        headline.append(f"{len(untracked)} untracked")
        parts.append(
            f"Untracked files remain in {tree}. Read each before deciding it is harmless — a probe "
            "fixture left behind reads as production code to the next session, and an agent's own "
            "report that it cleaned up has been wrong before. What stays is for whoever owns this "
            "tree to decide; nothing here asks for a deletion:\n"
            + shown(untracked[:LISTED], len(untracked))
        )
    if ignored:
        headline.append(f"{len(ignored)} gitignored source")
        parts.append(
            "Source files here are EXCLUDED by .gitignore, so they cannot reach CI while every "
            "local run stays green because the files are present locally:\n"
            + shown(ignored[:LISTED], len(ignored))
        )

    # systemMessage is the transcript channel; additionalContext is the model's and does not display.
    print(json.dumps({
        "systemMessage": "subagent left files behind — " + ", ".join(headline),
        "hookSpecificOutput": {
            "hookEventName": "SubagentStop",
            "additionalContext": "\n\n".join(parts),
        },
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())

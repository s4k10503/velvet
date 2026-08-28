#!/usr/bin/env python3
"""Report what a maintenance line is holding that nothing else watches.

Two readings, one fetch. A line's `## [Unreleased]` holding entries is a backport nobody shipped; a
commit that originated on the line and is not on `main` is a fix `main` will reproduce the moment a
branch is cut from it.

## The unreleased entries

`main` accumulating unreleased entries is the ordinary state between releases, so it is not read
here. A maintenance line is different: it receives backports, and a backport exists to be shipped,
so an entry sitting in its `## [Unreleased]` is work that was cut for a release nobody cut.

Nothing else says it. `published_check.py` asks whether a version was closed and left unpublished,
which is the opposite state — the entries here belong to no version yet, so that reading is silent
and correct while the work waits.

Measured before this was written: across the life of `2.x`, its `## [Unreleased]` has held entries
in exactly one window — 2026-08-23 to 2026-08-27 — and no session noticed until the line was asked
about directly. v2.1.1 and v2.1.2 never passed through a non-empty section.

## The line main has not taken

A fix made on the maintenance line has to reach `main`, or a branch cut from `main` reproduces what it
fixed. The general practice is to merge the line forward rather than pick each commit back, because
then git records the ancestry and the question has an answer nothing has to interpret:

    git merge-base --is-ancestor origin/<line> origin/main

#777 tried two per-commit readings and measured both failing — an `-x` trailer is stripped by a squash
merge, and reverse-applying a diff fails on every backport release because `main` has moved since. A
third, reading what each pull request cites, works and is prose. This one is git's own.

Measured when it was written: `main` did not contain `2.x` and had never merged it, so six commits and
three whole release sections were outside it. `main`'s CHANGELOG recorded none of 2.1.1, 2.1.2 or
2.1.3.

Exit 0 always.
"""

import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent / "lib"))

from repository import project_tree  # noqa: E402

CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"

# The repository's own naming, which CONTRIBUTING.md's maintenance-line section owns. A branch that
# does not match is not reported on rather than guessed at: a feature branch holds unreleased entries
# by design, and reporting those would bury the one case this exists for.
LINE = re.compile(r"^origin/([0-9]+)\.x$")

# The hook shares SessionStart's budget with the reports beside it, so the fetch is bounded and what
# follows reads refs already on disk.
FETCH_TIMEOUT = 5
READ_TIMEOUT = 3
# The API reads are per commit rather than per line, so they are the ones that can grow. A line
# normally carries a handful; a session-start report that outruns its budget prints nothing at all.
GH_TIMEOUT = 4

RELEASE = re.compile(r"^chore\(velvet\): release v")


def answer(args, cwd, timeout):
    try:
        done = subprocess.run(["git", "-C", str(cwd), *args],
                              capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError):
        return None
    return done.stdout if done.returncode == 0 else None


def lines(cwd):
    listed = answer(["for-each-ref", "--format=%(refname:short)", "refs/remotes/origin"],
                    cwd, READ_TIMEOUT)
    if listed is None:
        return []
    return sorted(name for name in listed.split() if LINE.match(name))


def waiting(text):
    """The `- ` items under `## [Unreleased]`, which is where a backport's entry lands."""
    start = text.find("## [Unreleased]")
    if start < 0:
        return []
    end = text.find("\n## [", start + len("## [Unreleased]"))
    body = text[start:] if end < 0 else text[start:end]
    return [line for line in body.splitlines() if line.startswith("- ")]


def items(text):
    """Every CHANGELOG item in the text, with its runs of whitespace flattened.

    A wrapped item is one item however the paragraph was rewrapped on the way across, and the first
    line alone would call a rewrapped one missing.
    """
    found, held = [], None
    for line in (text or "").splitlines():
        if line.startswith("- "):
            if held is not None:
                found.append(" ".join(held.split()))
            held = line[2:]
        elif held is not None:
            if not line.strip():
                found.append(" ".join(held.split()))
                held = None
            else:
                held += " " + line
    if held is not None:
        found.append(" ".join(held.split()))
    return found


def unmerged_into_main(cwd, line):
    """The items this line's CHANGELOG carries that `main`'s does not, or None.

    Read of the content rather than of the ancestry, because nothing that reaches `main` here can
    leave an ancestry: both branches refuse a direct push, so every change arrives through a pull
    request and `settle.py` squashes it, which writes a commit whose only parent is `main`. Measured
    after the line was merged forward -- `merge-base --is-ancestor` was still false, and `git cherry`
    reported all eight commits outstanding, the squash having combined their patches into one whose
    id matches none of them.

    Weaker than an ancestry: an item reworded on the way across reads as missing. That is a line too
    many on a report, where the ancestry reading was a report nobody could ever clear.
    """
    there = answer(["show", f"origin/main:{CHANGELOG}"], cwd, READ_TIMEOUT)
    here = answer(["show", f"{line}:{CHANGELOG}"], cwd, READ_TIMEOUT)
    if there is None or here is None:
        return None
    outstanding = [item for item in items(here) if item not in set(items(there))]
    return outstanding or None


def report(cwd):
    for line in lines(cwd):
        text = answer(["show", f"{line}:{CHANGELOG}"], cwd, READ_TIMEOUT)
        if text is None:
            continue
        entries = waiting(text)
        if not entries:
            continue
        first = entries[0][2:].strip()
        yield ("{} holds {} unreleased CHANGELOG {}, starting with:\n"
               "  {}{}\n"
               "A backport is cut to be shipped. Close the version on that line and dispatch it, or\n"
               "say why it waits.").format(
                   line, len(entries), "entry" if len(entries) == 1 else "entries",
                   first[:96], "…" if len(first) > 96 else "")


def main():
    cwd = project_tree()
    if cwd is None:
        return 0
    answer(["fetch", "--quiet", "origin", "+refs/heads/*:refs/remotes/origin/*"], cwd, FETCH_TIMEOUT)
    said = list(report(cwd))
    for line in lines(cwd):
        outstanding = unmerged_into_main(cwd, line)
        if outstanding:
            said.append(
                "main does not carry {} CHANGELOG {} that {} does, starting with:\n"
                "  {}{}\n"
                "A fix made on the line has to reach main, or a branch cut from main reproduces what\n"
                "it fixed. Carry them across — `cherry-pick -x` names where each came from.".format(
                    len(outstanding), "entry" if len(outstanding) == 1 else "entries", line,
                    outstanding[0][:96], "…" if len(outstanding[0]) > 96 else ""))
    if said:
        print("\n\n".join(said))
    return 0


if __name__ == "__main__":
    sys.exit(main())

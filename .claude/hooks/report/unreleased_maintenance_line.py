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

## The commit that stayed

A commit on the line that `main` cannot reach is either a backport, which came *from* `main`, or work
that originated on the line and owes `main` a pull request of its own. The two are told apart by what
the pull request names: a backport cites the `main`-side pull request it carries, and origination
cites none.

Neither of the readings the issue tried does it. An `-x` trailer is stripped by a squash merge, and a
reverse-apply fails on every backport release because `main` has moved since — both measured in #777.

Yield on today's line: five commits, three release squashes excluded, one backport silent, one
origination named. That one is #732, which stayed for three days before coming forward as #776, and
it removed a `branches:` filter `main` still carried — a line cut from `main` in that window would
have reproduced the defect in full.

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


def named_pull_request(subject):
    """The number a squash subject ends with, which is how a merge records the pull request."""
    found = re.search(r"\(#(\d+)\)\s*$", subject)
    return found.group(1) if found else None


def gh(args, cwd, timeout=READ_TIMEOUT):
    try:
        done = subprocess.run(["gh", *args], cwd=str(cwd),
                              capture_output=True, text=True, timeout=timeout)
    except (OSError, subprocess.SubprocessError):
        return None
    return done.stdout if done.returncode == 0 else None


def stayed(cwd, line):
    """Commits on `line` that `main` cannot reach and that originated there.

    A release squash is excluded by its own subject, and a backport by the `main`-side pull request
    its body cites. What is left owes `main` a pull request; nothing else reads for it.
    """
    listed = answer(["log", "--format=%h\t%s", f"origin/main..{line}"], cwd, READ_TIMEOUT)
    if listed is None:
        return []
    owed = []
    for row in listed.splitlines():
        short, _, subject = row.partition("\t")
        if RELEASE.match(subject):
            continue
        number = named_pull_request(subject)
        if number is None:
            # Undecidable rather than owed: this reading is built on what the pull request names, and
            # a commit that names none gives it nothing to read. A direct push is a different guard's.
            continue
        body = gh(["pr", "view", number, "--json", "body", "-q", ".body"], cwd, GH_TIMEOUT)
        if body is None:
            continue
        cited = [cite for cite in re.findall(r"#(\d+)", body) if cite != number]
        if not any(carried_from_main(cwd, cite) for cite in cited):
            owed.append((short, subject, f"#{number} cites no pull request based on main"))
    return owed


def carried_from_main(cwd, number):
    """Whether the cited pull request is one merged onto `main`, which is what a backport carries."""
    payload = gh(["pr", "view", number, "--json", "baseRefName,state", "-q",
                  ".baseRefName + \" \" + .state"], cwd, GH_TIMEOUT)
    if payload is None:
        return False
    return payload.split()[:2] == ["main", "MERGED"]


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
        for short, subject, why in stayed(cwd, line):
            said.append(
                "{} holds {} that main cannot reach and that did not come from it:\n"
                "  {} {}\n"
                "A fix written on the line comes forward as its own pull request, or a branch cut from\n"
                "main reproduces what it fixed. ({})".format(
                    line, "a commit", short, subject[:88], why))
    if said:
        print("\n\n".join(said))
    return 0


if __name__ == "__main__":
    sys.exit(main())

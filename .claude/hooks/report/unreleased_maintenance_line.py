#!/usr/bin/env python3
"""Report a maintenance line holding CHANGELOG entries no release has closed.

`main` accumulating unreleased entries is the ordinary state between releases, so it is not read
here. A maintenance line is different: it receives backports, and a backport exists to be shipped,
so an entry sitting in its `## [Unreleased]` is work that was cut for a release nobody cut.

Nothing else says it. `published_check.py` asks whether a version was closed and left unpublished,
which is the opposite state — the entries here belong to no version yet, so that reading is silent
and correct while the work waits.

Measured before this was written: across the life of `2.x`, its `## [Unreleased]` has held entries
in exactly one window — 2026-08-23 to 2026-08-27 — and no session noticed until the line was asked
about directly. v2.1.1 and v2.1.2 never passed through a non-empty section.

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
    if said:
        print("\n\n".join(said))
    return 0


if __name__ == "__main__":
    sys.exit(main())

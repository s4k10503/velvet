#!/usr/bin/env python3
"""Refuses an install example that pins a concrete release tag.

A pin written into a document is correct on the day it is written and silently wrong from the next
release on. The one this replaced named v1.0.0 and stood through seven releases and two majors,
until the series it named stopped being supported and the install instructions were still telling a
new user to pin it. A guard is what keeps the placeholder from being helpfully filled in again.

Only what a reader is shown is in scope -- markdown, and the comments in the workflow files. A
concrete pin inside code is the opposite case: `test_release_notes.py` asserts the generated note
carries the version being released, and that assertion is right to name one.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

# `.git#v<digit>` is the install-URL shape. A tag reference elsewhere -- a compare link, a changelog
# heading -- names a release that happened and stays true; only an install example goes stale.
CONCRETE_PIN = re.compile(r"\.git#v\d")

SKIP = {"scripts/release/pin_example_check.py", "scripts/release/test_pin_example_check.py"}


def documents(project):
    """Markdown anywhere, plus the workflow files, whose header comments carry install examples."""
    listed = subprocess.run(["git", "-C", str(project), "ls-files", "-z"],
                            capture_output=True, text=True, check=True)
    for name in listed.stdout.split("\0"):
        if not name or name in SKIP:
            continue
        if name.endswith(".md") or name.startswith(".github/workflows/"):
            yield name


def findings(project):
    found = []
    for name in documents(project):
        path = Path(project) / name
        try:
            text = path.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        for number, line in enumerate(text.splitlines(), 1):
            if CONCRETE_PIN.search(line):
                found.append((name, number, line.strip()))
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    args = parser.parse_args()

    found = findings(args.project)
    if not found:
        return 0
    print("An install example names a release tag, which the next release makes wrong:\n")
    for name, number, line in found:
        print("  {}:{}: {}".format(name, number, line))
    print("\nWrite the shape instead -- `#vX.Y.Z` -- and link the releases page for the current one.")
    return 1


if __name__ == "__main__":
    sys.exit(main())

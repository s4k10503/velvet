#!/usr/bin/env python3
"""Refuses an install example that pins a concrete tag.

A pin written into a document is correct on the day it is written and silently wrong from the next
release on. Nothing else asks the question: the tag resolves, the manifest is valid, and the reader
installs a version the project may no longer support.

Scope is markdown and the workflow files. Code is out of it deliberately, and one line depends on
that: `test_release_notes.py` asserts the generated note carries the version being released, which is
a concrete pin the repository means to keep. Widening `documents()` to `.py` reddens that line.

The rejected alternative to anchoring on the `.git` suffix is reading the fragment alone, which
reports an issue reference and a colour as pins.

A commit SHA is out of scope where it can be told from a tag, which is what reading the fragment to
its end is for: by its first character instead, every SHA beginning on a digit would be reported. One
shape stays in, and nothing can take it out -- an all-numeric abbreviated SHA is spelled exactly like
a version.
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

CONCRETE_PIN = re.compile(r"\.git/?(\?[^\s#]*)?#v?\d+(?:\.\d+)*(?:-[\w.]+)?(?![\w-]|\.\w)")


def documents(project):
    listed = subprocess.run(["git", "-C", str(project), "ls-files", "-z"],
                            capture_output=True, text=True, check=True)
    for name in listed.stdout.split("\0"):
        if name and (name.endswith(".md") or name.startswith(".github/workflows/")):
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
            for match in CONCRETE_PIN.finditer(line):
                found.append((name, number, match.start() + 1, line))
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    args = parser.parse_args()

    found = findings(args.project)
    if not found:
        return 0
    print("An install example names a tag, which the next release makes wrong:\n")
    for name, number, column, line in found:
        print("  {}:{}:{}: {}".format(name, number, column, line))
    print("\nWrite the shape instead -- `#vX.Y.Z` -- and link the releases page for the current one.")
    return 1


if __name__ == "__main__":
    sys.exit(main())

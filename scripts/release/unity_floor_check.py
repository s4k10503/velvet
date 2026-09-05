#!/usr/bin/env python3
"""Refuses a README naming a Unity release other than the one `package.json` declares.

A README's requirement is what a consumer reads before installing, and the release in it is a copy of
a value the manifest owns. The copy has gone wrong here before -- both READMEs told a reader the 6.3
line was enough while the manifest named a release inside it -- though what this refuses is the
narrower shape that leaves the copy behind: the manifest moves and the README's release stands still.

The floor is `unity` joined to `unityRelease`. A manifest carrying no `unityRelease` names a series, so
no release equals it and every one a README names is reported -- loudly, rather than passing the
question over.

Scope is the tracked READMEs, and it is what keeps out the documents that name a release for a
different reason: the CHANGELOG cites the releases a floor bump crossed, and CONTRIBUTING.md and
CLAUDE.md name the editor a contributor installs, which `ProjectSettings/ProjectVersion.txt` owns and
which may sit above the floor.

Only the release spelling is read. A bare `6000.3` is the series, and reading it as a release would
refuse a document naming the LTS line beside the version it requires.

What it does not ask is whether a README states the floor at all: one naming no release passes, which
is what keeps the sample and generator READMEs out without a file list to maintain.
"""

import argparse
import json
import re
import subprocess
import sys
from pathlib import Path

MANIFEST = "Packages/com.velvet.core/package.json"
# Four leading digits is Unity's own spelling -- a year, or the 6000 line -- and requiring them keeps
# an ordinary semantic version out of the scan.
RELEASE = re.compile(r"(?<![\w.])\d{4}\.\d+\.\d+[a-z]\d+\b")


def floor(project):
    """The lowest release the manifest declares: `unity` joined to `unityRelease`."""
    manifest = json.loads(Path(project, MANIFEST).read_text(encoding="utf-8"))
    series = manifest.get("unity", "")
    release = manifest.get("unityRelease", "")
    return "{}.{}".format(series, release) if release else series


def documents(project):
    listed = subprocess.run(["git", "-C", str(project), "ls-files", "-z"],
                            capture_output=True, text=True, check=True)
    for name in listed.stdout.split("\0"):
        if name and Path(name).name == "README.md":
            yield name


def findings(project):
    declared = floor(project)
    found = []
    for name in documents(project):
        try:
            text = Path(project, name).read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        for number, line in enumerate(text.splitlines(), 1):
            for match in RELEASE.finditer(line):
                if match.group() != declared:
                    found.append((name, number, match.start() + 1, match.group()))
    return found


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    args = parser.parse_args()

    found = findings(args.project)
    if not found:
        return 0
    print("A README names a Unity release that package.json does not declare ({}):\n"
          .format(floor(args.project)))
    for name, number, column, release in found:
        print("  {}:{}:{}: {}".format(name, number, column, release))
    print("\nMove the README, or move the manifest's unity/unityRelease -- the two state one floor.")
    return 1


if __name__ == "__main__":
    sys.exit(main())

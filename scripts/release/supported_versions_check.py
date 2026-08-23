#!/usr/bin/env python3
"""Refuses a version `SECURITY.md`'s table does not say is supported.

The table is the only statement a user has of whether their version still receives fixes, and it names
versions, so it is the kind of document that goes wrong by standing still. The prose it replaced named
none and could not; what makes the table worth that is a check rather than an intention -- the file was
written once and not touched at any of the twelve releases that followed.

Asked of the version `package.json` declares, so the failure lands on the pull request that closes a
version rather than after the dispatch has published it.
"""

import argparse
import json
import re
import sys
from pathlib import Path

ROW = re.compile(r"^\|\s*([0-9]+(?:\.[0-9]+)*)\.x\s*\|\s*(\S+)\s*\|")
SUPPORTED = "✅"


def rows(security_md):
    for line in security_md.splitlines():
        found = ROW.match(line)
        if found:
            yield found.group(1), found.group(2)


def reason(version, security_md):
    """None when the table says this version is supported, else what to put right."""
    for prefix, mark in rows(security_md):
        if version == prefix or version.startswith(prefix + "."):
            if mark == SUPPORTED:
                return None
            return ("SECURITY.md marks {}.x as {}, and package.json declares {}. A release marks the "
                    "series it ships as supported.".format(prefix, mark, version))
    return ("SECURITY.md has no row covering {}, which is the version package.json declares. Add one, "
            "and decide what happens to the series it succeeds.".format(version))


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    args = parser.parse_args()

    project = Path(args.project)
    version = json.loads((project / "Packages/com.velvet.core/package.json").read_text())["version"]
    answer = reason(version, (project / "SECURITY.md").read_text())
    if answer is None:
        return 0
    print(answer)
    return 1


if __name__ == "__main__":
    sys.exit(main())

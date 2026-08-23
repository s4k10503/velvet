#!/usr/bin/env python3
"""Refuses a version `SECURITY.md`'s table does not say is supported.

The table is the only statement a user has of whether their version still receives fixes, and it goes
wrong by standing still. The file was written once and not touched at any of the twelve releases that
followed, which is why this is a check rather than an intention.

Three refusals: a version no row covers, a version on a row marked otherwise, and a version more than
one row covers. The last is refused rather than settled by document order, which would decide silently
and read as a rule.

What it does not ask is whether a row still marked supported should be: a series that stopped
receiving fixes with its row left standing passes.

Asked of the version `package.json` declares rather than of a published tag, so a release can be
refused before it exists.
"""

import argparse
import json
import re
import sys
from pathlib import Path

ROW = re.compile(r"^\s*\|\s*([0-9]+(?:\.[0-9]+)*)\.x\s*\|([^|]*)(?:\||$)")
SUPPORTED = "\u2705"
VARIATION_SELECTOR = "\ufe0f"


def rows(security_md):
    for line in security_md.splitlines():
        found = ROW.match(line)
        if found:
            yield found.group(1), found.group(2).replace(VARIATION_SELECTOR, "").strip()


def reason(version, security_md):
    """None when the table says this version is supported, else what to put right."""
    covering = [(prefix, mark) for prefix, mark in rows(security_md)
                if version.startswith(prefix + ".")]
    if len(covering) > 1:
        return ("SECURITY.md has {} rows covering {}: {}. One version belongs to one series."
                .format(len(covering), version, ", ".join(p + ".x" for p, _ in covering)))
    if covering:
        prefix, mark = covering[0]
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

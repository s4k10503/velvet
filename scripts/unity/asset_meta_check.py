#!/usr/bin/env python3
"""Refuses an asset committed without its `.meta`, and a `.meta` committed without its asset.

The roots read are `Assets` and the package, the two tracked folders the project imports. Each root
itself carries no `.meta`; everything beneath it does.

Only what git tracks is read, files and the directories holding them alike. A directory holding no
tracked file is not part of the project as anyone else receives it, so its `.meta` is reported as an
orphan rather than its absence excused.

A path is out of scope, as is a `.meta` naming one, when a component below its root starts with `.`,
ends with `~` or is named `cvs`, or when its last component, a file, ends with `.tmp`: the names
Unity does not import.
"""

import argparse
import subprocess
import sys

ROOTS = ("Assets", "Packages/com.velvet.core")
META = ".meta"


def tracked(project, root):
    listed = subprocess.run(["git", "-C", str(project), "ls-files", "-z", "--", root],
                            capture_output=True, text=True, check=True)
    return [name for name in listed.stdout.split("\0") if name]


def in_scope_prefix(parts):
    """The leading components of a root-relative path that come before any one Unity does not import."""
    for index, part in enumerate(parts):
        if (part.startswith(".") or part.endswith("~") or part == "cvs"
                or (index == len(parts) - 1 and part.endswith(".tmp"))):
            return parts[:index]
    return parts


def findings(project):
    """Returns (missing, orphaned): assets lacking a `.meta`, and `.meta` files lacking an asset."""
    metas = set()
    assets = set()
    for root in ROOTS:
        prefix = root + "/"
        for name in tracked(project, root):
            relative = name[len(prefix):]
            is_meta = relative.endswith(META)
            parts = (relative[:-len(META)] if is_meta else relative).split("/")
            scoped = in_scope_prefix(parts)
            if is_meta:
                if scoped == parts:
                    metas.add(name[:-len(META)])
                continue
            # A directory holding only out-of-scope entries is still on disk, so it still needs a `.meta`.
            for depth in range(1, len(scoped) + 1):
                assets.add(prefix + "/".join(scoped[:depth]))
    return sorted(assets - metas), sorted(metas - assets)


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=".", help="repository root (default: cwd)")
    args = parser.parse_args()

    missing, orphaned = findings(args.project)
    for name in missing:
        print("missing .meta: {}{}".format(name, META))
    for name in orphaned:
        print("orphaned .meta: {}{} (no tracked asset)".format(name, META))
    if missing or orphaned:
        print("\nCommit a .meta beside each new asset and directory, and remove a .meta together"
              " with its asset.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())

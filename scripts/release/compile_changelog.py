#!/usr/bin/env python3
"""Fold the CHANGELOG fragments into CHANGELOG.md's open sections and delete them.

The first step of closing a version: CONTRIBUTING.md's release section owns where it sits and what
follows it. What this writes is `release_notes.compose`'s reading of the tree, which is the reading
the release checks take of the open sections.

    python3 scripts/release/compile_changelog.py
"""

import argparse
import sys
from pathlib import Path

from release_notes import (
    DEFAULT_CHANGELOG,
    DEFAULT_FRAGMENTS,
    ReleaseNotesError,
    compose,
    fragment_section,
    fragments_in,
)


def compile_fragments(changelog, fragments):
    """Write the composed CHANGELOG and delete the fragments it took, returning how many it took.

    The CHANGELOG is written before anything is deleted, so a write that fails leaves every fragment
    where it was.
    """
    found = fragments_in(fragments)
    composed = compose(Path(changelog).read_text(encoding="utf-8"), found)
    Path(changelog).write_text(composed, encoding="utf-8")
    taken = [path for path, _ in found if fragment_section(path) is not None]
    for path in taken:
        (Path(fragments) / path).unlink()
    return len(taken)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--changelog", default=str(DEFAULT_CHANGELOG))
    parser.add_argument("--fragments", default=str(DEFAULT_FRAGMENTS))
    args = parser.parse_args(argv)
    try:
        taken = compile_fragments(args.changelog, args.fragments)
    except ReleaseNotesError as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"folded {taken} fragment(s) into {args.changelog}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

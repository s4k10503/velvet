#!/usr/bin/env python3
"""Fold the committed CHANGELOG fragments into CHANGELOG.md's open sections and delete them.

The first step of closing a version: CONTRIBUTING.md's release section owns where it sits and what
follows it. The fragments are read from HEAD, the tree the release checks read them from, and a
fragment on disk that HEAD does not hold as it stands is refused rather than compiled: an untracked
one would put an entry in the note for a change no commit carries.

    python3 scripts/release/compile_changelog.py
"""

import argparse
import subprocess
import sys
from pathlib import Path

from release_notes import (
    DEFAULT_CHANGELOG,
    FRAGMENT_PATH,
    REPO_ROOT,
    ReleaseNotesError,
    compose,
    fragment_section,
    fragments_at,
    fragments_in,
)

CHANGELOG_PATH = DEFAULT_CHANGELOG.relative_to(REPO_ROOT)


def compile_fragments(project):
    """Write the composed CHANGELOG and delete the fragments it took, returning how many it took.

    The CHANGELOG is written before anything is deleted, so a write that fails leaves every fragment
    where it was.
    """
    project = Path(project)
    committed = [(path, text) for path, text in fragments_at(project, "HEAD")
                 if fragment_section(path) is not None]
    on_disk = [(path, text) for path, text in fragments_in(project / FRAGMENT_PATH)
               if not Path(path).name.startswith(".")]
    differing = sorted({path for path, _ in set(committed) ^ set(on_disk)})
    if differing:
        raise ReleaseNotesError(
            f"{len(differing)} fragment(s) on disk differ from HEAD, starting with "
            f"{FRAGMENT_PATH}/{differing[0]}. Commit or remove them first: the release checks read "
            "the committed fragments.")
    changelog = project / CHANGELOG_PATH
    changelog.write_text(compose(changelog.read_text(encoding="utf-8"), committed), encoding="utf-8")
    for path, _ in committed:
        (project / FRAGMENT_PATH / path).unlink()
    return len(committed)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--project", default=str(REPO_ROOT), help="repository root")
    args = parser.parse_args(argv)
    try:
        taken = compile_fragments(args.project)
    except (ReleaseNotesError, subprocess.CalledProcessError) as error:
        print(f"error: {error}", file=sys.stderr)
        return 1
    print(f"folded {taken} fragment(s) into {Path(args.project) / CHANGELOG_PATH}")
    return 0


if __name__ == "__main__":
    sys.exit(main())

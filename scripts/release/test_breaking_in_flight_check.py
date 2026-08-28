#!/usr/bin/env python3
"""Unit tests for scripts/release/breaking_in_flight_check.py.

The refusal is the easy half. What the tests are mostly about is the two ways it must not pass: a
change closing a version while a branch carries a breaking entry nobody mentioned, and a listing that
could not be read at all — because a read that did not happen looks exactly like a read that found
nothing, and only one of them is a reason to let a release through.

Run: python3 scripts/release/test_breaking_in_flight_check.py
"""

import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))

from breaking_in_flight_check import (  # noqa: E402
    UNNAMED, UNREADABLE, released, section, unnamed,
)

CHECK = HERE / "breaking_in_flight_check.py"
CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"

OPEN = """# Changelog

## [Unreleased]

## [Unreleased — breaking]

## [2.1.0] - 2026-08-09

### Highlights

- A release.
"""

CLOSED = OPEN.replace("## [Unreleased]\n", "## [Unreleased]\n\n## [3.0.0] - 2026-08-27\n")

CARRYING = OPEN.replace(
    "## [Unreleased — breaking]\n",
    "## [Unreleased — breaking]\n\n### Changed\n\n- An API a caller has to edit around.\n")

STUB_GH = """#!/bin/sh
printf '%s' "$VELVET_IN_FLIGHT_LIST"
exit "$VELVET_IN_FLIGHT_CODE"
"""


def git(cwd, *args):
    subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True, text=True)


def repository():
    """A tree whose HEAD closes 3.0.0 over a base that did not, and a branch carrying an entry."""
    root = Path(tempfile.mkdtemp())
    git(root, "init", "--quiet", "--initial-branch=main")
    git(root, "config", "user.email", "t@example.com")
    git(root, "config", "user.name", "t")
    (root / CHANGELOG).parent.mkdir(parents=True)
    (root / CHANGELOG).write_text(OPEN)
    git(root, "add", CHANGELOG)
    git(root, "commit", "--quiet", "-m", "base")
    base = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                          capture_output=True, text=True).stdout.strip()
    git(root, "checkout", "--quiet", "-b", "in-flight")
    (root / CHANGELOG).write_text(CARRYING)
    git(root, "commit", "--quiet", "-am", "carrying")
    branch = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                            capture_output=True, text=True).stdout.strip()
    git(root, "checkout", "--quiet", "main")
    (root / CHANGELOG).write_text(CLOSED)
    git(root, "commit", "--quiet", "-am", "release")
    return root, base, branch


def run(root, base, listing, code=0, body=None):
    stub = root / "bin"
    stub.mkdir(exist_ok=True)
    (stub / "gh").write_text(STUB_GH)
    (stub / "gh").chmod(0o755)
    env = dict(os.environ,
               PATH=f"{stub}{os.pathsep}{os.environ['PATH']}",
               VELVET_IN_FLIGHT_LIST=listing,
               VELVET_IN_FLIGHT_CODE=str(code))
    args = [sys.executable, str(CHECK), "--base", base, "--result", "HEAD"]
    if body is not None:
        path = root / "body.md"
        path.write_text(body)
        args += ["--body-file", str(path)]
    return subprocess.run(args, cwd=str(root), capture_output=True, text=True, env=env, timeout=60)


def listing(number, oid):
    return ('[{"number": %d, "title": "replace the async type",'
            ' "headRefName": "in-flight", "headRefOid": "%s"}]' % (number, oid))


class Readings(unittest.TestCase):
    def test_Given_ASectionHoldingItems_When_Read_Then_OnlyItsOwnAreReturned(self):
        # Arrange / Act
        entries = section(CARRYING, "Unreleased — breaking")

        # Assert
        self.assertEqual(entries, ["- An API a caller has to edit around."])

    def test_Given_AChangelogClosingAVersion_When_Read_Then_TheVersionIsNamed(self):
        # Arrange / Act
        opened = released(CLOSED) - released(OPEN)

        # Assert
        self.assertEqual(opened, {"3.0.0"})

    def test_Given_ABodyNamingThePullRequest_When_Read_Then_NoneIsMissing(self):
        # Arrange — a bare number is how CONTRIBUTING asks a body to cite one.
        missing = unnamed([{"number": 377}], "left out of this one on purpose: #377")

        # Assert
        self.assertEqual(missing, [])


class Decisions(unittest.TestCase):
    def test_Given_AnUnnamedBreakingEntryInFlight_When_TheVersionCloses_Then_ItIsRefused(self):
        # Arrange
        root, base, branch = repository()

        # Act
        done = run(root, base, listing(377, branch), body="Closes nothing in particular.")

        # Assert
        self.assertEqual((done.returncode, "#377" in done.stderr), (UNNAMED, True))

    def test_Given_TheSameEntryNamedInTheBody_When_TheVersionCloses_Then_ItPasses(self):
        # Arrange — naming it is the whole requirement; "not this one" is still a decision.
        root, base, branch = repository()

        # Act
        done = run(root, base, listing(377, branch), body="#377 waits for the major after this.")

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_NoBreakingEntryInFlight_When_TheVersionCloses_Then_ItPasses(self):
        # Arrange
        root, base, _ = repository()

        # Act
        done = run(root, base, "[]", body="A release.")

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_AListingThatCouldNotBeRead_When_TheVersionCloses_Then_ItIsNotAPass(self):
        # Arrange — the failure this exists to refuse looks identical to an empty listing.
        root, base, _ = repository()

        # Act
        done = run(root, base, "", code=1, body="A release.")

        # Assert
        self.assertEqual(done.returncode, UNREADABLE)

    def test_Given_AChangeClosingNoVersion_When_Decided_Then_NothingIsAsked(self):
        # Arrange
        root, base, branch = repository()

        # Act — measured against its own HEAD, so no version opens across the pair.
        done = run(root, "HEAD", listing(377, branch), body="No release here.")

        # Assert
        self.assertEqual(done.returncode, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

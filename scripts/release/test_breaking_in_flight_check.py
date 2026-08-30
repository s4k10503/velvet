#!/usr/bin/env python3
"""Unit tests for scripts/release/breaking_in_flight_check.py.

The refusal is the easy half. What the tests are mostly about is the two ways it must not pass: a
change closing a version while a branch carries a breaking entry nobody mentioned, and a listing that
could not be read at all — because a read that did not happen looks exactly like a read that found
nothing, and only one of them is a reason to let a release through.

Run: python3 scripts/release/test_breaking_in_flight_check.py
"""

import os
import shutil
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

    def test_Given_AHeadPushedSinceTheCheckout_When_Read_Then_ItIsFetchedRatherThanReported(self):
        # Arrange — the shas come from a live listing and the objects from a checkout taken before
        # it, so any pull request pushed while a queue drains names a commit this tree does not
        # hold. A clone of main alone is that tree.
        root, base, branch = repository()
        clone = Path(tempfile.mkdtemp()) / "clone"
        subprocess.run(["git", "clone", "--quiet", "--no-local", "--branch", "main", "--single-branch",
                        str(root), str(clone)], check=True, capture_output=True)
        absent = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", branch],
                                capture_output=True).returncode != 0

        # Act
        done = run(clone, base, listing(377, branch), body="A release.")

        # Assert — the absence rides along, because a clone that happened to hold the head would
        # reach this outcome without the fetch under test running at all.
        self.assertEqual((absent, done.returncode, "went unread" in done.stderr),
                         (True, UNNAMED, False))

    def test_Given_AChangeClosingNoVersion_When_Decided_Then_NothingIsAsked(self):
        # Arrange
        root, base, branch = repository()

        # Act — measured against its own HEAD, so no version opens across the pair.
        done = run(root, "HEAD", listing(377, branch), body="No release here.")

        # Assert
        self.assertEqual(done.returncode, 0)


WITH_ENTRY = OPEN.replace(
    "## [Unreleased — breaking]\n",
    "## [Unreleased — breaking]\n\n### Changed\n\n- An API a caller has to edit around.\n")


class WhereTheEntriesWent(unittest.TestCase):
    """The heading guard cannot see where the entries went, and two edits passed every reading."""

    def tree(self, before, after):
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        git(root, "init", "--quiet", "--initial-branch=main")
        git(root, "config", "user.email", "t@example.com")
        git(root, "config", "user.name", "t")
        (root / CHANGELOG).parent.mkdir(parents=True)
        (root / CHANGELOG).write_text(before)
        git(root, "add", CHANGELOG)
        git(root, "commit", "--quiet", "-m", "base")
        base = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                              capture_output=True, text=True).stdout.strip()
        (root / CHANGELOG).write_text(after)
        git(root, "commit", "--quiet", "-am", "after")
        git(root, "remote", "add", "origin", str(root))
        return root, base

    def test_Given_AnEntryThatLeftTheSection_When_TheResultCarriesItNowhere_Then_ItIsRefused(self):
        # Arrange -- the emptiness reading only ever saw a section emptied completely; measured,
        # moving a single entry out moved no case at all.
        root, base = self.tree(WITH_ENTRY, OPEN)

        # Act
        done = run(root, base, "[]")

        # Assert
        self.assertEqual((done.returncode, "carries them nowhere" in done.stderr), (UNNAMED, True))

    # GREEN_ON_BASE(characterization): the control for the reading beside it, and a control is
    # green on both sides -- the base passes this because it passes everything of this shape, which
    # is the defect the red case names.
    def test_Given_AnEntryReclassified_When_ItLandsInAnotherSection_Then_ItPasses(self):
        # Arrange -- the control, and the legitimate case the static forms refuse: moving one out
        # is a reclassification, and it is still in the file.
        moved = OPEN.replace("## [Unreleased]\n",
                             "## [Unreleased]\n\n### Changed\n\n- An API a caller has to edit around.\n")
        root, base = self.tree(WITH_ENTRY, moved)

        # Act
        done = run(root, base, "[]")

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_AMajorThatLeavesItsEntries_When_TheBodyIsSilent_Then_ItIsRefused(self):
        # Arrange -- measured: a 3.0.0 closed with every entry left behind passes every other
        # reading, and its note describes none of the breaks it ships.
        closing_major = WITH_ENTRY.replace("## [Unreleased]\n",
                                           "## [Unreleased]\n\n## [3.0.0] - 2026-09-01\n")
        root, base = self.tree(WITH_ENTRY, closing_major)

        # Act
        done = run(root, base, "[]", body="Closes something.")

        # Assert
        self.assertEqual((done.returncode, "leaves 1 entr" in done.stderr), (UNNAMED, True))

    # GREEN_ON_BASE(characterization): as above. The base asks nothing about where the entries
    # went, so it passes whatever the body says; what this pins is that saying so is what clears it.
    def test_Given_AMajorThatLeavesItsEntries_When_TheBodySaysSo_Then_ItPasses(self):
        # Arrange -- the control: an entry can wait for the major after this one, which is a
        # decision rather than a defect, so it is asked for rather than refused.
        closing_major = WITH_ENTRY.replace("## [Unreleased]\n",
                                           "## [Unreleased]\n\n## [3.0.0] - 2026-09-01\n")
        root, base = self.tree(WITH_ENTRY, closing_major)

        # Act
        done = run(root, base, "[]",
                   body="The one entry left in Unreleased — breaking waits for 4.0.0.")

        # Assert
        self.assertEqual(done.returncode, 0)


class RecordingIsNotClosing(unittest.TestCase):
    """A version the remote already tags is carried across, not closed.

    Carrying a maintenance line's released section forward brings its heading with it, and read as a
    release it puts the question to a body that decides nothing about it: the version shipped before
    the pull request existed.
    """

    def setUp(self):
        self.root, self.base, self.branch = repository()
        # An origin to ask, pointing at the tree itself: the reading is of the remote's tags, and a
        # checkout with none reads as none, which is the direction that asks about everything.
        git(self.root, "remote", "add", "origin", str(self.root))

    def test_Given_AVersionTheRemoteTags_When_TheChangeCarriesIt_Then_NothingIsAsked(self):
        # Arrange -- the forward-carry: the section arrives, the tag is already out.
        git(self.root, "tag", "v3.0.0")

        # Act
        done = run(self.root, self.base, listing(377, self.branch))

        # Assert
        self.assertEqual((done.returncode, "closes no version" in done.stdout), (0, True))

    # GREEN_ON_BASE(characterization): the control, and it is what the base does for every version.
    # Reddening it would mean this change had moved the ordinary reading rather than carved one
    # state out of it.
    def test_Given_AVersionNoTagNames_When_TheChangeAddsIt_Then_TheBodyIsAsked(self):
        # Arrange -- the control: with no tag the same tree is a release, which is what every
        # ordinary one looks like here, and the body has to decide about the work in flight.
        # Act
        done = run(self.root, self.base, listing(377, self.branch))

        # Assert
        self.assertEqual(done.returncode, 2)


if __name__ == "__main__":
    unittest.main(verbosity=2)

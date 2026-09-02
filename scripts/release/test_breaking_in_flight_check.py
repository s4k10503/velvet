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
    git(root, "remote", "add", "origin", str(root))
    return root, base, branch


def run(root, base, listing, code=0, body=None, result="HEAD"):
    stub = root / "bin"
    stub.mkdir(exist_ok=True)
    (stub / "gh").write_text(STUB_GH)
    (stub / "gh").chmod(0o755)
    env = dict(os.environ,
               PATH=f"{stub}{os.pathsep}{os.environ['PATH']}",
               VELVET_IN_FLIGHT_LIST=listing,
               VELVET_IN_FLIGHT_CODE=str(code))
    args = [sys.executable, str(CHECK), "--base", base, "--result", result]
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


RELEASE = "v2.1.0-main"

RECLASSIFIED = OPEN.replace(
    "## [Unreleased]\n",
    "## [Unreleased]\n\n### Changed\n\n- An API a caller has to edit around.\n")

MINOR_CARRYING = OPEN.replace(
    "## [Unreleased]\n",
    "## [Unreleased]\n\n## [2.2.0] - 2026-09-02\n\n### Changed\n\n"
    "- An API a caller has to edit around.\n")

MAJOR_CARRYING = OPEN.replace(
    "## [Unreleased]\n",
    "## [Unreleased]\n\n## [3.0.0] - 2026-09-01\n\n### Changed\n\n"
    "- An API a caller has to edit around.\n")

MINOR_AFTER_MAJOR = MAJOR_CARRYING.replace(
    "## [Unreleased]\n", "## [Unreleased]\n\n## [3.1.0] - 2026-09-02\n\n### Fixed\n\n- A fix.\n")

MAJOR_OVER_NOTHING = OPEN.replace(
    "## [Unreleased]\n", "## [Unreleased]\n\n## [3.0.0] - 2026-09-01\n\n### Fixed\n\n- A fix.\n")

MINOR_OVER_NOTHING = OPEN.replace(
    "## [Unreleased]\n", "## [Unreleased]\n\n## [2.2.0] - 2026-09-02\n\n### Fixed\n\n- A fix.\n")

TWO_LINES = OPEN.replace("- A release.\n", "- A release.\n- Another.\n")


class ReleaseHistory(unittest.TestCase):
    def history(self, *texts, tags=()):
        """A repository whose commits carry `texts` in order, `tags` naming {index: tag} on them,
        with itself as origin. Returns (root, the commits)."""
        root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        git(root, "init", "--quiet", "--initial-branch=main")
        git(root, "config", "user.email", "t@example.com")
        git(root, "config", "user.name", "t")
        (root / CHANGELOG).parent.mkdir(parents=True)
        commits = []
        for index, text in enumerate(texts):
            (root / CHANGELOG).write_text(text)
            git(root, "add", CHANGELOG)
            git(root, "commit", "--quiet", "-m", f"step {index}")
            commits.append(subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                                          capture_output=True, text=True).stdout.strip())
            if index in dict(tags):
                git(root, "tag", dict(tags)[index])
        git(root, "remote", "add", "origin", str(root))
        return root, commits


class SinceTheLastRelease(ReleaseHistory):
    """The breaking section's history from the newest `-main` tag the result descends from.

    Both trees of the one-step reading can be right while the pair of changes is wrong: the one
    moving the entries out closes nothing and is asked nothing, and the one closing a minor over the
    section it left empty is what a correct minor after a major looks like.
    """

    def test_Given_AnEntryMovedOutByOneChange_When_TheNextClosesAMinorOverIt_Then_TheSecondIsRefused(self):
        # Arrange -- the entry sits in the section at the release itself, so the release's own copy
        # is the one sighting of it; the move is the first commit after.
        root, commits = self.history(WITH_ENTRY, RECLASSIFIED, MINOR_CARRYING, tags={0: RELEASE})

        # Act
        move = run(root, commits[0], "[]", result=commits[1])
        close = run(root, commits[1], "[]", body="A minor.")

        # Assert
        self.assertEqual((move.returncode, close.returncode, RELEASE in close.stderr),
                         (0, UNNAMED, True))

    def test_Given_AnEntryWrittenAndMovedSinceTheRelease_When_AMinorClosesOverIt_Then_ItIsRefused(self):
        # Arrange -- never in the release's copy, so only the commits since it hold the sighting.
        root, commits = self.history(OPEN, WITH_ENTRY, RECLASSIFIED, MINOR_CARRYING,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[2], "[]", body="A minor.")

        # Assert
        self.assertEqual((done.returncode, "carries in no major" in done.stderr), (UNNAMED, True))

    def test_Given_AnEntryDroppedByOneChange_When_TheNextClosesAMajorWithoutIt_Then_TheSecondIsRefused(self):
        # Arrange -- the major's side of the same hole: dropped from the section it was moved to,
        # which the one-step reading of the breaking section does not watch, then closed past.
        root, commits = self.history(WITH_ENTRY, RECLASSIFIED, OPEN, MAJOR_OVER_NOTHING,
                                     tags={0: RELEASE})
        dropped = "edit around" not in subprocess.run(
            ["git", "-C", str(root), "show", f"{commits[2]}:{CHANGELOG}"],
            capture_output=True, text=True).stdout

        # Act
        drop = run(root, commits[1], "[]", result=commits[2])
        close = run(root, commits[2], "[]", body="A major.")

        # Assert -- the drop rides along: kept anywhere in the file, the entry reaches the same
        # refusal, and what this pins is that leaving the file is not what the one-step reading sees.
        self.assertEqual((dropped, drop.returncode, close.returncode), (True, 0, UNNAMED))

    def test_Given_AMajorCarryingTheSection_When_ItCloses_Then_ThePassSaysWhatItReadBackTo(self):
        # Arrange -- the entry the release's copy holds is in the major being closed.
        root, commits = self.history(WITH_ENTRY, MAJOR_CARRYING, tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]", body="A major.")

        # Assert
        self.assertEqual((done.returncode, RELEASE in done.stdout), (0, True))

    def test_Given_AMajorThatCarriedTheSection_When_AMinorClosesAfterIt_Then_ItPasses(self):
        # Arrange -- the correct minor after a major, which the two-change hole looked like.
        root, commits = self.history(WITH_ENTRY, MAJOR_CARRYING, MINOR_AFTER_MAJOR,
                                     tags={0: RELEASE, 1: "v3.0.0-main"})

        # Act
        done = run(root, commits[1], "[]", body="A minor after the major.")

        # Assert
        self.assertEqual((done.returncode, "v3.0.0-main" in done.stdout), (0, True))

    def test_Given_AnEntryOnTheSideAMergeResolvedAway_When_TheMergeClosesAMinor_Then_ItIsRefused(self):
        # Arrange -- the entry's one sighting is on the side branch, and the merge took main's copy
        # of the file whole, so the merge's own tree holds it nowhere.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        git(root, "checkout", "--quiet", "-b", "side")
        (root / CHANGELOG).write_text(WITH_ENTRY)
        git(root, "commit", "--quiet", "-am", "written on the side")
        git(root, "checkout", "--quiet", "main")
        (root / CHANGELOG).write_text(MINOR_OVER_NOTHING)
        git(root, "commit", "--quiet", "-am", "a minor")
        git(root, "merge", "--quiet", "--no-ff", "-s", "ours", "-m", "keeping main's copy", "side")

        # Act
        done = run(root, commits[0], "[]", body="A minor.")

        # Assert
        self.assertEqual((done.returncode, "carries in no major" in done.stderr), (UNNAMED, True))

    def test_Given_NoReleaseTagReaches_When_Decided_Then_TheOneStepReadingSaysSo(self):
        # Arrange -- a repository before its first release.
        root, commits = self.history(WITH_ENTRY, RECLASSIFIED, MINOR_CARRYING)

        # Act
        done = run(root, commits[1], "[]", body="A minor.")

        # Assert -- the hole stays open there, and the pass says how deep it read.
        self.assertEqual((done.returncode, "one step" in done.stdout), (0, True))


class ATagWhoseCommitIsNotHere(ReleaseHistory):
    """A `-main` tag the remote names at a commit this checkout has not got, and a remote that names
    nothing because it could not be listed.

    In a complete history that commit is one the result does not descend from; in one cut short it
    may be the release the reading was looking for, and none is the answer that passes.
    """

    def test_Given_NoRemoteToList_When_Decided_Then_ItIsNotAPass(self):
        # Arrange -- the failure this exists to refuse looks identical to a remote tagging nothing.
        root, commits = self.history(WITH_ENTRY, RECLASSIFIED, MINOR_CARRYING, tags={0: RELEASE})
        git(root, "remote", "remove", "origin")

        # Act
        done = run(root, commits[1], "[]", body="A minor.")

        # Assert
        self.assertEqual((done.returncode, "read back to a release" in done.stderr),
                         (UNREADABLE, True))

    def test_Given_AHistoryCutShortOfTheTaggedCommit_When_Decided_Then_ItIsNotAPass(self):
        # Arrange -- a shallow clone: the remote names the release, the clone holds neither the tag
        # nor its commit, and nothing in it says whether HEAD descends from it.
        root, commits = self.history(OPEN, RECLASSIFIED, tags={0: RELEASE})
        clone = Path(tempfile.mkdtemp()) / "clone"
        self.addCleanup(shutil.rmtree, clone.parent, ignore_errors=True)
        subprocess.run(["git", "clone", "--quiet", "--depth", "1", "--no-tags",
                        "file://" + str(root), str(clone)], check=True, capture_output=True)
        absent = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", commits[0]],
                                capture_output=True).returncode != 0

        # Act
        done = run(clone, "HEAD", "[]")

        # Assert
        self.assertEqual((absent, done.returncode, RELEASE in done.stderr),
                         (True, UNREADABLE, True))

    def test_Given_ACompleteHistoryWithoutAMaintenanceLinesCommit_When_Decided_Then_TheReachableReleaseIsRead(self):
        # Arrange -- a clone of main alone: the line's release is tagged at a commit the clone has
        # not got, and its history is whole, so that commit is one HEAD does not descend from.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        git(root, "checkout", "--quiet", "-b", "line")
        (root / CHANGELOG).write_text(OPEN.replace("- A release.\n", "- A release.\n- A patch.\n"))
        git(root, "commit", "--quiet", "-am", "a patch on the line")
        git(root, "tag", "v2.1.4-main")
        lined = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                               capture_output=True, text=True).stdout.strip()
        git(root, "checkout", "--quiet", "main")
        clone = Path(tempfile.mkdtemp()) / "clone"
        self.addCleanup(shutil.rmtree, clone.parent, ignore_errors=True)
        subprocess.run(["git", "clone", "--quiet", "--no-local", "--no-tags", "--single-branch",
                        "--branch", "main", str(root), str(clone)], check=True, capture_output=True)
        absent = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", lined],
                                capture_output=True).returncode != 0

        # Act
        done = run(clone, commits[0], "[]")

        # Assert
        self.assertEqual((absent, done.returncode, RELEASE in done.stdout), (True, 0, True))


class PublishedSections(ReleaseHistory):
    """A dated section is the note its release shipped, and it is held to the tag's copy."""

    def test_Given_ADatedSectionLosingALine_When_Decided_Then_ItIsRefusedNamingTheTag(self):
        # Arrange
        root, commits = self.history(OPEN, OPEN.replace("- A release.\n", ""), tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed" in done.stderr,
                          RELEASE in done.stderr), (UNNAMED, True, True))

    def test_Given_ADatedSectionCorrected_When_Decided_Then_ItIsRefusedToo(self):
        # Arrange -- the decision: a file cannot tell a correction from a deletion.
        root, commits = self.history(OPEN, OPEN.replace("- A release.", "- A release, corrected."),
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed" in done.stderr), (UNNAMED, True))

    def test_Given_ADatedSectionReordered_When_Decided_Then_ItIsRefused(self):
        # Arrange -- the same lines in another order is another note.
        root, commits = self.history(
            TWO_LINES, TWO_LINES.replace("- A release.\n- Another.\n", "- Another.\n- A release.\n"),
            tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed" in done.stderr), (UNNAMED, True))

    def test_Given_ADatedSectionGone_When_Decided_Then_ItIsRefusedAsGone(self):
        # Arrange
        root, commits = self.history(OPEN, OPEN.split("## [2.1.0]")[0], tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: gone" in done.stderr), (UNNAMED, True))

    def test_Given_ADatedSectionUntouched_When_Decided_Then_ItPassesHavingReadTheTag(self):
        # Arrange -- the ordinary change, and the one a reading that never reached the tag would
        # also pass.
        root, commits = self.history(OPEN, RECLASSIFIED, tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, RELEASE in done.stdout), (0, True))

    def test_Given_ASectionClosedSinceTheTag_When_ItsTextChanges_Then_ItIsNotHeld(self):
        # Arrange -- not published yet, so not the tag's to hold: what holds it is the write-time
        # guard, once a tag carries it.
        root, commits = self.history(OPEN, MAJOR_CARRYING,
                                     MAJOR_CARRYING.replace("edit around.", "edit around, still."),
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, RELEASE in done.stdout), (0, True))

    def test_Given_ATagOnlyTheRemoteHolds_When_Decided_Then_ItIsStillTheOneReadBackTo(self):
        # Arrange -- asked of the remote, for the reason published_check gives: a clone that fetched
        # no tags is the shape a stale or empty local list takes.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        clone = Path(tempfile.mkdtemp()) / "clone"
        self.addCleanup(shutil.rmtree, clone.parent, ignore_errors=True)
        subprocess.run(["git", "clone", "--quiet", "--no-local", "--no-tags", str(root), str(clone)],
                       check=True, capture_output=True)
        git(clone, "config", "user.email", "t@example.com")
        git(clone, "config", "user.name", "t")
        (clone / CHANGELOG).write_text(OPEN.replace("- A release.\n", ""))
        git(clone, "commit", "--quiet", "-am", "lose a line")
        held_locally = subprocess.run(["git", "-C", str(clone), "tag", "-l"],
                                      capture_output=True, text=True).stdout.strip()

        # Act
        done = run(clone, commits[0], "[]")

        # Assert -- the absence rides along, because a clone that happened to hold the tag would
        # reach this outcome without the remote being asked at all.
        self.assertEqual((held_locally, done.returncode, RELEASE in done.stderr),
                         ("", UNNAMED, True))


class DispatchMirror(unittest.TestCase):
    """What this module restates from upm.yml: the `-main` tag it leaves on the release commit."""

    def test_Given_TheDispatchWorkflow_When_ItsMainTagIsRead_Then_ItIsTheSpellingThisModuleLooksFor(self):
        # Arrange -- the workflow's spelling, instantiated, has to be what version_key reads a
        # version out of; a rename on either side leaves the readings with no release to reach.
        import breaking_in_flight_check as check
        workflow = (HERE.parent.parent / ".github" / "workflows" / "upm.yml").read_text()
        spelled = 'MAIN_TAG="v${VERSION}-main"'

        # Assert
        self.assertEqual((spelled in workflow,
                          check.version_key(spelled[len('MAIN_TAG="'):-1].replace("${VERSION}", "1.2.3"))),
                         (True, (1, 2, 3)))


if __name__ == "__main__":
    unittest.main(verbosity=2)

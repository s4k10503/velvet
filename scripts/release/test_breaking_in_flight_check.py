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

TYPO = OPEN.replace(
    "## [Unreleased — breaking]\n",
    "## [Unreleased — breaking]\n\n### Changed\n\n- An API a caller has to edit arond.\n")

GROWN = OPEN.replace("- A release.\n", "- A release.\n- A highlight written after the release.\n")

ON_THE_LINE = OPEN.replace(
    "## [2.1.0]", "## [2.1.4] - 2026-08-30\n\n### Fixed\n\n- A patch on the line.\n\n## [2.1.0]")

SUBSECTIONS = OPEN.replace(
    "### Highlights\n\n- A release.\n",
    "### Highlights\n\n- A release.\n\n### Changed\n\n- Changed one.\n- Changed two.\n"
    "\n### Fixed\n\n- Fixed one.\n")

SHORT_OF_ONE = SUBSECTIONS.replace("- Changed two.\n", "")

PUT_BACK_ELSEWHERE = SHORT_OF_ONE.replace("- Fixed one.\n", "- Changed two.\n- Fixed one.\n")

DUPLICATED = SUBSECTIONS.replace("- Changed one.\n", "- Changed one.\n- Changed one.\n")

# A copy whose missing lines sit next to each other, so the one put back has no copy-neighbour of
# its own on one side. Both directions, since a bound is written per side.
THREE_CHANGED = SUBSECTIONS.replace("- Changed two.\n", "- Changed two.\n- Changed three.\n")

SHORT_OF_TWO_CHANGED = THREE_CHANGED.replace("- Changed two.\n- Changed three.\n", "")

PUT_BACK_BELOW_ITS_BLOCK = SHORT_OF_TWO_CHANGED.replace(
    "- Fixed one.\n", "- Changed two.\n- Fixed one.\n")

PUT_BACK_IN_ITS_BLOCK = SHORT_OF_TWO_CHANGED.replace(
    "- Changed one.\n", "- Changed one.\n- Changed two.\n")

THREE_FIXED = SUBSECTIONS.replace("- Fixed one.\n", "- Fixed one.\n- Fixed two.\n- Fixed three.\n")

SHORT_OF_TWO_FIXED = THREE_FIXED.replace("- Fixed one.\n- Fixed two.\n", "")

PUT_BACK_ABOVE_ITS_BLOCK = SHORT_OF_TWO_FIXED.replace(
    "- Changed two.\n", "- Changed two.\n- Fixed two.\n")

# A copy carrying one line twice, against a base that merged the two blocks it heads and moved the
# block between them up -- the shape a section reordered after its release takes.
TWO_FIXED_BLOCKS = OPEN.replace(
    "### Highlights\n\n- A release.\n",
    "### Highlights\n\n- A release.\n\n### Fixed\n\n- Fixed one.\n"
    "\n### Changed\n\n- Changed one.\n\n### Fixed\n\n- Fixed two.\n")

MERGED_FIXED = OPEN.replace(
    "### Highlights\n\n- A release.\n",
    "### Highlights\n\n- A release.\n\n### Changed\n\n- Changed one.\n"
    "\n### Fixed\n\n- Fixed one.\n- Fixed two.\n")

FIXED_SPLITTING_CHANGED = MERGED_FIXED.replace(
    "### Changed\n\n- Changed one.\n", "### Changed\n\n### Fixed\n\n- Changed one.\n")

# A base whose blocks are in the other order, so the line the copy puts below the missing one is
# above the line it puts above it.
BLOCKS_REORDERED = SUBSECTIONS.replace(
    "### Changed\n\n- Changed one.\n- Changed two.\n\n### Fixed\n\n- Fixed one.\n",
    "### Fixed\n\n- Fixed one.\n\n### Changed\n\n- Changed one.\n")

PUT_BACK_ABOVE_THE_BLOCKS = BLOCKS_REORDERED.replace(
    "### Fixed\n", "- Changed two.\n\n### Fixed\n")

CARRIED_GROWN = ON_THE_LINE.replace(
    "- A patch on the line.\n", "- A patch on the line.\n- A bullet that release never shipped.\n")


def revision(root, name="HEAD"):
    return subprocess.run(["git", "-C", str(root), "rev-parse", name],
                          capture_output=True, text=True).stdout.strip()


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
            commits.append(self.commit(root, text, f"step {index}"))
            if index in dict(tags):
                git(root, "tag", dict(tags)[index])
        git(root, "remote", "add", "origin", str(root))
        return root, commits

    def commit(self, root, text, message="a step"):
        """One more commit on the checked-out branch, carrying `text`. Returns it."""
        (root / CHANGELOG).write_text(text)
        git(root, "add", CHANGELOG)
        git(root, "commit", "--quiet", "-m", message)
        return revision(root)

    def line(self, root, text, tag):
        """A maintenance line: a branch `line` off the checked-out commit, carrying `text` under
        `tag`, with main checked out again. Returns the tagged commit."""
        git(root, "checkout", "--quiet", "-b", "line")
        lined = self.commit(root, text, "on the line")
        git(root, "tag", tag)
        git(root, "checkout", "--quiet", "main")
        return lined

    def clone(self, root, *options):
        """A clone of `root`, whose origin it is."""
        clone = Path(tempfile.mkdtemp()) / "clone"
        self.addCleanup(shutil.rmtree, clone.parent, ignore_errors=True)
        subprocess.run(["git", "clone", "--quiet", *options, str(root), str(clone)],
                       check=True, capture_output=True)
        return clone


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

    # GREEN_ON_BASE(characterization): the base reads one step and walks no history, so a typo the
    # branch wrote and corrected is nothing to it. What this pins is that the memory added here is
    # main's and not the pull request's: walk `tag..result` instead of `tag..base` and the typo is
    # sighted, the major carries it nowhere, and the correct change is refused.
    def test_Given_ALineWrittenAndCorrectedOnTheBranch_When_ItsMajorCloses_Then_ItPasses(self):
        # Arrange -- the typo is in the pull request's own history and in no commit of main, and the
        # major closes carrying the corrected line.
        root, commits = self.history(OPEN, TYPO, WITH_ENTRY, MAJOR_CARRYING, tags={0: RELEASE})
        on_the_branch = "arond" in subprocess.run(
            ["git", "-C", str(root), "show", f"{commits[1]}:{CHANGELOG}"],
            capture_output=True, text=True).stdout

        # Act
        done = run(root, commits[0], "[]", body="A major.")

        # Assert -- the typo rides along: without it in the branch's history there is nothing for a
        # walk of the result's own commits to sight, and the case would pass for no reason.
        self.assertEqual((on_the_branch, done.returncode, done.stderr), (True, 0, ""))

    def test_Given_AnEntryASideOfAMergeOnMainResolvedAway_When_AMinorClosesAfterIt_Then_ItIsRefused(self):
        # Arrange -- the entry's one sighting is on the side a merge on main took nothing from, so
        # no tree of main ever held it, and the merge is the base the closing change is read from.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        git(root, "checkout", "--quiet", "-b", "side")
        self.commit(root, WITH_ENTRY, "written on the side")
        git(root, "checkout", "--quiet", "main")
        self.commit(root, OPEN.replace("## [Unreleased]\n", "## [Unreleased]\n\n### Fixed\n\n- A fix.\n"))
        git(root, "merge", "--quiet", "--no-ff", "-s", "ours", "-m", "keeping main's copy", "side")
        merged = revision(root)
        self.commit(root, MINOR_OVER_NOTHING, "a minor")

        # Act
        done = run(root, merged, "[]", body="A minor.")

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
    """A `-main` tag the result is not found to descend from, and a remote that names nothing
    because it could not be listed.

    Where the history under the result is whole, such a tag is a maintenance line's; where it is cut
    short there, the cut may be what hides the path, and none is the answer that passes.
    """

    def test_Given_AShallowCheckoutCutUnderTheResult_When_TheTagsCommitIsHereAboveNoPath_Then_ItIsNotAPass(self):
        # Arrange -- the tag's commit is fetched on its own, so it is here, while the two commits
        # between it and HEAD are not: `git merge-base` finds no path and says no.
        root, commits = self.history(WITH_ENTRY, RECLASSIFIED, MINOR_CARRYING, tags={0: RELEASE})
        clone = self.clone("file://" + str(root), "--depth", "2", "--no-tags")
        git(clone, "fetch", "--quiet", "--depth", "1", "origin", "tag", RELEASE)
        here = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", commits[0]],
                              capture_output=True).returncode == 0
        no_path = subprocess.run(["git", "-C", str(clone), "merge-base", "--is-ancestor",
                                  commits[0], "HEAD"], capture_output=True).returncode

        # Act
        done = run(clone, commits[1], "[]", body="A minor.")

        # Assert -- what the clone holds rides along, since a clone with the path would reach a pass
        # by reading the release, and one without the commit is the other case below.
        self.assertEqual((here, no_path, done.returncode, RELEASE in done.stderr),
                         (True, 1, UNREADABLE, True))

    def test_Given_AMaintenanceLineFetchedShallow_When_Decided_Then_ItsTagIsPassedOver(self):
        # Arrange -- the cut is under the line's tip and not under HEAD, whose history is whole, so
        # the line's tag is one HEAD does not descend from and the reading goes on.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        clone = self.clone(root, "--no-local", "--no-tags", "--single-branch", "--branch", "main")
        git(clone, "fetch", "--quiet", "--depth", "1", "origin", "line")
        cut = (clone / ".git" / "shallow").exists()

        # Act
        done = run(clone, commits[0], "[]")

        # Assert
        self.assertEqual((cut, done.returncode, RELEASE in done.stdout), (True, 0, True))

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
        lined = self.line(root, ON_THE_LINE, "v2.1.4-main")
        clone = self.clone(root, "--no-local", "--no-tags", "--single-branch", "--branch", "main")
        absent = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", lined],
                                capture_output=True).returncode != 0

        # Act
        done = run(clone, commits[0], "[]")

        # Assert
        self.assertEqual((absent, done.returncode, RELEASE in done.stdout), (True, 0, True))

    def test_Given_ACarriedSectionWhoseTagsCommitIsNotHere_When_Decided_Then_ItIsNotAPass(self):
        # Arrange -- the same clone, with main carrying the line's section: the note exists on the
        # remote and cannot be compared here, which is not the same as there being none.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        lined = self.line(root, ON_THE_LINE, "v2.1.4-main")
        self.commit(root, ON_THE_LINE, "carried forward")
        clone = self.clone(root, "--no-local", "--no-tags", "--single-branch", "--branch", "main")
        absent = subprocess.run(["git", "-C", str(clone), "cat-file", "-e", lined],
                                capture_output=True).returncode != 0

        # Act
        done = run(clone, "HEAD", "[]")

        # Assert
        self.assertEqual((absent, done.returncode, "v2.1.4-main" in done.stderr),
                         (True, UNREADABLE, True))


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

    def test_Given_ARefusal_When_ItSaysWhereToReadTheCopy_Then_ItNamesTheCommitTheRemoteTags(self):
        # Arrange -- the copy compared came from the commit `ls-remote` named, and a checkout's own
        # tag of that name can be another commit or none: this one holds a `v3.0.0-main` for a
        # release that was withdrawn.
        root, commits = self.history(OPEN, OPEN.replace("- A release.\n", ""), tags={0: RELEASE})
        named = revision(root, RELEASE)

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, f"git show {named}:" in done.stderr), (UNNAMED, True))

    def test_Given_ADatedHeadingsDateChanged_When_Decided_Then_ItIsRefused(self):
        # Arrange -- the heading is a line of the note too.
        root, commits = self.history(
            OPEN, OPEN.replace("## [2.1.0] - 2026-08-09", "## [2.1.0] - 2026-08-10"),
            tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ASectionGrownPastItsTag_When_ItIsTheBases_Then_ItPasses(self):
        # Arrange -- main's older sections carry a Highlights block their tags' copies do not, and
        # were reworded and reordered after their releases, so the tag's copy is not what a section
        # is held to: the base is.
        root, commits = self.history(OPEN, GROWN, tags={0: RELEASE})
        grown = "written after the release" in subprocess.run(
            ["git", "-C", str(root), "show", f"HEAD:{CHANGELOG}"],
            capture_output=True, text=True).stdout

        # Act
        done = run(root, "HEAD", "[]")

        # Assert -- the growth rides along, since a section equal to its tag's copy passes any
        # reading and would say nothing about this one.
        self.assertEqual((grown, done.returncode,
                          "each dated section to its own release's tag" in done.stdout),
                         (True, 0, True))

    def test_Given_AHeldSectionGrownByTheChange_When_Decided_Then_ItIsRefusedAgainstTheBase(self):
        # Arrange -- the write-time guard's growth reading, at merge time: a line the tag never
        # carried arrives in a section the base carried without it.
        root, commits = self.history(OPEN, GROWN, tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ALineWrittenAfterTheRelease_When_TheChangeDeletesIt_Then_ItIsRefusedAgainstTheBase(self):
        # Arrange -- the result is the tag's copy to the letter, and a line the base carried is gone.
        root, commits = self.history(OPEN, GROWN, OPEN, tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ASectionPublishedOnTheMaintenanceLine_When_ItLosesALine_Then_ItIsRefusedNamingThatTag(self):
        # Arrange -- main carries the line's section as the line published it, and the newest tag
        # main descends from is older than that release.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        base = self.commit(root, ON_THE_LINE, "carried forward")
        self.commit(root, ON_THE_LINE.replace("- A patch on the line.\n", ""), "lose a line")

        # Act
        done = run(root, base, "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.4]: changed against the base, and v2.1.4-main" in done.stderr),
                         (UNNAMED, True))

    def test_Given_AMaintenanceSectionBroughtInShort_When_Decided_Then_ItIsRefusedNamingItsTag(self):
        # Arrange -- the base does not carry the section, so the tag's copy is the one memory of it,
        # and the carry-forward drops a line of that copy.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        self.commit(root, ON_THE_LINE.replace("- A patch on the line.\n", ""), "carried short")

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.4]: brought in changed against v2.1.4-main's copy"
                          in done.stderr), (UNNAMED, True))

    def test_Given_ALineTheCopyHasPutBackWhereItHasIt_When_Decided_Then_ItPasses(self):
        # Arrange -- the repair the refusal advertises: the base is short of a line its tag's copy
        # carries, and the change puts that line back.
        root, commits = self.history(SUBSECTIONS, SHORT_OF_ONE, SUBSECTIONS, tags={0: RELEASE})
        was_short = "- Changed two." not in subprocess.run(
            ["git", "-C", str(root), "show", f"{commits[1]}:{CHANGELOG}"],
            capture_output=True, text=True).stdout

        # Act
        done = run(root, commits[1], "[]")

        # Assert -- what the base was short of rides along, and the reading having run with it: a
        # change touching no dated section passes without going near this branch, and a run that
        # read no tag at all passes every dated-section shape with it.
        self.assertEqual((was_short, done.returncode,
                          "each dated section to its own release's tag" in done.stdout),
                         (True, 0, True))

    def test_Given_ALineTheCopyHasPutBackElsewhere_When_Decided_Then_ItIsRefused(self):
        # Arrange -- the same line, under `### Fixed` rather than where the copy has it. The note
        # would then describe it as a fix.
        root, commits = self.history(SUBSECTIONS, SHORT_OF_ONE, PUT_BACK_ELSEWHERE,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ALineWhoseCopyNeighbourBelowIsMissingToo_When_ItGoesBackUnderTheNextHeading_Then_ItIsRefused(self):
        # Arrange -- two lines next to each other in the copy are missing, so the one put back has
        # no copy-neighbour of its own below it, and it lands under `### Fixed`. The note would then
        # describe a change as a fix.
        root, commits = self.history(THREE_CHANGED, SHORT_OF_TWO_CHANGED, PUT_BACK_BELOW_ITS_BLOCK,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ALineWhoseCopyNeighbourAboveIsMissingToo_When_ItGoesBackUnderThePreviousHeading_Then_ItIsRefused(self):
        # Arrange -- the same absence on the other side: the line put back has no copy-neighbour
        # above it, and it lands under `### Changed`. Each side is bounded separately, so one of
        # them holding says nothing about the other.
        root, commits = self.history(THREE_FIXED, SHORT_OF_TWO_FIXED, PUT_BACK_ABOVE_ITS_BLOCK,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ALineWhoseCopyNeighbourBelowIsMissingToo_When_ItGoesBackInItsOwnBlock_Then_ItPasses(self):
        # Arrange -- the repair the refusal advertises, for the same base: one of the two missing
        # lines goes back where the copy has it, and the other stays missing.
        root, commits = self.history(THREE_CHANGED, SHORT_OF_TWO_CHANGED, PUT_BACK_IN_ITS_BLOCK,
                                     tags={0: RELEASE})
        still_short = "- Changed three." not in subprocess.run(
            ["git", "-C", str(root), "show", f"HEAD:{CHANGELOG}"],
            capture_output=True, text=True).stdout

        # Act
        done = run(root, commits[1], "[]")

        # Assert -- the other line still being absent rides along, and the reading having run with
        # it: with both put back the placement is bounded by copy-neighbours that are here, which
        # says nothing about the bound this pins, and a run that read no tag passes every shape.
        self.assertEqual((still_short, done.returncode,
                          "each dated section to its own release's tag" in done.stdout),
                         (True, 0, True))

    def test_Given_ACopyCarryingOneLineTwice_When_ThePutBackSplitsAHeadingFromItsEntry_Then_ItIsRefused(self):
        # Arrange -- the base merged the two blocks that line heads and moved the block between them
        # up, so the two occurrences bound a stretch the base reordered. The heading lands between
        # `### Changed` and its only entry, publishing that entry as a fix.
        root, commits = self.history(TWO_FIXED_BLOCKS, MERGED_FIXED, FIXED_SPLITTING_CHANGED,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ABaseCarryingTheBlocksInTheOtherOrder_When_ALineGoesBackAboveThemBoth_Then_ItIsRefused(self):
        # Arrange -- the copy puts `### Fixed` below the missing line and `- Changed one.` above it,
        # and this base has them the other way round, so the line goes back above both with nothing
        # between it and either end of that stretch to disagree.
        root, commits = self.history(SUBSECTIONS, BLOCKS_REORDERED, PUT_BACK_ABOVE_THE_BLOCKS,
                                     tags={0: RELEASE})

        # Act
        done = run(root, commits[1], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_ALineTheSectionAlreadyCarriesAddedAgain_When_Decided_Then_ItIsRefused(self):
        # Arrange -- the copy carries that line, so a reading asking only which lines may arrive
        # allows a second one, and the note ships the bullet twice.
        root, commits = self.history(SUBSECTIONS, DUPLICATED, tags={0: RELEASE})

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.0]: changed against the base" in done.stderr),
                         (UNNAMED, True))

    def test_Given_AMaintenanceSectionCarriedInWhole_When_Decided_Then_ItPasses(self):
        # Arrange -- the other branch that lets a change through: a section the base has not got,
        # arriving as its tag's copy.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        self.commit(root, ON_THE_LINE, "carried forward")
        published = "v2.1.4-main" in subprocess.run(
            ["git", "-C", str(root), "ls-remote", "--tags", "origin"],
            capture_output=True, text=True).stdout

        # Act
        done = run(root, commits[0], "[]")

        # Assert -- the tag rides along, and the reading having run with it: a section no release
        # tags is unpublished and passes without being held at all.
        self.assertEqual((published, done.returncode,
                          "each dated section to its own release's tag" in done.stdout),
                         (True, 0, True))

    def test_Given_AMaintenanceSectionCarriedInGrown_When_Decided_Then_ItIsRefusedNamingItsTag(self):
        # Arrange -- the base has not got the section, so once it lands the base is what holds it
        # and no deletion is allowed: a bullet its release never published would be permanent.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        self.commit(root, CARRIED_GROWN, "carried forward, with one more")

        # Act
        done = run(root, commits[0], "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.4]: brought in changed against v2.1.4-main's copy"
                          in done.stderr), (UNNAMED, True))

    def test_Given_ACarriedMaintenanceSectionDeleted_When_Decided_Then_ItIsRefusedAsGone(self):
        # Arrange -- the base carried it and no release on this line did, so the base is what holds
        # it.
        root, commits = self.history(OPEN, tags={0: RELEASE})
        self.line(root, ON_THE_LINE, "v2.1.4-main")
        base = self.commit(root, ON_THE_LINE, "carried forward")
        self.commit(root, OPEN, "drop the section")

        # Act
        done = run(root, base, "[]")

        # Assert
        self.assertEqual((done.returncode, "## [2.1.4]: gone, and v2.1.4-main carries it" in done.stderr),
                         (UNNAMED, True))

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

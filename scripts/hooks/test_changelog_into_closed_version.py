#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/changelog_into_closed_version.py.

A refuse guard that stops matching is indistinguishable from one that had nothing to say — exit 0 and
no output — so the cases hold both directions: what it refuses, and the release-time edits it must
let through. The second half is where it has already been measured wrong, by walking the release
procedure by hand, which was the only reading it got.

Run: python3 scripts/hooks/test_changelog_into_closed_version.py
"""

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/changelog_into_closed_version.py"
CHANGELOG_REL = "Packages/com.velvet.core/CHANGELOG.md"

RELEASED = """# Changelog

## [Unreleased]

### Fixed

- Something not yet shipped.

## [Unreleased — breaking]

### Changed

- An API a caller has to edit around.

## [2.0.0] - 2026-08-02

### Fixed

- A thing that shipped.
"""


class Verdicts(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="closed-version-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        self.changelog = self.root / CHANGELOG_REL
        self.changelog.parent.mkdir(parents=True)
        self.changelog.write_text(RELEASED)

    def edit(self, old, new):
        return judged(self.root, self.changelog, old, new)

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AnEntryFiledIntoAReleasedSection_When_Judged_Then_ItIsRefused(self):
        # Arrange — the version shipped without the line, and would ship missing it.
        code, said = self.edit("- A thing that shipped.\n",
                               "- A thing that shipped.\n- A thing that did not.\n")

        # Act / Assert
        self.assertEqual((code, "2.0.0" in said), (2, True))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AReleasedHeadingLosingItsDate_When_Judged_Then_ItIsRefused(self):
        # Arrange — renaming, stripping the date and deleting the section are one edit reached three
        # ways: whatever sits under an undated heading is not compared at all.
        code, said = self.edit("## [2.0.0] - 2026-08-02\n", "## [2.0.0]\n")

        # Act / Assert
        self.assertEqual((code, "no released section" in said), (2, True))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AnEditWritingASecondHeadingForOneVersion_When_Judged_Then_ItIsRefused(self):
        # Arrange — a note is rebuilt from the first heading matching its version, so only one of the
        # two is ever published and their order decides which.
        code, said = self.edit("## [Unreleased]\n", "## [Unreleased]\n\n## [2.0.0] - 2026-08-02\n")

        # Act / Assert
        self.assertEqual((code, "second" in said), (2, True))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AnEntryFiledUnderUnreleased_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the ordinary edit, and the one a guard that stopped matching would also pass.
        code, said = self.edit("- Something not yet shipped.\n",
                               "- Something not yet shipped.\n- And another.\n")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_TheBreakingSectionDrainedIntoTheVersionClosing_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the release-time edit this guard has already been measured refusing. The version
        # being closed is not released yet, so nothing under it is compared.
        code, said = self.edit(
            "## [Unreleased — breaking]\n\n### Changed\n\n- An API a caller has to edit around.\n",
            "")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AHighlightsBlockOnTheVersionClosing_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the other half of closing a version, and the order CONTRIBUTING.md prescribes:
        # everything written into the open section first, the rename last.
        code, said = self.edit("## [Unreleased]\n",
                               "## [Unreleased]\n\n### Highlights\n\n- What this release leads with.\n")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    # GREEN_ON_BASE(characterization): the guard is unchanged here. Every case is green on
    # both sides by construction, which is what a suite for existing behaviour is — and the
    # behaviour had no suite at all, so a guard that stopped matching read as one with
    # nothing to say.
    def test_Given_AChangelogInAnotherRepository_When_Judged_Then_ItIsNotThisGuardsToRefuse(self):
        # Arrange — a genuinely foreign checkout, which is what separates out-of-scope from the
        # worktrees this repository does its branch work in.
        elsewhere = Path(tempfile.mkdtemp(prefix="closed-version-foreign-"))
        self.addCleanup(shutil.rmtree, elsewhere, ignore_errors=True)
        subprocess.run(["git", "-C", str(elsewhere), "init", "--quiet"],
                       check=True, capture_output=True)
        foreign = elsewhere / CHANGELOG_REL
        foreign.parent.mkdir(parents=True)
        foreign.write_text(RELEASED)
        event = {"tool_name": "Edit", "cwd": str(self.root),
                 "tool_input": {"file_path": str(foreign),
                                "old_string": "- A thing that shipped.\n",
                                "new_string": "- A thing that shipped.\n- A thing that did not.\n"}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30,
                              env=dict(os.environ, CLAUDE_PROJECT_DIR=str(self.root)))

        # Act / Assert
        self.assertEqual((done.returncode, done.stderr), (0, ""))


TAGGED = RELEASED.replace("- A thing that shipped.\n",
                          "- A thing that shipped.\n- Another thing that shipped.\n")

# A published section with two blocks, so a line can go back under the wrong one, and with two of
# its entries next to each other in the copy so the one put back has no copy-neighbour below it.
SUBSECTIONED = RELEASED.replace(
    "- A thing that shipped.\n",
    "- A thing that shipped.\n- A second thing that shipped.\n- A third thing that shipped.\n"
    "\n### Changed\n\n- A change that shipped.\n")

SHORT_OF_TWO = SUBSECTIONED.replace(
    "- A second thing that shipped.\n- A third thing that shipped.\n", "")

# A published section of two blocks, against a file that carries them in the other order and is
# short of the entry the note ends with.
LAST_LINE = RELEASED.replace(
    "### Fixed\n\n- A thing that shipped.\n",
    "### Changed\n\n- A change that shipped.\n- Another change that shipped.\n"
    "\n### Fixed\n\n- A thing that shipped.\n- Another thing that shipped.\n")

# A published section of two blocks, against a file short of the second one entire, so its heading
# can go back where the copy has it and still head nothing.
WHOLE_BLOCK = RELEASED.replace(
    "### Fixed\n\n- A thing that shipped.\n",
    "### Fixed\n\n- A thing that shipped.\n\n### Changed\n\n- A change that shipped.\n")

BLOCKS_SWAPPED = RELEASED.replace(
    "### Fixed\n\n- A thing that shipped.\n",
    "### Fixed\n\n- A thing that shipped.\n"
    "\n### Changed\n\n- A change that shipped.\n- Another change that shipped.\n")


def judged(root, changelog, old, new):
    """The guard's verdict on an Edit of `changelog` replacing `old` with `new`, as (exit code,
    stderr)."""
    event = {"tool_name": "Edit", "cwd": str(root),
             "tool_input": {"file_path": str(changelog), "old_string": old, "new_string": new}}
    done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                          capture_output=True, text=True, timeout=30,
                          env=dict(os.environ, CLAUDE_PROJECT_DIR=str(root)))
    return done.returncode, done.stderr


def git(root, *args):
    return subprocess.run(["git", "-C", str(root), *args], check=True,
                          capture_output=True, text=True).stdout.strip()


def published(case, text, prefix):
    """A repository whose `v2.0.0-main` commit carries `text`, itself as origin, cleaned up with
    `case`. Returns (its root, its CHANGELOG)."""
    root = Path(tempfile.mkdtemp(prefix=prefix))
    case.addCleanup(shutil.rmtree, root, ignore_errors=True)
    changelog = root / CHANGELOG_REL
    changelog.parent.mkdir(parents=True)
    changelog.write_text(text)
    for args in (["init", "--quiet", "--initial-branch=main"],
                 ["config", "user.email", "t@example.com"], ["config", "user.name", "t"],
                 ["add", CHANGELOG_REL], ["commit", "--quiet", "-m", "release"],
                 ["tag", "v2.0.0-main"], ["remote", "add", "origin", str(root)]):
        git(root, *args)
    return root, changelog


class InItsPlace(unittest.TestCase):
    """Where a line the copy has may go back, which is what separates a repair from a rewrite.

    The merge-time twin holds the reading itself; these hold that this guard reaches it, in both
    directions its verdict can go.
    """

    def setUp(self):
        self.root, self.changelog = published(self, SUBSECTIONED, "closed-version-place-")
        self.changelog.write_text(SHORT_OF_TWO)

    def test_Given_ALineWhoseCopyNeighbourBelowIsMissingToo_When_ItGoesBackUnderTheNextHeading_Then_ItIsRefused(self):
        # Arrange -- the file is short of two entries next to each other in the copy, so the one put
        # back has no copy-neighbour of its own below it, and it lands under `### Changed`.

        # Act
        code, said = judged(self.root, self.changelog, "- A change that shipped.\n",
                            "- A second thing that shipped.\n- A change that shipped.\n")

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

    def test_Given_ALineWhoseCopyNeighbourBelowIsMissingToo_When_ItGoesBackInItsOwnBlock_Then_ItIsLetThrough(self):
        # Arrange -- the repair the refusal advertises, over the same file: the entry goes back
        # where the copy has it, and the one below it in the copy stays missing.
        still_short = "- A third thing that shipped." not in self.changelog.read_text()

        # Act
        code, said = judged(self.root, self.changelog, "- A thing that shipped.\n",
                            "- A thing that shipped.\n- A second thing that shipped.\n")

        # Assert -- the other entry still being absent rides along: with both put back the placement
        # is bounded by copy-neighbours that are here, which says nothing about the bound this pins.
        self.assertEqual((still_short, code, said), (True, 0, ""))


class TheCopysLastLine(unittest.TestCase):
    """The entry a release's note ends with, against a file that carries the blocks in the other
    order.

    The whole copy is above that entry, so nothing bounds it below and nothing over it disagrees:
    what it comes to rest against is the reading left.
    """

    def setUp(self):
        self.root, self.changelog = published(self, LAST_LINE, "closed-version-last-")
        self.changelog.write_text(BLOCKS_SWAPPED)

    def test_Given_TheCopysLastLine_When_ItIsAppendedUnderAnotherHeading_Then_ItIsRefused(self):
        # Arrange -- the file is short of that entry, and the put-back lands after the last change
        # rather than after the fix the copy puts above it, publishing a fix as a change.

        # Act
        code, said = judged(self.root, self.changelog, "- Another change that shipped.\n",
                            "- Another change that shipped.\n- Another thing that shipped.\n")

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))


class AgainstTheTag(unittest.TestCase):
    """A released section is held to the newest `-main` tag on the remote that HEAD descends from.

    The growth reading alone let a line deleted from a published note run, and a reorder; a reword
    it refused without naming what the note was changed away from.
    """

    def setUp(self):
        self.root, self.changelog = published(self, TAGGED, "closed-version-tag-")

    def test_Given_AReleasedLineDeleted_When_Judged_Then_ItIsRefusedNamingTheTag(self):
        # Act
        code, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

    def test_Given_ARefusal_When_ItSaysWhereToReadTheCopy_Then_ItNamesTheCommitTheRemoteTags(self):
        # Arrange -- the copy compared came from the commit `ls-remote` named, and a checkout's own
        # tag of that name can be another commit or none, for the reason the declaration on
        # `test_Given_ATagOnlyTheCheckoutHolds_...` gives.
        named = git(self.root, "rev-parse", "v2.0.0-main")

        # Act
        code, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((code, f"git show {named}:" in said), (2, True))

    def test_Given_AReleasedLineCorrected_When_Judged_Then_ItIsRefusedNamingTheTag(self):
        # Arrange -- the decision: the file cannot tell a correction from a deletion.
        old, new = "- A thing that shipped.", "- A thing that shipped, corrected."

        # Act
        code, said = judged(self.root, self.changelog, old, new)

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

    def test_Given_ReleasedLinesReordered_When_Judged_Then_ItIsRefused(self):
        # Arrange -- the same lines in another order is another note.
        old = "- A thing that shipped.\n- Another thing that shipped.\n"
        new = "- Another thing that shipped.\n- A thing that shipped.\n"

        # Act
        code, said = judged(self.root, self.changelog, old, new)

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

    def test_Given_ASectionBroughtBackToTheTag_When_Judged_Then_ItIsLetThrough(self):
        # Arrange -- the file has already lost a line against the tag. Putting it back adds a line
        # to a released section, which is what the growth reading refuses, and it is the repair.
        self.changelog.write_text(TAGGED.replace("- Another thing that shipped.\n", ""))

        # Act
        code, said = judged(self.root, self.changelog, "- A thing that shipped.\n",
                            "- A thing that shipped.\n- Another thing that shipped.\n")

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ATagOnlyTheRemoteHolds_When_AReleasedLineIsDeleted_Then_ItIsRefusedNamingTheTag(self):
        # Arrange -- a clone that fetched no tags, whose origin holds the release.
        clone = Path(tempfile.mkdtemp(prefix="closed-version-clone-")) / "clone"
        self.addCleanup(shutil.rmtree, clone.parent, ignore_errors=True)
        git(self.root, "clone", "--quiet", "--no-local", "--no-tags", str(self.root), str(clone))
        held_locally = git(clone, "tag", "-l")

        # Act
        code, said = judged(clone, clone / CHANGELOG_REL, "- Another thing that shipped.\n", "")

        # Assert -- the absence rides along, because a clone that happened to hold the tag would
        # reach this outcome without the remote being asked at all.
        self.assertEqual((held_locally, code, "v2.0.0-main" in said), ("", 2, True))

    # GREEN_ON_BASE(characterization): the base holds no section to any tag, so a deletion runs
    # there whatever the checkout's tag list says. What this pins is that a tag the remote never
    # published is not a release either; a checkout was found holding a `v3.0.0-main` for one that
    # was withdrawn.
    def test_Given_ATagOnlyTheCheckoutHolds_When_AReleasedLineIsDeleted_Then_TheOneStepReadingLetsItThrough(self):
        # Arrange -- an origin holding the commit and not the tag.
        elsewhere = Path(tempfile.mkdtemp(prefix="closed-version-origin-"))
        self.addCleanup(shutil.rmtree, elsewhere, ignore_errors=True)
        git(elsewhere, "init", "--quiet", "--bare")
        git(self.root, "remote", "set-url", "origin", str(elsewhere))
        git(self.root, "push", "--quiet", "origin", "HEAD:refs/heads/main")
        published = git(self.root, "ls-remote", "--tags", "origin")

        # Act
        code, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((published, code, said), ("", 0, ""))

    # GREEN_ON_BASE(characterization): without a tag the reading is the one the base already has.
    # That reading refuses growth and lets a removal run, and this pins that no tag means it rather
    # than a refusal of everything or of nothing.
    def test_Given_NoTagReaches_When_AReleasedLineIsDeleted_Then_TheOneStepReadingLetsItThrough(self):
        # Arrange
        git(self.root, "tag", "-d", "v2.0.0-main")

        # Act
        code, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AReleasedHeadingsDateChanged_When_Judged_Then_ItIsRefusedNamingTheTag(self):
        # Arrange -- the heading is a line of the note too.
        old, new = "## [2.0.0] - 2026-08-02", "## [2.0.0] - 2026-08-03"

        # Act
        code, said = judged(self.root, self.changelog, old, new)

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

    def test_Given_ASectionPublishedOnAnotherLine_When_ItLosesALine_Then_ItIsRefusedNamingItsOwnTag(self):
        # Arrange -- the line's release is tagged where HEAD does not descend from, and main carries
        # its section as the line published it.
        carried = TAGGED.replace(
            "## [2.0.0]", "## [2.0.1] - 2026-08-08\n\n### Fixed\n\n- A patch on the line.\n\n## [2.0.0]")
        git(self.root, "checkout", "--quiet", "-b", "line")
        self.changelog.write_text(carried)
        git(self.root, "commit", "--quiet", "-am", "on the line")
        git(self.root, "tag", "v2.0.1-main")
        git(self.root, "checkout", "--quiet", "main")
        self.changelog.write_text(carried)
        git(self.root, "commit", "--quiet", "-am", "carried forward")

        # Act
        code, said = judged(self.root, self.changelog, "- A patch on the line.\n", "")

        # Assert
        self.assertEqual((code, "v2.0.1-main" in said), (2, True))

    def published_elsewhere(self):
        """A `## [2.0.1]` section the line published under `v2.0.1-main`, which main does not carry.
        Returns the section's text."""
        section = "## [2.0.1] - 2026-08-08\n\n### Fixed\n\n- A patch on the line.\n\n"
        git(self.root, "checkout", "--quiet", "-b", "line")
        self.changelog.write_text(TAGGED.replace("## [2.0.0]", section + "## [2.0.0]"))
        git(self.root, "commit", "--quiet", "-am", "on the line")
        git(self.root, "tag", "v2.0.1-main")
        git(self.root, "checkout", "--quiet", "main")
        return section

    # GREEN_ON_BASE(characterization): a section newly dated on one side is compared on neither by
    # the base, so the carry-forward runs there whatever it carries. What this pins is that the
    # same carry-forward still runs once a section the file has not got has to arrive as its
    # tag's copy.
    def test_Given_AMaintenanceSectionCarriedInWhole_When_Judged_Then_ItIsLetThrough(self):
        # Arrange
        section = self.published_elsewhere()
        published = "v2.0.1-main" in git(self.root, "ls-remote", "--tags", "origin")

        # Act
        code, said = judged(self.root, self.changelog, "## [2.0.0]", section + "## [2.0.0]")

        # Assert -- the tag rides along: a section no release tags is let through by not being held
        # at all, which is not what this pins.
        self.assertEqual((published, code, said), (True, 0, ""))

    def test_Given_AMaintenanceSectionCarriedInGrown_When_Judged_Then_ItIsRefusedNamingItsTag(self):
        # Arrange -- once the section lands, the file is what holds it and no deletion is allowed,
        # so a bullet its release never published would be permanent.
        section = self.published_elsewhere()

        # Act
        code, said = judged(self.root, self.changelog, "## [2.0.0]",
                            section.replace("- A patch on the line.\n",
                                            "- A patch on the line.\n- A bullet it never shipped.\n")
                            + "## [2.0.0]")

        # Assert
        self.assertEqual((code, "v2.0.1-main" in said), (2, True))

    def test_Given_AMaintenanceSectionCarriedInShort_When_Judged_Then_ItIsRefusedNamingItsTag(self):
        # Arrange -- the base does not carry the section, so the tag's copy is the one memory of
        # it, and the carry-forward drops a line of that copy.
        section = self.published_elsewhere()

        # Act
        code, said = judged(self.root, self.changelog, "## [2.0.0]",
                            section.replace("- A patch on the line.\n", "") + "## [2.0.0]")

        # Assert
        self.assertEqual((code, "v2.0.1-main" in said), (2, True))

    def test_Given_AHeadingPutBackOnItsOwn_When_ItIsRefused_Then_TheRemediesAreNamed(self):
        # Arrange -- the file is short of a whole block, and the heading goes back where the copy
        # has it, ahead of nothing. Refusing it is right, and the advice above says a line put back
        # where the copy has it is what gets through, which is what this contributor just did.
        root, changelog = published(self, WHOLE_BLOCK, "closed-version-whole-")
        changelog.write_text(RELEASED)
        one_edit, _ = judged(root, changelog, "- A thing that shipped.\n",
                             "- A thing that shipped.\n\n### Changed\n\n- A change that shipped.\n")
        changelog.write_text(RELEASED)

        # Act
        code, said = judged(root, changelog, "- A thing that shipped.\n",
                            "- A thing that shipped.\n\n### Changed\n")

        # Assert -- the one-edit route passing rides along, since advice naming a way through is
        # worth nothing if that way is refused too. The other route it names is held to that by
        # the case below.
        self.assertEqual((one_edit, code,
                          "an entry put back in the same edit or lines the section already "
                          "carries" in said),
                         (0, 2, True))

    # GREEN_ON_BASE(characterization): the base accepts this route too. What this branch
    # changed is the refusal that named only the other one, so the case holds the message
    # to a verdict rather than pinning one this branch moved.
    def test_Given_AHeadingPutBackAboveLinesTheSectionCarries_When_Judged_Then_ItPasses(self):
        # Arrange -- the base merged the block into the one above it, so the heading goes back on
        # its own and comes to head lines the section already carries. The refusal names this route
        # beside the one-edit one, and is held to it for the reason the case above gives.
        root, changelog = published(self, WHOLE_BLOCK, "closed-version-merged-")
        merged = WHOLE_BLOCK.replace("- A thing that shipped.\n\n### Changed\n\n",
                                     "- A thing that shipped.\n")
        changelog.write_text(merged)
        to_the_end, _ = judged(root, changelog, "- A change that shipped.\n",
                               "- A change that shipped.\n\n### Changed\n")
        changelog.write_text(merged)

        # Act
        code, _ = judged(root, changelog, "- A thing that shipped.\n- A change that shipped.\n",
                         "- A thing that shipped.\n\n### Changed\n\n- A change that shipped.\n")

        # Assert -- the same base sending that heading past the entry rides along. The two
        # results carry the same lines and differ only in where the heading sits, so the landing
        # is what separates the verdicts.
        self.assertEqual((to_the_end, code), (2, 0))

    def test_Given_APutBackMadeHere_When_ItIsUndone_Then_ItIsRefusedTowardsGit(self):
        # Arrange -- the file has lost a line against the tag, and the put-back this guard lets
        # through is made in the editor.
        self.changelog.write_text(TAGGED.replace("- Another thing that shipped.\n", ""))
        put_back, _ = judged(self.root, self.changelog, "- A thing that shipped.\n",
                             "- A thing that shipped.\n- Another thing that shipped.\n")
        self.changelog.write_text(TAGGED)

        # Act -- undoing it. What the file was before the put-back is not something this reads: it
        # compares the edit against the file, where the merge-time check has the base commit.
        undone, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((put_back, undone, "Revert it with git" in said), (0, 2, True))

    def test_Given_ASectionGrownPastItsTag_When_TheAddedLineIsDeleted_Then_ItIsRefused(self):
        # Arrange -- a line the tag's copy never carried, which is what main's older sections hold;
        # deleting it leaves the section the tag's copy to the letter, and the file cannot tell that
        # from a deletion of the note.
        self.changelog.write_text(TAGGED.replace(
            "- Another thing that shipped.\n",
            "- Another thing that shipped.\n- A highlight written after the release.\n"))

        # Act
        code, said = judged(self.root, self.changelog, "- A highlight written after the release.\n", "")

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

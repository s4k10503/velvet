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


class AgainstTheTag(unittest.TestCase):
    """A released section is held to the newest `-main` tag on the remote that HEAD descends from.

    The growth reading alone let a line deleted from a published note run, and a reorder; a reword
    it refused without naming what the note was changed away from.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="closed-version-tag-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.changelog = self.root / CHANGELOG_REL
        self.changelog.parent.mkdir(parents=True)
        self.changelog.write_text(TAGGED)
        for args in (["init", "--quiet"],
                     ["config", "user.email", "t@example.com"], ["config", "user.name", "t"],
                     ["add", CHANGELOG_REL], ["commit", "--quiet", "-m", "release"],
                     ["tag", "v2.0.0-main"], ["remote", "add", "origin", str(self.root)]):
            git(self.root, *args)

    def test_Given_AReleasedLineDeleted_When_Judged_Then_ItIsRefusedNamingTheTag(self):
        # Act
        code, said = judged(self.root, self.changelog, "- Another thing that shipped.\n", "")

        # Assert
        self.assertEqual((code, "v2.0.0-main" in said), (2, True))

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


if __name__ == "__main__":
    unittest.main(verbosity=2)

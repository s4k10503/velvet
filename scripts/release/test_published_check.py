#!/usr/bin/env python3
"""Unit tests for published_check.py.

Each decision is separated from the git readings so most of these run without a repository in the
state under test. The readings get a repository built for them, because they are what all three
callers go through and a fault in them answers "published" — the same answer a clean base gives.

Run: python3 scripts/release/test_published_check.py
"""

import contextlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import published_check
from published_check import (
    CHANGELOG_PATH,
    PACKAGE_JSON_PATH,
    consistency_reason,
    drain_reason,
    publication_reason,
    reopened_by,
    unpublished_reason,
)

PUBLISHED = """# Changelog

## [Unreleased]

### Added

- Something not yet released.

## [2.0.1] - 2026-08-08

### Highlights

- A fix.
"""

UNDATED = PUBLISHED.replace("## [2.0.1] - 2026-08-08", "## [2.0.1]")

AHEAD = PUBLISHED.replace("## [Unreleased]", "## [2.1.0] - 2026-08-09")

MISSING = """# Changelog

## [Unreleased]

## [2.0.0] - 2026-08-02

### Highlights

- The one before.
"""

TAGS = {"v2.0.0", "v2.0.0-main", "v2.0.1", "v2.0.1-main"}

# Two open sections with a break waiting in each of two subsections. The `### Changed` one is last in
# its subsection here and is not last in DRAINED, so the drain readings compare two spellings of one
# entry and only a split that ends an entry at the heading below it matches them.
WAITING = """# Changelog

## [Unreleased]

### Added

- Something a minor may ship.

### Changed

- Something else a minor may ship.

## [Unreleased — breaking]

### Changed

- An API a caller has to edit around.

### Fixed

- Behaviour a working application would notice.

## [2.0.1] - 2026-08-08

### Highlights

- A fix.
"""

# The release CONTRIBUTING.md prescribes: entries moved up, heading left standing with none, rename last.
DRAINED = """# Changelog

## [3.0.0] - 2026-09-01

### Added

- Something a minor may ship.

### Changed

- An API a caller has to edit around.
- Something else a minor may ship.

### Fixed

- Behaviour a working application would notice.

## [Unreleased — breaking]

## [2.0.1] - 2026-08-08

### Highlights

- A fix.
"""

# A major closed with the section untouched, which is what forgetting to drain it looks like.
LEFT_BEHIND = WAITING.replace("## [Unreleased]", "## [3.0.0] - 2026-09-01")

# The same drain with one entry dropped on the way instead of carried.
DROPPED = DRAINED.replace("- An API a caller has to edit around.\n", "")

# A minor that takes a waiting break with it.
SHIPPED_BY_A_MINOR = DRAINED.replace("## [3.0.0] - 2026-09-01", "## [2.2.0] - 2026-09-01")

# A minor closing with the section untouched. The trunk carries the code those entries describe,
# so this ships them.
MINOR = WAITING.replace("## [Unreleased]", "## [2.2.0] - 2026-09-01")

# A line cut before the breaking section existed, which is what the maintenance line is.
NO_BREAKING_SECTION = WAITING[:WAITING.index("## [Unreleased — breaking]")] + "## [2.0.1] - 2026-08-08\n"
NO_BREAKING_SECTION_MINOR = NO_BREAKING_SECTION.replace("## [Unreleased]", "## [2.2.0] - 2026-09-01")

# The same drain with one entry copy-edited on the way instead of carried across unchanged.
REWORDED_ON_THE_WAY = DRAINED.replace("- An API a caller has to edit around.",
                                      "- An API that callers have to edit around.")

# The major above, closing in the same change as a patch cut off the maintenance line. The whole
# section moved into 3.0.0, which is what a rule reading only the section's before and after charges
# to 2.1.1.
MAJOR_BESIDE_A_PATCH = DRAINED.replace(
    "## [2.0.1] - 2026-08-08",
    "## [2.1.1] - 2026-08-30\n\n### Highlights\n\n- A backported fix.\n\n## [2.0.1] - 2026-08-08")

# One entry decided not to be breaking after all, in a change that closes nothing.
RECLASSIFIED = WAITING.replace(
    "### Changed\n\n- Something else a minor may ship.",
    "### Changed\n\n- An API a caller has to edit around.\n- Something else a minor may ship.",
).replace("### Changed\n\n- An API a caller has to edit around.\n\n### Fixed", "### Fixed")


def package_json(version="2.0.1"):
    return json.dumps({"name": "com.velvet.core", "version": version} if version
                      else {"name": "com.velvet.core"})


class ConsistencyDecisionTests(unittest.TestCase):
    def test_Given_ThePackageVersionNamesTheNewestClosedSection_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertIsNone(consistency_reason(PUBLISHED, package_json()))

    def test_Given_ThePackageVersionHasNoSection_When_Decided_Then_TheAbsentSectionIsNamed(self):
        # Arrange — package.json bumped without the CHANGELOG being closed.
        reason = consistency_reason(MISSING, package_json())

        # Assert
        self.assertIn("has no '## [2.0.1]' section", reason)

    def test_Given_TheSectionIsOpen_When_Decided_Then_TheDateIsAskedFor(self):
        # Arrange — a heading written without its date builds a note nobody can date.
        reason = consistency_reason(UNDATED, package_json())

        # Assert
        self.assertIn("carries no date", reason)

    def test_Given_ANewerVersionIsClosedAbove_When_Decided_Then_TheBumpIsAskedFor(self):
        # Arrange — a release whose CHANGELOG landed without its package.json bump. Reading only the
        # version package.json names would report the older, published one and pass.
        reason = consistency_reason(AHEAD, package_json())

        # Assert
        self.assertIn("bump package.json to 2.1.0", reason)

    def test_Given_PackageJsonDeclaresNoVersion_When_Decided_Then_ThatIsTheReason(self):
        # Act / Assert
        self.assertIn("declares no version", consistency_reason(PUBLISHED, package_json(version=None)))


class DrainDecisionTests(unittest.TestCase):
    """What a release may do to the section that holds what waits for a major."""

    def test_Given_AMajorClosingOverItsWaitingBreaks_When_Decided_Then_TheOnesLeftBehindAreNamed(self):
        # Arrange — the version closes, package.json bumps, and the entries stay put. The note is
        # built from the closed section alone, so each one ships described by nothing.
        reason = drain_reason(WAITING, LEFT_BEHIND)

        # Assert
        self.assertIn("3.0.0 is a major and '## [Unreleased — breaking]' still lists 2 entries",
                      reason)

    def test_Given_AMajorThatMovesTheWholeSectionIntoItself_When_Decided_Then_ThereIsNoReason(self):
        # Arrange — the entry moved out of `### Changed` is last in its subsection at the base and
        # not at the result, so the two spellings match only for a reader that ends an entry at the
        # heading below it.
        reason = drain_reason(WAITING, DRAINED)

        # Assert
        self.assertIsNone(reason)

    def test_Given_AMajorThatDropsAnEntryInsteadOfCarryingIt_When_Decided_Then_TheLostOneIsNamed(self):
        # Arrange — the section is empty at the result either way, so emptiness cannot separate this
        # from the case above.
        reason = drain_reason(WAITING, DROPPED)

        # Assert
        self.assertIn("1 entry left '## [Unreleased — breaking]' and no entry of 3.0.0 carries "
                      "that text", reason)

    def test_Given_AMinorClosingOverAnEntryItTakesWithIt_When_Decided_Then_ItIsRefused(self):
        # Arrange — the same drain under a version that is not a major, which is a break shipped to
        # callers who read the range as compatible.
        reason = drain_reason(WAITING, SHIPPED_BY_A_MINOR)

        # Assert
        self.assertIn("2.2.0 is not a major", reason)

    def test_Given_AMinorClosingOverAFullSection_When_Decided_Then_ItIsRefused(self):
        # Arrange — the section is exactly where the change found it, and the tree it releases holds
        # the code those entries describe, so the minor ships every one of them undescribed.
        reason = drain_reason(WAITING, MINOR)

        # Assert
        self.assertIn("2.2.0 is not a major and '## [Unreleased — breaking]' still lists", reason)

    def test_Given_AMinorOnALineWithNoBreakingSection_When_Decided_Then_ThereIsNoReason(self):
        # Arrange — the maintenance line, which was cut before the section existed and so carries
        # neither the section nor the code its entries describe.
        reason = drain_reason(NO_BREAKING_SECTION, NO_BREAKING_SECTION_MINOR)

        # Assert
        self.assertIsNone(reason)

    def test_Given_AMajorDrainThatRewordedAnEntryOnTheWay_When_Decided_Then_TheRepairIsNamed(self):
        # Arrange — the entry arrived, so the break is described and this refusal is a false one. It
        # stands because the reading sees only that the text is gone, which a drop leaves too; what
        # the message owes the reader is the way out.
        reason = drain_reason(WAITING, REWORDED_ON_THE_WAY)

        # Assert
        self.assertIn("make any wording change in a change that closes no version", reason)

    def test_Given_AMajorClosingBesideAPatchInOneChange_When_Decided_Then_ThereIsNoReason(self):
        # Arrange — 3.0.0 drains the section correctly and 2.1.1 closes below it. Both are new here,
        # so both are asked, and the section's own before and after cannot say which one emptied it.
        reason = drain_reason(WAITING, MAJOR_BESIDE_A_PATCH)

        # Assert
        self.assertIsNone(reason)

    def test_Given_AnEntryReclassifiedByAChangeThatClosesNothing_When_Decided_Then_ThereIsNoReason(self):
        # Arrange — deciding an entry was never breaking is an edit of its own, and the file it
        # leaves is the file a minor that shipped the break would leave.
        reason = drain_reason(WAITING, RECLASSIFIED)

        # Assert
        self.assertIsNone(reason)


class PublicationDecisionTests(unittest.TestCase):
    def test_Given_TheVersionIsClosedAndTagged_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertIsNone(publication_reason(PUBLISHED, package_json(), TAGS))

    def test_Given_TheVersionIsClosedAndUntagged_When_Decided_Then_TheDispatchIsNamedWithARef(self):
        # Arrange — the release commit is on main and the dispatch never ran. A dispatch without --ref
        # builds from the branch tip, which is the harm the reason is about.
        reason = publication_reason(PUBLISHED, package_json(), {"v2.0.0", "v2.0.0-main"})

        # Assert
        self.assertIn("gh workflow run upm.yml --ref release/2.0.1 -f version=2.0.1", reason)

    def test_Given_AResultReopeningTheUnpublishedVersion_When_Decided_Then_ItAnswersForIt(self):
        # Arrange — a withdrawn release leaves its base closing a version no tag answers for. The
        # change that reopens the section is the repair, and asked of the base alone it refuses
        # itself and every merge waiting behind it.
        reopened = PUBLISHED.replace("## [2.0.1] - 2026-08-08", "## [Unreleased]")

        # Act / Assert
        self.assertTrue(reopened_by(PUBLISHED, {"v2.0.0", "v2.0.0-main"}, reopened))

    def test_Given_AResultStillClosingIt_When_Decided_Then_TheRefusalStands(self):
        # Arrange — the ordinary change on top of an unpublished release, which is what the refusal
        # is for and has to keep refusing.
        # Act / Assert
        self.assertFalse(reopened_by(PUBLISHED, {"v2.0.0", "v2.0.0-main"}, PUBLISHED))

    def test_Given_AResultReopeningOnlyOneOfTwo_When_Decided_Then_TheRefusalStands(self):
        # Arrange — two dated sections and no tag for either. Reopening the newer leaves the older
        # closed and unanswered, which a reading of "did anything change" would let through.
        two = PUBLISHED.replace(
            "## [2.0.1] - 2026-08-08",
            "## [2.0.1] - 2026-08-08\n\n### Highlights\n\n- A fix.\n\n## [2.0.0] - 2026-08-02")
        reopened = two.replace("## [2.0.1] - 2026-08-08", "## [Unreleased — again]")

        # Act / Assert
        self.assertFalse(reopened_by(two, {"v1.0.0", "v1.0.0-main"}, reopened))

    def test_Given_TheTagsCarryANameThatMerelyStartsTheSame_When_Decided_Then_ItIsStillUnpublished(self):
        # Arrange — a prefix match would read v2.0.1-main as the release tag it is not.
        reason = publication_reason(PUBLISHED, package_json(), {"v2.0.10", "v2.0.1-main"})

        # Assert
        self.assertIn("v2.0.1 closed in the CHANGELOG and never published", reason)

    def test_Given_ATreeConsistencyAlreadyRefuses_When_Decided_Then_TheMergePathStaysOpen(self):
        # Arrange — AHEAD rather than UNDATED: a tree with no dated section at all answers None from the
        # tag branch too, so it cannot tell the suppression from its absence. Here 2.1.0 is closed above
        # the 2.0.1 package.json names, and both are untagged — so without the suppression every merge is
        # refused, including the bump that repairs it, with no direct push to main to escape through.
        reason = publication_reason(AHEAD, package_json(), {"v2.0.0"})

        # Assert
        self.assertIsNone(reason)

    def test_Given_TwoVersionsAreUnpublished_When_Decided_Then_TheOlderIsTheOneToDispatch(self):
        # Arrange — the CHANGELOG is newest-first, so naming the first would send the maintainer to
        # publish 2.1.0 before 2.0.1: the split force-push would leave the upm branch on the older
        # package, and the note's compare range would run backwards.
        reason = publication_reason(AHEAD, package_json(version="2.1.0"), {"v2.0.0"})

        # Assert
        self.assertIn("--ref release/2.0.1 -f version=2.0.1", reason)

    def test_Given_AnEarlierVersionWasSkippedPast_When_Decided_Then_ItIsStillNamed(self):
        # Arrange — 2.1.0 closed and published above an unpublished 2.0.1. Asking only about the version
        # package.json names would take the question off 2.0.1 for good.
        reason = publication_reason(AHEAD, package_json(version="2.1.0"),
                                    {"v2.0.0", "v2.1.0"})

        # Assert
        self.assertIn("v2.0.1", reason)

    def test_Given_ARemoteWithNoReleaseTagAtAll_When_Decided_Then_ItIsNotCalledUnpublished(self):
        # Arrange — a copy with no release history, where naming a dispatch would be an instruction
        # with nothing behind it.
        reason = publication_reason(PUBLISHED, package_json(), {"some-marker"})

        # Assert
        self.assertIsNone(reason)


class DispatchMirrorTests(unittest.TestCase):
    """The two things this module restates from upm.yml: the tag it pushes, and the input it takes.

    A rename of either fails in a direction nothing else reports. The tag: the history's earlier `v`
    tags keep `RELEASE_TAG` matching, so the guard does not fall silent — it jams shut, refusing every
    merge over a version that was in fact published under the new name. The input: the command the
    refusal prints is the maintainer's whole instruction, and a dispatch naming an input the workflow
    no longer declares answers 422.
    """

    def workflow(self):
        return (Path(published_check.REPO_ROOT) / ".github" / "workflows" / "upm.yml").read_text()

    def test_Given_TheDispatchWorkflow_When_ItsTagIsRead_Then_ItIsStillTheSpellingThisModuleLooksFor(self):
        # Assert
        self.assertIn('TAG="v${VERSION}"', self.workflow())

    def test_Given_TheDispatchWorkflow_When_ItsInputsAreRead_Then_TheOneTheRepairNamesIsDeclared(self):
        # Arrange — the repair prints `-f version=…`, which is this input by name. Matched as a whole
        # declaration line: a rename to release_version still contains "version:".
        inputs = self.workflow().split("workflow_dispatch:", 1)[1].split("permissions:", 1)[0]
        declared = [line.strip() for line in inputs.splitlines() if line.strip().endswith(":")]

        # Assert
        self.assertIn("version:", declared)


def git(directory, *args):
    subprocess.run(["git", "-C", str(directory), *args], check=True, capture_output=True, text=True)


class GitReadingTests(unittest.TestCase):
    """The half that reads a repository, built rather than mocked: a stub for `git show` would be a
    stub for the thing under test."""

    def setUp(self):
        self.stack = contextlib.ExitStack()
        self.addCleanup(self.stack.close)

    def repository(self, changelog=PUBLISHED, package=None, tags=("v2.0.1",)):
        path = Path(self.stack.enter_context(tempfile.TemporaryDirectory()))
        git(path, "init", "--quiet", "--initial-branch", "main")
        git(path, "config", "user.email", "test@example.invalid")
        git(path, "config", "user.name", "Test")
        for relative, text in ((CHANGELOG_PATH, changelog), (PACKAGE_JSON_PATH, package or package_json())):
            written = path / relative
            written.parent.mkdir(parents=True, exist_ok=True)
            written.write_text(text)
        git(path, "add", "-A")
        git(path, "commit", "--quiet", "-m", "state")
        for tag in tags:
            git(path, "tag", tag)
        git(path, "remote", "add", "origin", str(path))
        return path

    def test_Given_ARepositoryHoldingAPublishedVersion_When_Read_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertIsNone(unpublished_reason(self.repository(), "HEAD"))

    def test_Given_ARepositoryWhoseRemoteLacksTheTag_When_Read_Then_TheDispatchIsNamed(self):
        # Arrange — the shape main was in for a day: the release commit landed, nothing tagged.
        reason = unpublished_reason(self.repository(tags=("v2.0.0",)), "HEAD")

        # Assert
        self.assertIn("--ref release/2.0.1 -f version=2.0.1", reason)

    def forward_merge(self, tags):
        """A repository whose result records a version its base did not, under `tags`.

        Driven through the command rather than `drain_reason`, because the reading under test is
        whether the tags reach it at all: called directly, a tree that does not pass them raises
        instead of deciding, and a raise separates nothing.
        """
        recorded = WAITING.replace(
            "## [2.0.1] - 2026-08-08",
            "## [2.1.3] - 2026-08-27\n\n### Fixed\n\n- A backported fix.\n\n## [2.0.1] - 2026-08-08")
        path = self.repository(changelog=WAITING, package=package_json(version="2.1.3"), tags=tags)
        (path / CHANGELOG_PATH).write_text(recorded)
        git(path, "commit", "--quiet", "-a", "-m", "forward merge")
        argv = ["published_check.py", "--project", str(path), "--base", "HEAD~1", "--result", "HEAD"]
        err = io.StringIO()
        with mock.patch.object(sys, "argv", argv), contextlib.redirect_stderr(err), \
                contextlib.redirect_stdout(io.StringIO()):
            published_check.main()
        return err.getvalue()

    def test_Given_AVersionTheRemoteAlreadyTags_When_Read_Then_ItIsRecordedNotClosed(self):
        # Arrange — merging a maintenance line forward brings its released sections across, and the
        # merge publishes nothing: the line's own dispatch already did.
        said = self.forward_merge(("v2.0.1", "v2.1.3", "v2.1.3-main"))

        # Act / Assert
        self.assertNotIn("2.1.3", said)

    # GREEN_ON_BASE(characterization): an untagged version closed before the tags reached the reading
    # and closes after, and it is the half the change must not buy its silence with.
    def test_Given_AVersionNoTagAnswersFor_When_Read_Then_ItIsStillClosing(self):
        # Arrange — the same shape with no tag, which is a release rather than a record.
        said = self.forward_merge(("v2.0.1",))

        # Act / Assert
        self.assertIn("2.1.3 is not a major", said)

    def test_Given_ARevisionThatDoesNotExist_When_Read_Then_ItAnswersCleanRatherThanRaising(self):
        # Arrange — a branch that was never fetched is ordinary on a developer's machine, and refusing
        # there would train the reader to work around the guard.
        repository = self.repository()

        # Act / Assert
        self.assertIsNone(unpublished_reason(repository, "origin/nothing-like-this"))


if __name__ == "__main__":
    unittest.main(verbosity=2)

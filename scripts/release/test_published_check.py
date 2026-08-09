#!/usr/bin/env python3
"""Unit tests for published_check.py.

Each decision is separated from the git readings so most of these run without a repository in the
state under test. The readings get a repository built for them, because they are what all three
callers go through and a fault in them answers "published" — the same answer a clean base gives.

Run: python3 scripts/release/test_published_check.py
"""

import contextlib
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from published_check import (
    CHANGELOG_PATH,
    PACKAGE_JSON_PATH,
    consistency_reason,
    publication_reason,
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

    def test_Given_TheTagsCarryANameThatMerelyStartsTheSame_When_Decided_Then_ItIsStillUnpublished(self):
        # Arrange — a prefix match would read v2.0.1-main as the release tag it is not.
        reason = publication_reason(PUBLISHED, package_json(), {"v2.0.10", "v2.0.1-main"})

        # Assert
        self.assertIn("v2.0.1 closed in the CHANGELOG and never published", reason)

    def test_Given_ATreeConsistencyAlreadyRefuses_When_Decided_Then_TheMergePathStaysOpen(self):
        # Arrange — the three consistency faults are repaired by a commit, so refusing merges for them
        # would leave the repair itself unmergeable with no direct push to main to escape through.
        reason = publication_reason(UNDATED, package_json(), {"v2.0.0"})

        # Assert
        self.assertIsNone(reason)

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

    def test_Given_ARevisionThatDoesNotExist_When_Read_Then_ItAnswersCleanRatherThanRaising(self):
        # Arrange — a branch that was never fetched is ordinary on a developer's machine, and refusing
        # there would train the reader to work around the guard.
        repository = self.repository()

        # Act / Assert
        self.assertIsNone(unpublished_reason(repository, "origin/nothing-like-this"))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for published_check.py's decision.

The state under test lasted an afternoon on this repository and cost a release: v2.0.1 was closed in
the CHANGELOG, merged, and left undispatched while five more pull requests landed on top of it. The
decision is separated from the git readings so these run without a repository in that state.

Run: python3 scripts/release/test_published_check.py
"""

import json
import unittest

from published_check import publication_reason

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


def package_json(version="2.0.1"):
    return json.dumps({"name": "com.velvet.core", "version": version} if version
                      else {"name": "com.velvet.core"})


class PublicationDecisionTests(unittest.TestCase):
    def test_Given_TheVersionIsClosedAndTagged_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertIsNone(publication_reason(PUBLISHED, package_json(), {"v2.0.0", "v2.0.1"}))

    def test_Given_TheVersionIsClosedAndUntagged_When_Decided_Then_TheDispatchIsNamed(self):
        # Arrange — the release commit is on main and the dispatch never ran.
        reason = publication_reason(PUBLISHED, package_json(), {"v2.0.0"})

        # Assert
        self.assertIn("gh workflow run upm.yml -f version=2.0.1", reason)

    def test_Given_TheTagsCarryAnUnrelatedName_When_Decided_Then_TheVersionIsStillUnpublished(self):
        # Arrange — a prefix match would read v2.0.1-main as the release tag it is not.
        reason = publication_reason(PUBLISHED, package_json(), {"v2.0.10", "v2.0.1-main"})

        # Assert
        self.assertIsNotNone(reason)

    def test_Given_ThePackageVersionHasNoSection_When_Decided_Then_TheAbsentSectionIsNamed(self):
        # Arrange — package.json bumped without the CHANGELOG being closed.
        reason = publication_reason(MISSING, package_json(), {"v2.0.0"})

        # Assert
        self.assertIn("has no '## [2.0.1]' section", reason)

    def test_Given_TheSectionIsOpen_When_Decided_Then_TheDateIsAskedForRatherThanTheDispatch(self):
        # Arrange — a heading written without its date builds a note nobody can date.
        reason = publication_reason(UNDATED, package_json(), {"v2.0.0"})

        # Assert
        self.assertIn("carries no date", reason)

    def test_Given_PackageJsonDeclaresNoVersion_When_Decided_Then_ThatIsTheReason(self):
        # Act / Assert
        self.assertIn("declares no version",
                      publication_reason(PUBLISHED, package_json(version=None), set()))

    def test_Given_ANewerVersionIsClosedAbove_When_Decided_Then_TheBumpIsAskedFor(self):
        # Arrange — a release whose CHANGELOG landed without its package.json bump. Reading only the
        # version package.json names would report the older, published one and pass.
        reason = publication_reason(AHEAD, package_json(), {"v2.0.1"})

        # Assert
        self.assertIn("bump package.json to 2.1.0", reason)


if __name__ == "__main__":
    unittest.main(verbosity=2)

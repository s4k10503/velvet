#!/usr/bin/env python3
"""Unit tests for the version derivation `published_check.py` asks of a closing change.

Kept apart from test_published_check.py because these cases name functions only the derivation
provides: in that file a module-level import of them stops every case there from loading on a tree
without them, including those that read the command and could answer by an assertion.

Run: python3 scripts/test_quality/run_suite.py scripts/release/test_derived_version.py
"""

import unittest

from published_check import bump_of, derived_version_reason
from test_published_check import DRAINED, MINOR, MISSING, PUBLISHED, WAITING


class DerivedVersionTests(unittest.TestCase):
    """Whether the version a change closes is the one the tree derives."""

    # Nothing in the open section but a fix, which is what a patch ships.
    FIX_ONLY = PUBLISHED.replace("### Added", "### Fixed")

    @staticmethod
    def closing(changelog, version):
        return changelog.replace("## [Unreleased]", f"## [{version}] - 2026-09-01")

    def test_Given_APatchClosingOverAnAddedEntry_When_Decided_Then_TheMinorIsNamed(self):
        # Act
        reason = derived_version_reason(PUBLISHED, self.closing(PUBLISHED, "2.0.2"), lambda _: [])

        # Assert
        self.assertIn("the version this tree derives is 2.1.0, a minor", reason)

    def test_Given_AMinorClosingOverAnAddedEntry_When_Decided_Then_ThereIsNoReason(self):
        # Act
        reason = derived_version_reason(PUBLISHED, self.closing(PUBLISHED, "2.1.0"), lambda _: [])

        # Assert
        self.assertIsNone(reason)

    def test_Given_APatchClosingOverAGainedMember_When_Decided_Then_TheMemberIsQuoted(self):
        # Act
        reason = derived_version_reason(
            self.FIX_ONLY, self.closing(self.FIX_ONLY, "2.0.2"), lambda _: ["method Velvet.V.B(): System.Void"])

        # Assert
        self.assertIn("method Velvet.V.B(): System.Void", reason)

    def test_Given_APatchClosingOverFixesOnly_When_Decided_Then_ThereIsNoReason(self):
        # Act
        reason = derived_version_reason(self.FIX_ONLY, self.closing(self.FIX_ONLY, "2.0.2"), lambda _: [])

        # Assert
        self.assertIsNone(reason)

    def test_Given_TheGainedMembersAskedAgainstTheReleaseBelow_When_Decided_Then_ItIsThatVersionPassed(self):
        # Arrange
        asked = []

        # Act
        derived_version_reason(self.FIX_ONLY, self.closing(self.FIX_ONLY, "2.0.2"),
                               lambda previous: asked.append(previous) or [])

        # Assert
        self.assertEqual(["2.0.1"], asked)

    def test_Given_AMajorDrainingTheBreakingSection_When_Decided_Then_ThereIsNoReason(self):
        # Act
        reason = derived_version_reason(WAITING, DRAINED, lambda _: [])

        # Assert
        self.assertIsNone(reason)

    def test_Given_AMajorClosingOverNoBreakingEntry_When_Decided_Then_TheDerivedMinorIsNamed(self):
        # Act
        reason = derived_version_reason(PUBLISHED, self.closing(PUBLISHED, "3.0.0"), lambda _: [])

        # Assert
        self.assertIn("3.0.0 closes as a major over 2.0.1", reason)

    def test_Given_AMinorClosingOverTheBreakingSection_When_Decided_Then_TheMajorIsNamed(self):
        # Act
        reason = derived_version_reason(WAITING, MINOR, lambda _: [])

        # Assert
        self.assertIn("the version this tree derives is 3.0.0, a major", reason)

    def test_Given_AVersionTheRemoteAlreadyTags_When_Decided_Then_ItIsRecordedNotJudged(self):
        # Act
        reason = derived_version_reason(
            PUBLISHED, self.closing(PUBLISHED, "2.0.2"), lambda _: [], tags={"v2.0.2"})

        # Assert
        self.assertIsNone(reason)

    def test_Given_TheOldestVersionInTheFile_When_Decided_Then_ItIsNobodysBump(self):
        # Act
        reason = derived_version_reason(MISSING, MISSING, lambda _: [])

        # Assert
        self.assertIsNone(reason)


class BumpOfTests(unittest.TestCase):
    RELEASED = ["3.1.0", "3.0.1", "3.0.0", "2.1.4"]

    def test_Given_ANewMajor_When_Asked_Then_ItIsAMajor(self):
        # Act / Assert
        self.assertEqual("major", bump_of(self.RELEASED, "3.0.0"))

    def test_Given_ANewMinor_When_Asked_Then_ItIsAMinor(self):
        # Act / Assert
        self.assertEqual("minor", bump_of(self.RELEASED, "3.1.0"))

    def test_Given_ANewPatch_When_Asked_Then_ItIsAPatch(self):
        # Act / Assert
        self.assertEqual("patch", bump_of(self.RELEASED, "3.0.1"))

    def test_Given_TheOldestRelease_When_Asked_Then_ItIsNobodysBump(self):
        # Act / Assert
        self.assertIsNone(bump_of(self.RELEASED, "2.1.4"))


if __name__ == "__main__":
    unittest.main()

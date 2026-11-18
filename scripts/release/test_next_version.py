#!/usr/bin/env python3
"""Unit tests for next_version.py.

Run: python3 scripts/test_quality/run_suite.py scripts/release/test_next_version.py
"""

import subprocess
import unittest

from next_version import (
    Unreadable,
    api_gained,
    public_api_gained_since,
    added_entries,
    derive,
    newest_reachable_release,
)
import next_version

ENTRY = "- An entry."


class DeriveTests(unittest.TestCase):
    def test_Given_ABreakingEntry_When_Derived_Then_TheNextMajorIsNamed(self):
        # Act
        derivation = derive("2.1.4", ["- A break."], [], [])

        # Assert
        self.assertEqual("3.0.0", derivation.version)

    def test_Given_ABreakingEntryBesideAGainedMember_When_Derived_Then_TheMajorWins(self):
        # Act
        derivation = derive("2.1.4", ["- A break."], ["method X"], [ENTRY])

        # Assert
        self.assertEqual("major", derivation.bump)

    def test_Given_AGainedMemberAndNothingElse_When_Derived_Then_TheNextMinorIsNamed(self):
        # Act
        derivation = derive("2.1.4", [], ["method X"], [])

        # Assert
        self.assertEqual("2.2.0", derivation.version)

    def test_Given_AnAddedEntryAndNoGainedMember_When_Derived_Then_TheNextMinorIsNamed(self):
        # Act
        derivation = derive("3.0.0", [], [], [ENTRY])

        # Assert
        self.assertEqual("3.1.0", derivation.version)

    def test_Given_NothingToReport_When_Derived_Then_TheNextPatchIsNamed(self):
        # Act
        derivation = derive("3.0.0", [], [], [])

        # Assert
        self.assertEqual("3.0.1", derivation.version)

    def test_Given_TheReasonForAMinor_When_Derived_Then_TheFirstGainedLineIsQuoted(self):
        # Act
        derivation = derive("3.0.0", [], ["method Velvet.V.Fresh(): System.Void"], [])

        # Assert
        self.assertIn("method Velvet.V.Fresh(): System.Void", derivation.reason)

    def test_Given_AVersionWithTwoDigitParts_When_Derived_Then_ItIsBumpedNumerically(self):
        # Act
        derivation = derive("2.9.10", [], [], [])

        # Assert
        self.assertEqual("2.9.11", derivation.version)


class NormalisationTests(unittest.TestCase):
    """What the dump format may change without a member being gained."""

    def test_Given_TheSameMemberFiledUnderAnAssembly_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("method Velvet.V.Text(System.String): Velvet.VNode",
                            "[Velvet] method Velvet.V.Text(System.String): Velvet.VNode")

        # Assert
        self.assertEqual([], gained)

    def test_Given_AParameterNewlyAnnotatedNullable_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.Text(System.String): Velvet.VNode",
                            "[Velvet] method Velvet.V.Text(System.String?): Velvet.VNode")

        # Assert
        self.assertEqual([], gained)

    def test_Given_AParameterNewlyAnnotatedOblivious_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.Text(System.Object): Velvet.VNode",
                            "[Velvet] method Velvet.V.Text(System.Object~): Velvet.VNode")

        # Assert
        self.assertEqual([], gained)

    def test_Given_AGenericTypeNewlyListingItsParameterNames_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("[Velvet] ctor Velvet.Ref`1.ctor(): System.Void",
                            "[Velvet] ctor Velvet.Ref`1[T].ctor(): System.Void")

        # Assert
        self.assertEqual([], gained)

    def test_Given_ATypeDeclarationNewlyListingItsParameterNames_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("[Velvet] type Velvet.Store`1", "[Velvet] type Velvet.Store`1[TState]")

        # Assert
        self.assertEqual([], gained)

    def test_Given_AChangedParameterType_When_Compared_Then_TheNewSignatureIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.Text(System.String): Velvet.VNode",
                            "[Velvet] method Velvet.V.Text(System.Int32): Velvet.VNode")

        # Assert
        self.assertEqual(["method Velvet.V.Text(System.Int32): Velvet.VNode"], gained)

    def test_Given_ANewMember_When_Compared_Then_ItIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.A(): System.Void",
                            "[Velvet] method Velvet.V.A(): System.Void\n"
                            "[Velvet] method Velvet.V.B(): System.Void")

        # Assert
        self.assertEqual(["method Velvet.V.B(): System.Void"], gained)

    def test_Given_ARemovedMember_When_Compared_Then_NothingIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.A(): System.Void\n"
                            "[Velvet] method Velvet.V.B(): System.Void",
                            "[Velvet] method Velvet.V.A(): System.Void")

        # Assert
        self.assertEqual([], gained)

    def test_Given_ANewGenericArgumentThatIsAQualifiedType_When_Compared_Then_TheMemberIsGained(self):
        # Act
        gained = api_gained("[Velvet] method Velvet.V.A(System.Func`1[System.String]): System.Void",
                            "[Velvet] method Velvet.V.A(System.Func`1[System.Int32]): System.Void")

        # Assert
        self.assertEqual(["method Velvet.V.A(System.Func`1[System.Int32]): System.Void"], gained)


class GainedSinceTests(unittest.TestCase):
    @staticmethod
    def reading(missing):
        def read_at(project, ref, path):
            if ref == missing:
                raise subprocess.CalledProcessError(128, "git", stderr="fatal: invalid object name")
            return "[Velvet] method Velvet.V.A(): System.Void"
        return read_at

    def test_Given_TheReleaseTagIsMissing_When_TheGainIsRead_Then_TheTagIsNamedAsUnreadable(self):
        # Act / Assert
        with self.assertRaisesRegex(Unreadable, "cannot be read at v3.0.0-main"):
            public_api_gained_since(".", "3.0.0", "HEAD", self.reading("v3.0.0-main"))

    def test_Given_TheRevisionIsMissing_When_TheGainIsRead_Then_TheRevisionIsNamedAsUnreadable(self):
        # Act / Assert
        with self.assertRaisesRegex(Unreadable, "cannot be read at HEAD"):
            public_api_gained_since(".", "3.0.0", "HEAD", self.reading("HEAD"))

    def test_Given_BothReadable_When_TheGainIsRead_Then_TheDifferenceIsReturned(self):
        # Act
        gained = public_api_gained_since(".", "3.0.0", "HEAD", self.reading(None))

        # Assert
        self.assertEqual([], gained)


class AddedEntriesTests(unittest.TestCase):
    CHANGELOG = """# Changelog

## [Unreleased]

### Added

- A new utility.

### Fixed

- A fix.

## [1.0.0] - 2026-01-01
"""

    def test_Given_AnOpenSectionWithAnAddedHeading_When_Read_Then_OnlyThoseEntriesAreReturned(self):
        # Act
        found = [entry.strip() for entry in added_entries(self.CHANGELOG, "Unreleased")]

        # Assert
        self.assertEqual(["- A new utility."], found)

    def test_Given_NoSuchSection_When_Read_Then_NothingIsReturned(self):
        # Act
        found = added_entries(self.CHANGELOG, "9.9.9")

        # Assert
        self.assertEqual([], found)


class NewestReleaseTests(unittest.TestCase):
    def test_Given_TagsOfTwoLines_When_TheNewestIsAsked_Then_TheHighestVersionIsNamedNumerically(self):
        # Arrange
        def git(project, *args):
            return "v2.9.0-main\nv2.10.0-main\nv1.0.0-main\nvnext-main\n"

        # Act
        newest = newest_reachable_release(".", "HEAD", git)

        # Assert
        self.assertEqual("2.10.0", newest)

    def test_Given_ANewerTagOnTheLineThatTheRevisionDoesNotHave_When_TheNewestIsAsked_Then_ItIsRefused(self):
        # Arrange
        def git(project, *args):
            return "v2.1.4-main\n" if "--merged" in args else "v2.1.4-main\nv2.1.5-main\nv3.0.0-main\n"

        # Act / Assert
        with self.assertRaisesRegex(Unreadable, "v2.1.5-main"):
            newest_reachable_release(".", "HEAD", git)

    def test_Given_ANewerTagOnAnotherLine_When_TheNewestIsAsked_Then_TheLinesOwnIsNamed(self):
        # Arrange
        def git(project, *args):
            return "v2.1.4-main\n" if "--merged" in args else "v2.1.4-main\nv3.0.0-main\n"

        # Act
        newest = newest_reachable_release(".", "HEAD", git)

        # Assert
        self.assertEqual("2.1.4", newest)

    def test_Given_NoReleaseTag_When_TheNewestIsAsked_Then_NoneIsReturned(self):
        # Act
        newest = newest_reachable_release(".", "HEAD", lambda project, *args: "")

        # Assert
        self.assertIsNone(newest)


class SkippedFromTests(unittest.TestCase):
    PUBLISHED = {"2.1.4", "3.0.0", "3.0.1"}

    def test_Given_APatchTwoAboveTheNewestRelease_When_Asked_Then_ThatReleaseIsNamed(self):
        # Act / Assert
        self.assertEqual(next_version.skipped_from(self.PUBLISHED, "3.0.3"), "3.0.1")

    def test_Given_AMinorTwoAboveTheNewestRelease_When_Asked_Then_ThatReleaseIsNamed(self):
        # Act / Assert
        self.assertEqual(next_version.skipped_from(self.PUBLISHED, "3.2.0"), "3.0.1")

    def test_Given_EachSuccessorOfTheNewestRelease_When_Asked_Then_NoneSkips(self):
        # Act — the first successor read as a skip, which none should be.
        skipping = next((version for version in ("3.0.2", "3.1.0", "4.0.0")
                         if next_version.skipped_from(self.PUBLISHED, version)), None)

        # Assert
        self.assertIsNone(skipping)

    def test_Given_ALinesPatchBelowANewerMajor_When_Asked_Then_ItFollowsItsOwnRelease(self):
        # Act / Assert
        self.assertIsNone(next_version.skipped_from(self.PUBLISHED, "2.1.5"))

    def test_Given_TwoDigitParts_When_Asked_Then_TheNewestIsReadNumerically(self):
        # Act / Assert
        self.assertIsNone(next_version.skipped_from({"2.1.9", "2.1.10"}, "2.1.11"))

    def test_Given_NothingPublished_When_TheFirstReleaseIsAsked_Then_NothingSkips(self):
        # Act / Assert
        self.assertIsNone(next_version.skipped_from(set(), "1.0.0"))


if __name__ == "__main__":
    unittest.main()

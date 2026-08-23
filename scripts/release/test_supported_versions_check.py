#!/usr/bin/env python3
"""Holds `supported_versions_check.py` against the three answers it has: supported, marked otherwise,
and named nowhere."""

import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import supported_versions_check as check  # noqa: E402

TABLE = """## Supported versions

| Version | Supported |
| ------- | --------- |
| 2.1.x   | ✅        |
| 2.0.x   | ❌        |
| 1.x     | ❌        |
"""


class SupportedVersionsCheckTests(unittest.TestCase):
    def test_Given_AVersionTheTableMarksSupported_When_ItIsRead_Then_NothingIsReported(self):
        # Act
        answer = check.reason("2.1.0", TABLE)
        # Assert
        self.assertIsNone(answer)

    def test_Given_AVersionOnASeriesTheTableMarksOtherwise_When_ItIsRead_Then_TheMarkIsNamed(self):
        # Act
        answer = check.reason("2.0.9", TABLE)
        # Assert
        self.assertIn("marks 2.0.x as", answer or "")

    def test_Given_AMinorTheTableHasNoRowFor_When_ItIsRead_Then_TheMissingRowIsNamed(self):
        # Arrange -- the release main can ship next, which is what the table stands still through
        # Act
        answer = check.reason("2.2.0", TABLE)
        # Assert
        self.assertIn("no row covering 2.2.0", answer or "")

    def test_Given_AMajorTheTableHasNoRowFor_When_ItIsRead_Then_TheMissingRowIsNamed(self):
        # Act
        answer = check.reason("3.0.0", TABLE)
        # Assert
        self.assertIn("no row covering 3.0.0", answer or "")

    def test_Given_ARowNamingAWholeMajor_When_AVersionOfItIsRead_Then_ThatRowAnswers(self):
        # Arrange -- `1.x` covers 1.6.0, where `2.1.x` covers only 2.1.z
        # Act
        answer = check.reason("1.6.0", TABLE)
        # Assert
        self.assertIn("marks 1.x as", answer or "")

    def test_Given_TheRepositorysOwnTable_When_ItsDeclaredVersionIsRead_Then_NothingIsReported(self):
        # Arrange
        root = Path(__file__).resolve().parents[2]
        import json
        version = json.loads((root / "Packages/com.velvet.core/package.json").read_text())["version"]
        # Act
        answer = check.reason(version, (root / "SECURITY.md").read_text())
        # Assert
        self.assertIsNone(answer)


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for the decisions neuter_check.py makes before and after its Unity runs.

The runs need a licence; the decisions do not, and it is the decisions that make a sweep's output mean
anything. Both cases below are ones where the harness would otherwise report full coverage: a filter
that ran nothing reports no holes, and a fixture already red reports every case as killed by the cut.

Run: python3 scripts/test_quality/test_neuter_check.py
"""

import importlib.util
import json
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports neuter_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "neuter_check", Path(__file__).with_name("neuter_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


neuter_check = load_module()


def declaring_sources(short_name):
    """The test sources declaring a class by that name.

    Sought as a class declaration rather than as a file name: one file may declare several fixtures, and
    the file that shares its name with one of them is not the file the others live in.
    """
    pattern = re.compile(rf"\bclass\s+{re.escape(short_name)}\b")
    return [str(path) for path in sorted(REPO_ROOT.glob("Packages/**/Tests/**/*.cs"))
            if pattern.search(path.read_text())]


class BaselineProblem(unittest.TestCase):
    def test_Given_AFilterThatRanNoCases_When_TheBaselineIsRead_Then_ItIsRefused(self):
        # Act
        problem, red = neuter_check.baseline_problem("Velvet.Tests.NotAClass", {})

        # Assert
        self.assertEqual((bool(problem), red), (True, []))

    def test_Given_ARefusedFilter_When_TheMessageIsRead_Then_ItNamesTheFilter(self):
        # Act
        problem, _ = neuter_check.baseline_problem("Velvet.Tests.NotAClass", {})

        # Assert
        self.assertIn("Velvet.Tests.NotAClass", problem)

    def test_Given_AGreenBaseline_When_ItIsRead_Then_ItIsAccepted(self):
        # Act / Assert
        self.assertEqual(
            neuter_check.baseline_problem("F", {"a": "Passed", "b": "Passed"}), (None, []))

    def test_Given_ARedBaseline_When_ItIsRead_Then_OnlyTheRedCasesAreNamed(self):
        # Act
        problem, red = neuter_check.baseline_problem(
            "F", {"a": "Failed", "b": "Passed", "c": "Inconclusive"})

        # Assert
        self.assertEqual((bool(problem), red), (True, ["a", "c"]))


class CutMap(unittest.TestCase):
    """The map is data the sweep cannot check for itself without spending an editor run on it."""

    def setUp(self):
        self.map = json.loads((REPO_ROOT / neuter_check.CUTS_FILE).read_text())

    def test_Given_EveryFixture_When_ItsCutsAreResolved_Then_EachIsDeclared(self):
        # Arrange
        declared = {cut["name"] for cut in self.map["cuts"]}

        # Act
        dangling = sorted(
            f"{entry['fixture']} names '{name}'"
            for entry in self.map["fixtures"] for name in entry["cuts"] if name not in declared)

        # Assert
        self.assertEqual(dangling, [])

    def test_Given_EveryDeclaredCut_When_TheFixturesAreRead_Then_SomeFixtureAsksIt(self):
        # A cut nothing asks about is disabled by no sweep, so it protects nothing while reading as
        # coverage in the map.
        # Arrange
        asked = {name for entry in self.map["fixtures"] for name in entry["cuts"]}

        # Act
        unasked = sorted(cut["name"] for cut in self.map["cuts"] if cut["name"] not in asked)

        # Assert
        self.assertEqual(unasked, [])

    def test_Given_EveryFixture_When_ItsPlatformIsRead_Then_ItMatchesWhereTheSourceLives(self):
        # A PlayMode fixture asked under EditMode selects no case and reports no hole, which reads as
        # every cut covered. Derived from the source tree so the declaration cannot drift from it.
        # Act
        wrong = []
        for entry in self.map["fixtures"]:
            found = declaring_sources(entry["fixture"].rsplit(".", 1)[-1])
            expected = "PlayMode" if any("PlayMode" in path for path in found) else "EditMode"
            if not found or entry.get("platform") != expected:
                wrong.append(f"{entry['fixture']}: declares {entry.get('platform')!r}, source says {expected}")

        # Assert
        self.assertEqual(wrong, [])

    def test_Given_EveryCutEdit_When_ItsFileIsSought_Then_ItExists(self):
        # Act
        missing = sorted(
            edit["file"] for cut in self.map["cuts"] for edit in cut["edits"]
            if not (REPO_ROOT / edit["file"]).is_file())

        # Assert
        self.assertEqual(missing, [])


if __name__ == "__main__":
    unittest.main()

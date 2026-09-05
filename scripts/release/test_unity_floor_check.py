#!/usr/bin/env python3
"""Holds `unity_floor_check.py` against what it has to tell apart: a release from the series it sits
in, a README from a document naming a release for another reason, a tracked file from an untracked
one, the nested package README from the root one, and a manifest declaring a release from one
declaring none."""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import unity_floor_check  # noqa: E402

SERIES = "6000.3"
FLOOR = SERIES + ".23f1"
EARLIER = SERIES + ".11f1"
PACKAGE_README = "Packages/com.velvet.core/README.md"


class UnityFloorCheckTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.directory, True)

    def repository_holding(self, files, release="23f1"):
        subprocess.run(["git", "init", "-q", self.directory], check=True)
        manifest = {"name": "com.velvet.core", "unity": SERIES}
        if release is not None:
            manifest["unityRelease"] = release
        held = dict(files)
        held[unity_floor_check.MANIFEST] = json.dumps(manifest)
        for name, text in held.items():
            path = Path(self.directory) / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        subprocess.run(["git", "-C", self.directory, "add", "-A"], check=True)
        return self.directory

    def test_Given_ARootReadme_When_ItNamesTheDeclaredFloor_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "Unity " + FLOOR + " or newer\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ARootReadme_When_ItNamesAnEarlierRelease_Then_ItIsReportedAtThatLine(self):
        # Arrange
        project = self.repository_holding({"README.md": "\nUnity " + EARLIER + " or newer\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([("README.md", 2, 7, EARLIER)], found)

    def test_Given_ThePackageReadme_When_ItNamesAnEarlierRelease_Then_ItIsReported(self):
        # Arrange -- the nested README states the same requirement, so it is in the same scope
        project = self.repository_holding({PACKAGE_README: "requires Unity " + EARLIER + "\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([PACKAGE_README], [name for name, _, _, _ in found])

    def test_Given_AReadme_When_ItNamesTheSeriesAlone_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "Unity " + SERIES + " LTS\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AReadmeAtNeitherKnownPath_When_ItNamesAnEarlierRelease_Then_ItIsReported(self):
        # Arrange -- scope is the name, so a README nobody listed carries the requirement too
        sample = "Assets/VelvetStarterSample/README.md"
        project = self.repository_holding({sample: "needs Unity " + EARLIER + "\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([sample], [name for name, _, _, _ in found])

    def test_Given_AChangelog_When_ItCitesAnEarlierRelease_Then_NothingIsReported(self):
        # Arrange -- a floor bump's entry cites the releases it crossed
        project = self.repository_holding({"CHANGELOG.md": "fixed in " + EARLIER + "\n"})
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnUntrackedReadme_When_ItNamesAnEarlierRelease_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({})
        Path(project, "README.md").write_text("Unity " + EARLIER + "\n", encoding="utf-8")
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AManifestDeclaringNoUnityRelease_When_AReadmeNamesOne_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "Unity " + FLOOR + "\n"}, release=None)
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual([FLOOR], [release for _, _, _, release in found])

    def test_Given_TwoStaleReleasesOnOneLine_When_TheyAreRead_Then_EachIsReportedAtItsOwnColumn(self):
        # Arrange
        line = "from " + EARLIER + " to " + EARLIER
        project = self.repository_holding({"README.md": line + "\n"})
        # Act
        columns = [column for _, _, column, _ in unity_floor_check.findings(project)]
        # Assert
        self.assertEqual([6, len(line) - len(EARLIER) + 1], columns)

    def test_Given_AReadmeThatDoesNotDecode_When_ItIsRead_Then_TheRestOfTheScanStillAnswers(self):
        # Arrange
        project = self.repository_holding({PACKAGE_README: "", "README.md": "Unity " + EARLIER + "\n"})
        Path(project, PACKAGE_README).write_bytes(b"\xff\xfe " + EARLIER.encode() + b" \xff\n")
        # Act
        found = unity_floor_check.findings(project)
        # Assert
        self.assertEqual(["README.md"], [name for name, _, _, _ in found])

    def test_Given_AReadmeNamingAnEarlierRelease_When_TheScriptIsRun_Then_ItExitsNonZero(self):
        # Arrange
        project = self.repository_holding({"README.md": "Unity " + EARLIER + "\n"})
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "unity_floor_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((1, True), (run.returncode, "README.md:1:7: " + EARLIER in run.stdout))

    def test_Given_AReadmeNamingTheDeclaredFloor_When_TheScriptIsRun_Then_ItExitsZero(self):
        # Arrange
        project = self.repository_holding({"README.md": "Unity " + FLOOR + "\n"})
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "unity_floor_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((0, ""), (run.returncode, run.stdout))


if __name__ == "__main__":
    unittest.main(verbosity=2)

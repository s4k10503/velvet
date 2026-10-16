#!/usr/bin/env python3
"""Holds `package_meta_check.py` against what it has to tell apart: a file from a directory, a
tracked `.meta` from an untracked one, a directory holding tracked files from one holding none, a
path Unity imports from one under a `~` or `.` component at any depth, and the package from the
rest of the repository.

Run: python3 scripts/unity/test_package_meta_check.py
"""

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import package_meta_check  # noqa: E402

ROOT = package_meta_check.PACKAGE + "/"


class PackageMetaCheckTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.directory, True)

    def repository_holding(self, tracked, untracked=()):
        subprocess.run(["git", "init", "-q", self.directory], check=True)
        for name in list(tracked) + list(untracked):
            path = Path(self.directory) / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("x", encoding="utf-8")
        for name in tracked:
            subprocess.run(["git", "-C", self.directory, "add", "--", name], check=True)
        return self.directory

    def test_Given_ATrackedFileWithoutAMeta_When_Checked_Then_ItIsReportedMissing(self):
        # Arrange
        project = self.repository_holding([ROOT + "package.json"])
        # Act
        missing, _ = package_meta_check.findings(project)
        # Assert
        self.assertEqual([ROOT + "package.json"], missing)

    def test_Given_ADirectoryWithoutAMeta_When_ItsFileHasOne_Then_TheDirectoryIsReportedMissing(self):
        # Arrange
        project = self.repository_holding([ROOT + "Runtime/A.cs", ROOT + "Runtime/A.cs.meta"])
        # Act
        missing, _ = package_meta_check.findings(project)
        # Assert
        self.assertEqual([ROOT + "Runtime"], missing)

    def test_Given_EveryFileAndDirectoryWithAMeta_When_Checked_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding([
            ROOT + "package.json", ROOT + "package.json.meta",
            ROOT + "Runtime.meta", ROOT + "Runtime/Hooks.meta",
            ROOT + "Runtime/Hooks/A.cs", ROOT + "Runtime/Hooks/A.cs.meta",
        ])
        # Act
        found = package_meta_check.findings(project)
        # Assert
        self.assertEqual(([], []), found)

    def test_Given_AMetaWhoseFileIsNotTracked_When_Checked_Then_ItIsReportedOrphaned(self):
        # Arrange
        project = self.repository_holding([ROOT + "Gone.cs.meta"])
        # Act
        _, orphaned = package_meta_check.findings(project)
        # Assert
        self.assertEqual([ROOT + "Gone.cs"], orphaned)

    def test_Given_ADirectoryMetaWhoseDirectoryHoldsOnlyUntrackedFiles_When_Checked_Then_ItIsReportedOrphaned(self):
        # Arrange
        project = self.repository_holding([ROOT + "Empty.meta"], untracked=[ROOT + "Empty/A.cs"])
        # Act
        _, orphaned = package_meta_check.findings(project)
        # Assert
        self.assertEqual([ROOT + "Empty"], orphaned)

    def test_Given_AMetaThatIsNotTracked_When_ItsFileIs_Then_TheFileIsReportedMissing(self):
        # Arrange
        project = self.repository_holding([ROOT + "README.md"], untracked=[ROOT + "README.md.meta"])
        # Act
        missing, _ = package_meta_check.findings(project)
        # Assert
        self.assertEqual([ROOT + "README.md"], missing)

    def test_Given_FilesUnderATildeDirectoryNestedInTheRuntime_When_Checked_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding([
            ROOT + "Runtime.meta", ROOT + "Runtime/Notes~/a.md", ROOT + "Runtime/Notes~.meta",
        ])
        # Act
        found = package_meta_check.findings(project)
        # Assert
        self.assertEqual(([], []), found)

    def test_Given_ADotFileInsideTheRuntime_When_Checked_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding([
            ROOT + "Runtime.meta", ROOT + "Runtime/.gitkeep", ROOT + "Runtime/.hidden/a.cs",
        ])
        # Act
        found = package_meta_check.findings(project)
        # Assert
        self.assertEqual(([], []), found)

    def test_Given_AFileOutsideThePackageWithoutAMeta_When_Checked_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding(["Assets/Scene.unity", "scripts/x.py"])
        # Act
        found = package_meta_check.findings(project)
        # Assert
        self.assertEqual(([], []), found)


if __name__ == "__main__":
    unittest.main()

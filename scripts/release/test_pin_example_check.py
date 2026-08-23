#!/usr/bin/env python3
"""Holds `pin_example_check.py` against what it has to tell apart: a pin from a branch or a shape, a
document from code, a tracked file from an untracked one, and one pin on a line from two."""

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pin_example_check  # noqa: E402

VELVET = "https://github.com/s4k10503/velvet.git#v" + "1.0.0"
PATHED = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#" + "2.5.0"
BRANCH = "https://github.com/s4k10503/velvet.git#upm"
SHAPE = "https://github.com/s4k10503/velvet.git#v" + "X.Y.Z"
ANCHOR = "https://github.com/s4k10503/velvet/blob/main/MIGRATION.md#v" + "200"


class PinExampleCheckTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.mkdtemp()
        self.addCleanup(shutil.rmtree, self.directory, True)

    def repository_holding(self, files):
        subprocess.run(["git", "init", "-q", self.directory], check=True)
        for name, text in files.items():
            path = Path(self.directory) / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        subprocess.run(["git", "-C", self.directory, "add", "-A"], check=True)
        return self.directory

    def test_Given_AMarkdownInstallExample_When_ItNamesATag_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "install with `" + VELVET + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([("README.md", 1)], [(name, number) for name, number, _, _ in found])

    def test_Given_AUrlCarryingUpmsPathSegment_When_ItNamesATagWithNoPrefix_Then_ItIsReported(self):
        project = self.repository_holding({"README.md": PATHED + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_TwoPinsOnOneLine_When_TheyAreRead_Then_EachIsReportedAtItsOwnColumn(self):
        # Arrange
        project = self.repository_holding({"README.md": "a `" + VELVET + "` b `" + VELVET + "`\n"})
        # Act
        columns = [column for _, _, column, _ in pin_example_check.findings(project)]
        # Assert
        self.assertEqual((2, True), (len(columns), len(set(columns)) == len(columns)))

    def test_Given_AWorkflowLineThatIsNoComment_When_ItNamesATag_Then_ItIsReported(self):
        project = self.repository_holding({".github/workflows/upm.yml": "    run: git clone " + VELVET + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AnInstallExample_When_ItNamesABranch_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": BRANCH + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnInstallExample_When_ItNamesTheShape_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": SHAPE + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AProseLinkNoManifestResolves_When_ItsAnchorBeginsOnAVersion_Then_NothingIsReported(self):
        project = self.repository_holding({"README.md": ANCHOR + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ACodeAssertionOnAGeneratedNote_When_ItNamesATag_Then_NothingIsReported(self):
        project = self.repository_holding({"scripts/release/test_notes.py": "assertIn('" + VELVET + "')\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnUntrackedDocument_When_ItNamesATag_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({})
        Path(project, "README.md").write_text(VELVET + "\n", encoding="utf-8")
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ADocumentNamingATag_When_TheScriptIsRun_Then_ItExitsNonZero(self):
        project = self.repository_holding({"README.md": VELVET + "\n"})
        column = VELVET.index(".git") + 1
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "pin_example_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((1, True), (run.returncode,
                                     "README.md:1:{}: {}".format(column, VELVET) in run.stdout))


if __name__ == "__main__":
    unittest.main(verbosity=2)

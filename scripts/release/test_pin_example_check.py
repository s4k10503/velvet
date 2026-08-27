#!/usr/bin/env python3
"""Holds `pin_example_check.py` against what it has to tell apart: a pin from a branch, a shape or a
commit SHA; a `.git` URL from an address carrying none; a document from code; a tracked file from an
untracked one; and one pin on a line from two."""

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pin_example_check  # noqa: E402

VELVET = "https://github.com/s4k10503/velvet.git#v" + "1.0.0"
SLASHED = "https://github.com/s4k10503/velvet.git/#v" + "1.0.0"
SUFFIXED = "https://github.com/s4k10503/velvet.git#v" + "1.0.0" + "-main"
PATHED = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#" + "2.5.0"
QUERIED_BRANCH = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#upm"
BRANCH = "https://github.com/s4k10503/velvet.git#upm"
SHAPE = "https://github.com/s4k10503/velvet.git#v" + "X.Y.Z"
ANCHOR = "https://github.com/s4k10503/velvet/blob/main/MIGRATION.md#v" + "200"
SHA = "https://github.com/s4k10503/velvet.git#" + "1abc234"
ABBREVIATED = "`...UniTask#" + "2.5.0" + "`), and pin Velvet with `...velvet.git#v" + "1.0.0" + "`."


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
        project = self.repository_holding({"README.md": "\ninstall with `" + VELVET + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([("README.md", 2)], [(name, number) for name, number, _, _ in found])

    def test_Given_AUrlCarryingAQuery_When_ItsFragmentNamesATagWithNoPrefix_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": PATHED + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AGitSuffixCarryingATrailingSlash_When_ItNamesATag_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": SLASHED + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AFragmentNamingATagThatCarriesASuffix_When_ItIsRead_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": SUFFIXED + "\n"})
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

    def test_Given_AQueriedBranchUrlAbuttingAPin_When_TheyAreRead_Then_ThePinKeepsItsOwnColumn(self):
        # Arrange
        line = "`" + QUERIED_BRANCH + "`,`" + VELVET + "`"
        project = self.repository_holding({"README.md": line + "\n"})
        # Act
        columns = [column for _, _, column, _ in pin_example_check.findings(project)]
        # Assert
        self.assertEqual([line.index(".git#v") + 1], columns)

    def test_Given_AWorkflowLineThatIsNoComment_When_ItNamesATag_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({".github/workflows/upm.yml": "    run: git clone " + VELVET + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AnIndentedLineNamingATag_When_ItIsReported_Then_TheColumnIndexesTheGitSuffixInTheReportedText(self):
        # Arrange
        project = self.repository_holding({".github/workflows/upm.yml": "    run: git clone " + VELVET + "\n"})
        # Act
        _, _, column, text = pin_example_check.findings(project)[0]
        # Assert
        self.assertEqual(".git", text[column - 1:column + 3])

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

    def test_Given_AFragmentThatIsACommitSha_When_ItBeginsOnADigit_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": SHA + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnAddressWithNoGitSuffix_When_ItsFragmentIsAVersion_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": ANCHOR + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ASpellingWithNoGitSuffix_When_ItSitsBesideAPin_Then_OnlyThePinIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": ABBREVIATED + "\n"})
        # Act
        columns = [column for _, _, column, _ in pin_example_check.findings(project)]
        # Assert
        self.assertEqual([ABBREVIATED.index(".git#v") + 1], columns)

    def test_Given_ACodeAssertionOnAGeneratedNote_When_ItNamesATag_Then_NothingIsReported(self):
        # Arrange
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

    def test_Given_ADocumentThatDoesNotDecode_When_ItIsRead_Then_TheRestOfTheScanStillAnswers(self):
        # Arrange
        project = self.repository_holding({"CONTRIBUTING.md": "", "README.md": VELVET + "\n"})
        Path(project, "CONTRIBUTING.md").write_bytes(b"\xff\xfe pin \xff\n")
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(["README.md"], [name for name, _, _, _ in found])

    def test_Given_AFragmentNamingAMaintenanceBranch_When_ItIsRead_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "https://github.com/o/r.git#2.x\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ADocumentNamingATag_When_TheScriptIsRun_Then_ItExitsNonZero(self):
        # Arrange
        project = self.repository_holding({"README.md": VELVET + "\n"})
        column = VELVET.index(".git") + 1
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "pin_example_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((1, True), (run.returncode,
                                     "README.md:1:{}: {}".format(column, VELVET) in run.stdout))

    def test_Given_APinClosingASentence_When_ItIsRead_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "Install with " + VELVET + ".\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AnAllNumericAbbreviatedSha_When_ItIsRead_Then_ItIsReportedLikeATag(self):
        # Arrange -- nothing can tell it from a version, and the docstring says so
        project = self.repository_holding({"README.md": "https://github.com/o/r.git#1234567\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_ADocumentNamingNoTag_When_TheScriptIsRun_Then_ItExitsZero(self):
        # Arrange
        project = self.repository_holding({"README.md": BRANCH + "\n"})
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "pin_example_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((0, ""), (run.returncode, run.stdout))


if __name__ == "__main__":
    unittest.main(verbosity=2)

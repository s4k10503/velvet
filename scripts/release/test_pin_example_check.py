#!/usr/bin/env python3
"""Holds `pin_example_check.py` against the distinctions it decides: concrete against shape, a
document against code, and tracked against not."""

import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pin_example_check  # noqa: E402

# Built rather than written: a literal here would be one this file has to keep out of scope by hand.
PLAIN = ".git#v" + "1.0.0"
PATHED = "github.com/o/r.git?path=/Packages/x#v" + "1.0.0"
NO_SUFFIX = "github.com/o/r#v" + "1.0.0"
SHAPE = ".git#v" + "X.Y.Z"


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

    def test_Given_AMarkdownInstallExample_When_ItNamesARelease_Then_ItIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "install with `x" + PLAIN + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([("README.md", 1)], [(name, number) for name, number, _, _ in found])

    def test_Given_APinAUpmUrlShapePutsBeyondThePlainForm_When_ItIsRead_Then_ItIsReported(self):
        # Arrange -- a `?path=` segment and a missing `.git` are both spellings UPM accepts
        project = self.repository_holding({"README.md": PATHED + "\n" + NO_SUFFIX + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([1, 2], [number for _, number, _, _ in found])

    def test_Given_TwoPinsOnOneLine_When_TheyAreRead_Then_BothAreReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "a`x" + PLAIN + "` b`y" + PLAIN + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(2, len(found))

    def test_Given_AWorkflowFile_When_AnyLineNamesARelease_Then_ItIsReported(self):
        # Arrange -- every line, not only the header comments
        project = self.repository_holding({".github/workflows/upm.yml": "    run: git clone x" + PLAIN + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AMarkdownInstallExample_When_ItNamesTheShape_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({"README.md": "install with `x" + SHAPE + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ACodeAssertionOnAGeneratedNote_When_ItNamesARelease_Then_NothingIsReported(self):
        # Arrange -- the generator writes the version being released, and its test is right to say so
        project = self.repository_holding({"scripts/release/test_notes.py": "assertIn('x" + PLAIN + "')\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnUntrackedDocument_When_ItNamesARelease_Then_NothingIsReported(self):
        # Arrange
        project = self.repository_holding({})
        Path(project, "README.md").write_text("x" + PLAIN + "\n", encoding="utf-8")
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ADocumentNamingARelease_When_TheScriptIsRun_Then_ItExitsNonZero(self):
        # Arrange -- findings() answering is not the same as the CI step failing
        project = self.repository_holding({"README.md": "x" + PLAIN + "\n"})
        # Act
        run = subprocess.run([sys.executable, str(Path(__file__).resolve().parent / "pin_example_check.py"),
                              "--project", project], capture_output=True, text=True)
        # Assert
        self.assertEqual((1, True), (run.returncode, "README.md:1:" in run.stdout))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Holds `pin_example_check.py` against the two readings it has to keep apart."""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import pin_example_check  # noqa: E402

# Built rather than written, so this file carries no literal the check would find in itself.
CONCRETE = ".git#v" + "1.0.0"
SHAPE = ".git#v" + "X.Y.Z"


def repository_holding(files):
    directory = tempfile.mkdtemp()
    subprocess.run(["git", "init", "-q", directory], check=True)
    for name, text in files.items():
        path = Path(directory) / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
    subprocess.run(["git", "-C", directory, "add", "-A"], check=True)
    return directory


class PinExampleCheckTests(unittest.TestCase):
    def test_Given_AMarkdownInstallExample_When_ItNamesARelease_Then_ItIsReported(self):
        # Arrange
        project = repository_holding({"README.md": "install with `x" + CONCRETE + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([("README.md", 1)], [(name, number) for name, number, _ in found])

    def test_Given_AWorkflowComment_When_ItNamesARelease_Then_ItIsReported(self):
        # Arrange
        project = repository_holding({".github/workflows/upm.yml": "# x" + CONCRETE + "\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual(1, len(found))

    def test_Given_AMarkdownInstallExample_When_ItNamesTheShape_Then_NothingIsReported(self):
        # Arrange
        project = repository_holding({"README.md": "install with `x" + SHAPE + "`\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_ACodeAssertionOnAGeneratedNote_When_ItNamesARelease_Then_NothingIsReported(self):
        # Arrange -- the generator writes the version being released, and its test is right to say so
        project = repository_holding({"scripts/release/test_notes.py": "assertIn('x" + CONCRETE + "')\n"})
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)

    def test_Given_AnUntrackedDocument_When_ItNamesARelease_Then_NothingIsReported(self):
        # Arrange
        project = repository_holding({})
        Path(project, "README.md").write_text("x" + CONCRETE + "\n", encoding="utf-8")
        # Act
        found = pin_example_check.findings(project)
        # Assert
        self.assertEqual([], found)


if __name__ == "__main__":
    unittest.main(verbosity=2)

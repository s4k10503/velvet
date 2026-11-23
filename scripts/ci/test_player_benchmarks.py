#!/usr/bin/env python3
"""Unit tests for player_benchmarks.py: the editor command it hands licensed_editor.py, and the runs it
refuses before any editor starts.

Run: python3 scripts/ci/test_player_benchmarks.py
"""

import importlib.util
import tempfile
import unittest
from pathlib import Path


def load_module():
    """Imports player_benchmarks by path, since scripts/ci is not a package."""
    spec = importlib.util.spec_from_file_location(
        "player_benchmarks", Path(__file__).with_name("player_benchmarks.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


player_benchmarks = load_module()

ENABLED = {player_benchmarks.ENVIRONMENT_VARIABLE: "1"}


class Recorder:
    def __init__(self, status=0):
        self.status = status
        self.runs = []

    def __call__(self, command, what):
        self.runs.append(command)
        return self.status


def run_main(arguments, environ):
    recorder = Recorder()
    with tempfile.TemporaryDirectory() as holder:
        status = player_benchmarks.main(arguments + ["--output", str(Path(holder) / "out")],
                                        run=recorder, environ=environ)
    return status, recorder.runs


class PlayerBenchmarksTests(unittest.TestCase):

    def test_Given_AFilter_When_TheCommandIsBuilt_Then_ItBuildsAndRunsALinuxPlayer(self):
        # Arrange
        status, runs = run_main(["--filter", "Velvet.Tests.X"], ENABLED)

        # Act
        command = runs[0]

        # Assert
        self.assertEqual(command[command.index("-testPlatform") + 1], "StandaloneLinux64")

    def test_Given_AFilter_When_TheCommandIsBuilt_Then_TheFilterIsPassedThrough(self):
        # Arrange
        status, runs = run_main(["--filter", "Velvet.Tests.X"], ENABLED)

        # Act
        command = runs[0]

        # Assert
        self.assertEqual(command[command.index("-testFilter") + 1], "Velvet.Tests.X")

    def test_Given_TheCommand_When_ItIsRead_Then_ItDoesNotQuitTheEditorEarly(self):
        # Arrange
        status, runs = run_main(["--filter", "Velvet.Tests.X"], ENABLED)

        # Act
        command = runs[0]

        # Assert
        self.assertNotIn("-quit", command)

    def test_Given_NoOptIn_When_Run_Then_NoEditorStarts(self):
        # Arrange
        status, runs = run_main(["--filter", "Velvet.Tests.X"], {})

        # Act
        started = len(runs)

        # Assert
        self.assertEqual(started, 0)

    def test_Given_NoOptIn_When_Run_Then_ItIsRefused(self):
        # Arrange
        status, runs = run_main(["--filter", "Velvet.Tests.X"], {})

        # Act
        refused = status

        # Assert
        self.assertEqual(refused, 2)

    def test_Given_AnEmptyFilter_When_Run_Then_NoEditorStarts(self):
        # Arrange
        status, runs = run_main(["--filter", ";"], ENABLED)

        # Act
        started = len(runs)

        # Assert
        self.assertEqual(started, 0)

    def test_Given_TheEditorFails_When_Run_Then_ItsStatusIsReturned(self):
        # Arrange
        recorder = Recorder(status=3)

        # Act
        with tempfile.TemporaryDirectory() as holder:
            status = player_benchmarks.main(["--filter", "X", "--output", str(Path(holder) / "o")],
                                            run=recorder, environ=ENABLED)

        # Assert
        self.assertEqual(status, 3)


if __name__ == "__main__":
    unittest.main()

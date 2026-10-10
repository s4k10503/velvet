#!/usr/bin/env python3
"""Unit tests for reuse_verdict.py: which earlier campaign a head's key is taken from.

Run: python3 scripts/ci/test_reuse_verdict.py
"""

import contextlib
import importlib.util
import io
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("reuse_verdict", Path(__file__).with_name("reuse_verdict.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


reuse_verdict = load_module()
campaign = reuse_verdict.campaign

KEY = "c3" * 32
ENVIRONMENT = {"GITHUB_REPOSITORY": "owner/name", "GITHUB_REF_NAME": "topic", "GITHUB_RUN_ID": "9100"}


def passed(number):
    return {"id": 9000 + number, "run_number": number, "path": campaign.PATH, "status": "completed",
            "conclusion": "success"}


class Reader:
    """A stand-in for the API: workflow runs by listing, and each run's key artifacts."""

    def __init__(self, runs, keys, failing=False):
        self.runs, self.keys, self.failing, self.asked = runs, keys, failing, []

    def __call__(self, path):
        self.asked.append(path)
        if self.failing:
            raise RuntimeError("gh api failed: HTTP 502")
        if "/workflows/mutation.yml/runs" in path:
            return {"workflow_runs": self.runs}
        run_id = int(path.split("/runs/")[1].split("/")[0])
        names = self.keys.get(run_id, [])
        return {"total_count": len(names), "artifacts": [{"name": name} for name in names]}


def reused_by(reader, *arguments, environ=ENVIRONMENT):
    printed = io.StringIO()
    with contextlib.redirect_stdout(printed), contextlib.redirect_stderr(io.StringIO()):
        code = reuse_verdict.main(list(arguments), environ, reader)
    return code, printed.getvalue().strip()


class ReuseTests(unittest.TestCase):
    def test_Given_AnEarlierPassOnTheBranchWithTheKey_When_Asked_Then_ItsRunIsPrinted(self):
        # Arrange
        reader = Reader([passed(1)], {9001: [campaign.key_artifact_name(KEY)]})

        # Act
        code, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual((code, printed), (0, "reused=9001"))

    def test_Given_NoEarlierPassWithTheKey_When_Asked_Then_NothingIsPrinted(self):
        # Arrange
        reader = Reader([passed(1)], {9001: [campaign.key_artifact_name("d4" * 32)]})

        # Act
        code, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual((code, printed), (0, "reused="))

    def test_Given_TheCurrentRunHoldingTheKey_When_Asked_Then_ItIsNotItsOwnSource(self):
        # Arrange -- a re-run of the verdict job lists the run it is in.
        reader = Reader([passed(100)], {9100: [campaign.key_artifact_name(KEY)]})

        # Act
        _, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(printed, "reused=")

    def test_Given_TwoPassesWithTheKey_When_Asked_Then_TheNewerIsTaken(self):
        # Arrange
        reader = Reader([passed(1), passed(4)], {9001: [campaign.key_artifact_name(KEY)],
                                                9004: [campaign.key_artifact_name(KEY)]})

        # Act
        _, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(printed, "reused=9004")

    def test_Given_AReadingThatFails_When_Asked_Then_NothingIsReused(self):
        # Arrange
        reader = Reader([passed(1)], {}, failing=True)

        # Act
        code, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual((code, printed), (0, "reused="))

    def test_Given_TheBranchOfTheHead_When_Asked_Then_OnlyItsRunsAreListed(self):
        # Arrange
        reader = Reader([], {})

        # Act
        reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(reader.asked, [
            "repos/owner/name/actions/workflows/mutation.yml/runs?status=success&branch=topic&per_page=20"])


class NameTests(unittest.TestCase):
    def test_Given_AKeyMeasuredHere_When_Named_Then_NoRunIsCited(self):
        # Act
        _, printed = reused_by(Reader([], {}), "--name", KEY)

        # Assert
        self.assertEqual(printed, "name=Mutation campaign key " + KEY)

    def test_Given_AKeyReusedFromARun_When_Named_Then_TheRunIsCited(self):
        # Act
        _, printed = reused_by(Reader([], {}), "--name", KEY, "--from", "9001")

        # Assert
        self.assertEqual(printed, "name=Mutation campaign key {} reused from run 9001".format(KEY))


if __name__ == "__main__":
    unittest.main()

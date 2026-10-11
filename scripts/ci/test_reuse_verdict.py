#!/usr/bin/env python3
"""Unit tests for reuse_verdict.py: which earlier campaign a head's key is taken from.

Run: python3 scripts/ci/test_reuse_verdict.py
"""

import contextlib
import importlib.util
import io
import json
import tempfile
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

    def __init__(self, runs, keys, failing=False, expired=(), raising=None):
        self.runs, self.keys, self.failing, self.asked = runs, keys, failing, []
        self.expired, self.raising = set(expired), raising

    def __call__(self, path):
        self.asked.append(path)
        if self.failing:
            raise RuntimeError("gh api failed: HTTP 502")
        if self.raising is not None:
            raise self.raising
        if "/workflows/mutation.yml/runs" in path:
            return {"workflow_runs": self.runs}
        run_id = int(path.split("/runs/")[1].split("/")[0])
        names = self.keys.get(run_id, [])
        return {"total_count": len(names),
                "artifacts": [{"name": name, "expired": run_id in self.expired} for name in names]}


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

    def test_Given_AReadingThatIsNotJson_When_Asked_Then_NothingIsReused(self):
        # Arrange -- json.loads raises a ValueError, which is not a RuntimeError.
        reader = Reader([passed(1)], {}, raising=ValueError("Expecting value"))

        # Act
        code, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual((code, printed), (0, "reused="))

    def test_Given_AListingOfTheWrongShape_When_Asked_Then_NothingIsReused(self):
        # Arrange
        reader = Reader([passed(1)], {}, raising=AttributeError("'list' object has no attribute 'get'"))

        # Act
        code, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual((code, printed), (0, "reused="))

    def test_Given_OnlyAReuseOfTheKeyOnTheBranch_When_Asked_Then_TheHeadIsMeasured(self):
        # Arrange -- the run that measured it has aged out of the listing, and its reuse remains.
        reader = Reader([passed(4)], {9004: [campaign.key_artifact_name(KEY, 9001)]})

        # Act
        _, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(printed, "reused=")

    def test_Given_TheMeasuringRunsArtifactsExpired_When_Asked_Then_TheHeadIsMeasured(self):
        # Arrange
        reader = Reader([passed(1)], {9001: [campaign.key_artifact_name(KEY)]}, expired=[9001])

        # Act
        _, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(printed, "reused=")

    def test_Given_TheMeasuringRunAndALaterReuse_When_Asked_Then_TheMeasuringRunIsNamed(self):
        # Arrange
        reader = Reader([passed(1), passed(4)], {9001: [campaign.key_artifact_name(KEY)],
                                                9004: [campaign.key_artifact_name(KEY, 9001)]})

        # Act
        _, printed = reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(printed, "reused=9001")

    def test_Given_TheBranchOfTheHead_When_Asked_Then_OnlyItsRunsAreListed(self):
        # Arrange
        reader = Reader([], {})

        # Act
        reused_by(reader, "--key", KEY)

        # Assert
        self.assertEqual(reader.asked, [
            "repos/owner/name/actions/workflows/mutation.yml/runs?status=success&branch=topic&per_page=20"])


def finished(number, conclusion="success"):
    return dict(passed(number), conclusion=conclusion)


class LedgerReader:
    """The API as a ledger fetch sees it: runs, each run's artifacts, and what a download writes."""

    def __init__(self, runs, ledgers, expired=(), undownloadable=(), contents=None):
        self.runs, self.ledgers, self.expired = runs, ledgers, set(expired)
        self.undownloadable, self.contents, self.downloaded = set(undownloadable), contents or {}, []

    def __call__(self, path):
        if "/workflows/mutation.yml/runs" in path:
            self.listing = path
            return {"workflow_runs": self.runs}
        run_id = int(path.split("/runs/")[1].split("/")[0])
        names = self.ledgers.get(run_id, [])
        return {"total_count": len(names),
                "artifacts": [{"id": run_id * 10 + position, "name": name, "expired": run_id in self.expired}
                              for position, name in enumerate(names)]}

    def download(self, _slug, run_id, directory):
        self.downloaded.append(run_id)
        if run_id in self.undownloadable:
            raise RuntimeError("gh run download failed: HTTP 404")
        Path(directory, "ledger.json").write_text(self.contents.get(run_id, json.dumps({"run": run_id})))


def fetched(reader, environ=ENVIRONMENT):
    """(the files the fetch left, what it printed) over a fresh directory."""
    printed = io.StringIO()
    with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stdout(printed), \
            contextlib.redirect_stderr(io.StringIO()):
        code = reuse_verdict.main(["--ledgers", directory], environ, reader, reader.download)
        files = sorted(path.name for path in Path(directory).iterdir())
    return code, files, printed.getvalue().strip()


LEDGER = campaign.LEDGER_ARTIFACT


class LedgerFetchTests(unittest.TestCase):
    def test_Given_AFailedAndAPassedRunWithLedgers_When_Fetched_Then_BothAreKept(self):
        # Arrange -- a campaign that failed on a survivor is the one whose kills the next round needs.
        reader = LedgerReader([finished(1, "failure"), finished(2)], {9001: [LEDGER], 9002: [LEDGER]})

        # Act
        code, files, _ = fetched(reader)

        # Assert
        self.assertEqual((code, files), (0, ["0000000001-9001.json", "0000000002-9002.json"]))

    def test_Given_AListingOfFinishedRuns_When_Fetched_Then_TheListingIsNotFilteredToPasses(self):
        # Arrange
        reader = LedgerReader([], {})

        # Act
        fetched(reader)

        # Assert
        self.assertEqual(reader.listing,
                         "repos/owner/name/actions/workflows/mutation.yml/runs?status=completed&branch=topic&per_page=20")

    def test_Given_ARunWithoutALedger_When_Fetched_Then_NothingIsDownloadedForIt(self):
        # Arrange -- a run that reused a whole verdict, or predates ledgers.
        reader = LedgerReader([finished(1)], {9001: [campaign.key_artifact_name(KEY)]})

        # Act
        fetched(reader)

        # Assert
        self.assertEqual(reader.downloaded, [])

    def test_Given_ALedgerThatExpired_When_Fetched_Then_NothingIsDownloadedForIt(self):
        # Arrange
        reader = LedgerReader([finished(1)], {9001: [LEDGER]}, expired=[9001])

        # Act
        fetched(reader)

        # Assert
        self.assertEqual(reader.downloaded, [])

    def test_Given_TwoLedgersInOneRun_When_Fetched_Then_NeitherIsTaken(self):
        # Arrange -- which of the two the run wrote is not something the listing says.
        reader = LedgerReader([finished(1)], {9001: [LEDGER, LEDGER]})

        # Act
        _, files, _ = fetched(reader)

        # Assert
        self.assertEqual(files, [])

    def test_Given_ADownloadThatFails_When_Fetched_Then_TheOtherRunsAreStillTaken(self):
        # Arrange
        reader = LedgerReader([finished(1), finished(2)], {9001: [LEDGER], 9002: [LEDGER]},
                              undownloadable=[9002])

        # Act
        code, files, _ = fetched(reader)

        # Assert
        self.assertEqual((code, files), (0, ["0000000001-9001.json"]))

    def test_Given_ALedgerThatIsNotJson_When_Fetched_Then_ItIsLeftOut(self):
        # Arrange
        reader = LedgerReader([finished(1)], {9001: [LEDGER]}, contents={9001: "<html>"})

        # Act
        _, files, _ = fetched(reader)

        # Assert
        self.assertEqual(files, [])

    def test_Given_TheCurrentRunsOwnLedger_When_Fetched_Then_ItIsNotItsOwnSource(self):
        # Arrange -- a re-run of the plan lists the run it is in.
        reader = LedgerReader([finished(100)], {9100: [LEDGER]})

        # Act
        _, files, _ = fetched(reader)

        # Assert
        self.assertEqual(files, [])

    def test_Given_AListingThatFails_When_Fetched_Then_NothingIsFetchedAndTheStepPasses(self):
        # Arrange
        def failing(_path):
            raise RuntimeError("gh api failed: HTTP 502")

        reader = LedgerReader([], {})
        printed = io.StringIO()

        # Act
        with tempfile.TemporaryDirectory() as directory, contextlib.redirect_stderr(io.StringIO()), \
                contextlib.redirect_stdout(printed):
            code = reuse_verdict.main(["--ledgers", directory], ENVIRONMENT, failing, reader.download)

        # Assert
        self.assertEqual((code, printed.getvalue()), (0, ""))

    def test_Given_ARunOfAnotherWorkflow_When_Fetched_Then_ItIsNotASource(self):
        # Arrange
        other = dict(finished(1), path=".github/workflows/test.yml")
        reader = LedgerReader([other], {9001: [LEDGER]})

        # Act
        fetched(reader)

        # Assert
        self.assertEqual(reader.downloaded, [])


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

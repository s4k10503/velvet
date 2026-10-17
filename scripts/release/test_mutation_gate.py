#!/usr/bin/env python3
"""Unit tests for mutation_gate.py: the release gate's two halves and the way past it.

The record's own decision (`mutation_check.py --gate`) is tested in test_mutation_check.py's
RecordGateTests; here the campaign's scripts are stand-ins, so what is tested is what the gate hands
them and what it makes of their answer.

Run: python3 scripts/release/test_mutation_gate.py
"""

import contextlib
import importlib.util
import io
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("mutation_gate", Path(__file__).with_name("mutation_gate.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


gate = load_module()

SHA = "c" * 40

# Stand-ins for the campaign's scripts: the scope is a fixed name, the record a no-op, and the decision
# exits with what `verdict` holds, printing the arguments it was given.
STAND_INS = {
    "scripts/ci/change_base.py": "def campaign_scope():\n    return 'scope-commit'\n",
    "scripts/ci/mutation_record.py": "def fetch(project, directory):\n    return 0\n",
    "scripts/test_quality/mutation_check.py": (
        "import sys\nfrom pathlib import Path\n"
        "print(' '.join(sys.argv[1:]))\n"
        "sys.exit(int((Path(__file__).parent / 'verdict').read_text()))\n"),
    "scripts/test_quality/verdict": "0",
    "scripts/pr/red_base.py": "REQUIRED_WORKFLOWS = ('test.yml', 'generators.yml')\n",
}


class Project:
    def __init__(self, files=STAND_INS):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-gate-"))
        for name, text in files.items():
            (self.root / name).parent.mkdir(parents=True, exist_ok=True)
            (self.root / name).write_text(text)
        for command in (["init", "-q"], ["add", "-A"], ["commit", "-qm", "release"]):
            subprocess.run(["git", "-c", "user.email=p@e", "-c", "user.name=P", *command], cwd=self.root,
                           check=True, capture_output=True)
        self.head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.root, capture_output=True,
                                   text=True).stdout.strip()

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


def runs(*conclusions):
    """A listing of completed push runs, numbered in order, concluding as given."""
    return {"workflow_runs": [{"id": number, "run_number": number, "status": "completed",
                               "conclusion": conclusion} for number, conclusion in enumerate(conclusions, 1)]}


class CiProblemTests(unittest.TestCase):
    def test_Given_EveryWorkflowsNewestPushRunPassed_When_Read_Then_NothingIsWrong(self):
        # Act
        found = gate.ci_problems("o/r", SHA, ("test.yml", "generators.yml"), lambda path: runs("success"))

        # Assert
        self.assertEqual(found, [])

    def test_Given_AWorkflowWhoseNewestPushRunFailed_When_Read_Then_ItIsNamed(self):
        # Act
        found = gate.ci_problems("o/r", SHA, ("test.yml",), lambda path: runs("success", "failure"))

        # Assert
        self.assertEqual(found, ["test.yml's push run 2 at cccccccccccc concluded failure"])

    def test_Given_AFailedRunAPassingReRunFollowed_When_Read_Then_NothingIsWrong(self):
        # Act
        found = gate.ci_problems("o/r", SHA, ("test.yml",), lambda path: runs("failure", "success"))

        # Assert
        self.assertEqual(found, [])

    def test_Given_AWorkflowWithNoCompletedPushRun_When_Read_Then_ItIsNamed(self):
        # Act
        found = gate.ci_problems("o/r", SHA, ("test.yml",), lambda path: {"workflow_runs": []})

        # Assert
        self.assertEqual(found, ["test.yml has no completed push run at cccccccccccc"])


class GateTests(unittest.TestCase):
    def setUp(self):
        self.project = Project()
        self.addCleanup(self.project.remove)

    def decide(self, verdict="0", branch="2.x", read=lambda path: runs("success"), release=None, skip="",
               record=()):
        (self.project.root / "scripts/test_quality/verdict").write_text(verdict)
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            code = gate.main(["--project", str(self.project.root), "--release", release or self.project.head,
                              "--skip", skip, *record],
                             {"DEFAULT_BRANCH": "main", "GITHUB_REF_NAME": branch, "GITHUB_REPOSITORY": "o/r"},
                             read)
        return code, printed.getvalue()

    def test_Given_ARecordThatAnswersTheScope_When_Gated_Then_TheReleasePasses(self):
        # Act
        code, _ = self.decide("0")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ARecordThatLeavesAMutantUnanswered_When_Gated_Then_TheReleaseIsRefused(self):
        # Act
        code, _ = self.decide("1")

        # Assert
        self.assertEqual(code, 1)

    def test_Given_TheReleaseCommit_When_Gated_Then_TheDecisionReadsTheScopeAndTheRecord(self):
        # Act
        _, printed = self.decide("0")

        # Assert
        self.assertIn("--base scope-commit --gate --reuse-from", printed)

    def test_Given_ACommitWithoutTheRecordTooling_When_Gated_Then_TheReleaseIsRefused(self):
        # Arrange — a line cut before the record existed.
        (self.project.root / "scripts/ci/mutation_record.py").unlink()

        # Act
        code, _ = self.decide("0")

        # Assert
        self.assertEqual(code, 1)

    def test_Given_TheDefaultBranchWithAFailedRequiredRun_When_Gated_Then_TheReleaseIsRefused(self):
        # Act
        code, _ = self.decide("0", branch="main", read=lambda path: runs("failure"))

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AMaintenanceLine_When_Gated_Then_ItsCiIsNotRead(self):
        # Arrange — push runs are read on the default branch alone, and a read here would fail.
        def refuse(path):
            raise AssertionError("read " + path)

        # Act
        code, _ = self.decide("0", branch="2.x", read=refuse)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AReleaseCommitOtherThanTheCheckout_When_Gated_Then_TheReleaseIsRefused(self):
        # Act
        code, _ = self.decide("0", release=SHA)

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AReasonToSkip_When_TheRecordLeavesAMutantUnanswered_Then_TheReleasePasses(self):
        # Act
        code, _ = self.decide("1", skip="the record branch was lost")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_TheSameCommitAndRecord_When_GatedTwice_Then_TheAnswersAreIdentical(self):
        # Arrange — one record directory for both, as the fetch would leave it.
        record = ("--record", str(self.project.root / "record"))

        # Act
        first, second = self.decide("1", record=record), self.decide("1", record=record)

        # Assert
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main(verbosity=2)

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
    """A repository on `main` holding the stand-ins, whose origin is itself."""

    def __init__(self, files=STAND_INS):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-gate-"))
        for name, text in files.items():
            (self.root / name).parent.mkdir(parents=True, exist_ok=True)
            (self.root / name).write_text(text)
        self.git("init", "-q", "-b", "main")
        self.commit("release")
        self.git("remote", "add", "origin", str(self.root))

    def git(self, *arguments):
        return subprocess.run(["git", "-c", "user.email=p@e", "-c", "user.name=P", *arguments], cwd=self.root,
                              check=True, capture_output=True, text=True).stdout.strip()

    def commit(self, message, **files):
        """Commits `files` (a path under the root, with `/` as `__`) and returns the new head."""
        for name, text in files.items():
            (self.root / name.replace("__", "/")).write_text(text)
        self.git("add", "-A")
        self.git("commit", "-q", "--allow-empty", "-m", message)
        self.head = self.git("rev-parse", "HEAD")
        return self.head

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

    def verdict(self, code):
        """Commits a stand-in decision exiting with `code` on the current branch."""
        self.project.commit("verdict", scripts__test_quality__verdict=code)

    def decide(self, read=lambda path: runs("success"), release=None, skip="", record=(), project=None,
               default="main"):
        printed = io.StringIO()
        root = project or self.project.root
        head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=root, capture_output=True, text=True).stdout.strip()
        with contextlib.redirect_stdout(printed):
            code = gate.main(["--project", str(root), "--release", release or head, "--skip", skip, *record],
                             {"DEFAULT_BRANCH": default, "GITHUB_REPOSITORY": "o/r"}, read)
        return code, printed.getvalue()

    def test_Given_ARecordThatAnswersTheScope_When_Gated_Then_TheReleasePasses(self):
        # Act
        code, _ = self.decide()

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ARecordThatLeavesAMutantUnanswered_When_Gated_Then_TheReleaseIsRefused(self):
        # Arrange
        self.verdict("1")

        # Act
        code, _ = self.decide()

        # Assert
        self.assertEqual(code, 1)

    def test_Given_TheReleaseCommit_When_Gated_Then_TheDecisionReadsTheScopeAndTheRecord(self):
        # Act
        _, printed = self.decide()

        # Assert
        self.assertIn("--project . --base scope-commit --gate --reuse-from", printed)

    def test_Given_AnEditTheReleaseCommitDoesNotHold_When_Gated_Then_TheCommitIsWhatIsRead(self):
        # Arrange — the committed decision refuses; the checkout's uncommitted one would pass.
        self.verdict("1")
        (self.project.root / "scripts/test_quality/verdict").write_text("0")

        # Act
        code, _ = self.decide()

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ARecordHoldingAForeignCommit_When_Gated_Then_TheReleaseIsRefusedNamingIt(self):
        # Arrange — the record's reader refuses it, as mutation_record.fetch does.
        self.project.commit("foreign", **{"scripts__ci__mutation_record.py": (
            "def fetch(project, directory):\n    raise SystemExit('abc123: committed by someone')\n")})

        # Act
        code, printed = self.decide()
        refusal = printed if code == 1 else ""

        # Assert
        self.assertIn("abc123: committed by someone", refusal)

    def test_Given_ACommitWithoutTheRecordTooling_When_Gated_Then_TheReleaseIsRefused(self):
        # Arrange — a line cut before the record existed.
        (self.project.root / "scripts/ci/mutation_record.py").unlink()
        self.project.commit("no record tooling")

        # Act
        code, _ = self.decide()

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AShallowClone_When_Gated_Then_TheReleaseIsRefused(self):
        # Arrange — a history cut short can hide the scope's tag and cutover, which empties the scope.
        shallow = self.project.root.parent / (self.project.root.name + "-shallow")
        self.addCleanup(shutil.rmtree, shallow, True)
        subprocess.run(["git", "clone", "-q", "--depth", "1", "file://{}".format(self.project.root), str(shallow)],
                       check=True, capture_output=True)

        # Act
        code, _ = self.decide(project=shallow)

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ACommitOnTheDefaultBranchWithAFailedRequiredRun_When_Gated_Then_TheReleaseIsRefused(self):
        # Act
        code, _ = self.decide(read=lambda path: runs("failure"))

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ACommitOnTheDefaultBranchDispatchedFromAnotherRef_When_Gated_Then_ItsCiIsRead(self):
        # Arrange — the ref a release is dispatched from does not decide whether its CI is read.
        self.project.git("checkout", "-q", "-b", "2.x")

        # Act
        code, _ = self.decide(read=lambda path: runs("failure"))

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ACommitOnlyAMaintenanceLineHolds_When_Gated_Then_ItsCiIsNotRead(self):
        # Arrange — push runs are read on the default branch alone, and a read here would fail.
        self.project.git("checkout", "-q", "-b", "2.x")
        self.project.commit("a fix on the line")

        def refuse(path):
            raise AssertionError("read " + path)

        # Act
        code, _ = self.decide(read=refuse)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AReleaseCommitTheRepositoryDoesNotHold_When_Gated_Then_TheReleaseIsRefusedNamingIt(self):
        # Act
        code, printed = self.decide(release=SHA)
        refusal = printed if code == 1 else ""

        # Assert — named, so the refusal is not the missing-tooling one a tree that is not there gives too.
        self.assertIn("no worktree of {}".format(SHA), refusal)

    def test_Given_AReasonToSkip_When_TheRecordLeavesAMutantUnanswered_Then_TheReleasePasses(self):
        # Arrange
        self.verdict("1")

        # Act
        code, _ = self.decide(skip="the record branch was lost")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_TheSameCommitAndRecord_When_GatedTwice_Then_TheAnswersAreIdentical(self):
        # Arrange — one record directory for both, as the fetch would leave it.
        self.verdict("1")
        record = ("--record", str(self.project.root.parent / (self.project.root.name + "-record")))

        def answer():
            """The gate's exit and output, or a value equal to nothing where it decided other than once."""
            code, printed = self.decide(record=record)
            return "{}\n{}".format(code, printed) if printed.count("--gate") == 1 else object()

        # Act
        first, second = answer(), answer()

        # Assert
        self.assertEqual(first, second)


if __name__ == "__main__":
    unittest.main(verbosity=2)

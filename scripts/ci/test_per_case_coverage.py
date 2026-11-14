#!/usr/bin/env python3
"""Unit tests for per_case_coverage.py: which files it compresses while the editor writes them, and the
run it hands licensed_editor.py.

The editor is replaced by a writer that names its files the way Code Coverage numbers them, since what
the cases ask is what each directory holds afterwards.

Run: python3 scripts/ci/test_per_case_coverage.py
"""

import gzip
import importlib.util
import json
import os
import re
import subprocess
import tempfile
import time
import unittest
from pathlib import Path


def load_module():
    """Imports per_case_coverage by path, since scripts/ci is not a package."""
    spec = importlib.util.spec_from_file_location(
        "per_case_coverage", Path(__file__).with_name("per_case_coverage.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


per_case_coverage = load_module()

REPO_ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ROOT = Path("Packages/com.velvet.core")
WORKFLOW = REPO_ROOT / ".github/workflows/per-case-coverage.yml"


def name(number):
    return "TestCoverageResults_{:04d}.xml".format(number)


def content(number):
    return "<CoverageSession>{}</CoverageSession>".format(number).encode()


def write(directory, numbers):
    directory.mkdir(parents=True, exist_ok=True)
    for number in numbers:
        (directory / name(number)).write_bytes(content(number))


def copy_of(target, number):
    return gzip.decompress((target / "EditMode" / (name(number) + ".gz")).read_bytes())


def project(holder, version=per_case_coverage.PINNED_PACKAGE, asmdefs=(("Runtime", "Velvet"),)):
    """A project resolving Code Coverage `version`, whose package declares each (folder, name) asmdef."""
    root = Path(holder) / "project"
    (root / "Packages").mkdir(parents=True)
    (root / "Packages" / "packages-lock.json").write_text(json.dumps(
        {"dependencies": {per_case_coverage.PACKAGE: {"version": version}}}))
    for folder, declared in asmdefs:
        directory = root / PACKAGE_ROOT / folder
        directory.mkdir(parents=True, exist_ok=True)
        (directory / (declared + ".asmdef")).write_text(json.dumps({"name": declared}))
    return root


def assembly_filter(command):
    """The assemblyFilters value of the -coverageOptions a launch was given."""
    options = command[command.index("-coverageOptions") + 1].split(";")
    return [option for option in options if option.startswith("assemblyFilters:")][0][len("assemblyFilters:"):]


class Editor:
    """Stands in for `licensed_editor.run_licensed`, writing `numbers` where the package would and
    answering with `status`. With `blocked_for`, a file stands where the copies' directory goes for that
    long after the files are written, so every compressing pass in that time fails; with `keep_blocked`,
    it is never taken away."""

    def __init__(self, numbers=(0, 1), status=0, wait_for_copy=0.0, blocked_for=0.0, keep_blocked=False):
        self.numbers, self.status, self.wait_for_copy = numbers, status, wait_for_copy
        self.blocked_for, self.keep_blocked = blocked_for, keep_blocked
        self.commands = []
        self.copied_during_run = False

    def __call__(self, command, what):
        self.commands.append(command)
        coverage = Path(command[command.index("-coverageResultsPath") + 1])
        blocker = coverage.parent / "per-case"
        if self.blocked_for or self.keep_blocked:
            blocker.write_text("")
        write(coverage / "project-opencov" / "EditMode", self.numbers)
        if self.blocked_for:
            time.sleep(self.blocked_for)
            blocker.unlink()
        per_case = blocker / "project-opencov" / "EditMode" / (name(self.numbers[0]) + ".gz")
        deadline = time.time() + self.wait_for_copy
        while time.time() < deadline and not per_case.exists():
            time.sleep(0.01)
        self.copied_during_run = per_case.exists()
        return self.status


def run_main(holder, editor, *extra, poll=3600, root=None):
    output = Path(holder) / "output"
    status = per_case_coverage.main(["--platform", "EditMode", "--output", str(output),
                                     "--project", str(root or project(holder)), *extra], run=editor, poll=poll)
    return status, output


class CompressFinishedTests(unittest.TestCase):

    def compress(self, numbers, *passes):
        """The source and target directories after a pass of `compress_finished` for each `final`."""
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        source, target = Path(holder.name) / "coverage", Path(holder.name) / "per-case"
        write(source / "EditMode", numbers)
        for final in passes:
            per_case_coverage.compress_finished(source, target, final=final)
        return source / "EditMode", target

    def test_Given_PerCaseFilesBeingWritten_When_CompressedBeforeTheEditorExits_Then_TheNewestIsLeftAsItWas(self):
        # Arrange — the package may still be writing the newest one.
        numbers = (0, 1)

        # Act
        source, _ = self.compress(numbers, False)

        # Assert
        self.assertEqual((source / name(1)).read_bytes(), content(1))

    def test_Given_PerCaseFilesBeingWritten_When_CompressedBeforeTheEditorExits_Then_AnOlderOnesCopyHoldsWhatItHeld(self):
        # Arrange
        numbers = (0, 1)

        # Act
        _, target = self.compress(numbers, False)

        # Assert
        self.assertEqual(copy_of(target, 0), content(0))

    def test_Given_ACompressedFile_When_ItsFolderIsRead_Then_AnEmptyFileStandsUnderItsName(self):
        # Arrange — the package numbers its next file by how many .xml files the folder holds.
        numbers = (0, 1)

        # Act
        source, _ = self.compress(numbers, False)

        # Assert — None where the name is gone, which an emptied file never reads as.
        path = source / name(0)
        self.assertEqual(path.stat().st_size if path.exists() else None, 0)

    def test_Given_TheTenThousandthFile_When_TheFolderIsRead_Then_ItIsTakenAsTheNewestByItsNumber(self):
        # Arrange — by name, `_9999` sorts after `_10000` and would be taken for the newest.
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        write(Path(holder.name), (9999, 10000))

        # Act
        newest = per_case_coverage.numbered_files(Path(holder.name))[-1].name

        # Assert
        self.assertEqual(newest, "TestCoverageResults_10000.xml")

    def test_Given_AFileCompressedDuringTheRun_When_TheEditorHasExited_Then_ItsCopyIsNotReplacedByTheEmptyOne(self):
        # Arrange
        _, target = self.compress((0, 1), False)
        copy = target / "EditMode" / (name(0) + ".gz")
        written = copy.read_bytes()

        # Act
        per_case_coverage.compress_finished(target.parent / "coverage", target, final=True)

        # Assert
        self.assertEqual(copy.read_bytes(), written)


class MainTests(unittest.TestCase):

    def holder(self):
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        return holder.name

    def test_Given_AProjectResolvingAnotherCoverageVersion_When_Run_Then_NoEditorStartsAndTheRunIsRefused(self):
        # Arrange
        holder, editor = self.holder(), Editor()
        root = project(holder, version="1.4.0")

        # Act
        status = per_case_coverage.main(["--platform", "EditMode", "--output", str(Path(holder) / "out"),
                                         "--project", str(root)], run=editor)

        # Assert — the status only where no editor was started, which no refusal can read as.
        self.assertEqual(status if not editor.commands else "an editor started", 2)

    def test_Given_AnOutputThatAlreadyHoldsARun_When_Run_Then_NoEditorStartsAndTheRunIsRefused(self):
        # Arrange — a second run's files would take the first run's names and be passed over.
        holder, editor = self.holder(), Editor()
        (Path(holder) / "output" / "per-case").mkdir(parents=True)
        (Path(holder) / "output" / "per-case" / (name(0) + ".gz")).write_bytes(b"")

        # Act
        status, _ = run_main(holder, editor)

        # Assert — the status only where no editor was started, which no refusal can read as.
        self.assertEqual(status if not editor.commands else "an editor started", 2)

    def test_Given_AProjectDeclaringNoPackageAssembly_When_Run_Then_NoEditorStartsAndTheRunIsRefused(self):
        # Arrange — an empty filter would leave the package's default to choose what is measured.
        holder, editor = self.holder(), Editor()
        root = project(holder, asmdefs=())

        # Act
        status, _ = run_main(holder, editor, root=root)

        # Assert — the status only where no editor was started, which no refusal can read as.
        self.assertEqual(status if not editor.commands else "an editor started", 2)

    def test_Given_TheRepositorysProject_When_TheEditorIsLaunched_Then_TheWeaversAssemblyIsMeasured(self):
        # Arrange — the weaver fixtures run Unity.Velvet.CodeGen in the editor domain.
        editor = Editor()

        # Act
        run_main(self.holder(), editor, root=REPO_ROOT)

        # Assert
        self.assertIn("+Unity.Velvet.CodeGen", assembly_filter(editor.commands[0]).split(","))

    def test_Given_AsmdefsUnderATestsAndATildeFolder_When_TheEditorIsLaunched_Then_OnlyThePackagesOwnIsMeasured(self):
        # Arrange
        holder, editor = self.holder(), Editor()
        root = project(holder, asmdefs=(("Runtime", "Velvet"), ("Runtime/Tests/Editor", "Velvet.Tests.A"),
                                        ("TestUtilities", "Velvet.TestUtilities"), ("Samples~/App", "Sample")))

        # Act
        run_main(holder, editor, root=root)

        # Assert
        self.assertEqual(assembly_filter(editor.commands[0]), "+Velvet")

    def test_Given_ARun_When_TheEditorIsLaunched_Then_ItIsAskedForEachCasesReferences(self):
        # Arrange
        editor = Editor()

        # Act
        run_main(self.holder(), editor)

        # Assert
        command = editor.commands[0]
        self.assertIn("generateTestReferences", command[command.index("-coverageOptions") + 1].split(";"))

    def test_Given_FilesTheEditorLeftBehind_When_ItHasExited_Then_EachIsCompressed(self):
        # Arrange — a poll longer than the run, so nothing but the closing pass compresses.
        editor = Editor(numbers=(0, 1))

        # Act
        _, output = run_main(self.holder(), editor)

        # Assert
        self.assertEqual(sorted(path.name for path in (output / "per-case").rglob("*.gz")),
                         [name(0) + ".gz", name(1) + ".gz"])

    def test_Given_AnEditorStillRunning_When_AnEarlierPassCouldNotCompress_Then_ALaterOneCompressesBeforeItExits(self):
        # Arrange — the passes in the first fifth of a second fail, and the editor then waits for the
        # copy of its older file before exiting.
        editor = Editor(numbers=(0, 1), wait_for_copy=10.0, blocked_for=0.2)

        # Act
        run_main(self.holder(), editor, poll=0.01)

        # Assert
        self.assertTrue(editor.copied_during_run)

    def test_Given_AnEditorThatFailed_When_TheRunEnds_Then_ItsStatusIsTheRunsOwn(self):
        # Arrange
        editor = Editor(status=3)

        # Act
        status, _ = run_main(self.holder(), editor)

        # Assert
        self.assertEqual(status, 3)

    def test_Given_AnEditorThatPassed_When_ItsFilesCannotBeCompressed_Then_TheRunFails(self):
        # Arrange — the editor leaves a file where the copies' directory has to be.
        holder, editor = self.holder(), Editor(keep_blocked=True)

        # Act
        status, _ = run_main(holder, editor)

        # Assert
        self.assertEqual(status, 1)


def job_block(text, job):
    """The lines of `job` in a workflow, up to the next job key."""
    lines = text.splitlines()
    start = lines.index("  {}:".format(job))
    end = next((index for index in range(start + 1, len(lines))
                if re.match(r"^  [A-Za-z_][\w-]*:\s*$", lines[index])), len(lines))
    return lines[start:end]


def run_sha_step(github_ref, default_branch="main"):
    """What the commit job's sha step writes to GITHUB_OUTPUT for a run on `github_ref`, its env read
    off the workflow with the default branch's expression standing for `default_branch`."""
    block = job_block(WORKFLOW.read_text(), "commit")
    step = block[block.index("      - id: sha") + 1:]
    step = step[:next((index for index, line in enumerate(step) if line.startswith("      - ")), len(step))]
    run = next(index for index, line in enumerate(step) if line.startswith("        run:"))
    env = dict(re.findall(r"^          (\w+): (.+)$", "\n".join(step[:run]), re.M))
    body = step[run + 1:]
    body = body[:next((index for index, line in enumerate(body)
                       if line.strip() and not line.startswith(" " * 10)), len(body))]
    inline = step[run][len("        run:"):].strip()
    script = inline if inline != "|" else "\n".join(line[10:] for line in body)
    default = {name: value.replace("${{ github.event.repository.default_branch }}", default_branch)
               for name, value in env.items()}
    with tempfile.TemporaryDirectory() as holder:
        output = Path(holder) / "output"
        output.write_text("")
        subprocess.run(["bash", "-e", "-c", script], cwd=str(REPO_ROOT), check=True, capture_output=True,
                       env=dict(os.environ, GITHUB_REF=github_ref, GITHUB_OUTPUT=str(output), **default))
        return output.read_text()


class WorkflowTests(unittest.TestCase):

    def test_Given_ARunDispatchedOntoAnotherBranch_When_TheCommitJobRuns_Then_ItResolvesNoCommit(self):
        # Arrange — that run's head is the other branch's, which settle.py waits on.
        github_ref = "refs/heads/my-pruning-branch"

        # Act
        written = run_sha_step(github_ref)

        # Assert
        self.assertEqual(written, "")

    def test_Given_ARunOnTheDefaultBranch_When_TheCommitJobRuns_Then_ItResolvesTheCommit(self):
        # Arrange
        github_ref = "refs/heads/main"

        # Act
        written = run_sha_step(github_ref)

        # Assert
        self.assertTrue(written.startswith("sha="))

    def test_Given_TheWorkflow_When_ItsSuiteJobIsRead_Then_ItRunsOnlyForACommitTheCommitJobResolved(self):
        # Arrange
        block = job_block(WORKFLOW.read_text(), "suite")

        # Act
        condition = [line for line in block if line.startswith("    if:")][0]

        # Assert
        self.assertIn("needs.commit.outputs.sha != ''", condition)


if __name__ == "__main__":
    unittest.main(verbosity=2)

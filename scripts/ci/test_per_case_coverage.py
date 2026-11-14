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


def project(holder, version=per_case_coverage.PINNED_PACKAGE):
    root = Path(holder) / "project"
    (root / "Packages").mkdir(parents=True)
    (root / "Packages" / "packages-lock.json").write_text(json.dumps(
        {"dependencies": {per_case_coverage.PACKAGE: {"version": version}}}))
    return root


class Editor:
    """Stands in for `licensed_editor.run_licensed`, writing `numbers` where the package would and
    answering with `status`. With `blocked_for`, a file stands where the copies' directory goes for that
    long after the files are written, so every compressing pass in that time fails."""

    def __init__(self, numbers=(0, 1), status=0, wait_for_copy=0.0, blocked_for=0.0):
        self.numbers, self.status, self.wait_for_copy = numbers, status, wait_for_copy
        self.blocked_for = blocked_for
        self.commands = []
        self.copied_during_run = False

    def __call__(self, command, what):
        self.commands.append(command)
        coverage = Path(command[command.index("-coverageResultsPath") + 1])
        blocker = coverage.parent / "per-case"
        if self.blocked_for:
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


def run_main(holder, editor, *extra, poll=3600):
    output = Path(holder) / "output"
    status = per_case_coverage.main(["--platform", "EditMode", "--output", str(output),
                                     "--project", str(project(holder)), *extra], run=editor, poll=poll)
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
        # Arrange — a file where the copies' directory has to be.
        holder, editor = self.holder(), Editor()
        (Path(holder) / "output").mkdir()
        (Path(holder) / "output" / "per-case").write_text("")

        # Act
        status, _ = run_main(holder, editor)

        # Assert
        self.assertEqual(status, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)

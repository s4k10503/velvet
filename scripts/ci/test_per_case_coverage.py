#!/usr/bin/env python3
"""Unit tests for per_case_coverage.py: which files it compresses while the editor writes them, which
cases each launch is given, and the run it hands licensed_editor.py.

The editor is replaced by a writer that names its files the way Code Coverage numbers them and a results
file that names the cases it was filtered to, since what the cases ask is what each directory holds
afterwards.

Run: python3 scripts/ci/test_per_case_coverage.py
"""

import gzip
import importlib.util
import json
import os
import re
import subprocess
import tempfile
import threading
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


CASES = tuple("Velvet.Tests.Fixture{}.Given_{}".format(fixture, case) for fixture in "ABC" for case in "123")


def results_file(path, names, total=None):
    path.parent.mkdir(parents=True, exist_ok=True)
    cases = "".join('<test-case fullname="{}" result="Passed"/>'.format(name) for name in names)
    path.write_text('<test-run total="{}">{}</test-run>'.format(len(names) if total is None else total, cases))


class Editor:
    """Stands in for the editor `measure` launches. A launch without `-enableCodeCoverage` is the plain
    run, and writes a results file naming `cases`. A launch with it takes the cases its `-testFilter`
    selects, writes `numbers` where the package would, and answers `status`; one over more than
    `kill_over` cases is killed as the container kills an editor, answering -9 with its first file
    written. With `ran`, its results file claims that many cases instead of the ones selected."""

    def __init__(self, cases=CASES, numbers=(0, 1), status=0, plain_status=0, kill_over=None, ran=None,
                 blocked=False):
        self.cases, self.numbers, self.status, self.plain_status = cases, numbers, status, plain_status
        self.kill_over, self.ran, self.blocked = kill_over, ran, blocked
        self.commands = []

    def launched(self):
        """Each launch with coverage on, as the cases its filter selected."""
        return [self.selected(command) for command in self.commands if "-enableCodeCoverage" in command]

    def selected(self, command):
        terms = command[command.index("-testFilter") + 1].split(";")
        return [name for name in self.cases if any(re.search(term, name) for term in terms)]

    def __call__(self, command):
        self.commands.append(command)
        results = Path(command[command.index("-testResults") + 1])
        if "-enableCodeCoverage" not in command:
            results_file(results, self.cases)
            return self.plain_status
        chosen = self.selected(command)
        coverage = Path(command[command.index("-coverageResultsPath") + 1])
        killed = self.kill_over is not None and len(chosen) > self.kill_over
        if self.blocked:
            (coverage.parent.parent / "per-case").mkdir(parents=True, exist_ok=True)
            (coverage.parent.parent / "per-case" / coverage.name).write_text("")
        folder = coverage / "project-opencov" / "EditMode"
        write(folder, self.numbers)
        if killed:
            # The package was streaming its newest file when the editor died.
            newest = folder / name(self.numbers[-1])
            newest.write_bytes(newest.read_bytes()[:len(newest.read_bytes()) // 2])
            return -9
        results_file(results, chosen, self.ran)
        return self.status


def run_measure(holder, editor, *extra, poll=3600, root=None):
    output = Path(holder) / "output"
    status = per_case_coverage.main(["--measure", "--platform", "EditMode", "--output", str(output),
                                     "--project", str(root or project(holder)), *extra],
                                    launch=editor, poll=poll)
    return status, output


def run_main(holder, run, *extra, root=None):
    output = Path(holder) / "output"
    status = per_case_coverage.main(["--platform", "EditMode", "--output", str(output),
                                     "--project", str(root or project(holder)), *extra], run=run)
    return status, output


class Licence:
    """Stands in for `licensed_editor.run_licensed`, which runs a command and answers its status."""

    def __init__(self, status=0):
        self.status, self.commands = status, []

    def __call__(self, command, what):
        self.commands.append(command)
        return self.status


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


class ShardingTests(unittest.TestCase):

    def test_Given_ASuiteAndAShardCount_When_EachBlockIsTaken_Then_TheBlocksHoldEveryCaseOnce(self):
        # Arrange
        names = ["case{:02d}".format(number) for number in range(10)]

        # Act
        blocks = [per_case_coverage.block_of(names, shard, 4) for shard in (1, 2, 3, 4)]

        # Assert
        self.assertEqual([name for block in blocks for name in block], names)

    def test_Given_NamesCarryingRegexMetacharactersAndSeparators_When_AFilterIsBuilt_Then_ItSelectsOnlyThem(self):
        # Arrange — a nested fixture's `+`, an argument list, and a `;` that would split a term, the
        # first half of which is a prefix of "aZ"; each of the others differs from one of them by the
        # one character a loose term would let through.
        wanted = ["N.Outer+Inner.Given_X(1,2)", "N.Outer.Given_Y(\"a;b\")"]
        others = ["N.OuterInner.Given_X(1,2)", "N.Outer+Inner.Given_X(1,3)", "N.Outer.Given_Y(\"aXb\")",
                  "N.Outer.Given_Y(\"aZ\")", "N.Outer+Inner.Given_X(1,2)tail"]
        value = per_case_coverage.filter_for(wanted)

        # Act
        selected = [name for name in wanted + others if any(re.search(term, name) for term in value.split(";"))]

        # Assert — "aXb" is the one near name a `;` read as any character lets through, and the cost of
        # keeping the term whole.
        self.assertEqual(selected, wanted + ["N.Outer.Given_Y(\"aXb\")"])

    def test_Given_AResultsFile_When_ItsRosterIsRead_Then_EachNameIsHeldOnceInOrder(self):
        # Arrange
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        path = Path(holder.name) / "results.xml"
        results_file(path, ["b.Two", "a.One", "b.Two"])

        # Act
        names = per_case_coverage.roster_of(path)

        # Assert
        self.assertEqual(names, ["a.One", "b.Two"])

    def test_Given_NoResultsFile_When_ItsRosterIsRead_Then_NoCaseIsNamed(self):
        # Arrange
        path = Path(tempfile.gettempdir()) / "per-case-coverage-no-such-results.xml"

        # Act
        names = per_case_coverage.roster_of(path)

        # Assert
        self.assertEqual(names, [])


class MemoryTests(unittest.TestCase):

    def test_Given_ACgroupFileHoldingBytes_When_MemoryIsRead_Then_ItIsInMebibytes(self):
        # Arrange
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        path = Path(holder.name) / "memory.current"
        path.write_text("{}\n".format(3 * 1024 * 1024 * 5))

        # Act
        held = per_case_coverage.memory_in_use((path,))

        # Assert
        self.assertEqual(held, 15)

    def test_Given_NoCgroupFile_When_MemoryIsRead_Then_NothingIsReported(self):
        # Arrange
        path = Path(tempfile.gettempdir()) / "per-case-coverage-no-such-cgroup"

        # Act
        held = per_case_coverage.memory_in_use((path,))

        # Assert
        self.assertIsNone(held)

    def test_Given_AWatcherSamplingMemory_When_ItIsStopped_Then_ThePeakIsTheLargestSample(self):
        # Arrange
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        samples = iter([100, 700, 300] + [300] * 1000)
        stop, peak = threading.Event(), [0]
        watcher = threading.Thread(target=per_case_coverage.watch, args=(
            Path(holder.name) / "coverage", Path(holder.name) / "per-case", stop, 0.01, peak,
            lambda: next(samples)))

        # Act
        watcher.start()
        time.sleep(0.3)
        stop.set()
        watcher.join()

        # Assert
        self.assertEqual(peak[0], 700)


class MainTests(unittest.TestCase):

    def holder(self):
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        return holder.name

    def refused(self, holder, licence, root=None, *extra):
        status, _ = run_main(holder, licence, *extra, root=root)
        # The status only where nothing was started, which no refusal can read as.
        return status if not licence.commands else "a run started"

    def test_Given_AProjectResolvingAnotherCoverageVersion_When_Run_Then_NothingStartsAndTheRunIsRefused(self):
        # Arrange
        holder, licence = self.holder(), Licence()

        # Act
        refused = self.refused(holder, licence, project(holder, version="1.4.0"))

        # Assert
        self.assertEqual(refused, 2)

    def test_Given_AnOutputThatAlreadyHoldsARun_When_Run_Then_NothingStartsAndTheRunIsRefused(self):
        # Arrange — a second run's files would take the first run's names and be passed over.
        holder, licence = self.holder(), Licence()
        (Path(holder) / "output" / "per-case").mkdir(parents=True)
        (Path(holder) / "output" / "per-case" / (name(0) + ".gz")).write_bytes(b"")

        # Act
        refused = self.refused(holder, licence)

        # Assert
        self.assertEqual(refused, 2)

    def test_Given_AProjectDeclaringNoPackageAssembly_When_Run_Then_NothingStartsAndTheRunIsRefused(self):
        # Arrange — an empty filter would leave the package's default to choose what is measured.
        holder, licence = self.holder(), Licence()

        # Act
        refused = self.refused(holder, licence, project(holder, asmdefs=()))

        # Assert
        self.assertEqual(refused, 2)

    def test_Given_AShardPastTheLastOne_When_Run_Then_NothingStartsAndTheRunIsRefused(self):
        # Arrange — a block no shard takes would leave its cases unmeasured with every job green.
        holder, licence = self.holder(), Licence()

        # Act
        refused = self.refused(holder, licence, None, "--shard", "7/6")

        # Assert
        self.assertEqual(refused, 2)

    def test_Given_AShard_When_Run_Then_TheLicensedCommandIsTheMeasuringRunOfThatShard(self):
        # Arrange
        holder, licence = self.holder(), Licence()

        # Act
        run_main(holder, licence, "--shard", "2/6")

        # Assert
        command = licence.commands[0]
        self.assertEqual(command[command.index("--measure"):][:1] + [command[command.index("--shard") + 1]],
                         ["--measure", "2/6"])

    def test_Given_TheLicensedRun_When_ItEnds_Then_ItsStatusIsTheRunsOwn(self):
        # Arrange
        holder, licence = self.holder(), Licence(status=3)

        # Act
        status, _ = run_main(holder, licence)

        # Assert
        self.assertEqual(status, 3)


class MeasureTests(unittest.TestCase):

    def holder(self):
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        return holder.name

    def test_Given_NineCasesAndLaunchesOfFour_When_Measured_Then_TheLaunchesTakeFourFourAndOne(self):
        # Arrange
        editor = Editor()

        # Act
        run_measure(self.holder(), editor, "--cases-per-launch", "4")

        # Assert
        self.assertEqual([len(cases) for cases in editor.launched()], [4, 4, 1])

    def test_Given_TheSuitesCases_When_Measured_Then_EachIsInOneLaunch(self):
        # Arrange
        editor = Editor()

        # Act
        run_measure(self.holder(), editor, "--cases-per-launch", "4")

        # Assert
        self.assertEqual(sorted(case for cases in editor.launched() for case in cases), sorted(CASES))

    def test_Given_TheSecondOfThreeShards_When_Measured_Then_OnlyItsBlockIsLaunched(self):
        # Arrange
        editor = Editor()

        # Act
        run_measure(self.holder(), editor, "--shard", "2/3")

        # Assert
        self.assertEqual([case for cases in editor.launched() for case in cases], sorted(CASES)[3:6])

    def test_Given_APlainRunThatFailed_When_Measured_Then_NoLaunchWithCoverageStarts(self):
        # Arrange — a suite that does not hold on the commit measures nothing worth reading.
        editor = Editor(plain_status=2)

        # Act
        status, _ = run_measure(self.holder(), editor)

        # Assert — the status only where no launch with coverage started.
        self.assertEqual(status if not editor.launched() else "a launch started", 2)

    def test_Given_ALaunchTheContainerKilled_When_Measured_Then_ItsCasesRunAgainAsHalves(self):
        # Arrange — nine cases are over what this editor survives, and four or five are not.
        editor = Editor(kill_over=5)

        # Act
        run_measure(self.holder(), editor, "--cases-per-launch", "9")

        # Assert
        self.assertEqual([len(cases) for cases in editor.launched()], [9, 4, 5])

    def test_Given_ALaunchTheContainerKilled_When_ItsHalvesPass_Then_TheRunPasses(self):
        # Arrange
        editor = Editor(kill_over=5)

        # Act
        status, _ = run_measure(self.holder(), editor, "--cases-per-launch", "9")

        # Assert
        self.assertEqual(status, 0)

    def test_Given_AKilledLaunchOfOneCase_When_Measured_Then_TheRunFailsWithTheKill(self):
        # Arrange — one case alone is more than the container holds.
        editor = Editor(cases=CASES[:2], kill_over=0)

        # Act
        status, _ = run_measure(self.holder(), editor, "--cases-per-launch", "1")

        # Assert
        self.assertEqual(status, -9)

    def test_Given_ALaunchThatRanFewerCasesThanItWasGiven_When_Measured_Then_TheRunFails(self):
        # Arrange — a filter that matched too little reports green over the smaller set.
        editor = Editor(ran=1)

        # Act
        status, _ = run_measure(self.holder(), editor, "--cases-per-launch", "4")

        # Assert
        self.assertEqual(status, 1)

    def test_Given_ALaunchThatFailedItsCases_When_Measured_Then_TheRunFailsWithItsStatus(self):
        # Arrange
        editor = Editor(status=2)

        # Act
        status, _ = run_measure(self.holder(), editor)

        # Assert
        self.assertEqual(status, 2)

    def test_Given_ALaunchTheContainerKilled_When_ItsFilesAreCompressed_Then_TheCutOffNewestIsNotAmongThem(self):
        # Arrange — a lone case cannot be halved, so its launch's records are all there is to read.
        editor = Editor(cases=CASES[:1], kill_over=0)

        # Act
        _, output = run_measure(self.holder(), editor, "--cases-per-launch", "1")

        # Assert
        self.assertEqual(sorted(path.name for path in (output / "per-case").rglob("*.gz")), [name(0) + ".gz"])

    def test_Given_AFailedLaunch_When_Measured_Then_TheManifestNamesIt(self):
        # Arrange
        editor = Editor(status=2)

        # Act
        _, output = run_measure(self.holder(), editor)

        # Assert
        manifest = json.loads((output / "per-case" / "shard.json").read_text())
        self.assertEqual(manifest["problems"], ["launch 0001: status 2"])

    def test_Given_AShardOfTwo_When_Measured_Then_TheManifestHoldsTheWholeRostersLength(self):
        # Arrange
        editor = Editor()

        # Act
        _, output = run_measure(self.holder(), editor, "--shard", "2/2")

        # Assert
        manifest = json.loads((output / "per-case" / "shard.json").read_text())
        self.assertEqual(manifest["roster"], len(CASES))

    def test_Given_ARun_When_ALaunchIsMade_Then_ItIsAskedForEachCasesReferences(self):
        # Arrange
        editor = Editor()

        # Act
        run_measure(self.holder(), editor)

        # Assert
        command = [command for command in editor.commands if "-enableCodeCoverage" in command][0]
        self.assertIn("generateTestReferences", command[command.index("-coverageOptions") + 1].split(";"))

    def test_Given_TheRepositorysProject_When_ALaunchIsMade_Then_TheWeaversAssemblyIsMeasured(self):
        # Arrange — the weaver fixtures run Unity.Velvet.CodeGen in the editor domain.
        editor = Editor()

        # Act
        run_measure(self.holder(), editor, root=REPO_ROOT)

        # Assert
        command = [command for command in editor.commands if "-enableCodeCoverage" in command][0]
        self.assertIn("+Unity.Velvet.CodeGen", assembly_filter(command).split(","))

    def test_Given_AsmdefsUnderATestsAndATildeFolder_When_ALaunchIsMade_Then_OnlyThePackagesOwnIsMeasured(self):
        # Arrange
        holder, editor = self.holder(), Editor()
        root = project(holder, asmdefs=(("Runtime", "Velvet"), ("Runtime/Tests/Editor", "Velvet.Tests.A"),
                                        ("TestUtilities", "Velvet.TestUtilities"), ("Samples~/App", "Sample")))

        # Act
        run_measure(holder, editor, root=root)

        # Assert
        command = [command for command in editor.commands if "-enableCodeCoverage" in command][0]
        self.assertEqual(assembly_filter(command), "+Velvet")

    def test_Given_ARun_When_Measured_Then_TheAssembliesItWasAskedForAreWrittenBesideTheRecords(self):
        # Arrange — the reader refuses an asked assembly no record reaches, and reads what was asked here.
        editor = Editor()

        # Act
        _, output = run_measure(self.holder(), editor)

        # Assert — None where no such file was written, which the list it names never reads as.
        written = output / "per-case" / "asked-assemblies.txt"
        self.assertEqual(written.read_text() if written.exists() else None, "Velvet\n")

    def test_Given_FilesALaunchLeftBehind_When_ItHasExited_Then_EachIsCompressedUnderTheLaunchs_Folder(self):
        # Arrange — a poll longer than the run, so nothing but the closing pass compresses.
        editor = Editor(cases=CASES[:1], numbers=(0, 1))

        # Act
        _, output = run_measure(self.holder(), editor)

        # Assert
        self.assertEqual(sorted(path.relative_to(output / "per-case").as_posix()
                                for path in (output / "per-case").rglob("*.gz")),
                         ["0001/project-opencov/EditMode/" + name(0) + ".gz",
                          "0001/project-opencov/EditMode/" + name(1) + ".gz"])

    def test_Given_TwoLaunches_When_TheirFilesAreRead_Then_NeitherTakesTheOthersNames(self):
        # Arrange — each launch's folder is its own, so the second's `_0000` is not passed over.
        editor = Editor(cases=CASES[:2], numbers=(0, 1))

        # Act
        _, output = run_measure(self.holder(), editor, "--cases-per-launch", "1")

        # Assert
        self.assertEqual(len(list((output / "per-case").rglob("*.gz"))), 4)

    def test_Given_AnEditorThatPassed_When_ItsFilesCannotBeCompressed_Then_TheRunFails(self):
        # Arrange — the editor leaves a file where the launch's copies' folder has to be.
        editor = Editor(blocked=True)

        # Act
        status, _ = run_measure(self.holder(), editor)

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
    """(exit status, what it wrote to GITHUB_OUTPUT) of the commit job's sha step for a run on
    `github_ref`, its env read off the workflow with the default branch's expression standing for
    `default_branch`."""
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
        done = subprocess.run(["bash", "-e", "-c", script], cwd=str(REPO_ROOT), capture_output=True,
                              env=dict(os.environ, GITHUB_REF=github_ref, GITHUB_OUTPUT=str(output), **default))
        return done.returncode, output.read_text()


class WorkflowTests(unittest.TestCase):

    def test_Given_ARunDispatchedOntoAnotherBranch_When_TheCommitJobRuns_Then_ItResolvesNoCommit(self):
        # Arrange — that run's head is the other branch's, which settle.py waits on.
        github_ref = "refs/heads/my-pruning-branch"

        # Act
        _, written = run_sha_step(github_ref)

        # Assert
        self.assertEqual(written, "")

    def test_Given_ARunOnTheDefaultBranch_When_TheCommitJobRuns_Then_ItResolvesTheCommit(self):
        # Arrange
        github_ref = "refs/heads/main"

        # Act
        _, written = run_sha_step(github_ref)

        # Assert
        self.assertTrue(written.startswith("sha="))

    def test_Given_AnEventNamingNoDefaultBranch_When_TheCommitJobRuns_Then_TheStepFails(self):
        # Arrange — skipping there would end every dispatch green with nothing measured.
        github_ref = "refs/heads/main"

        # Act
        status, _ = run_sha_step(github_ref, default_branch="")

        # Assert
        self.assertEqual(status, 1)

    def test_Given_TheWorkflow_When_ItsSuiteJobIsRead_Then_ItRunsOnlyForACommitTheCommitJobResolved(self):
        # Arrange
        block = job_block(WORKFLOW.read_text(), "suite")

        # Act
        condition = [line for line in block if line.startswith("    if:")][0]

        # Assert
        self.assertIn("needs.commit.outputs.sha != ''", condition)

    def test_Given_TheWorkflowsMatrix_When_ItsShardsAreRead_Then_EachSuiteHasEveryBlockOfItsCount(self):
        # Arrange — a block no shard takes is a run of cases measured by nobody, with every job green.
        block = "\n".join(job_block(WORKFLOW.read_text(), "suite"))
        read = re.findall(r"\{ testMode: (\w+), shard: (\d+), shards: (\d+) \}", block)
        by_mode = {}
        for mode, shard, shards in read:
            by_mode.setdefault(mode, []).append((int(shard), int(shards)))

        # Act
        problems = ["{}: {}".format(mode, entries) for mode, entries in sorted(by_mode.items())
                    if [shard for shard, _ in entries] != list(range(1, entries[0][1] + 1))
                    or {shards for _, shards in entries} != {entries[0][1]}]

        # Assert — the floor is on what was read, since a reader finding no entry reports no problem.
        self.assertEqual(problems or sorted(by_mode), ["EditMode", "PlayMode"])

    def test_Given_TheWorkflow_When_ItsReportJobIsRead_Then_ItRunsWhateverTheSuiteJobsEndedWith(self):
        # Arrange
        block = job_block(WORKFLOW.read_text(), "report")

        # Act
        condition = [line for line in block if line.startswith("    if:")][0]

        # Assert
        self.assertIn("!cancelled()", condition)

    def test_Given_TheWorkflow_When_ItsResultsUploadIsRead_Then_TheLaunchesLogsAreInIt(self):
        # Arrange
        block = "\n".join(job_block(WORKFLOW.read_text(), "suite"))
        upload = block[block.index("name: per-case-results-"):block.index("name: per-case-records-")]

        # Act
        paths = re.findall(r"Logs/per-case-coverage/(\w+)", upload)

        # Assert
        self.assertEqual(paths, ["results", "launches"])

    def test_Given_TheWorkflow_When_ItsReportStepExpectsShards_Then_TheyAreTheMatrixsCounts(self):
        # Arrange
        text = WORKFLOW.read_text()
        matrix = {mode: int(shards) for mode, shards in re.findall(
            r"\{ testMode: (\w+), shard: 1, shards: (\d+) \}", "\n".join(job_block(text, "suite")))}

        # Act
        expected = {mode: int(count) for mode, count in re.findall(
            r"--expect (\w+)=(\d+)", "\n".join(job_block(text, "report")))}

        # Assert
        self.assertEqual(expected, matrix)

    def test_Given_TheWorkflow_When_ItsReportStepIsRead_Then_ItChecksTheShardsManifests(self):
        # Arrange
        block = "\n".join(job_block(WORKFLOW.read_text(), "report"))

        # Act
        checked = "per_case_manifest.py Logs/per-case" in block

        # Assert
        self.assertTrue(checked)

    def test_Given_TheWorkflow_When_ItsSuiteStepIsRead_Then_ItIsGivenTheShardOfItsMatrixEntry(self):
        # Arrange
        block = "\n".join(job_block(WORKFLOW.read_text(), "suite"))

        # Act
        given = re.findall(r"^\s+SHARD: (.+)$", block, re.M)

        # Assert
        self.assertEqual(given, ["${{ matrix.shard }}/${{ matrix.shards }}"])

    def test_Given_TheWorkflow_When_ItsSuiteStepIsRead_Then_TheScriptIsPassedThatShard(self):
        # Arrange
        block = "\n".join(job_block(WORKFLOW.read_text(), "suite"))

        # Act
        passed = re.findall(r"per_case_coverage\.py (.*)$", block, re.M)

        # Assert
        self.assertIn('--shard "$SHARD"', passed[0].split("--platform", 1)[1])

    def test_Given_TheWorkflow_When_ItsReportJobDownloadsRecords_Then_ItsPatternTakesWhatEachShardUploads(self):
        # Arrange
        text = WORKFLOW.read_text()
        pattern = re.search(r"pattern: (\S+)", "\n".join(job_block(text, "report"))).group(1)
        uploaded = re.search(r"name: (per-case-records-\S+)", "\n".join(job_block(text, "suite"))).group(1)

        # Act
        taken = uploaded.startswith(pattern.rstrip("*")) and pattern.endswith("*")

        # Assert
        self.assertTrue(taken)


if __name__ == "__main__":
    unittest.main(verbosity=2)

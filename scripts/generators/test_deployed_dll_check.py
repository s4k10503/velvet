#!/usr/bin/env python3
"""Unit tests for deployed_dll_check.py's refusals and its byte comparison.

The refusal cases are the point: each is a state the check cannot compare in, and a check that exits 0
there is indistinguishable from one that compared and was satisfied. They run without a .NET SDK,
because an absent SDK is one of those states.

Run: python3 scripts/generators/test_deployed_dll_check.py
"""

import importlib.util
import io
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock


def load_module():
    """Imports the check by path, since scripts/generators is not a package."""
    spec = importlib.util.spec_from_file_location(
        "deployed_dll_check", Path(__file__).with_name("deployed_dll_check.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def load_build_script():
    """The repository's own build.py, for the cases that must not pass against a stand-in."""
    root = Path(__file__).resolve().parents[2] / "Packages/com.velvet.core/Generators~"
    return check.build_script(root)


check = load_module()

BUILD_PY = """
CONFIGURATION = "Release"
DEPLOYMENTS = (
    ("Only", "src/Only/Only.csproj", "src/Only/bin/Release/netstandard2.0/Only.dll", "../Plugins"),
)
"""


def deployment(root, built_bytes=b"same", committed_bytes=b"same"):
    """A single deployment on disk; None for either side leaves that file absent."""
    built = root / "built" / "Only.dll"
    committed = root / "committed" / "Only.dll"
    for path, content in ((built, built_bytes), (committed, committed_bytes)):
        if content is None:
            continue
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)
    return [check.Deployment("Only", root / "Only.csproj", built, committed)]


class ComparisonTests(unittest.TestCase):
    def test_Given_TheCommittedDllIsWhatTheSourcesBuild_When_Compared_Then_NothingIsReported(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            planned = deployment(Path(directory))

            # Act / Assert
            self.assertEqual(check.compare(planned), [])

    def test_Given_TheCommittedDllDiffersFromTheRebuild_When_Compared_Then_ItIsReported(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            planned = deployment(Path(directory), committed_bytes=b"stale")

            # Act
            problems = check.compare(planned)

            # Assert
            self.assertEqual(len(problems), 1)

    def test_Given_TheDifferenceIsOneByteInTheMiddle_When_Compared_Then_ItsOffsetIsNamed(self):
        # Arrange — a missed redeploy and a build that does not reproduce look the same without it.
        built, committed = b"velvet", b"velVet"

        # Act
        described = check.describe_difference(built, committed)

        # Assert
        self.assertIn("offset 3", described)

    def test_Given_NothingIsCommittedAtTheDeployedPath_When_Compared_Then_ItIsReportedNotSkipped(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            planned = deployment(Path(directory), committed_bytes=None)

            # Act
            problems = check.compare(planned)

            # Assert
            self.assertEqual(len(problems), 1)

    def test_Given_TheBuildProducedNoAssembly_When_Compared_Then_ItIsReportedNotSkipped(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            planned = deployment(Path(directory), built_bytes=None)

            # Act
            problems = check.compare(planned)

            # Assert
            self.assertEqual(len(problems), 1)

    def test_Given_BothSidesAreAbsent_When_Compared_Then_ItIsStillReported(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange — the state a wiped working tree is in, where nothing readable exists to agree.
            planned = deployment(Path(directory), built_bytes=None, committed_bytes=None)

            # Act
            problems = check.compare(planned)

            # Assert
            self.assertEqual(len(problems), 1)


class SdkPinTests(unittest.TestCase):
    def test_Given_TheInstalledSdkIsThePinnedOne_When_Asked_Then_ThereIsNoProblem(self):
        # Act / Assert
        self.assertIsNone(check.sdk_problem("10.0.103", "10.0.103"))

    def test_Given_TheInstalledSdkIsNotThePinnedOne_When_Asked_Then_ItIsAProblem(self):
        # Act / Assert
        self.assertIsNotNone(check.sdk_problem("10.0.103", "9.0.100"))

    def test_Given_GlobalJsonCannotBeRead_When_ThePinIsAsked_Then_ItRefuses(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange — an empty directory, so global.json is absent rather than malformed.
            root = Path(directory)

            # Act / Assert
            self.assertRaises(check.Refusal, check.pinned_sdk_version, root)

    def test_Given_GlobalJsonNamesNoVersion_When_ThePinIsAsked_Then_ItRefuses(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            root = Path(directory)
            (root / "global.json").write_text('{"sdk": {"rollForward": "disable"}}', encoding="utf-8")

            # Act / Assert
            self.assertRaises(check.Refusal, check.pinned_sdk_version, root)


class DeploymentMapTests(unittest.TestCase):
    def test_Given_BuildPyNamesADeployment_When_Read_Then_ItsCommittedPathIsDerivedFromTheName(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            root = Path(directory)
            (root / "build.py").write_text(BUILD_PY, encoding="utf-8")

            # Act
            planned = check.deployments(check.build_script(root), root)

            # Assert
            self.assertEqual(planned[0].committed, root / ".." / "Plugins" / "Only.dll")

    def test_Given_BuildPyIsAbsent_When_TheMapIsRead_Then_ItRefuses(self):
        with tempfile.TemporaryDirectory() as directory:
            # Act / Assert
            self.assertRaises(check.Refusal, check.build_script, Path(directory))

    def test_Given_BuildPyDeploysNothing_When_TheMapIsRead_Then_ItRefusesRatherThanCompareNothing(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            root = Path(directory)
            (root / "build.py").write_text('CONFIGURATION = "Release"\nDEPLOYMENTS = ()\n',
                                           encoding="utf-8")

            # Act / Assert
            self.assertRaises(check.Refusal, check.deployments, check.build_script(root), root)


class RecompileTests(unittest.TestCase):
    def test_Given_ABinLeftByOtherProperties_When_TheRealBuildCommandIsAsked_Then_ItForcesTheCompile(self):
        # Arrange — the real build.py, since a comment naming the flag must not satisfy this.
        real = load_build_script()

        # Act
        command = real.build_command("src/Only/Only.csproj")

        # Assert
        self.assertIn("--no-incremental", command)

    def test_Given_BuildPyWouldBuildAnotherConfiguration_When_Loaded_Then_ItRefuses(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange — CONFIGURATION comes from the environment, and the committed pair is Release.
            root = Path(directory)
            (root / "build.py").write_text('CONFIGURATION = "Debug"\nDEPLOYMENTS = ()\n',
                                           encoding="utf-8")

            # Act / Assert
            self.assertRaises(check.Refusal, check.build_script, root)


class BuildFailureTests(unittest.TestCase):
    def test_Given_TheBuildFails_When_TheCheckRuns_Then_ItRefusesRatherThanCompareAStaleOutput(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange — a build that returns non-zero leaves whatever bin/ already held in place.
            planned = deployment(Path(directory))

            # Act / Assert
            with mock.patch.object(check.subprocess, "run",
                                   return_value=subprocess.CompletedProcess([], 1)):
                self.assertRaises(check.Refusal, check.build,
                                  load_build_script(), Path(directory), planned)

    def test_Given_DotnetIsNotOnPath_When_TheCheckRuns_Then_ItRefuses(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            planned = deployment(Path(directory))

            # Act / Assert
            with mock.patch.object(check.subprocess, "run", side_effect=OSError("no dotnet")):
                self.assertRaises(check.Refusal, check.build,
                                  load_build_script(), Path(directory), planned)


class ExitCodeTests(unittest.TestCase):
    def test_Given_TheGeneratorsDirectoryIsAbsent_When_Run_Then_ItExitsRefusedRatherThanZero(self):
        with tempfile.TemporaryDirectory() as directory:
            # Arrange
            argv = ["deployed_dll_check.py", "--repo-root", directory]

            # Act / Assert
            with mock.patch.object(check.sys, "argv", argv), \
                    mock.patch.object(check.sys, "stderr", new=io.StringIO()):
                self.assertEqual(check.main(), check.REFUSED_EXIT)


if __name__ == "__main__":
    unittest.main(verbosity=2)

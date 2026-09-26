#!/usr/bin/env python3
"""Unit tests for scripts/unity/seed_library.py.

Whether two paths can share blocks is a property of the machine, and this suite runs on macOS and on
Linux runners, so `clones` and the command runner are stubbed wherever the verdict is not about the
machine itself. The stub runner copies bytes, which is what a clone looks like from outside.

Run: python3 scripts/unity/test_seed_library.py
"""

import io
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import seed_library  # noqa: E402

ENTRIES = ["ArtifactDB", "Artifacts", "PackageCache", "ScriptAssemblies", "SourceAssetDB"]
FILES = {"ArtifactDB", "SourceAssetDB"}


class SeedLibraryTests(unittest.TestCase):
    def setUp(self):
        self.holder = Path(tempfile.mkdtemp(prefix="seed-library-"))
        self.addCleanup(shutil.rmtree, self.holder, ignore_errors=True)
        self.source = self.holder / "other" / "Library"
        for name in ENTRIES:
            if name in FILES:
                self.source.mkdir(parents=True, exist_ok=True)
                (self.source / name).write_bytes(b"db")
            else:
                (self.source / name).mkdir(parents=True)
                (self.source / name / "held.bin").write_bytes(name.encode())
        self.destination = self.holder / "mine" / "Library"
        self.ran = []

    def runner(self, fail_at=None, stderr="clone refused", copies_first=False, after=None,
               interrupt=False):
        """A command runner that copies what `cp` would, failing at the entry named `fail_at`.

        `copies_first` places the failing entry before failing with `stderr`; `after` runs on each
        entry once it is placed; `interrupt` raises the interrupt a Ctrl-C delivers instead of
        returning.
        """
        def run(argv, **_):
            self.ran.append(argv)
            source, target = Path(argv[-2]), Path(argv[-1])
            failing = source.name == fail_at
            if failing and interrupt:
                raise KeyboardInterrupt
            if failing and not copies_first:
                return subprocess.CompletedProcess(argv, 1, "", stderr)
            if source.is_dir():
                shutil.copytree(str(source), str(target), symlinks=True)
            else:
                shutil.copy2(str(source), str(target))
            if after:
                after(target)
            return subprocess.CompletedProcess(argv, 1 if failing else 0, "",
                                               stderr if failing else "")
        return run

    def seed(self, platform="darwin", can_clone=True, **runner):
        """The script's exit code with `sys.platform` and `clones` as given, and its stderr.

        Its stdout lands in `self.told`.
        """
        err = io.StringIO()
        out = io.StringIO()
        with mock.patch.object(seed_library.sys, "platform", platform), \
                mock.patch.object(seed_library, "clones", lambda source, destination: can_clone):
            code = seed_library.seed(self.source, self.destination, run=self.runner(**runner),
                                     out=out, err=err)
        self.told = out.getvalue()
        return code, err.getvalue()

    def placed(self):
        """The names the destination holds, or None where there is no destination."""
        if not self.destination.is_dir():
            return None
        return sorted(path.name for path in self.destination.iterdir())

    def test_Given_AClone_When_ItIsDone_Then_EveryEntryButScriptAssembliesIsPlaced(self):
        # Act
        self.seed()

        # Assert
        self.assertEqual(sorted(path.name for path in self.destination.iterdir()),
                         [name for name in ENTRIES if name != "ScriptAssemblies"])

    def test_Given_macOS_When_ItClones_Then_EachEntryIsClonedWithTheCloneFlag(self):
        # Act
        self.seed(platform="darwin")

        # Assert
        self.assertEqual(sorted({tuple(argv[:3]) for argv in self.ran}), [("cp", "-c", "-a")])

    def test_Given_Linux_When_ItClones_Then_EachEntryAsksForAReflinkThatCannotFallBack(self):
        # Act
        self.seed(platform="linux")

        # Assert
        self.assertEqual(sorted({argv[2] for argv in self.ran}), ["--reflink=always"])

    def test_Given_macOSPathsThatCannotShareBlocks_When_Seeded_Then_NothingIsCopiedAndTheByteCopyIsNamed(self):
        # Act
        code, said = self.seed(platform="darwin", can_clone=False)

        # Assert
        self.assertEqual((code, self.destination.exists(), "rsync -a --exclude ScriptAssemblies" in said),
                         (1, False, True))

    def test_Given_ACloneThatFailsPartway_When_Seeded_Then_NoDestinationIsLeftBehind(self):
        # Arrange — `ArtifactDB` and `Artifacts` sort first, so `PackageCache` failing leaves a file
        # and a directory placed.
        # Act
        code, _ = self.seed(fail_at="PackageCache")

        # Assert
        self.assertEqual((code, self.destination.exists()), (1, False))

    def test_Given_AnEmptyDestinationAndAFailedClone_When_Seeded_Then_TheDirectoryStaysEmpty(self):
        # Arrange — a directory this did not make is emptied rather than removed.
        self.destination.mkdir(parents=True)

        # Act
        code, _ = self.seed(fail_at="PackageCache")

        # Assert
        self.assertEqual((code, self.destination.is_dir() and list(self.destination.iterdir())),
                         (1, []))

    def test_Given_ADestinationHoldingSomething_When_Seeded_Then_ItIsLeftAsItWas(self):
        # Arrange
        self.destination.mkdir(parents=True)
        (self.destination / "ArtifactDB").write_bytes(b"mine")

        # Act
        code, _ = self.seed()

        # Assert
        self.assertEqual((code, sorted(path.name for path in self.destination.iterdir())),
                         (1, ["ArtifactDB"]))

    def test_Given_ADirectoryInsideAProject_When_NoDestinationIsNamed_Then_ItIsTheProjectsLibrary(self):
        # Arrange
        root = self.holder / "project"
        (root / "ProjectSettings").mkdir(parents=True)
        (root / "ProjectSettings" / "ProjectVersion.txt").write_text("m_EditorVersion: 6000.3\n")
        deep = root / "Assets" / "Scripts"
        deep.mkdir(parents=True)

        # Act / Assert
        self.assertEqual(seed_library.project_library(deep), root / "Library")

    def test_Given_Linux_When_ClonesWouldSayNo_Then_TheReflinkIsStillAsked(self):
        # Arrange — off macOS `clones` answers no for every pair, and `--reflink=always` is what
        # decides there.
        # Act
        code, _ = self.seed(platform="linux", can_clone=False)

        # Assert
        self.assertEqual((code, self.placed()),
                         (0, [name for name in ENTRIES if name != "ScriptAssemblies"]))

    def test_Given_AFileThatLeftTheSourceWhileCloning_When_Seeded_Then_TheSeedStands(self):
        # Arrange — the line `cp` wrote when a source file was deleted under it, naming a path that
        # is gone.
        gone = self.source / "Artifacts" / "gone.bin"
        stderr = f"cp: {gone}: No such file or directory\n"

        # Act
        code, _ = self.seed(fail_at="Artifacts", stderr=stderr, copies_first=True)

        # Assert
        self.assertEqual((code, self.placed(), "in use" in self.told),
                         (0, [name for name in ENTRIES if name != "ScriptAssemblies"], True))

    def test_Given_AMissingFileReportThatIsStillThere_When_Seeded_Then_ItFails(self):
        # Arrange — the path the line names exists, so it is not a file that left the source.
        stderr = f"cp: {self.source / 'Artifacts' / 'held.bin'}: No such file or directory\n"

        # Act
        code, _ = self.seed(fail_at="Artifacts", stderr=stderr, copies_first=True)

        # Assert
        self.assertEqual((code, self.placed()), (1, None))

    def test_Given_AVanishedFileBesideAnotherError_When_Seeded_Then_ItFails(self):
        # Arrange
        gone = self.source / "Artifacts" / "gone.bin"
        stderr = (f"cp: {gone}: No such file or directory\n"
                  f"cp: {self.source / 'Artifacts' / 'held.bin'}: Permission denied\n")

        # Act
        code, _ = self.seed(fail_at="Artifacts", stderr=stderr, copies_first=True)

        # Assert
        self.assertEqual((code, self.placed()), (1, None))

    def test_Given_macOSPathsThatCanClone_When_ACloneFails_Then_NoByteCopyIsOffered(self):
        # Act
        code, said = self.seed(platform="darwin", fail_at="PackageCache")

        # Assert
        self.assertEqual((code, "rsync" in said), (1, False))

    def test_Given_Linux_When_AReflinkFails_Then_TheByteCopyIsOffered(self):
        # Act
        code, said = self.seed(platform="linux", fail_at="PackageCache")

        # Assert
        self.assertEqual((code, "rsync -a --exclude ScriptAssemblies" in said), (1, True))

    def test_Given_AReadOnlyDirectoryPlaced_When_ALaterCloneFails_Then_NoDestinationIsLeftBehind(self):
        # Arrange
        def seal(target):
            if target.name == "Artifacts":
                os.chmod(str(target), stat.S_IRUSR | stat.S_IXUSR)
        self.addCleanup(self.unseal)

        # Act
        code, _ = self.seed(fail_at="PackageCache", after=seal)

        # Assert
        self.assertEqual((code, self.placed()), (1, None))

    def unseal(self):
        sealed = self.destination / "Artifacts"
        if sealed.is_dir():
            os.chmod(str(sealed), stat.S_IRWXU)

    def test_Given_SomethingElseWritingTheDestination_When_ACloneFails_Then_ItIsLeftAlone(self):
        # Arrange — a file this run did not place appears while it runs.
        def intrude(target):
            if target.name == "Artifacts":
                (self.destination / "Foreign").write_bytes(b"not ours")

        # Act
        code, _ = self.seed(fail_at="PackageCache", after=intrude)

        # Assert
        self.assertEqual((code, self.placed()), (1, ["Foreign"]))

    def test_Given_AnInterrupt_When_Cloning_Then_NoDestinationIsLeftBehind(self):
        # Act — caught here as well, since one escaping would stop the whole run rather than fail this.
        try:
            code, _ = self.seed(fail_at="PackageCache", interrupt=True)
        except KeyboardInterrupt:
            code = "escaped"

        # Assert
        self.assertEqual((code, self.placed()), (130, None))

    def test_Given_TwoDevices_When_Asked_Then_ItDoesNotClone(self):
        # Arrange — the filesystem is stubbed as one that clones, so only the devices differ.
        devices = lambda path: 1 if str(path).startswith(str(self.source)) else 2  # noqa: E731

        # Act
        with mock.patch.object(seed_library, "device_of", devices), \
                mock.patch.object(seed_library, "filesystem_type", lambda path: "apfs"):
            verdict = seed_library.clones(self.source, self.destination)

        # Assert
        self.assertFalse(verdict)

    def test_Given_OneDeviceThatCannotClone_When_Asked_Then_ItDoesNotClone(self):
        # Act
        with mock.patch.object(seed_library, "device_of", lambda path: 1), \
                mock.patch.object(seed_library, "filesystem_type", lambda path: "hfs"):
            verdict = seed_library.clones(self.source, self.destination)

        # Assert
        self.assertFalse(verdict)

    def test_Given_OneAPFSDevice_When_Asked_Then_ItClones(self):
        # Act
        with mock.patch.object(seed_library, "device_of", lambda path: 1), \
                mock.patch.object(seed_library, "filesystem_type", lambda path: "apfs"):
            verdict = seed_library.clones(self.source, self.destination)

        # Assert
        self.assertTrue(verdict)

    @unittest.skipUnless(sys.platform == "darwin", "clonefile is macOS's")
    def test_Given_macOSTemporaryDirectories_When_SeededForReal_Then_EveryEntryButScriptAssembliesIsPlaced(self):
        # Arrange — nothing stubbed, so `clones` has to answer yes for macOS's own temporary
        # directory and `cp` has to take the flags as `clone_command` spells them.
        # Act
        code = seed_library.seed(self.source, self.destination, out=io.StringIO(), err=io.StringIO())

        # Assert
        placed = sorted(path.name for path in self.destination.iterdir()) \
            if self.destination.is_dir() else None
        self.assertEqual((code, placed), (0, [name for name in ENTRIES if name != "ScriptAssemblies"]))

    @unittest.skipUnless(sys.platform == "darwin", "clonefile is macOS's")
    def test_Given_ADestinationOnAnotherAPFSVolume_When_Asked_Then_ItDoesNotClone(self):
        # Arrange — the swap volume is APFS and is not the volume holding the temporary directory,
        # which the first two members check, so the filesystem type alone would say yes.
        volume = Path("/System/Volumes/VM")
        elsewhere = volume / "velvet-seed-library-absent" / "Library"

        # Act
        verdict = seed_library.clones(self.source, elsewhere)

        # Assert
        self.assertEqual((volume.stat().st_dev != self.source.stat().st_dev,
                          seed_library.filesystem_type(volume), verdict),
                         (True, "apfs", False))


if __name__ == "__main__":
    unittest.main(verbosity=2)

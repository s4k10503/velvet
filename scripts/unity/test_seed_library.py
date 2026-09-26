#!/usr/bin/env python3
"""Unit tests for scripts/unity/seed_library.py.

Whether two paths can share blocks is a property of the machine, and this suite runs on macOS and on
Linux runners, so `clones` and the command runner are stubbed wherever the verdict is not about the
machine itself. The stub runner copies bytes, which is what a clone looks like from outside.

Run: python3 scripts/unity/test_seed_library.py
"""

import io
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))

import seed_library  # noqa: E402

ENTRIES = ["Artifacts", "PackageCache", "ScriptAssemblies", "SourceAssetDB"]


class SeedLibraryTests(unittest.TestCase):
    def setUp(self):
        self.holder = Path(tempfile.mkdtemp(prefix="seed-library-"))
        self.addCleanup(shutil.rmtree, self.holder, ignore_errors=True)
        self.source = self.holder / "other" / "Library"
        for name in ENTRIES:
            if name == "SourceAssetDB":
                self.source.mkdir(parents=True, exist_ok=True)
                (self.source / name).write_bytes(b"db")
            else:
                (self.source / name).mkdir(parents=True)
                (self.source / name / "held.bin").write_bytes(name.encode())
        self.destination = self.holder / "mine" / "Library"
        self.ran = []

    def runner(self, fail_at=None):
        """A command runner that copies what `cp` would, failing at the entry named `fail_at`."""
        def run(argv, **_):
            self.ran.append(argv)
            source, target = Path(argv[-2]), Path(argv[-1])
            if source.name == fail_at:
                return subprocess.CompletedProcess(argv, 1, "", "clone refused")
            if source.is_dir():
                shutil.copytree(str(source), str(target), symlinks=True)
            else:
                shutil.copy2(str(source), str(target))
            return subprocess.CompletedProcess(argv, 0, "", "")
        return run

    def seed(self, platform="darwin", can_clone=True, fail_at=None):
        """The script's exit code with `sys.platform` and `clones` as given, and what it wrote."""
        err = io.StringIO()
        with mock.patch.object(seed_library.sys, "platform", platform), \
                mock.patch.object(seed_library, "clones", lambda source, destination: can_clone):
            code = seed_library.seed(self.source, self.destination, run=self.runner(fail_at),
                                     out=io.StringIO(), err=err)
        return code, err.getvalue()

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
        # Arrange — `Artifacts` sorts first, so `PackageCache` failing leaves one entry placed.
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

    @unittest.skipUnless(sys.platform == "darwin", "clonefile is macOS's")
    def test_Given_macOSTemporaryDirectories_When_SeededForReal_Then_EveryEntryButScriptAssembliesIsCloned(self):
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

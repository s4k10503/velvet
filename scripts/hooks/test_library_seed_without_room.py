#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/library_seed_without_room.py.

Filling the disk with seeded Libraries wedges every agent quietly: a Unity run that hits ENOSPC writes
no results XML, which reads exactly like a compile failure. Measured, ~90 worktrees at ~2.8 GB each
took the volume to 121 MiB free of 460 GiB.

Free space is stubbed rather than arranged, because the state under test is one nobody can put a test
machine into.

Run: python3 scripts/hooks/test_library_seed_without_room.py
"""

import contextlib
import importlib.util
import io
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/library_seed_without_room.py"

sys.path.insert(0, str(REPO_ROOT / ".claude/hooks/lib"))
_spec = importlib.util.spec_from_file_location("library_seed_without_room", GUARD)
guard = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(guard)

USAGE = type(shutil.disk_usage("/"))
COPIED = 4_000_000


class SeedRoomTests(unittest.TestCase):
    def setUp(self):
        self.holder = Path(tempfile.mkdtemp(prefix="seed-room-"))
        self.addCleanup(shutil.rmtree, self.holder, ignore_errors=True)
        self.source = self.holder / "Library"
        self.source.mkdir()
        (self.source / "big.bin").write_bytes(b"x" * COPIED)

    def judge(self, free, command=None):
        """The guard's verdict with `free` bytes on the volume, as (exit code, stderr)."""
        payload = {"tool_name": "Bash", "cwd": str(self.holder),
                   "tool_input": {"command": command or f"rsync -a {self.source}/ Library/"}}
        err = io.StringIO()
        with mock.patch.object(guard.shutil, "disk_usage",
                               lambda path: USAGE(total=1, used=1, free=free)), \
                mock.patch.object(sys, "stdin", io.StringIO(json.dumps(payload))), \
                contextlib.redirect_stderr(err):
            return guard.main(), err.getvalue()

    def test_Given_RoomForTheCopyAndOneMore_When_ASeedIsPosed_Then_ItGoesThrough(self):
        # Act / Assert
        self.assertEqual(self.judge(COPIED * 3), (0, ""))

    def test_Given_RoomForTheCopyAlone_When_ASeedIsPosed_Then_ItIsRefused(self):
        # Arrange — the copy fits and leaves nothing to work in, which is the state the wedge was in
        # one step earlier.
        code, said = self.judge(int(COPIED * 1.2))

        # Act / Assert
        self.assertEqual((code, "no room for it" in said), (2, True))

    def test_Given_ARefusal_When_ItIsRead_Then_ItNamesWhatToReclaim(self):
        # Arrange — a wedge that names no remedy is what the SessionStart report already was.
        _, said = self.judge(1)

        # Act / Assert
        self.assertIn("find /private/tmp/claude-*", said)

    def test_Given_ADestinationThatIsNotALibrary_When_Posed_Then_ItIsNotThisGuardsToRefuse(self):
        # Arrange — the control: a rule that read every copy would refuse most of a session's work.
        # Act / Assert
        self.assertEqual(
            self.judge(1, command=f"rsync -a {self.source}/ /tmp/velvet-not-a-library/"), (0, ""))

    def test_Given_AnUnexpandedSource_When_Posed_Then_ItIsRefusedRatherThanPassed(self):
        # Arrange — a literal names no directory this can size, and every reading of one fails, which
        # here would be the pass.
        code, said = self.judge(COPIED * 100, command='rsync -a "$OTHER/Library/" Library/')

        # Act / Assert
        self.assertEqual((code, "unexpanded" in said), (2, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

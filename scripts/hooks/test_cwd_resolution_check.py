#!/usr/bin/env python3
"""Every refusing guard is held to which tree it answers about when the command changes directory.

The sweep itself is `scripts/hooks/cwd_resolution_check.py`, which states what it reads and why one
reading is not enough. What is here is the sweep run over the guards, and five stand-in guards that
say whether the sweep can see anything at all.

The controls are the point. A sweep that returns a clean list because its shims stopped recording, or
because its two trees stopped being two, is indistinguishable from a sweep over guards that are all
correct — which is the property this whole family exists to remove, one level up. So a guard that
reads the directory it was handed, one that resolves the move, one that resolves it and then falls
back, one that refuses by printing rather than by exiting, and one that reads no tree at all are
posed alongside, and each is required to come back as the pair of outcomes its shape earns.

Run: python3 scripts/hooks/test_cwd_resolution_check.py
"""

import importlib.util
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
CHECK = REPO_ROOT / "scripts/hooks/cwd_resolution_check.py"
LIB = REPO_ROOT / ".claude/hooks/lib"


def load_module():
    """Imports the sweep by path, since scripts/hooks holds no packages."""
    spec = importlib.util.spec_from_file_location("cwd_resolution_check", CHECK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


cwd_resolution_check = load_module()

# Each stand-in is a whole guard: it declares what the sweep reads off a guard's source and then does
# one thing with the directory. The tree reading is `rev-parse` rather than anything a verdict turns
# on, because what is under test here is which directory the call was made about.
PREAMBLE = """\
import json
import subprocess
import sys

sys.path.insert(0, "VELVET_LIB")

HOOK_TOOLS = {"Bash"}
UNREADABLE_PROBE = {"command": "git status"}

event = json.load(sys.stdin)
command = event["tool_input"]["command"]
handed = event.get("cwd") or "."


def read(directory):
    subprocess.run(["git", "-C", str(directory), "rev-parse", "--show-toplevel"],
                   capture_output=True)
"""

BLIND = PREAMBLE + """
read(handed)
sys.exit(0)
"""

FOLLOWING = PREAMBLE + """
from shell_commands import UNRESOLVED_CD, command_directory

where = command_directory(command, handed)
if where is UNRESOLVED_CD:
    sys.exit(2)
read(where)
sys.exit(0)
"""

FALLING_BACK = PREAMBLE + """
from shell_commands import UNRESOLVED_CD, command_directory

where = command_directory(command, handed)
read(handed if where is UNRESOLVED_CD else where)
sys.exit(0)
"""

# Refuses the way `blind_git_add.py` does: a deny decision on stdout at exit 0. Read by the exit code
# alone this is a guard that let the tool through, which is this issue's own defect in the harness.
DENYING_ON_STDOUT = PREAMBLE + """
from shell_commands import UNRESOLVED_CD, command_directory

where = command_directory(command, handed)
if where is UNRESOLVED_CD:
    print(json.dumps({"hookSpecificOutput": {"hookEventName": "PreToolUse",
                                             "permissionDecision": "deny",
                                             "permissionDecisionReason": "no tree"}}))
    sys.exit(0)
read(where)
sys.exit(0)
"""

SILENT = PREAMBLE + """
sys.exit(0)
"""

CONTROLS = {
    "blind.py": BLIND,
    "following.py": FOLLOWING,
    "falling_back.py": FALLING_BACK,
    "denying_on_stdout.py": DENYING_ON_STDOUT,
    "silent.py": SILENT,
}


class ControlTests(unittest.TestCase):
    """What the sweep says about five guards whose answer is known before it runs."""

    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp(prefix="velvet-cwd-controls-"))
        for name, source in CONTROLS.items():
            (cls.root / name).write_text(source.replace("VELVET_LIB", str(LIB)), encoding="utf-8")
        found, _ = cwd_resolution_check.readings(cls.root, floor=0)
        cls.outcomes = {name: (placed, unplaced) for name, placed, unplaced, _ in found}

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.root, ignore_errors=True)

    def test_Given_AGuardReadingTheDirectoryItWasHanded_When_TheMoveCouldBePlaced_Then_TheSweepNamesIt(self):
        # Arrange / Act — the defect itself, posed to the instrument that has to see it. A sweep
        # scoring this one clean is scoring nothing, however clean the guards come back.
        # Assert
        self.assertEqual(self.outcomes["blind.py"],
                         (cwd_resolution_check.HANDED, cwd_resolution_check.HANDED))

    def test_Given_AGuardResolvingTheMove_When_TheMoveCouldBePlaced_Then_TheSweepSaysItFollows(self):
        # Arrange / Act — the other side. Without it, a sweep that called everything blind would
        # also produce a table nobody could argue with.
        # Assert
        self.assertEqual(self.outcomes["following.py"],
                         (cwd_resolution_check.FOLLOWS, cwd_resolution_check.REFUSES))

    def test_Given_AGuardFallingBackToTheHandedDirectory_When_TheMoveCannotBePlaced_Then_TheSweepNamesIt(self):
        # Arrange / Act — the half a single form misses: this one resolves every move it can place
        # and answers about the tree the command left for the one it cannot.
        # Assert
        self.assertEqual(self.outcomes["falling_back.py"],
                         (cwd_resolution_check.FOLLOWS, cwd_resolution_check.HANDED))

    def test_Given_AGuardRefusingOnStdout_When_TheMoveCannotBePlaced_Then_ItIsReadAsARefusal(self):
        # Arrange / Act — a deny decision printed at exit 0. Scored by the exit code alone it is a
        # guard that let the tool through, which is a false neutral of the same shape as the defect.
        # Assert
        self.assertEqual(self.outcomes["denying_on_stdout.py"],
                         (cwd_resolution_check.FOLLOWS, cwd_resolution_check.REFUSES))

    def test_Given_AGuardReadingNoTreeAtAll_When_BothFormsArePosed_Then_TheSweepDecidesNothing(self):
        # Arrange / Act — correct silence. Reported as undecided rather than as agreement, because
        # a guard whose subject is not the tree and one that read the wrong tree and had nothing to
        # say are the same silence.
        # Assert
        self.assertEqual(self.outcomes["silent.py"],
                         (cwd_resolution_check.UNDECIDED, cwd_resolution_check.UNDECIDED))


class TreeReadingTests(unittest.TestCase):
    """Which tree the sweep credits one recorded call to."""

    def test_Given_ACallCarryingADashC_When_ItsTreeIsRead_Then_TheDashCDecidesRatherThanTheDirectory(self):
        # Arrange — the shape a guard that resolves the move makes: it stays where the tool call
        # started and names the moved-to tree to git. Credited to the directory it ran in, every
        # such guard would read as blind.
        line = "/somewhere/handed\t-C /somewhere/moved rev-parse --show-toplevel"

        # Act
        found = cwd_resolution_check.addressed(line, Path("/somewhere/handed"),
                                               Path("/somewhere/moved"))

        # Assert
        self.assertEqual(found, cwd_resolution_check.MOVED_TREE)


class RefuseDirectoryTests(unittest.TestCase):
    """The guards themselves."""

    @classmethod
    def setUpClass(cls):
        cls.found, cls.faults = cwd_resolution_check.readings(
            REPO_ROOT / cwd_resolution_check.REFUSE_DIRECTORY)

    def test_Given_EveryRefusingGuard_When_ItIsPosedACdItCouldPlace_Then_NoneAnswersAboutTheHandedTree(self):
        # Arrange / Act — both forms at once, since a guard can fail either: a move it could have
        # placed and did not, and a move it could not place and answered about anyway.
        # Assert
        self.assertEqual(self.faults, [])

    def test_Given_TheSweepOverTheGuards_When_ItsDecisionsAreCounted_Then_MoreThanItsFloorWereDecided(self):
        # Arrange / Act — what the assertion above cannot say on its own. Shims that stopped
        # recording, or two trees that stopped differing, leave every guard undecided and every
        # fault list empty.
        found = len(cwd_resolution_check.decided(self.found))

        # Assert
        self.assertEqual(found >= cwd_resolution_check.DECIDED_FLOOR, True)


if __name__ == "__main__":
    unittest.main(verbosity=2)

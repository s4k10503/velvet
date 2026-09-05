#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/background_relative_path.py's path reading.

A guard routed around by one character is worse than no guard: the refusal that does not fire reads
identically to a command nobody needed to refuse. Measured before the glob was read — backgrounded,
`python3 script?/pr/settle.py watch` was allowed while `python3 scripts/pr/settle.py watch` was
refused, and the two name the same file.

Run: python3 scripts/hooks/test_background_relative_path.py
"""

import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/background_relative_path.py"


class PathSpellingTests(unittest.TestCase):
    def judge(self, command):
        """The guard's exit code for a backgrounded command in this repository."""
        event = {"tool_name": "Bash", "cwd": str(REPO_ROOT),
                 "tool_input": {"command": command, "run_in_background": True}}
        return subprocess.run([sys.executable, str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60).returncode

    def test_Given_AGlobSpelledRepoPath_When_Backgrounded_Then_ItIsRefused(self):
        # Arrange — the shell rewrites the selector into the path this exists to refuse.
        # Act / Assert
        self.assertEqual(self.judge("python3 script?/pr/settle.py watch"), 2)

    # GREEN_ON_BASE(characterization): the base answers this too, and it is the half the
    # widened reading could take with it — only running it says whether it did.
    def test_Given_ThePlainSpellingOfTheSamePath_When_Backgrounded_Then_ItIsStillRefused(self):
        # Arrange — the half that already worked, and what the glob reading must not lose.
        # Act / Assert
        self.assertEqual(self.judge("python3 scripts/pr/settle.py watch"), 2)

    # GREEN_ON_BASE(characterization): the base answers this too, and it is the half the
    # widened reading could take with it — only running it says whether it did.
    def test_Given_AnAbsolutePath_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — it says where it runs, which is the whole of what this asks.
        # Act / Assert
        self.assertEqual(self.judge("python3 /elsewhere/scripts/pr/settle.py watch"), 0)

    def test_Given_AMoveNamingNoDestination_When_Backgrounded_Then_EverySpellingIsStillRefused(self):
        # Arrange — each of these moves, so a reading that asks only whether the command moves has
        # it saying where it runs. None of them carries a destination the command's reader can
        # place, which is what the allowance is for. Both separators, because the spelling this was
        # matched by before read `cd` followed by any non-space as a move to somewhere.
        movers = ("popd", "pushd", "cd", "cd -", "popd || true", "cd &&", "cd - &&")

        # Act
        refused = sorted(mover for mover in movers if self.judge(
            f"{mover} python3 scripts/pr/settle.py watch" if mover.endswith("&&")
            else f"{mover}; python3 scripts/pr/settle.py watch") == 2)

        # Assert
        self.assertEqual(refused, sorted(movers))

    def test_Given_APopdOfANamedStackEntry_When_Backgrounded_Then_ItIsStillRefused(self):
        # Arrange — `+1` is an operand, so a reading that asks only whether the move carries one has
        # this saying where the command runs. Which directory the stack hands back is not in the
        # text, and this is the reading that decides it — `command_directory` has declined the mover
        # itself before its operand is reached.
        # Act / Assert
        self.assertEqual(self.judge("popd +1; python3 scripts/pr/settle.py watch"), 2)

    def test_Given_APushdOntoItsOwnStack_When_Backgrounded_Then_ItIsStillRefused(self):
        # Arrange — the same selector on the mover that does ordinarily name a destination, which is
        # why the operand alone cannot answer: `pushd /elsewhere` says where it runs and `pushd +1`
        # rotates a stack this cannot see.
        # Act / Assert
        self.assertEqual(self.judge("pushd +1; python3 scripts/pr/settle.py watch"), 2)

    def test_Given_AMoveBehindAWordNamingTheProgram_When_Backgrounded_Then_ItIsStillRefused(self):
        # Arrange — measured under bash and zsh alike, this runs the script where the tool call
        # started rather than in `/elsewhere`. Read as a move it says where it runs, and the guard
        # allows the very command it exists to refuse.
        # Act / Assert
        self.assertEqual(
            self.judge("nohup cd /elsewhere && python3 scripts/pr/settle.py watch"), 2)

    def test_Given_APushdAheadOfTheRelativePath_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — `pushd` says where the command runs as surely as `cd`. Matched as `cd` at the
        # head of the text, it says nothing, and the command is refused for naming a path it has
        # already placed.
        # Act / Assert
        self.assertEqual(self.judge("pushd /elsewhere && python3 scripts/pr/settle.py watch"), 0)

    def test_Given_AnAssignmentAheadOfTheMove_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — the shape a session types whenever it names a worktree once and moves into it.
        # Act / Assert
        self.assertEqual(
            self.judge("SP=/elsewhere; cd $SP && python3 scripts/pr/settle.py watch"), 0)

    # GREEN_ON_BASE(characterization): the base answers this too, and it is the half the
    # widened reading could take with it — only running it says whether it did.
    def test_Given_ACommandNamingNoRepoDirectory_When_Backgrounded_Then_ItIsLetThrough(self):
        # Arrange — the control: a reading that refused every glob would refuse this too.
        # Act / Assert
        self.assertEqual(self.judge("echo 'a?b' > /tmp/velvet-probe"), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

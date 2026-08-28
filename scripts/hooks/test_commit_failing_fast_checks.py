#!/usr/bin/env python3
"""Unit tests for the unexpanded-operand readings in .claude/hooks/refuse/commit_failing_fast_checks.py.

The two operand kinds are refused apart because the remedy for one does not reach the other. They were
folded into one list under a sentence written for a pathspec, and an agent's own `git -C "$SP" commit`
was refused with `$SP` printed under advice to name the paths or commit the index — two things that
cannot resolve a `-C`.

Run: python3 scripts/hooks/test_commit_failing_fast_checks.py
"""

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/commit_failing_fast_checks.py"


class UnexpandedOperands(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="fast-checks-")).resolve()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)

    def judge(self, command):
        """The guard's verdict, as (exit code, stderr)."""
        event = {"tool_name": "Bash", "cwd": str(self.root),
                 "tool_input": {"command": command}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60)
        return done.returncode, done.stderr

    def test_Given_AnUnexpandedDirectory_When_Refused_Then_TheRemedyIsToSpellItOut(self):
        # Arrange — naming the paths leaves the `-C` unresolved, and so does committing the index.
        code, said = self.judge('git -C "$SP" commit -m "x"')

        # Act / Assert
        self.assertEqual((code, "Spell the directory out" in said), (2, True))

    def test_Given_AnUnexpandedDirectory_When_Refused_Then_ThePathspecSentenceIsNotUsed(self):
        # Arrange — the sentence it used to get, which is false of a `-C`: the checks do not run over
        # nothing, they cannot find the tree to run over at all.
        code, said = self.judge('git -C "$SP" commit -m "x"')

        # Act / Assert — the refusal rides along, because a run that said nothing satisfies the
        # absence too.
        self.assertEqual((code, "Name the paths" in said), (2, False))

    # GREEN_ON_BASE(characterization): the base gives every unexpanded operand this sentence, which
    # is why the other two exist. It is the half the split had to leave where it was.
    def test_Given_AnUnexpandedPathspec_When_Refused_Then_ItKeepsItsOwnRemedy(self):
        # Arrange — the reading the sentence was written for, which this must leave where it is.
        code, said = self.judge('git commit -m "x" -- "$FILES"')

        # Act / Assert
        self.assertEqual((code, "Name the paths" in said), (2, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/commit_failing_fast_checks.py.

Four hold the unexpanded-operand readings. The two operand kinds are refused apart because the remedy
for one does not reach the other. They were folded into one list under a sentence written for a
pathspec, and an agent's own `git -C "$SP" commit` was refused with `$SP` printed under advice to name
the paths or commit the index — two things that cannot resolve a `-C`.

One holds which content the checks run over, the index's rather than the working tree's, by giving the
two different text and reading which of them the refusal quotes back.

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

# A file whose two versions are both distinctive, so that a refusal quoting one of them says which
# half the guard read. The staged half is what a fast check has to reject.
PROBE = "probe.py"
STAGED_AND_BROKEN = "indexed_and_broken = (\n"
TREE_AND_WHOLE = "tree_is_fine = 1\n"


class GuardTestCase(unittest.TestCase):
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


class UnexpandedOperands(GuardTestCase):
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
    def test_Given_ATildeSpelledPathspec_When_Judged_Then_TheContentIsNotSilentlyEmpty(self):
        # Arrange — the shell expands a `~` and git does not, so a pathspec spelled that way reached
        # git literally, matched nothing, and left every check below reading no content. Measured, it
        # passed all of them.
        code, said = self.judge("git commit -m x -- ~/velvet/a.cs")

        # Act / Assert — expanded it is an absolute path outside the repository, which git refuses,
        # so this reaches the unreadable-content refusal rather than the silence.
        self.assertEqual((code, "could not be read" in said), (2, True))

    def test_Given_AnUnexpandedPathspec_When_Refused_Then_ItKeepsItsOwnRemedy(self):
        # Arrange — the reading the sentence was written for, which this must leave where it is.
        code, said = self.judge('git commit -m "x" -- "$FILES"')

        # Act / Assert
        self.assertEqual((code, "Name the paths" in said), (2, True))


class IndexedContent(GuardTestCase):
    # GREEN_ON_BASE(refactor): the index read this guard's collapse onto a shared reader preserves.
    # `repository.git_bytes` takes its arguments the other way round from the reader removed here,
    # so passing `cwd` where the paths go is what reddens this — measured, on a TypeError no handler
    # catches, which exits 1 and lets the commit through rather than refusing it.
    def test_Given_ABlobBrokenInTheIndexAndWholeInTheTree_When_Judged_Then_TheRefusalQuotesTheIndex(self):
        # Arrange — the two halves carry different text, so the quoted line says which was read. A
        # refusal alone would not: the working tree's copy is what a commit of the index must not be
        # judged on, and both halves failing would be a case that passes either way — so what the
        # tree held while the guard ran is compared beside the refusal.
        (self.root / PROBE).write_text(STAGED_AND_BROKEN, encoding="utf-8")
        subprocess.run(["git", "-C", str(self.root), "add", PROBE], check=True,
                       capture_output=True, timeout=60)
        (self.root / PROBE).write_text(TREE_AND_WHOLE, encoding="utf-8")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, STAGED_AND_BROKEN.strip() in said,
                          (self.root / PROBE).read_text(encoding="utf-8")),
                         (2, True, TREE_AND_WHOLE))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/commit_failing_fast_checks.py.

Four hold the unexpanded-operand readings. The two operand kinds are refused apart because the remedy
for one does not reach the other. They were folded into one list under a sentence written for a
pathspec, and an agent's own `git -C "$SP" commit` was refused with `$SP` printed under advice to name
the paths or commit the index — two things that cannot resolve a `-C`.

Two hold which content the checks run over, the index's rather than the working tree's. The first
gives the two halves different text and reads which of them the refusal quotes back. The second
stages a name git spells back quoted: the guard handed `git show` that spelling with the quotes
still on it, and refused the commit for a reading git had answered.

Four hold the working-tree half. The one case that reached it before poses a pathspec git refuses,
so the listing came back unanswered and no file was ever opened. Three of the four are broken in the
tree alone and differ only in the name — one needing no quoting, one git quotes, one holding a
character universal newlines rewrite — so what separates them is which spelling of a listing reaches
`open`. The fourth is a path the listing names that `open` cannot reach at all, which the guard used
to drop and go on to refuse or allow on the rest.

Run: python3 scripts/hooks/test_commit_failing_fast_checks.py
"""

import json
import os
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

# One name needing no quoting, one git quotes under the default `core.quotePath`, and one holding a
# character universal newlines rewrite. A name whose bytes are not valid UTF-8 is not used here: it
# was the first arrangement tried for the quoting case, and this filesystem refused to create such
# a file, with `Illegal byte sequence`.
PLAIN = "plain.py"
QUOTED = "café.py"
RETURNING = "with-return\r.py"

# A symlink repointed at a name nothing answers to, which is a path the listing names and `open`
# cannot reach.
LINK = "link.py"
TARGET = "target.py"
GONE = "gone.py"

WHOLE = "whole = 1\n"
BROKEN = "broken = (\n"

# What a refusal says of a file a fast check rejected, against the two sentences it carries for a
# path that did not read at all. Both are exit 2, so the code alone separates neither from the
# other, and the remedy is the half a git that would not answer must not be given.
FAILED_A_CHECK = " failed a fast check"
NOT_OPENED = "a file this commit records could not be opened."
MAKE_IT_READABLE = "Make it readable, or commit without it."


class GuardTestCase(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="fast-checks-")).resolve()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)

    def judge(self, command):
        """The guard's verdict, as (exit code, stderr).

        Through a text pipe the return in `RETURNING` arrives as a line break, and the case posing
        that name compares against a refusal this reading had already repaired.
        """
        event = {"tool_name": "Bash", "cwd": str(self.root),
                 "tool_input": {"command": command}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event).encode("utf-8"),
                              capture_output=True, timeout=60)
        return done.returncode, done.stderr.decode("utf-8", "surrogateescape")

    def git(self, *args):
        done = subprocess.run(["git", "-C", str(self.root), "-c", "user.email=t@velvet",
                               "-c", "user.name=t", *args],
                              check=True, capture_output=True, timeout=60)
        return done.stdout.decode("utf-8", "surrogateescape")


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

    def test_Given_ANameGitQuotes_When_BrokenInTheIndex_Then_TheRefusalNamesIt(self):
        # Arrange — staged and never committed, so everything this commit records is the index's.
        (self.root / QUOTED).write_text(BROKEN, encoding="utf-8")
        self.git("add", QUOTED)

        # Act
        code, said = self.judge("git commit -m x")

        # Assert — the name, not merely the refusal: the base refuses this too, for having handed
        # `git show` a spelling no path answers to, and tells the reader to retry when git answers.
        self.assertEqual((code, QUOTED + FAILED_A_CHECK in said), (2, True))


class WorktreeContent(GuardTestCase):
    def broken_only_in_the_worktree(self, name):
        """The staged listing, after `name` is committed whole and broken in the working copy.

        Nothing is staged afterwards and the committed copy is whole, so a refusal can only have
        come from the working-tree read. Each case compares that listing beside its own verdict,
        which is what stops a future arrangement staging the broken text and leaving them all green.
        """
        (self.root / name).write_text(WHOLE, encoding="utf-8")
        self.git("add", name)
        self.git("commit", "-q", "-m", "arranged")
        (self.root / name).write_text(BROKEN, encoding="utf-8")
        return self.git("diff", "--cached", "--name-only")

    # GREEN_ON_BASE(characterization): a name needing no quoting is refused on the base too.
    # That is what makes the two awkward names below about which spellings reach the checks, rather
    # than about whether the working tree is read at all. Answering `[]` from `worktree_paths`
    # reddens it.
    def test_Given_APlainName_When_BrokenInTheWorktree_Then_TheRefusalNamesIt(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(PLAIN)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_ANameGitQuotes_When_BrokenInTheWorktree_Then_TheRefusalNamesIt(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(QUOTED)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — on the base the listing spells this name back quoted, `open` raises for it, and
        # the commit is allowed with nothing said.
        self.assertEqual((code, QUOTED + FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_ANameHoldingACarriageReturn_When_BrokenInTheWorktree_Then_TheRefusalNamesIt(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(RETURNING)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — the return has to survive the read: taking the same NUL-delimited listing
        # through a text pipe leaves this naming a different file, which `open` does not find.
        self.assertEqual((code, RETURNING + FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_APathTheListingNamesThatWillNotOpen_When_Judged_Then_TheRefusalNamesIt(self):
        # Arrange — the symlink is what changed, so the listing names it; following it finds
        # nothing. Its blob is the target name, which py_compile accepts, so a refusal here is
        # about the reading rather than about the content.
        (self.root / TARGET).write_text(WHOLE, encoding="utf-8")
        os.symlink(TARGET, self.root / LINK)
        self.git("add", TARGET, LINK)
        self.git("commit", "-q", "-m", "arranged")
        os.unlink(self.root / LINK)
        os.symlink(GONE, self.root / LINK)
        staged = self.git("diff", "--cached", "--name-only")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — the remedy alongside the subject, because a reader sent to retry when git
        # answers has been told a thing about git that this refusal did not establish.
        self.assertEqual((code, NOT_OPENED in said, LINK in said, MAKE_IT_READABLE in said, staged),
                         (2, True, True, True, ""))


if __name__ == "__main__":
    unittest.main(verbosity=2)

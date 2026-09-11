#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/commit_failing_fast_checks.py.

Run: python3 scripts/hooks/test_commit_failing_fast_checks.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/commit_failing_fast_checks.py"
CAMPAIGN = REPO_ROOT / "scripts/test_quality/mutation_check.py"

sys.path.insert(0, str(CAMPAIGN.parent))
from mutation_check import SENTINEL, Holder  # noqa: E402

# A file whose two versions are both distinctive, so that a refusal quoting one of them says which
# half the guard read. The staged half is what a fast check has to reject.
PROBE = "probe.py"
STAGED_AND_BROKEN = "indexed_and_broken = (\n"
TREE_AND_WHOLE = "tree_is_fine = 1\n"

# One name needing no quoting, one git quotes under the default `core.quotePath`, one holding a
# character universal newlines rewrite, one holding a line break that survives that rewriting, and
# one `str.splitlines()` reads as a single line.
PLAIN = "plain.py"
QUOTED = "café.py"
RETURNING = "with-return\r.py"
BREAKING = "with\nnewline.py"
OVERWRITING = "a\x1b[999Dnothing is wrong.py"

# A name whose bytes are not valid UTF-8, written into the index rather than onto disk so that the
# arrangement needs no filesystem able to carry it — measured, the one this was written on refused
# such a name with `Illegal byte sequence`. `RAW_BYTE_SHOWN` is how a refusal spells it, which is
# where the decoding that kept the byte is visible.
RAW_BYTE_NAME = b"bad\xffname.py"
RAW_BYTE_SHOWN = '"bad\\udcffname.py"'

# A symlink and the two names it is repointed at: one nothing answers to, one a file this repository
# holds. Only the first is a name Python will not parse, so the first has to be refused and the
# second allowed.
LINK = "link.py"
FIRST_TARGET = "first.py"
MISSING_TARGET = "1bad.py"
BROKEN_TARGET = "also.py"

# A symlink whose recorded target holds a byte UTF-8 does not map. The suffix is what sends
# those bytes to a check at all, and that check is what rejects them.
RAW_LINK = "raw.json"
RAW_TARGET = b'{"a": "\xff"}'

# A tracked file at a mode that lets nobody read it.
CLOSED = "closed.py"

STAGED = "staged.py"
EXCLUDED = "excluded.json"
TOP = "top.py"

# Where the guard reads the cuts a neuter sweep may be holding, and what validates them.
CUT_MAP = "scripts/test_quality/neuter_cuts.json"
CUT_VALIDATOR = "scripts/test_quality/neuter_check.py"

# Two submodule paths. Their index entries are written directly rather than cloned: what the guard
# reads is the mode and the listing, and `git submodule add` adds a working tree and a `.gitmodules`
# that neither reading looks at. The second ends in a suffix a fast check knows, which is what
# carries the answer for a path recording no blob as far as a check.
SUB = "sub"
SUB_CHECKED = "mod.py"
GITLINK_MODE = "160000"

WHOLE = "whole = 1\n"
BROKEN = "broken = (\n"

# What a refusal says of a file a fast check rejected, against the sentences it carries for a path
# that did not read at all. All are exit 2, so the code alone separates none of them, and the
# remedy is the half that must not be shared: one told to correct what git named where git never
# ran has been sent to look for a name nothing wrote.
FAILED_A_CHECK = " failed a fast check"
RETRY_WHEN_GIT_ANSWERS = "Retry when git answers."
CORRECT_WHAT_GIT_NAMED = "Correct what git named and retry."
NOT_COPIED = "the index could not be copied."
NOT_RUN = " could not be run."
NOT_VALIDATED = "the cut map could not be validated."
HOLDING = "a mutation campaign is holding one of these files."
NEUTER_CARRIED = "carries a neuter from a sweep."
PATHS_FROM_A_FILE = "its paths come from `--pathspec-from-file`"
NAMED_INDEX = "the index `GIT_INDEX_FILE` names"

# What git says of a directory with no repository at or above it.
NOT_A_REPOSITORY = "not a git repository"


class GuardTestCase(unittest.TestCase):
    def setUp(self):
        self.root = self.repository(Path(tempfile.mkdtemp(prefix="fast-checks-")).resolve())

    def repository(self, root):
        """`root` as a git repository this case may leave in any state."""
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        root.mkdir(parents=True, exist_ok=True)
        subprocess.run(["git", "-C", str(root), "init", "--quiet"], check=True,
                       capture_output=True, timeout=60)
        return root

    def judge(self, command, cwd=None, path=None, env=None):
        """The guard's verdict, as (exit code, stderr)."""
        event = {"tool_name": "Bash", "cwd": str(cwd or self.root),
                 "tool_input": {"command": command}}
        environment = dict(os.environ, **(env or {}))
        if path is not None:
            environment["PATH"] = path
        done = subprocess.run([sys.executable, str(GUARD)], input=json.dumps(event).encode("utf-8"),
                              capture_output=True, timeout=60, env=environment)
        return done.returncode, done.stderr.decode("utf-8", "surrogateescape")

    def scratch(self, prefix):
        """A directory outside the repository, removed with the case."""
        made = Path(tempfile.mkdtemp(prefix=prefix)).resolve()
        self.addCleanup(shutil.rmtree, made, ignore_errors=True)
        return made

    def only_git(self):
        """A PATH holding git and nothing else."""
        path = self.scratch("fast-checks-path-")
        os.symlink(shutil.which("git"), path / "git")
        return str(path)

    def logged_git(self):
        """A directory whose `git` writes each command it runs to `started`, and what `cat-file`
        and `update-index` are handed to `asked` and `indexed`, before running git."""
        log = self.scratch("fast-checks-git-")
        (log / "git").write_text(
            "#!/bin/sh\n"
            f'printf "%s\\n" "$*" >> "{log}/started"\n'
            'case " $* " in\n'
            f'  *" cat-file "*) tee -a "{log}/asked" | "{shutil.which("git")}" "$@" ;;\n'
            f'  *" update-index "*) tee -a "{log}/indexed" | "{shutil.which("git")}" "$@" ;;\n'
            f'  *) exec "{shutil.which("git")}" "$@" ;;\n'
            "esac\n", encoding="utf-8")
        os.chmod(log / "git", 0o755)
        return log

    def git(self, *args, root=None):
        done = subprocess.run(["git", "-C", str(root or self.root), "-c", "user.email=t@velvet",
                               "-c", "user.name=t", *args],
                              check=True, capture_output=True, timeout=60)
        return done.stdout.decode("utf-8", "surrogateescape")

    def index(self, record):
        """`record` written into the index, as the raw bytes of one `--index-info` line."""
        subprocess.run(["git", "-C", str(self.root), "update-index", "-z", "--index-info"],
                       input=record, check=True, capture_output=True, timeout=60)

    def blob(self, data):
        """`data` written into the object store, as the hash git gave it."""
        done = subprocess.run(["git", "-C", str(self.root), "hash-object", "-w", "--stdin"],
                              input=data, check=True, capture_output=True, timeout=60)
        return done.stdout.strip()

    def submodule(self, extra=None, path=SUB):
        """A nested repository at `path`, as the commit its HEAD names once `extra` is committed."""
        inner = self.root / path
        inner.mkdir(exist_ok=True)
        subprocess.run(["git", "-C", str(inner), "init", "--quiet"], check=True,
                       capture_output=True, timeout=60)
        if extra is None:
            (inner / "a.py").write_text(WHOLE, encoding="utf-8")
            self.git("add", "a.py", root=inner)
            self.git("commit", "-q", "-m", "one", root=inner)
        else:
            (inner / extra).write_text(WHOLE, encoding="utf-8")
            self.git("add", extra, root=inner)
            self.git("commit", "-q", "-m", "next", root=inner)
        return self.git("rev-parse", "HEAD", root=inner).strip()

    def opens(self, name):
        """Whether this process can read `name` at all."""
        try:
            with open(self.root / name, "rb"):
                return True
        except OSError:
            return False

    def committed(self, *names):
        """Each of `names` committed whole."""
        for name in names:
            (self.root / name).parent.mkdir(parents=True, exist_ok=True)
            (self.root / name).write_text(WHOLE, encoding="utf-8")
        self.git("add", *names)
        self.git("commit", "-q", "-m", "arranged")


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

    def test_Given_ATildeSpelledPathspec_When_Judged_Then_TheRefusalNamesTheOperandGitWasGiven(self):
        # Arrange — the shell expands a `~` and git does not, so a pathspec spelled that way reached
        # git literally, matched nothing, and left every check below reading no content. Measured, it
        # passed all of them. Expanded it is an absolute path outside the repository, which git
        # refuses, and the reader is then told to correct what git named.
        code, said = self.judge("git commit -m x -- ~/velvet/a.cs")

        # Act / Assert — the operand as git received it, since a reader who cannot see which one
        # git refused has nothing to correct.
        self.assertEqual((code, os.path.expanduser("~/velvet/a.cs") in said), (2, True))

    def test_Given_ATildeSpelledPathspec_When_Judged_Then_ItIsNotSentBackToGit(self):
        # Arrange — the same state read for the remedy rather than for the refusal. git answered
        # here, with a fatal naming the operand, so a reader told to wait for git waits on
        # something that has already happened.
        code, said = self.judge("git commit -m x -- ~/velvet/a.cs")

        # Act / Assert — both halves, since a refusal carrying neither satisfies the absence too.
        self.assertEqual((code, RETRY_WHEN_GIT_ANSWERS in said, CORRECT_WHAT_GIT_NAMED in said),
                         (2, False, True))

    def test_Given_ATildeSpelledDirectory_When_Judged_Then_TheRefusalNamesTheDirectoryGitWasGiven(self):
        # Arrange / Act — a directory that is not there, so git's refusal quotes the one it was
        # handed.
        code, said = self.judge("git -C ~/velvet-no-such-tree commit -m x")

        # Assert
        missing = os.path.expanduser("~/velvet-no-such-tree")
        self.assertEqual((code, missing in said, os.path.exists(missing)), (2, True, False))

    def test_Given_AnUnexpandedPathspec_When_Refused_Then_ItKeepsItsOwnRemedy(self):
        # Arrange — the reading the sentence was written for, which this must leave where it is.
        code, said = self.judge('git commit -m "x" -- "$FILES"')

        # Act / Assert
        self.assertEqual((code, "Name the paths" in said), (2, True))


class IndexedContent(GuardTestCase):
    # GREEN_ON_BASE(characterization): the base judges a commit of the index on the index too.
    def test_Given_ABlobBrokenInTheIndexAndWholeInTheTree_When_Judged_Then_TheRefusalQuotesTheIndex(self):
        # Arrange — the two halves carry different text, so the quoted line says which was read. A
        # refusal alone would not: the working tree's copy is what a commit of the index must not be
        # judged on, and both halves failing would be a case that passes either way — so what the
        # tree held while the guard ran is compared beside the refusal.
        (self.root / PROBE).write_text(STAGED_AND_BROKEN, encoding="utf-8")
        self.git("add", PROBE)
        (self.root / PROBE).write_text(TREE_AND_WHOLE, encoding="utf-8")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, STAGED_AND_BROKEN.strip() in said,
                          (self.root / PROBE).read_text(encoding="utf-8")),
                         (2, True, TREE_AND_WHOLE))

    # GREEN_ON_BASE(characterization): the base checks a staged blob's own bytes too.
    def test_Given_AStagedScriptBrokenByBareReturns_When_Judged_Then_ItsRecordedBytesAreChecked(self):
        # Arrange
        script, content = "returns.sh", b"#!/bin/bash\nif true; then\recho hi\rfi\n"
        (self.root / script).write_bytes(content)
        self.git("add", script)

        # Act
        code, said = self.judge("git commit -m x")

        # Assert — beside the verdict on the same script with its returns read as line feeds.
        (self.root / script).write_bytes(content.replace(b"\r", b"\n"))
        self.git("add", script)
        self.assertEqual((code, script + FAILED_A_CHECK in said, self.judge("git commit -m x")),
                         (2, True, (0, "")))

    def test_Given_AStagedScriptWhoseBrokenLineHoldsAByteOutsideTheEncoding_When_Judged_Then_TheRefusalNamesIt(self):
        # Arrange — the line `bash -n` rejects holds a byte UTF-8 does not map.
        (self.root / "raw.sh").write_bytes(b"#!/bin/bash\nif then \xff\n")
        self.git("add", "raw.sh")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, "raw.sh" + FAILED_A_CHECK in said), (2, True))

    def test_Given_ANameGitQuotes_When_BrokenInTheIndex_Then_TheRefusalNamesIt(self):
        # Arrange — staged and never committed, so everything this commit records is the index's.
        (self.root / QUOTED).write_text(BROKEN, encoding="utf-8")
        self.git("add", QUOTED)

        # Act
        code, said = self.judge("git commit -m x")

        # Assert — the name, not merely the refusal.
        self.assertEqual((code, QUOTED + FAILED_A_CHECK in said), (2, True))

    def test_Given_ANameHoldingAByteOutsideTheEncoding_When_BrokenInTheIndex_Then_TheRefusalNamesIt(self):
        # Arrange — the index alone, since the byte cannot be written into a name here.
        self.index(b"100644 " + self.blob(BROKEN.encode("utf-8")) + b"\t" + RAW_BYTE_NAME + b"\x00")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, RAW_BYTE_SHOWN + FAILED_A_CHECK in said), (2, True))

    def test_Given_ASubmoduleWhoseCommitIsStaged_When_Judged_Then_TheCommitIsAllowed(self):
        # Arrange — the staged listing names it and there is no blob behind it. That the index
        # really holds a gitlink is compared beside the verdict, since an entry at any other mode
        # poses nothing.
        self.index(("160000 " + self.submodule() + "\t" + SUB + "\x00").encode("utf-8"))
        listed = self.git("ls-files", "-s", "--", SUB).split()[0]

        # Act
        code, said = self.judge("git commit -m x")

        # Assert — what the commit then wrote sits in the comparison, because the guard is allowed
        # to skip this path only for as long as git records a commit there and no blob.
        self.git("commit", "-q", "-m", "recorded")
        self.assertEqual((code, said, listed, self.git("ls-tree", "HEAD", "--", SUB).split()[:2]),
                         (0, "", GITLINK_MODE, [GITLINK_MODE, "commit"]))

    # GREEN_ON_BASE(characterization): the base's staged listing leaves an intent-to-add entry out.
    def test_Given_AnIntentToAddEntry_When_TheIndexIsCommitted_Then_ItIsNotChecked(self):
        # Arrange — broken JSON behind `add -N`, and a staged file so that the commit records one.
        (self.root / "new.json").write_text('{"a": [1\n', encoding="utf-8")
        self.git("add", "-N", "new.json")
        (self.root / PLAIN).write_text(WHOLE, encoding="utf-8")
        self.git("add", PLAIN)
        listed = self.git("ls-files", "--", "new.json")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert — what the commit then wrote sits in the comparison, because leaving the entry
        # unchecked is right only for as long as git records nothing there.
        self.git("commit", "-q", "-m", "recorded")
        self.assertEqual((code, said, listed, self.git("ls-tree", "--name-only", "HEAD")),
                         (0, "", "new.json\n", PLAIN + "\n"))

    def test_Given_NoPythonToCompileWith_When_AScriptIsCommitted_Then_TheRefusalSaysTheCheckDidNotRun(self):
        # Arrange
        (self.root / PLAIN).write_text(WHOLE, encoding="utf-8")
        self.git("add", PLAIN)

        # Act
        code, said = self.judge("git commit -m x", path=self.only_git())

        # Assert — beside the sentence a check that ran and failed writes, which is not this one's.
        self.assertEqual((code, PLAIN + NOT_RUN in said, FAILED_A_CHECK in said), (2, True, False))


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
    # That is what makes the awkward names below about which spellings reach the checks, rather
    # than about whether the working tree is read at all.
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

        # Assert
        self.assertEqual((code, QUOTED + FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_ANameHoldingACarriageReturn_When_BrokenInTheWorktree_Then_ItsContentIsChecked(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(RETURNING)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — that a check ran and rejected the content, rather than which name it named.
        self.assertEqual((code, FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_ANameHoldingALineBreak_When_Refused_Then_TheRefusalLineIsWhole(self):
        # Arrange — a break a text pipe leaves alone, so what this reads is the spelling of the
        # refusal rather than the spelling of the listing.
        staged = self.broken_only_in_the_worktree(BREAKING)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — the whole first line, since a name written into it raw divides that line and
        # leaves a substring of it present in the wreckage either way. Taken as the leading slice
        # rather than the leading element: a state that says nothing at all has no first line, and
        # reaching for one there raises where this has to disagree.
        self.assertEqual(
            (code, said.splitlines()[:1], staged),
            (2, ["Refusing `git commit`: " + json.dumps(BREAKING) + FAILED_A_CHECK + "."], ""))

    def test_Given_ANameHoldingAnEscapeSequence_When_Refused_Then_TheRefusalSpellsItOut(self):
        # Arrange — a name `str.splitlines()` reads as one line, so what separates this from the
        # case above is which characters the quoting predicate is asked about.
        staged = self.broken_only_in_the_worktree(OVERWRITING)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — the whole first line, for the reason the line-break case gives.
        self.assertEqual(
            (code, said.splitlines()[:1], staged),
            (2, ["Refusing `git commit`: " + json.dumps(OVERWRITING) + FAILED_A_CHECK + "."], ""))

    def test_Given_ASymlinkRepointedAtAMissingName_When_Judged_Then_TheRecordedTargetIsChecked(self):
        # Arrange — the symlink is what changed, so the listing names it; following it finds
        # nothing. The name it now carries is one Python will not parse, so a refusal here is a
        # check having read the recorded target rather than having failed to open anything.
        (self.root / FIRST_TARGET).write_text(WHOLE, encoding="utf-8")
        os.symlink(FIRST_TARGET, self.root / LINK)
        self.git("add", FIRST_TARGET, LINK)
        self.git("commit", "-q", "-m", "arranged")
        os.unlink(self.root / LINK)
        os.symlink(MISSING_TARGET, self.root / LINK)
        staged = self.git("diff", "--cached", "--name-only")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because reading the target's
        # name instead of the file it points at is right only for as long as that name is the blob.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, LINK + FAILED_A_CHECK in said, staged,
                          self.git("show", "HEAD:" + LINK)),
                         (2, True, "", MISSING_TARGET))

    def test_Given_ASymlinkRepointedAtABrokenFile_When_Judged_Then_TheTargetsContentIsNotRead(self):
        # Arrange — the target resolves and holds text no check accepts, while the name it is
        # reached by parses. The base follows the link and refuses over content this commit does
        # not record. That the target really is broken is compared beside the verdict, since a
        # whole one would leave this passing whichever content was read.
        (self.root / FIRST_TARGET).write_text(WHOLE, encoding="utf-8")
        (self.root / BROKEN_TARGET).write_text(BROKEN, encoding="utf-8")
        os.symlink(FIRST_TARGET, self.root / LINK)
        self.git("add", FIRST_TARGET, BROKEN_TARGET, LINK)
        self.git("commit", "-q", "-m", "arranged")
        os.unlink(self.root / LINK)
        os.symlink(BROKEN_TARGET, self.root / LINK)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert
        self.assertEqual((code, said, (self.root / BROKEN_TARGET).read_text(encoding="utf-8")),
                         (0, "", BROKEN))

    def test_Given_ASymlinkWhoseTargetHoldsAByteOutsideTheEncoding_When_Judged_Then_ItsRecordedBytesAreChecked(self):
        # Arrange — the target is the blob, and as bytes it is not content the check for this
        # suffix accepts.
        (self.root / FIRST_TARGET).write_text(WHOLE, encoding="utf-8")
        os.symlink(FIRST_TARGET, self.root / RAW_LINK)
        self.git("add", FIRST_TARGET, RAW_LINK)
        self.git("commit", "-q", "-m", "arranged")
        os.unlink(self.root / RAW_LINK)
        os.symlink(os.fsdecode(RAW_TARGET), self.root / RAW_LINK)
        staged = self.git("diff", "--cached", "--name-only")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because checking the target's
        # own bytes is right only for as long as those bytes are the blob.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, RAW_LINK + FAILED_A_CHECK in said, staged,
                          self.git("show", "HEAD:" + RAW_LINK)),
                         (2, True, "", os.fsdecode(RAW_TARGET)))

    # GREEN_ON_BASE(characterization): the base allows this too.
    def test_Given_ASubmoduleWhoseCommitMoved_When_Judged_Then_TheCommitIsAllowed(self):
        # Arrange — committed at the first commit and moved on afterwards, so the worktree listing
        # names it and the staged one does not. Both listings are compared beside the verdict.
        self.index(("160000 " + self.submodule() + "\t" + SUB + "\x00").encode("utf-8"))
        self.git("commit", "-q", "-m", "arranged")
        self.submodule(extra="b.py")
        listings = (self.git("diff", "--cached", "--name-only"), self.git("diff", "--name-only"))

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because the guard is allowed
        # to skip this path only for as long as git records a commit there and no blob.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, said, listings, self.git("ls-tree", "HEAD", "--", SUB).split()[:2]),
                         (0, "", ("", SUB + "\n"), [GITLINK_MODE, "commit"]))

    # GREEN_ON_BASE(characterization): the base allows this too.
    def test_Given_ASubmoduleAtAPathAFastCheckKnows_When_Judged_Then_TheCommitIsAllowed(self):
        # Arrange — the case above with the suffix changed, which is the whole of what separates
        # them: a path whose suffix no fast check knows is returned on before its content is read.
        self.index(("160000 " + self.submodule(path=SUB_CHECKED) + "\t" + SUB_CHECKED
                    + "\x00").encode("utf-8"))
        self.git("commit", "-q", "-m", "arranged")
        self.submodule(extra="b.py", path=SUB_CHECKED)
        listings = (self.git("diff", "--cached", "--name-only"), self.git("diff", "--name-only"))

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because the guard is allowed
        # to skip this path only for as long as git records a commit there and no blob; and the
        # suffix beside it, since a path renamed past the checks poses nothing and says nothing.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, said, listings, os.path.splitext(SUB_CHECKED)[1],
                          self.git("ls-tree", "HEAD", "--", SUB_CHECKED).split()[:2]),
                         (0, "", ("", SUB_CHECKED + "\n"), os.path.splitext(PROBE)[1],
                          [GITLINK_MODE, "commit"]))

    def test_Given_ATrackedFileThatWillNotOpen_When_Judged_Then_TheRefusalIsGitsAccountOfIt(self):
        # Arrange — a tracked file whose mode lets nobody read it.
        staged = self.broken_only_in_the_worktree(CLOSED)
        os.chmod(self.root / CLOSED, 0o000)
        self.addCleanup(os.chmod, self.root / CLOSED, 0o644)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert
        self.assertEqual((code, CLOSED in said, CORRECT_WHAT_GIT_NAMED in said, self.opens(CLOSED),
                          staged), (2, True, True, False, ""))

    # GREEN_ON_BASE(characterization): the base runs no hook in this state either.
    def test_Given_AHookOnEveryIndexWrite_When_Judged_Then_ItDoesNotRun(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(PLAIN)
        hook = self.root / ".git" / "hooks" / "post-index-change"
        hook.write_text("#!/bin/sh\ntouch ran\n", encoding="utf-8")
        os.chmod(hook, 0o755)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — beside the refusal, so a guard that never reached the working tree cannot pass,
        # and beside an index write of git's own afterwards, which does run the hook.
        ran = (self.root / "ran").exists()
        self.git("add", PLAIN)
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said, ran, (self.root / "ran").exists(),
                          staged), (2, True, False, True, ""))

    # GREEN_ON_BASE(characterization): the base runs no hook in this state either.
    def test_Given_AHookOnEveryIndexWrite_When_CommittedByPathspec_Then_ItDoesNotRun(self):
        # Arrange
        staged = self.broken_only_in_the_worktree(PLAIN)
        hook = self.root / ".git" / "hooks" / "post-index-change"
        hook.write_text("#!/bin/sh\ntouch ran\n", encoding="utf-8")
        os.chmod(hook, 0o755)

        # Act
        code, said = self.judge("git commit -m x -- " + PLAIN)

        # Assert — as in the case above.
        ran = (self.root / "ran").exists()
        self.git("add", PLAIN)
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said, ran, (self.root / "ran").exists(),
                          staged), (2, True, False, True, ""))

    # GREEN_ON_BASE(characterization): the base writes no shared index in this state either.
    def test_Given_AnIndexSplitOnEveryWrite_When_Judged_Then_NoSharedIndexIsLeftBehind(self):
        # Arrange
        self.git("config", "core.splitIndex", "true")
        self.git("config", "splitIndex.maxPercentChange", "0")
        staged = self.broken_only_in_the_worktree(PLAIN)
        before = sorted(path.name for path in (self.root / ".git").glob("sharedindex.*"))

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — beside the shared indexes the repository already held, so a repository that
        # never split cannot pass.
        after = sorted(path.name for path in (self.root / ".git").glob("sharedindex.*"))
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said, bool(before), after, staged),
                         (2, True, True, before, ""))

    def test_Given_ARelativePathspecFromASubdirectory_When_Judged_Then_TheFileItNamesIsChecked(self):
        # Arrange
        (self.root / "sub").mkdir()
        staged = self.broken_only_in_the_worktree("sub/" + PLAIN)

        # Act
        code, said = self.judge("git commit -m x -- " + PLAIN, cwd=self.root / "sub")

        # Assert
        self.assertEqual((code, "sub/" + PLAIN + FAILED_A_CHECK in said, staged), (2, True, ""))

    def test_Given_ASymlinkReplacedByABrokenFile_When_Judged_Then_TheFileIsChecked(self):
        # Arrange
        (self.root / FIRST_TARGET).write_text(WHOLE, encoding="utf-8")
        os.symlink(FIRST_TARGET, self.root / LINK)
        self.git("add", FIRST_TARGET, LINK)
        self.git("commit", "-q", "-m", "arranged")
        os.unlink(self.root / LINK)
        (self.root / LINK).write_text(BROKEN, encoding="utf-8")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because this is a file's
        # content to check only for as long as git records a file there.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, LINK + FAILED_A_CHECK in said,
                          self.git("ls-tree", "HEAD", "--", LINK).split()[:2]),
                         (2, True, ["100644", "blob"]))

    def test_Given_ACarriageReturnGitNormalises_When_Judged_Then_TheNormalisedBytesAreChecked(self):
        # Arrange — the working copy holds CRLF, which `.gitattributes` asks git to record as LF.
        script = "normalised.sh"
        (self.root / ".gitattributes").write_text("*.sh text eol=lf\n", encoding="utf-8")
        (self.root / script).write_bytes(b"#!/bin/bash\necho first\n")
        self.git("add", ".gitattributes", script)
        self.git("commit", "-q", "-m", "arranged")
        (self.root / script).write_bytes(b"#!/bin/bash\r\nif true; then\r\necho hi\r\nfi\r\n")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — what the commit then wrote sits in the comparison, because checking git's copy
        # rather than the working one is right only for as long as git's copy is the blob.
        self.git("commit", "-a", "-q", "-m", "recorded")
        self.assertEqual((code, said, self.git("show", "HEAD:" + script)),
                         (0, "", "#!/bin/bash\nif true; then\necho hi\nfi\n"))

    # GREEN_ON_BASE(characterization): the base checks a working copy's own bytes too.
    def test_Given_AWorkingCopyBrokenByBareReturns_When_Judged_Then_ItsRecordedBytesAreChecked(self):
        # Arrange — committed as another script, so its line-feed copy below is a change too.
        script, content = "returns.sh", b"#!/bin/bash\nif true; then\recho hi\rfi\n"
        (self.root / script).write_bytes(b"#!/bin/bash\necho first\n")
        self.git("add", script)
        self.git("commit", "-q", "-m", "arranged")
        (self.root / script).write_bytes(content)

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert — beside the verdict on the same script with its returns read as line feeds.
        (self.root / script).write_bytes(content.replace(b"\r", b"\n"))
        self.assertEqual((code, script + FAILED_A_CHECK in said, self.judge("git commit -a -m x")),
                         (2, True, (0, "")))


class PathspecCommits(GuardTestCase):
    def staged_beside(self, named):
        """`STAGED` staged broken, beside `named` changed whole in its working copy alone."""
        for name in (named, STAGED):
            (self.root / name).write_text(WHOLE, encoding="utf-8")
        self.git("add", named, STAGED)
        self.git("commit", "-q", "-m", "arranged")
        (self.root / named).write_text(TREE_AND_WHOLE, encoding="utf-8")
        (self.root / STAGED).write_text(BROKEN, encoding="utf-8")
        self.git("add", STAGED)

    def test_Given_AnotherFileStagedBroken_When_APathspecAloneIsCommitted_Then_ItIsNotChecked(self):
        # Arrange
        self.staged_beside(PLAIN)

        # Act
        code, said = self.judge("git commit -m x -- " + PLAIN)

        # Assert — what the commit then wrote sits in the comparison, because leaving the staged
        # file unchecked is right only for as long as the commit leaves it out.
        self.git("commit", "-q", "-m", "recorded", "--", PLAIN)
        self.assertEqual((code, said, self.git("show", "HEAD:" + STAGED)), (0, "", WHOLE))

    # GREEN_ON_BASE(characterization): the base checks what is staged beside a pathspec too.
    def test_Given_AnotherFileStagedBroken_When_CommittedWithInclude_Then_ItIsChecked(self):
        # Arrange
        self.staged_beside(PLAIN)

        # Act
        code, said = self.judge("git commit -m x -i -- " + PLAIN)

        # Assert — what the commit then wrote sits in the comparison, because `-i` is what takes
        # the staged file into it.
        self.git("commit", "-q", "-m", "recorded", "-i", "--", PLAIN)
        self.assertEqual((code, STAGED + FAILED_A_CHECK in said,
                          self.git("show", "HEAD:" + STAGED)), (2, True, BROKEN))

    # GREEN_ON_BASE(characterization): the base checks what is staged beside a pathspec too.
    def test_Given_AnotherFileStagedBroken_When_CommittedWithIncludeAbbreviated_Then_ItIsChecked(self):
        # Arrange
        self.staged_beside(PLAIN)

        # Act
        code, said = self.judge("git commit -m x --inc -- " + PLAIN)

        # Assert — what the commit then wrote sits in the comparison, as above.
        self.git("commit", "-q", "-m", "recorded", "--inc", "--", PLAIN)
        self.assertEqual((code, STAGED + FAILED_A_CHECK in said,
                          self.git("show", "HEAD:" + STAGED)), (2, True, BROKEN))

    # GREEN_ON_BASE(characterization): the base's listings name nothing under a removed directory.
    def test_Given_ADirectoryRemovedFromTheIndex_When_CommittedByPathspec_Then_TheCommitIsAllowed(self):
        # Arrange — so the pathspec matches nothing but what HEAD holds.
        self.committed("old/" + PLAIN)
        self.git("rm", "-q", "-r", "old")

        # Act
        code, said = self.judge("git commit -m x -- old")

        # Assert — what the commit then wrote sits in the comparison, because allowing it is right
        # only for as long as git records the removal.
        self.git("commit", "-q", "-m", "recorded", "--", "old")
        self.assertEqual((code, said, self.git("ls-tree", "--name-only", "HEAD")), (0, "", ""))

    def test_Given_AFileRemovedFromTheIndexAlone_When_ItsDirectoryIsCommitted_Then_ItIsChecked(self):
        # Arrange — broken in the working copy, and in HEAD but not in the index. The file beside
        # it keeps the pathspec matching something the index holds.
        self.committed("sub/" + PLAIN, "sub/" + STAGED)
        self.git("rm", "-q", "--cached", "sub/" + PLAIN)
        (self.root / "sub" / PLAIN).write_text(BROKEN, encoding="utf-8")
        listed = self.git("ls-files", "--", "sub/" + PLAIN)

        # Act
        code, said = self.judge("git commit -m x -- sub")

        # Assert — what the commit then wrote sits in the comparison, because checking the file is
        # right only for as long as git takes it back from the working tree.
        self.git("commit", "-q", "-m", "recorded", "--", "sub")
        self.assertEqual((code, "sub/" + PLAIN + FAILED_A_CHECK in said, listed,
                          self.git("show", "HEAD:sub/" + PLAIN)), (2, True, "", BROKEN))

    def test_Given_AnAssumeUnchangedFileBrokenOnDisk_When_CommittedByPathspec_Then_ItIsChecked(self):
        # Arrange
        self.committed(PLAIN)
        self.git("update-index", "--assume-unchanged", PLAIN)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")
        listed = self.git("ls-files", "-v", "--", PLAIN)

        # Act
        code, said = self.judge("git commit -m x -- " + PLAIN)

        # Assert — what the commit then wrote sits in the comparison, as above.
        self.git("commit", "-q", "-m", "recorded", "--", PLAIN)
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said, listed,
                          self.git("show", "HEAD:" + PLAIN)), (2, True, "h " + PLAIN + "\n", BROKEN))

    # GREEN_ON_BASE(characterization): the base's working-tree listing leaves a skip-worktree entry out.
    def test_Given_ASkipWorktreeEntryBrokenOnDisk_When_CommittedByPathspec_Then_ItIsNotChecked(self):
        # Arrange — beside a file changed whole, so that the commit records one.
        self.committed(PLAIN, STAGED)
        self.git("update-index", "--skip-worktree", PLAIN)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")
        (self.root / STAGED).write_text(TREE_AND_WHOLE, encoding="utf-8")

        # Act
        code, said = self.judge(f"git commit -m x -- {PLAIN} {STAGED}")

        # Assert — what the commit then wrote sits in the comparison, because leaving the file
        # unchecked is right only for as long as git keeps HEAD's copy of it.
        self.git("commit", "-q", "-m", "recorded", "--", PLAIN, STAGED)
        self.assertEqual((code, said, self.git("show", "HEAD:" + PLAIN)), (0, "", WHOLE))

    def test_Given_PathsReadFromAFile_When_Judged_Then_TheCommitIsRefused(self):
        # Arrange — the file names a path broken in its working copy alone, which git records. The
        # flag spelled out, and abbreviated as far as git reads it as this flag alone.
        self.committed(PLAIN)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")
        (self.root / "paths").write_text(PLAIN + "\n", encoding="utf-8")

        # Act
        verdicts = [self.judge("git commit -m x " + flag)
                    for flag in ("--pathspec-from-file=paths", "--pathspec-fr paths")]

        # Assert
        self.assertEqual([(code, PATHS_FROM_A_FILE in said) for code, said in verdicts],
                         [(2, True)] * 2)

    # GREEN_ON_BASE(characterization): the base checks the staged file in this state too.
    def test_Given_AFileReplacedByADirectory_When_ItsFileIsCommittedByPathspec_Then_ItIsChecked(self):
        # Arrange — a name that is a file in HEAD and a directory in the index.
        self.committed("tool")
        self.git("rm", "-q", "tool")
        (self.root / "tool").mkdir()
        (self.root / "tool" / PLAIN).write_text(BROKEN, encoding="utf-8")
        self.git("add", "tool/" + PLAIN)

        # Act
        code, said = self.judge("git commit -m x -- tool/" + PLAIN)

        # Assert — what the commit then wrote sits in the comparison, because checking the file is
        # right only for as long as git records it.
        self.git("commit", "-q", "-m", "recorded", "--", "tool/" + PLAIN)
        self.assertEqual((code, "tool/" + PLAIN + FAILED_A_CHECK in said,
                          self.git("show", "HEAD:tool/" + PLAIN)), (2, True, BROKEN))

    # GREEN_ON_BASE(characterization): the base checks the staged file in this state too.
    def test_Given_ADirectoryReplacedByAFile_When_TheFileIsCommittedByPathspec_Then_ItIsChecked(self):
        # Arrange — a name that is a directory in HEAD and a file in the index.
        self.committed("settings.json/" + PLAIN)
        self.git("rm", "-q", "-r", "settings.json")
        (self.root / "settings.json").write_text(BROKEN, encoding="utf-8")
        self.git("add", "settings.json")

        # Act
        code, said = self.judge("git commit -m x -- settings.json")

        # Assert — what the commit then wrote sits in the comparison, as above.
        self.git("commit", "-q", "-m", "recorded", "--", "settings.json")
        self.assertEqual((code, "settings.json" + FAILED_A_CHECK in said,
                          self.git("show", "HEAD:settings.json")), (2, True, BROKEN))

    def below_a_broken_file(self):
        """`sub`, holding a file changed whole and one broken, below a file broken in its working
        copy alone whose name sorts after both, so that a check reaching the broken one in `sub`
        refuses over that first."""
        self.committed(TOP, "sub/" + STAGED, "sub/" + EXCLUDED)
        (self.root / TOP).write_text(BROKEN, encoding="utf-8")
        (self.root / "sub" / STAGED).write_text(TREE_AND_WHOLE, encoding="utf-8")
        (self.root / "sub" / EXCLUDED).write_text(BROKEN, encoding="utf-8")
        return self.root / "sub"

    def test_Given_PathspecsThatOnlyExcludeFromASubdirectory_When_Judged_Then_AFileOutsideItIsChecked(self):
        # Arrange — the exclusion spelled five ways.
        sub = self.below_a_broken_file()
        spellings = [":(exclude)" + EXCLUDED, ":!" + EXCLUDED, ":^" + EXCLUDED,
                     ":(icase,exclude)" + EXCLUDED, ":/!sub/" + EXCLUDED]

        # Act
        verdicts = [self.judge(f"git commit -m x -- '{spelling}'", cwd=sub)
                    for spelling in spellings]

        # Assert — what the commit then wrote sits in the comparison, because checking the file
        # above is right only for as long as git takes it into the commit.
        self.git("commit", "-q", "-m", "recorded", "--", spellings[0], root=sub)
        self.assertEqual(([(code, TOP + FAILED_A_CHECK in said) for code, said in verdicts],
                          self.git("show", "HEAD:" + TOP)),
                         ([(2, True)] * len(spellings), BROKEN))

    # GREEN_ON_BASE(characterization): the base leaves the file above unchecked here too.
    def test_Given_PathspecsThatDoNotOnlyExcludeFromASubdirectory_When_Judged_Then_AFileOutsideItIsNotChecked(self):
        # Arrange — an exclusion beside a path, magic that is not an exclusion, and an exclusion's
        # name in an attribute's value behind an escaped comma.
        sub = self.below_a_broken_file()
        (sub / ".gitattributes").write_text(STAGED + " x=a,exclude\n", encoding="utf-8")
        operands = [f"{STAGED} ':!{EXCLUDED}'", f"':/sub/{STAGED}'", f"':(top)sub/{STAGED}'",
                    f"':(attr:x=a\\,exclude){STAGED}'"]

        # Act
        verdicts = [self.judge("git commit -m x -- " + each, cwd=sub) for each in operands]

        # Assert — what the commit then wrote sits in the comparison, because leaving the file
        # above unchecked is right only for as long as the commit leaves it out.
        self.git("commit", "-q", "-m", "recorded", "--", STAGED, ":!" + EXCLUDED, root=sub)
        self.assertEqual((verdicts, self.git("show", "HEAD:" + TOP)),
                         ([(0, "")] * len(operands), WHOLE))

    # GREEN_ON_BASE(characterization): the base lists this file alone here too.
    def test_Given_PathspecsReadLiterally_When_AFileNamedAsAnExclusionIsCommitted_Then_ItIsAllowed(self):
        # Arrange — git's documented literals for true, one of them in capitals.
        named = ":!" + PLAIN
        (self.root / named).write_text(WHOLE, encoding="utf-8")
        self.git("--literal-pathspecs", "add", named)
        self.git("commit", "-q", "-m", "arranged")
        (self.root / named).write_text(TREE_AND_WHOLE, encoding="utf-8")

        # Act
        verdicts = [self.judge(f"git commit -m x -- '{named}'",
                               env={"GIT_LITERAL_PATHSPECS": value})
                    for value in ("1", "TRUE", "yes", "on")]

        # Assert — what the commit then wrote sits in the comparison, because reading the pathspec as
        # a name is right only for as long as git reads it as one.
        self.git("--literal-pathspecs", "commit", "-q", "-m", "recorded", "--", named)
        self.assertEqual((verdicts, self.git("show", "HEAD:" + named)),
                         ([(0, "")] * 4, TREE_AND_WHOLE))


class GitProcesses(GuardTestCase):
    def judge_logging(self, command):
        """The guard's verdict under a `git` that logs, beside the directory holding the log."""
        log = self.logged_git()
        return self.judge(command, path=str(log) + os.pathsep + os.environ["PATH"]), log

    # GREEN_ON_BASE(characterization): the base opens the working tree's files itself under `-a`.
    def test_Given_MoreChangedFiles_When_Judged_Then_NoMoreGitProcessesStart(self):
        # Arrange — twelve files committed whole, and the verdict over one of them changed. Each
        # changes to text of its own, since files changed alike record one blob between them.
        names = [f"file{number}.py" for number in range(12)]
        self.committed(*names)
        (self.root / names[0]).write_text("changed = 0\n", encoding="utf-8")
        one, one_log = self.judge_logging("git commit -a -m x")
        for number, name in enumerate(names[1:], 1):
            (self.root / name).write_text(f"changed = {number}\n", encoding="utf-8")
        changed = self.git("diff", "--name-only").splitlines()

        # Act
        many, many_log = self.judge_logging("git commit -a -m x")

        # Assert
        started = [len((log / "started").read_text(encoding="utf-8").splitlines())
                   for log in (one_log, many_log)]
        self.assertEqual((one, many, len(changed), started[1]),
                         ((0, ""), (0, ""), len(names), started[0]))

    def test_Given_AChangedFileNoCheckReads_When_Judged_Then_ItsBlobIsNotRead(self):
        # Arrange — one suffix a fast check reads and one none does.
        self.committed(PLAIN, "Unread.cs")
        (self.root / PLAIN).write_text(TREE_AND_WHOLE, encoding="utf-8")
        (self.root / "Unread.cs").write_text("class Unread {}\n", encoding="utf-8")
        changed = self.git("diff", "--name-only")
        blobs = [self.git("hash-object", name).strip() for name in ("Unread.cs", PLAIN)]

        # Act
        (code, said), log = self.judge_logging("git commit -a -m x")

        # Assert — beside the blob a check does read, so a git nobody asked cannot pass.
        asked = "".join(each.read_text(encoding="utf-8")
                        for each in (log / "started", log / "asked") if each.exists())
        self.assertEqual((code, said, changed, [blob in asked for blob in blobs]),
                         (0, "", "Unread.cs\n" + PLAIN + "\n", [False, True]))

    def test_Given_APathspecCommit_When_Judged_Then_TheIndexIsHandedNoAbsolutePath(self):
        # Arrange
        self.committed(PLAIN)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")

        # Act
        (code, said), log = self.judge_logging("git commit -m x -- " + PLAIN)

        # Assert — beside the refusal, so a guard that never reached the file cannot pass.
        indexed = log / "indexed"
        handed = indexed.read_bytes().split(b"\0")[:-1] if indexed.exists() else []
        self.assertEqual(
            (code, PLAIN + FAILED_A_CHECK in said, [os.path.isabs(path) for path in handed]),
            (2, True, [False]))


class CampaignsAndCuts(GuardTestCase):
    def campaign_holding(self, name):
        """A campaign's record holding `name`, beside the script that reads it."""
        script = self.root / CAMPAIGN.relative_to(REPO_ROOT)
        script.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy(CAMPAIGN, script)
        Holder(self.root / SENTINEL).hold(self.root / name, WHOLE, BROKEN, "probe")

    # GREEN_ON_BASE(characterization): the base reads every file it lists, whatever its suffix.
    def test_Given_ACutsSourceCarryingItsNeuter_When_Committed_Then_ItIsRefused(self):
        # Arrange — a suffix no fast check reads, so the cut is all that asks for this blob.
        (self.root / CUT_MAP).parent.mkdir(parents=True)
        (self.root / CUT_MAP).write_text(json.dumps({"cuts": [{"edits": [
            {"file": "Cut.cs", "anchor": "void Run()", "neuter": "return;"}]}]}), encoding="utf-8")
        (self.root / "Cut.cs").write_text(
            "class Cut\n{\n    void Run()\n    {\n        return;\n    }\n}\n", encoding="utf-8")
        self.git("add", "Cut.cs")

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, NEUTER_CARRIED in said), (2, True))

    def test_Given_ACampaignHoldingANameOutsideTheEncoding_When_Judged_Then_TheRefusalSaysSo(self):
        # Arrange — the name in the index alone, as for the index cases above, and a caller whose
        # Python writes its streams strictly.
        self.index(b"100644 " + self.blob(WHOLE.encode("utf-8")) + b"\t" + RAW_BYTE_NAME + b"\x00")
        self.campaign_holding(os.fsdecode(RAW_BYTE_NAME))
        strict = {"PYTHONIOENCODING": "utf-8:strict"}

        # Act
        code, said = self.judge("git commit -m x", env=strict)

        # Assert — the name too, as the refusal spells it, since a name in UTF-8 is refused alike;
        # and that this caller's Python cannot write the name, since one that can poses nothing.
        writes = subprocess.run([sys.executable, "-c", "print('\\udcff')"], capture_output=True,
                                timeout=60, env=dict(os.environ, **strict)).returncode
        self.assertEqual((code, HOLDING in said, RAW_BYTE_SHOWN.strip('"') in said, writes),
                         (2, True, True, 1))

    # GREEN_ON_BASE(characterization): the base asks the campaign about every file it reads.
    def test_Given_ACampaignHoldingASourceNoCheckReads_When_ItIsCommitted_Then_TheRefusalSaysSo(self):
        # Arrange
        self.committed("Held.cs")
        (self.root / "Held.cs").write_text("class Held {}\n", encoding="utf-8")
        self.campaign_holding("Held.cs")

        # Act
        code, said = self.judge("git commit -a -m x")

        # Assert
        self.assertEqual((code, HOLDING in said), (2, True))

    def test_Given_NoPythonToValidateTheCutMap_When_TheMapIsCommitted_Then_TheRefusalSaysSo(self):
        # Arrange — the validator is there to run, and the map is what the commit records.
        (self.root / CUT_VALIDATOR).parent.mkdir(parents=True)
        (self.root / CUT_VALIDATOR).write_text("", encoding="utf-8")
        (self.root / CUT_MAP).write_text(json.dumps({"cuts": [], "fixtures": []}), encoding="utf-8")
        self.git("add", CUT_MAP)

        # Act
        code, said = self.judge("git commit -m x", path=self.only_git())

        # Assert
        self.assertEqual((code, NOT_VALIDATED in said), (2, True))

    def verdict_beside_cut_map(self, shape):
        """The verdict on the commit arranged below, with the working tree's cut map as `shape`."""
        (self.root / CUT_MAP).write_text(json.dumps(shape), encoding="utf-8")
        code, said = self.judge("git commit -m x")
        return code, PLAIN + FAILED_A_CHECK in said

    def test_Given_ACutMapOfAnotherShape_When_ABrokenFileIsCommitted_Then_ItIsStillChecked(self):
        # Arrange — JSON each time, and none of it the map's shape: a list, an edit with no file,
        # and cuts that are not a list.
        (self.root / CUT_MAP).parent.mkdir(parents=True)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")
        self.git("add", PLAIN)

        # Act
        verdicts = [self.verdict_beside_cut_map(shape)
                    for shape in ([], {"cuts": [{"edits": [{}]}]}, {"cuts": 1})]

        # Assert
        self.assertEqual(verdicts, [(2, True)] * 3)


class TreeTheChecksRunIn(GuardTestCase):
    def test_Given_ARootWhoseNameEndsInASpace_When_Judged_Then_TheChecksReachIt(self):
        # Arrange — a reading that trims whitespace takes the space off and names a directory that
        # does not exist, which leaves every commit in such a tree refused for a reading nobody
        # can repair.
        spaced = self.repository(self.root / "spaced ")
        (spaced / PLAIN).write_text(WHOLE, encoding="utf-8")
        self.git("add", PLAIN, root=spaced)
        self.git("commit", "-q", "-m", "arranged", root=spaced)
        (spaced / PLAIN).write_text(BROKEN, encoding="utf-8")

        # Act
        code, said = self.judge("git commit -a -m x", cwd=spaced)

        # Assert
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said), (2, True))

    def test_Given_ATreeThatIsNoRepository_When_Judged_Then_TheRefusalNamesWhatGitSaid(self):
        # Arrange — outside `self.root`, since a directory under it would find that repository
        # above. Falling back to this directory as the tree sends every reading below to a git that
        # has already refused, and what those readings answer is about a question none of them asked.
        plain = Path(tempfile.mkdtemp(prefix="fast-checks-none-")).resolve()
        self.addCleanup(shutil.rmtree, plain, ignore_errors=True)

        # Act
        code, said = self.judge("git commit -a -m x", cwd=plain)

        # Assert
        self.assertEqual((code, NOT_A_REPOSITORY in said), (2, True))

    def elsewhere(self):
        """A second repository, holding `PLAIN` committed whole and broken in its working copy."""
        other = self.repository(Path(tempfile.mkdtemp(prefix="fast-checks-other-")).resolve())
        (other / PLAIN).write_text(WHOLE, encoding="utf-8")
        self.git("add", PLAIN, root=other)
        self.git("commit", "-q", "-m", "arranged", root=other)
        (other / PLAIN).write_text(BROKEN, encoding="utf-8")
        return other

    def test_Given_ACommitAimedByGitDirAndWorkTreeFlags_When_Judged_Then_ThatTreeIsChecked(self):
        # Arrange — started in a repository holding nothing to check.
        other = self.elsewhere()

        # Act
        code, said = self.judge(f"git --git-dir={other}/.git --work-tree={other} commit -a -m x")

        # Assert
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said), (2, True))

    def test_Given_ACommitAimedByGitDirAndWorkTreeVariables_When_Judged_Then_ThatTreeIsChecked(self):
        # Arrange — started in a directory in no repository.
        other = self.elsewhere()
        plain = Path(tempfile.mkdtemp(prefix="fast-checks-none-")).resolve()
        self.addCleanup(shutil.rmtree, plain, ignore_errors=True)

        # Act
        code, said = self.judge(f"GIT_DIR={other}/.git GIT_WORK_TREE={other} git commit -a -m x",
                                cwd=plain)

        # Assert
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said), (2, True))

    def test_Given_AnIndexNamedByAnAssignment_When_Judged_Then_TheCommitIsRefused(self):
        # Arrange — that index holds a broken blob, and the repository's own stages nothing.
        self.committed(PLAIN)
        other = self.scratch("fast-checks-index-") / "index"
        shutil.copy(self.root / ".git" / "index", other)
        (self.root / PLAIN).write_text(BROKEN, encoding="utf-8")
        subprocess.run(["git", "-C", str(self.root), "add", PLAIN], check=True, capture_output=True,
                       timeout=60, env=dict(os.environ, GIT_INDEX_FILE=str(other)))
        (self.root / PLAIN).write_text(WHOLE, encoding="utf-8")

        # Act
        code, said = self.judge(f"GIT_INDEX_FILE={other} git commit -m x")

        # Assert
        self.assertEqual((code, NAMED_INDEX in said), (2, True))

    def test_Given_ARelativeDirectory_When_Judged_Then_ItIsReadFromWhereTheCommandRuns(self):
        # Arrange
        other = self.elsewhere()

        # Act
        code, said = self.judge(f"git -C {os.path.relpath(other, self.root)} commit -a -m x")

        # Assert
        self.assertEqual((code, PLAIN + FAILED_A_CHECK in said), (2, True))

    def test_Given_AnIndexThatWillNotOpen_When_Judged_Then_TheRefusalSaysSo(self):
        # Arrange
        (self.root / PLAIN).write_text(WHOLE, encoding="utf-8")
        self.git("add", PLAIN)
        os.chmod(self.root / ".git" / "index", 0o000)
        self.addCleanup(os.chmod, self.root / ".git" / "index", 0o644)

        # Act
        code, said = self.judge("git commit -m x")

        # Assert
        self.assertEqual((code, NOT_COPIED in said, self.opens(".git/index")), (2, True, False))

    # GREEN_ON_BASE(characterization): the base answers a git that never ran with this remedy too.
    def test_Given_NoGitOnThePath_When_Judged_Then_TheRemedyIsToRetry(self):
        # Arrange
        empty = self.root / "empty"
        empty.mkdir()

        # Act
        code, said = self.judge("git commit -m x", path=str(empty))

        # Assert
        self.assertEqual((code, RETRY_WHEN_GIT_ANSWERS in said), (2, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

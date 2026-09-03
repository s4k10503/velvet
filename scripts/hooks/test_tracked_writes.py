#!/usr/bin/env python3
"""Unit tests for .claude/hooks/lib/tracked_writes.py and the two guards it puts on Bash.

The reading is an under-approximation by construction, so a case that only asks what it names would
pass for one that names everything and refuses the session's every command. Several of the cases
below are therefore the other direction — a scratch write, an operand the shell has yet to expand, a
directory the command moved out of, a file name that is markdown inside a heredoc — because a guard
sized wrongly there does not fail, it fires.

The other half a passing suite would still hide: whether either guard's verdict is the one its
subject asks for. A shell write onto a tracked file has to reach the refusal the editing tools
reach, the command that clears that refusal has to keep running, and the message has to say what the
reading did not see rather than implying it saw everything.

Run: python3 scripts/hooks/test_tracked_writes.py
"""

import importlib.util
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
READY_PR_GUARD = REPO_ROOT / ".claude/hooks/refuse/edit_while_a_ready_pr_sits.py"
CLOSED_VERSION_GUARD = REPO_ROOT / ".claude/hooks/refuse/changelog_into_closed_version.py"
LIBRARY = REPO_ROOT / ".claude/hooks/lib/tracked_writes.py"

CHANGELOG_REL = "Packages/com.velvet.core/CHANGELOG.md"

# What a refusal has to admit the reading did not see, spelled here rather than read back from
# `tracked_writes.UNREAD`: a comparison of that tuple against the sentence built from it is satisfied
# by however many entries it has, so it holds when a gap quietly drops out of both.
UNREAD_GAPS = [
    "yet to expand",
    "moves into partway through",
    "`>&`",
    "`>|`",
    "`cp -t`",
    "`tee`, and `git mv`",
    "`xargs` and `sudo`",
    "inside a script or a program",
]
RELEASED = """# Changelog

## [Unreleased]

### Fixed

- Something not yet shipped.

## [2.0.0] - 2026-08-02

### Fixed

- A thing that shipped.
"""


def load_module():
    """Imports tracked_writes by path, since .claude holds no packages."""
    sys.path.insert(0, str(LIBRARY.parent))
    spec = importlib.util.spec_from_file_location("tracked_writes", LIBRARY)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


tracked_writes = load_module()


def scratch(prefix):
    return Path(tempfile.mkdtemp(prefix="velvet-" + prefix + "-"))


class ReadingTests(unittest.TestCase):
    """What the command reader names, before any question of a repository."""

    def setUp(self):
        self.root = scratch("write-reading")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def named(self, command):
        return tracked_writes.literal_write_targets(command, str(self.root))

    def under(self, *parts):
        return str(self.root.joinpath(*parts))

    def test_Given_ASedRewritingAFileInPlace_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — the BSD spelling, whose empty extension is an operand of its own.
        found = self.named("sed -i '' -e s/a/b/ notes.md")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_ASedNotRewritingInPlace_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — the same command without `-i` writes its result to stdout.
        found = self.named("sed -e s/a/b/ notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ASingleQuotedRedirectOperand_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — the mask blanks the quoted span, so an operand skip that reads the mask
        # walks over the operand and off the end of the line.
        found = self.named("printf x > 'notes.md'")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_ADoubleQuotedRedirectOperand_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act
        found = self.named('printf x > "notes.md"')

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_AQuotedOperandAgainstAnAppend_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — `>>` with no space before the quote, which moves the operand's start.
        found = self.named("printf x >>'notes.md'")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_AnEscapedRedirectOperand_When_TheCommandIsRead_Then_TheWholeNameIsRead(self):
        # Arrange / Act — the escape is two blanked columns in the mask, so a skip reading the mask
        # starts one character into the name and refuses a file whose name nothing carries.
        found = self.named("printf x > \\notes.md")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_AQuotedOperandFollowedByAnArgument_When_TheCommandIsRead_Then_TheOperandIsNamed(self):
        # Arrange / Act — what the redirect writes is the quoted word, not the argument after it.
        found = self.named("cat > 'out.txt' notes.md")

        # Assert
        self.assertEqual(found, [self.under("out.txt")])

    def test_Given_ARedirectOperatorInsideAQuotedWord_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — a search for the character, which writes nothing. The quotes are gone by
        # the time a token is read back, so the word after it reads as a file being written, and
        # the refusal that follows names a write the author cannot point at.
        found = self.named("grep -n '>' notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AWriteAfterAPushd_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — `pushd` moves the shell as surely as `cd`, so the operand below belongs
        # to the directory it moved into rather than to the one the tool call started in.
        found = self.named("pushd /tmp && printf 'x\\n' > notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ATrackedNameInACommentAfterAMove_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — the destination is the last operand, and a comment's words are operands
        # too unless something stops them, so the name in the comment read as where the move lands.
        found = self.named("mv /tmp/a.md /tmp/b.md  # was notes.md")

        # Assert — the move's own destination is named; the comment's name is not.
        self.assertNotIn(self.under("notes.md"), found)

    def test_Given_ATrackedNameInACommentAfterASed_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — the same words reach the in-place reading, which offers every operand that
        # is not an option and lets the tracked-file test decide.
        found = self.named("sed -i '' -e s/a/b/ /tmp/x.md  # then hand-apply to notes.md")

        # Assert
        self.assertNotIn(self.under("notes.md"), found)

    def test_Given_AnArrowInsideAShellComment_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — `->` in a comment is not a redirect, and the name after it is not written.
        # A refusal here names a file the command never touches, which its author cannot answer.
        found = self.named("printf x > /tmp/out.txt  # rename: old.md -> notes.md")

        # Assert — the scratch path the command does write, and nothing after the `#`.
        self.assertEqual(found, ["/tmp/out.txt"])

    def test_Given_AQuotedSeparatorOnAHeredocOpenersLine_When_TheCommandIsRead_Then_TheRedirectIsRead(self):
        # Arrange / Act — the bar belongs to a regular alternation. Read as a pipe it split the
        # line, and the redirect after it landed in a segment of its own with the operator gone.
        found = self.named("cat <<'EOF' | grep -E \"^a|^b\" > notes.md\nbody\nEOF\n")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_ARedirectAfterAHeredocOpener_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — the opener's line carries the redirect, and the mask used to blank that
        # line from the delimiter on. This is how a session writes a file from the shell.
        found = self.named("cat <<'EOF' > notes.md\nbody\nEOF\n")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_AWriteOnALaterLineThanAHereString_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act — the write is on a LATER LINE, which is what the here-string reading costs:
        # read as a heredoc, `<<<` takes `<` for a delimiter, finds no body line equal to it and
        # blanks every remaining line. On one line the opener's own tail survives either way, so a
        # single-line case passes with the here-string reading left broken.
        found = self.named("grep -q velvet <<< foo\necho mid\nprintf 'x\\n' > notes.md")

        # Assert
        self.assertEqual(found, [self.under("notes.md")])

    def test_Given_ADescriptorSpellingThatWritesAFile_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — `>&` names a file here rather than a descriptor, and the split leaves the
        # `>` at a segment's end. A declared gap, so the case says so rather than pretending.
        found = self.named("make >& notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ARedirectPastTheClobberGuard_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — the bar of `>|` is a separator to the segment split, so the operand lands
        # in a segment of its own with no operator before it. A declared gap.
        found = self.named("printf x >| notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ACopyWithItsDestinationMovedOffTheEnd_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — `-t` puts the destination first, and a destination read off the wrong end
        # names a source. A declared gap rather than a guess at which end is which.
        found = self.named("cp -t sub notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ATee_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — in the issue's own list of shapes, and it reached a tracked file in none
        # of this project's transcripts. A declared gap.
        found = self.named("echo x | tee notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AGitMove_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — its destination is ordinarily a path git does not yet track, so it wants a
        # criterion of its own rather than this one. A declared gap.
        found = self.named("git mv old.md notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AWriterReachedThroughAnotherProgram_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — the path is an operand of the `sed`, so only the wrapper in front of it
        # keeps this out of the reading. Written with the path upstream of a pipe instead, the case
        # passes whether or not the wrapper is read, which is no case at all.
        found = self.named("xargs sed -i '' -e s/a/b/ notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AWriteMadeFromInsideAProgram_When_TheCommandIsRead_Then_ItIsNotNamed(self):
        # Arrange / Act — the path is an argument to an interpreter rather than an operand of a
        # write, and reading a program for its writes is the problem this declines to solve.
        found = self.named("python3 -c \"open('notes.md','w')\"")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ASedWhoseAttachedScriptCarriesAnI_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act — the script rides on its own option rather than in a token of its own, so
        # a reading that took `-e…` for a cluster of short options would find `-i` inside it.
        found = self.named("sed -e's/i/x/' notes.md")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AFileNamedInsideAHeredocBody_When_TheCommandIsRead_Then_OnlyTheRealTargetIsNamed(self):
        # Arrange — markdown a pull-request body carries: a blockquote, and a line naming a command.
        command = "cat > body.md <<'EOF'\n> notes.md is where that lives\ncp a notes.md\nEOF\n"

        # Act
        found = self.named(command)

        # Assert
        self.assertEqual(found, [self.under("body.md")])

    def test_Given_AWriteAfterTheCommandHasMovedAgain_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange — the shape a session builds a throwaway repository with, which lands its write
        # under the temporary directory rather than under the one the tool call started in.
        command = "S=/tmp/elsewhere\nmkdir -p $S\ncd $S\nprintf 'x\\n' > notes.md"

        # Act
        found = self.named(command)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnOperandTheShellHasYetToExpand_When_TheCommandIsRead_Then_NoFileIsNamed(self):
        # Arrange / Act
        found = self.named('sed -i \'\' -e s/a/b/ "$NOTES"')

        # Assert
        self.assertEqual(found, [])

    def test_Given_AMoveNamingADestination_When_TheCommandIsRead_Then_TheSourceUnderItIsNamed(self):
        # Arrange / Act — where the source lands if the destination is a directory. Whether it is
        # one is not arranged here: the reading offers both paths and the filesystem drops the
        # wrong one, which is the sibling case below.
        found = self.named("mv notes.md sub")

        # Assert
        self.assertIn(self.under("sub", "notes.md"), found)

    def test_Given_AMoveIntoADirectory_When_TheCommandIsRead_Then_TheDirectoryItselfIsNotNamed(self):
        # Arrange — a tracked file under the directory the move names, so git is asked about that
        # directory in the state the reading would ask about it.
        (self.root / "sub").mkdir()
        (self.root / "sub" / "kept.md").write_text("kept\n")
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        subprocess.run(["git", "-C", str(self.root), "add", "sub/kept.md"],
                       check=True, capture_output=True)

        # Act
        found = self.named("mv notes.md sub")

        # Assert — git's own answer rides in the comparison, because it is what makes dropping the
        # directory necessary: `ls-files --error-unmatch` succeeds for one holding a tracked file,
        # so a directory reaching the tracked test would be refused over a path nothing writes. A
        # git that stops answering so reddens this rather than leaving the filter unexplained.
        self.assertEqual(
            (subprocess.run(["git", "-C", str(self.root), "ls-files", "--error-unmatch", "--",
                             self.under("sub")], capture_output=True).returncode,
             self.under("sub") in found),
            (0, False))

    def test_Given_ACopyOntoAFile_When_TheCommandIsRead_Then_ThatFileIsNamed(self):
        # Arrange / Act
        found = self.named("cp /etc/hosts notes.md")

        # Assert
        self.assertIn(self.under("notes.md"), found)


class TrackingTests(unittest.TestCase):
    """Which of the names the reader gives back git places in a repository."""

    def setUp(self):
        self.root = scratch("write-tracking")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_Given_APathInNoRepositoryAtAll_When_TheTargetIsRead_Then_ItIsNotNamed(self):
        # Arrange — the deferral this repository's refusals ask a session to write is this shape:
        # an append onto a path under HOME, which no repository holds.
        command = "echo '1 held 0 s' >> " + str(self.root / ".velvet-pr-deferrals")

        # Act
        found = tracked_writes.tracked_writes(command, str(self.root))

        # Assert
        self.assertEqual(found, [])

    def test_Given_APathInsideTheGitDirectory_When_TheTargetIsRead_Then_ItIsNotNamed(self):
        # Arrange — git tracks nothing there, and asked about it from a `-C` inside the git directory
        # it fails rather than answering, which the unreadable reading would take for a refusal.
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        (self.root / ".git" / "info").mkdir(parents=True, exist_ok=True)
        (self.root / ".git" / "info" / "exclude").write_text("# nothing\n")

        # Act
        found = tracked_writes.tracked_writes("echo 'Logs/' >> .git/info/exclude", str(self.root))

        # Assert
        self.assertEqual(found, [])

    def test_Given_ATrackedFileInAWorktree_When_TheTargetIsRead_Then_ItIsNamed(self):
        # Arrange — a worktree carries `.git` as a file rather than a directory, and CONTRIBUTING.md
        # says branch work here happens in one. A repository test that reads for a directory is
        # silent in all of them and green on a plain checkout, which is what CI gives it.
        checkout = self.root / "checkout"
        checkout.mkdir()
        git = lambda *a: subprocess.run(["git", "-C", str(checkout), *a], check=True,
                                        capture_output=True)
        git("init", "--quiet")
        (checkout / "notes.md").write_text("x\n")
        git("add", "notes.md")
        git("-c", "user.email=a@b", "-c", "user.name=a", "commit", "-qm", "root")
        worktree = self.root / "worktree"
        git("worktree", "add", "-q", str(worktree), "--detach")

        # Act
        found = tracked_writes.tracked_writes("printf x > notes.md", str(worktree))

        # Assert — the shape of the worktree's `.git` rides along, because a directory there would
        # make this pass for the reason a plain checkout passes and pin nothing about a worktree.
        self.assertEqual(((worktree / ".git").is_dir(), found),
                         (False, [str(worktree / "notes.md")]))

    def test_Given_AGitThatRefusesTheRepository_When_TheTargetIsRead_Then_ItCountsAsTracked(self):
        # Arrange — a repository whose git answers `--version` and refuses every question about the
        # tree, which is what this project's Unity job meets: the container runs as root over a
        # checkout owned by another user, so `safe.directory` does not cover it. Read in a
        # subprocess, since the reading is of a program on PATH.
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        stub = self.root / "bin"
        stub.mkdir()
        (stub / "git").write_text(
            "#!/bin/sh\ncase \"$*\" in *--version*) echo 'git version 0'; exit 0;; esac\n"
            "echo 'fatal: detected dubious ownership in repository' >&2\nexit 128\n")
        (stub / "git").chmod(0o755)
        program = (f"import sys; sys.path.insert(0, {str(LIBRARY.parent)!r});"
                   " import tracked_writes;"
                   f" print(tracked_writes.tracked_writes('printf x > notes.md', {str(self.root)!r}))")

        # Act
        done = subprocess.run([sys.executable, "-B", "-c", program], capture_output=True, text=True,
                              timeout=60, env=dict(os.environ, PATH=str(stub) + os.pathsep
                                                   + os.environ.get("PATH", "")))

        # Assert — the exit code rides along, because a reader that died would print nothing and an
        # empty list is what standing down looks like.
        self.assertEqual((done.returncode, done.stdout.strip()),
                         (0, "[" + repr(str(self.root / "notes.md")) + "]"))


class GuardTests(unittest.TestCase):
    """What the two guards do with a Bash command that writes a file the repository tracks."""

    def setUp(self):
        self.root = scratch("write-guard")
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.home = self.root / "home"
        self.home.mkdir()
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        changelog = self.root / CHANGELOG_REL
        changelog.parent.mkdir(parents=True)
        changelog.write_text(RELEASED)
        (self.root / "CONTRIBUTING.md").write_text("How to contribute.\n")
        subprocess.run(["git", "-C", str(self.root), "add", "-A"], check=True, capture_output=True)
        # A watcher answering, and a pull request ready an hour ago, so the sitting branch is what
        # the ready-pull-request guard reaches rather than the branch about nothing watching.
        now = int(time.time())
        (self.home / ".velvet-pr-watch.heartbeat").write_text(f"{now} {os.getpid()}\n")
        (self.home / ".velvet-pr-ready").write_text(f"1 {now - 3600}\n")

    def verdict(self, guard, command):
        """(exit code, stderr) for one guard on one Bash command in this repository."""
        event = {"tool_name": "Bash", "cwd": str(self.root), "tool_input": {"command": command}}
        done = subprocess.run([sys.executable, "-B", str(guard)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=90,
                              env=dict(os.environ, HOME=str(self.home),
                                       CLAUDE_PROJECT_DIR=str(self.root)))
        return done.returncode, done.stderr

    def test_Given_AShellWriteOntoATrackedFile_When_APullRequestHasSat_Then_ItIsRefused(self):
        # Arrange / Act
        code, _ = self.verdict(READY_PR_GUARD, "printf 'x\\n' > CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 2)

    def test_Given_TheCommandThatClearsThatRefusal_When_APullRequestHasSat_Then_ItStillRuns(self):
        # Arrange / Act — the refusal names this, so a guard refusing it leaves no way out at all.
        code, _ = self.verdict(READY_PR_GUARD, "python3 scripts/pr/settle.py merge 1")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ARefusalOfAShellWrite_When_ItsTextIsRead_Then_ItStatesEveryGapInTheReading(self):
        # Arrange / Act
        _, said = self.verdict(READY_PR_GUARD, "printf 'x\\n' > CONTRIBUTING.md")

        # Assert
        self.assertEqual([gap for gap in UNREAD_GAPS if gap in said], UNREAD_GAPS)

    def test_Given_TheGapsThisSuiteKnows_When_TheModulesOwnListIsCounted_Then_ItCarriesTheSameNumber(self):
        # Arrange / Act — the count rather than the text, which is the half the case above cannot
        # hold: a gap added to the module and to nothing else leaves that one green.
        declared = len(tracked_writes.UNREAD)

        # Assert
        self.assertEqual(declared, len(UNREAD_GAPS))

    def test_Given_AShellWriteOntoATrackedChangelog_When_TheClosedVersionGuardReadsIt_Then_ItIsRefused(self):
        # Arrange / Act — a reword of an entry, which the Edit path refuses where the entry is in a
        # released section and allows where it is not. Neither is decidable from the command.
        code, _ = self.verdict(CLOSED_VERSION_GUARD,
                               "sed -i '' -e s/shipped/landed/ " + CHANGELOG_REL)

        # Assert
        self.assertEqual(code, 2)

    def test_Given_AShellWriteOntoAnotherTrackedFile_When_TheClosedVersionGuardReadsIt_Then_ItRuns(self):
        # Arrange / Act
        code, _ = self.verdict(CLOSED_VERSION_GUARD, "sed -i '' -e s/a/b/ CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

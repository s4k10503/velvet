#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/message_outside_its_worktree.py.

The defect is silent: `-F` succeeds, the tree is right, the diff is right, and only the prose is
another change's — so the cases hold the refusals and, just as much, the shapes that must still go
through, because a guard that refuses too much is one somebody turns off.

`ToplevelReadingTests` holds three roots git names that a reading of its answer has to take as
written: one holding a byte UTF-8 does not map, which a strict decode raised on so the hook exited 1
and let the command through, and two whose names end in a space or in a carriage return, each of
which a reading of the root has spent.

Run: python3 scripts/hooks/test_message_outside_its_worktree.py
"""

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/message_outside_its_worktree.py"

# A worktree name holding a byte UTF-8 does not map, recorded as `core.worktree`: git names it as the
# toplevel with no directory behind it, and APFS refuses to make one, with `Illegal byte sequence`.
UNDECODABLE_TREE = b"tree\xff"


class Verdicts(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="message-worktree-")).resolve()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        self.elsewhere = Path(tempfile.mkdtemp(prefix="message-shared-")).resolve()
        self.addCleanup(shutil.rmtree, self.elsewhere, ignore_errors=True)

    def judge(self, command, cwd=None):
        """The guard's verdict, as (exit code, stderr).

        Cases below match on words the refusal writes, never on a substring of the guard's own name:
        a tree without the guard exits 2 from python with the path in the message, and `outside` is
        in that path. Measured -- two cases passed on the base that way.
        """
        event = {"tool_name": "Bash", "cwd": str(cwd or self.root),
                 "tool_input": {"command": command}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30)
        return done.returncode, done.stderr

    def test_Given_AMessageAttachedToItsShortFlag_When_Judged_Then_ItIsRefused(self):
        # Arrange — git reads `-F/path` as readily as `-F /path`, and this guard read neither the
        # attached form nor a cluster. The attached one is unambiguous: no other letter can have
        # claimed the tail.
        code, said = self.judge(f"git commit -F{self.elsewhere}/msg.txt")

        # Act / Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    # GREEN_ON_BASE(characterization): the base refuses this because it reads every command with one
    # table, and this branch keeps refusing it because it reads git's with git's. What the case holds is
    # that routing gh through `pr_body` did not take `--file` with it — measured by posing git's operands
    # to `pr_body` too, which answers None and lets the path through.
    def test_Given_AGitCommitFileInGhsSpelling_When_Judged_Then_ItIsStillRefused(self):
        # Arrange — `--file` is not one of gh's options at all, so reading git's operands through gh's
        # table would lose this path. The two readings are kept apart for that, and this is what says so.
        code, said = self.judge(f"git commit --file {self.elsewhere}/msg.txt")

        # Act / Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_ABodyFileBehindABooleanShorthand_When_Judged_Then_ItIsStillRefused(self):
        # Arrange — one character, and the guard saw no body file at all: a cluster is read a letter
        # at a time, so `-d` has to be known before `-F` behind it is. gh posts the file either way.
        code, said = self.judge(f"gh pr create --title x -dF {self.elsewhere}/body.md")

        # Act / Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_APathStandingWhereAnotherOptionsValueGoes_When_Judged_Then_ItIsNotRefused(self):
        # Arrange — the other direction. `--title` takes a value, so gh reads `-F` as that value and
        # posts nothing from a file; refusing here is a refusal over a path the command never opens.
        code, said = self.judge(f"gh pr create --title -F {self.elsewhere}/body.md")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ACommitMessageAtASharedPath_When_Judged_Then_ItIsRefused(self):
        # Arrange — the shape that landed one change's message on another: a generic path outside
        # every worktree, written by whichever agent wrote it last.
        code, said = self.judge(f"git commit -F {self.elsewhere}/msg.txt")

        # Act / Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_ACommitMessageInsideTheWorktree_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — inside the worktree the change is in, which is where the refusal sends a message.
        code, said = self.judge(f"git commit -F {self.root}/msg.txt")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ARelativeMessagePath_When_Judged_Then_ItIsReadFromTheWorktree(self):
        # Arrange — the shell resolves it from the command's directory, and so does this.
        code, said = self.judge("git commit -F msg.txt")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_APullRequestBodyAtASharedPath_When_Judged_Then_ItIsRefused(self):
        # Arrange — the same defect through the other door, and the one a squash merge does not even
        # need: the body is the description a reader is handed.
        code, said = self.judge(
            f"gh pr create --title t --body-file {self.elsewhere}/pr-body.md")

        # Act / Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_AnInlineMessage_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — `-m` carries the message itself, so there is no path for anyone to overwrite.
        code, said = self.judge('git commit -m "a message nobody shares"')

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AnUnexpandedMessagePath_When_Judged_Then_ItIsRefusedRatherThanPassed(self):
        # Arrange — the literal names no path, and answering about it would pass every one of them,
        # which for a guard over "a path written once and read later" is the whole subject.
        code, said = self.judge('git commit -F "$MSG"')

        # Act / Assert
        self.assertEqual((code, "unexpanded" in said), (2, True))

    def test_Given_ALeadingCdIntoTheWorktree_When_Judged_Then_ThatIsTheTreeItIsAskedAbout(self):
        # Arrange — PreToolUse fires before the command runs, so the event's directory is where the
        # call started rather than where the message belongs.
        code, said = self.judge(f"cd {self.root} && git commit -F {self.root}/msg.txt",
                                cwd=self.elsewhere)

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AnUnexpandedPathOutsideAnyRepository_When_Judged_Then_ItIsStillRefused(self):
        # Arrange — the worktree reading stands down where git will not answer, and asking it first
        # took the unexpanded reading down with it. That is how the guard's own declared policy
        # disagreed with it: `PreExpansionPolicyTests` poses each probe where no worktree answers.
        code, said = self.judge('git commit -F "$MSG"', cwd=self.elsewhere)

        # Act / Assert
        self.assertEqual((code, "unexpanded" in said), (2, True))

    def test_Given_ACommandOutsideAnyRepository_When_Judged_Then_ItIsNotThisGuardsToRefuse(self):
        # Arrange — no worktree, so no worktree the message belongs to.
        code, said = self.judge(f"git commit -F {self.elsewhere}/msg.txt", cwd=self.elsewhere)

        # Act / Assert
        self.assertEqual((code, said), (0, ""))


class TreeTheCommitIsMadeIn(unittest.TestCase):
    def setUp(self):
        self.checkouts = Path(tempfile.mkdtemp(prefix="message-commit-tree-")).resolve()
        self.addCleanup(shutil.rmtree, self.checkouts, ignore_errors=True)
        self.primary = self.checkouts / "primary"
        self.worktree = self.checkouts / "worktree"
        for args in (["init", "--quiet", "-b", "main", str(self.primary)],
                     ["-C", str(self.primary), "config", "user.email", "t@velvet"],
                     ["-C", str(self.primary), "config", "user.name", "t"],
                     ["-C", str(self.primary), "commit", "--quiet", "--allow-empty", "-m", "root"],
                     ["-C", str(self.primary), "worktree", "add", "--quiet", "-b", "work",
                      str(self.worktree)]):
            subprocess.run(["git", *args], check=True, capture_output=True, timeout=30)
        self.outside = Path(tempfile.mkdtemp(prefix="message-no-repository-")).resolve()
        self.addCleanup(shutil.rmtree, self.outside, ignore_errors=True)

    @staticmethod
    def judge(command, cwd, home=None):
        """The guard's verdict, as (exit code, stderr)."""
        event = {"tool_name": "Bash", "cwd": str(cwd), "tool_input": {"command": command}}
        environment = dict(os.environ, **({} if home is None else {"HOME": str(home)}))
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30, env=environment)
        return done.returncode, done.stderr

    def committed(self, command):
        """The subject at the head of the worktree's branch once `command` has run from the primary
        checkout, run by a shell so that the text the guard was handed is what runs."""
        subprocess.run(command, shell=True, cwd=self.primary, capture_output=True, timeout=30)
        return subprocess.run(["git", "-C", str(self.worktree), "log", "-1", "--format=%s"],
                              check=True, capture_output=True, text=True, timeout=30).stdout.strip()

    def test_Given_ACommitMadeInAWorktreeFromThePrimary_When_ItsMessageIsInsideThatWorktree_Then_ItIsLetThrough(self):
        # Arrange — under the Logs of the worktree the change is in, where AGENTS.md sends it.
        command = f"git -C {self.worktree} commit -F {self.worktree}/Logs/run/msg.txt"

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ACommitMadeInAWorktreeFromThePrimary_When_ItsMessageIsInsideThePrimary_Then_ItIsRefusedNamingTheWorktree(self):
        # Arrange — inside the tree the command starts in, and outside the one the commit is made in.
        command = f"git -C {self.worktree} commit -F {self.primary}/Logs/run/msg.txt"

        # Act
        code, said = self.judge(command, self.primary)

        # Assert — the tree named, since the refusal sends the message there.
        self.assertEqual((code, f"is outside {self.worktree}, the worktree it describes" in said),
                         (2, True))

    def test_Given_ACommitAimedByGitDirAndWorkTreeFlags_When_ItsMessageIsInsideThatTree_Then_ItIsLetThrough(self):
        # Arrange
        command = (f"git --git-dir={self.worktree}/.git --work-tree={self.worktree} commit "
                   f"-F {self.worktree}/Logs/run/msg.txt")

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ACommitAimedByGitDirAndWorkTreeFlagsFromNoRepository_When_ItsMessageIsOutsideThatTree_Then_ItIsRefused(self):
        # Arrange — started in no repository, so without the git directory the flags name git finds
        # none and the guard stands down; and the message outside that directory as well as the
        # worktree, so the work tree they name is not what this turns on.
        command = (f"git --git-dir={self.worktree}/.git --work-tree={self.worktree} commit "
                   f"-F {self.checkouts}/msg.txt")

        # Act
        code, said = self.judge(command, self.outside)

        # Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_ACommitAimedByGitDirAndWorkTreeVariables_When_ItsMessageIsInsideThatTree_Then_ItIsLetThrough(self):
        # Arrange
        command = (f"GIT_DIR={self.worktree}/.git GIT_WORK_TREE={self.worktree} git commit "
                   f"-F {self.worktree}/Logs/run/msg.txt")

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_ACommitAimedByGitDirAndWorkTreeVariablesFromNoRepository_When_ItsMessageIsOutsideThatTree_Then_ItIsRefused(self):
        # Arrange — as with the flags above.
        command = (f"GIT_DIR={self.worktree}/.git GIT_WORK_TREE={self.worktree} git commit "
                   f"-F {self.checkouts}/msg.txt")

        # Act
        code, said = self.judge(command, self.outside)

        # Assert
        self.assertEqual((code, "the worktree it describes" in said), (2, True))

    def test_Given_ACommitMadeInASubdirectoryOfAWorktree_When_ARelativeMessageIsCommitted_Then_ItIsReadFromThatSubdirectory(self):
        # Arrange — `../msg.txt` is the worktree's own file from its `sub`, and the one beside the
        # checkouts from the primary; git is what says which it reads.
        (self.worktree / "sub").mkdir()
        (self.worktree / "msg.txt").write_text("the worktree's message\n", encoding="utf-8")
        (self.checkouts / "msg.txt").write_text("a message beside the checkouts\n", encoding="utf-8")
        command = f"git -C {self.worktree}/sub commit --quiet --allow-empty -F ../msg.txt"

        # Act
        code, said = self.judge(command, self.primary)
        recorded = self.committed(command)

        # Assert — what git recorded rides along, so the verdict is about the file git read.
        self.assertEqual((code, said, recorded), (0, "", "the worktree's message"))

    # GREEN_ON_BASE(characterization): the base lets this through as well, having judged the primary's file.
    # What reddens it is reading a relative path from the directory the command starts in, or from
    # that directory joined to a `-C`, while judging it against the tree the commit is made in.
    def test_Given_AWorkTreeTheCommandStartsOutside_When_ARelativeMessageIsCommitted_Then_ItIsReadFromThatTree(self):
        # Arrange — `msg.txt` sits in both trees, and the one git reads is the worktree's.
        (self.worktree / "msg.txt").write_text("the worktree's message\n", encoding="utf-8")
        (self.primary / "msg.txt").write_text("the primary's message\n", encoding="utf-8")
        command = (f"git --git-dir={self.worktree}/.git --work-tree={self.worktree} commit "
                   "--quiet --allow-empty -F msg.txt")

        # Act
        code, said = self.judge(command, self.primary)
        recorded = self.committed(command)

        # Assert — as in the case above.
        self.assertEqual((code, said, recorded), (0, "", "the worktree's message"))

    def test_Given_ACommitAimedByAnUnexpandedDirectory_When_ItsMessageIsSpelledOut_Then_ItIsRefused(self):
        # Arrange — the message sits in the tree the command starts in, so the tree the commit is
        # made in is the only thing left to refuse over.
        command = f'git -C "$WORKTREE" commit -F {self.primary}/Logs/run/msg.txt'

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, "Spell the directory out" in said), (2, True))

    def test_Given_ACommitAimedByATildeSpelledDirectory_When_ItsMessageIsOutsideThatTree_Then_ItIsRefused(self):
        # Arrange — HOME is where the checkouts sit, so once the shell has expanded it `~/worktree`
        # is the worktree.
        command = f"git -C ~/{self.worktree.name} commit -F {self.primary}/Logs/run/msg.txt"

        # Act
        code, said = self.judge(command, self.primary, home=self.checkouts)

        # Assert
        self.assertEqual((code, f"is outside {self.worktree}, the worktree it describes" in said),
                         (2, True))

    # GREEN_ON_BASE(characterization): the base reads one tree for every invocation and refuses this message too.
    # What reddens it is standing the whole command down when git names no tree for one of them.
    def test_Given_ACommitWhoseTreeGitWillNotName_When_ACommitAfterItCarriesAMessageFromOutside_Then_ThatOneIsRefused(self):
        # Arrange
        command = (f"git -C {self.checkouts}/missing commit -F {self.primary}/msg.txt; "
                   f"git commit -F {self.checkouts}/msg.txt")

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, f"{self.checkouts}/msg.txt\nis outside {self.primary}," in said),
                         (2, True))

    # GREEN_ON_BASE(characterization): the base judges the body against the tree the command runs in.
    # What reddens it is judging gh against a tree read off the `git -C` beside it.
    def test_Given_AGitDashCBesideAPullRequestBody_When_TheBodyIsInsideTheTreeTheCommandRunsIn_Then_ItIsLetThrough(self):
        # Arrange — gh takes no `-C`, so the one beside it moves nothing gh reads.
        command = (f"git -C {self.worktree} commit -m x && "
                   f"gh pr create --title t --body-file {self.primary}/Logs/run/body.md")

        # Act
        code, said = self.judge(command, self.primary)

        # Assert
        self.assertEqual((code, said), (0, ""))


class ToplevelReadingTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="message-worktree-toplevel-")).resolve()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.elsewhere = Path(tempfile.mkdtemp(prefix="message-shared-")).resolve()
        self.addCleanup(shutil.rmtree, self.elsewhere, ignore_errors=True)

    def repository(self, name):
        made = self.root / name
        subprocess.run(["git", "init", "--quiet", str(made)], check=True, capture_output=True)
        return made

    def undecodable_worktree(self):
        """A repository whose worktree git names with the byte, and that name as the guard's reading
        spells it."""
        made = self.repository("repository")
        tree = bytes(made) + b"/" + UNDECODABLE_TREE
        subprocess.run([b"git", b"-C", bytes(made), b"config", b"core.worktree", tree],
                       check=True, capture_output=True)
        return made, os.fsdecode(tree)

    @staticmethod
    def toplevel_is_undecodable(repository):
        """Whether git names the toplevel with the byte, read as bytes rather than through the
        decode under test."""
        raw = subprocess.run(["git", "-C", str(repository), "rev-parse", "--show-toplevel"],
                             capture_output=True, check=True).stdout
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            return True
        return False

    @staticmethod
    def judge(command, cwd):
        """The guard's verdict, as (exit code, stderr)."""
        event = {"tool_name": "Bash", "cwd": str(cwd), "tool_input": {"command": command}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30)
        return done.returncode, done.stderr

    def test_Given_AWorktreeGitNamesWithAByteThatIsNotUTF8_When_AMessageOutsideItIsCommitted_Then_ItIsRefused(self):
        # Arrange — the gate rides in the comparison because a toplevel that decoded would leave
        # nothing to fail on, and this case green having posed nothing.
        repository, _ = self.undecodable_worktree()
        arranged = self.toplevel_is_undecodable(repository)

        # Act
        code, said = self.judge(f"git commit -F {self.elsewhere}/msg.txt", repository)

        # Assert
        self.assertEqual((arranged, code, "the worktree it describes" in said), (True, 2, True))

    def test_Given_AWorktreeGitNamesWithAByteThatIsNotUTF8_When_AMessageInsideItIsCommitted_Then_ItIsLetThrough(self):
        # Arrange — the other side of the same worktree, so a reading that survives the byte by
        # refusing whatever it is asked is not what makes the case above pass.
        repository, tree = self.undecodable_worktree()
        arranged = self.toplevel_is_undecodable(repository)

        # Act
        code, said = self.judge(f"git commit -F {tree}/msg.txt", repository)

        # Assert
        self.assertEqual((arranged, code, said), (True, 0, ""))

    def test_Given_AWorktreeWhoseNameEndsInASpace_When_AMessageInsideItIsCommitted_Then_ItIsLetThrough(self):
        # Arrange — relative, so the path is read from inside the name the space belongs to.
        repository = self.repository("checkout ")

        # Act
        code, said = self.judge("git commit -F msg.txt", repository)

        # Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AWorktreeWhoseNameEndsInACarriageReturn_When_AMessageInsideItIsCommitted_Then_ItIsLetThrough(self):
        # Arrange
        repository = self.repository("checkout\r")

        # Act
        code, said = self.judge("git commit -F msg.txt", repository)

        # Assert
        self.assertEqual((code, said), (0, ""))


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/message_outside_its_worktree.py.

The defect is silent: `-F` succeeds, the tree is right, the diff is right, and only the prose is
another change's — so the cases hold the refusals and, just as much, the shapes that must still go
through, because a guard that refuses too much is one somebody turns off.

Run: python3 scripts/hooks/test_message_outside_its_worktree.py
"""

import json
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/message_outside_its_worktree.py"


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
        # Arrange — a worktree is per-agent here, so a file inside it is nobody else's to write.
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


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for amend_of_published_commit.py.

Every case runs the guard as a process against a real pair of repositories, because what it decides
is a question put to git and a stubbed answer would be this file deciding it instead.

`PredicateAgreementTests` is the odd one out: it holds git rather than the guard. Either of two
commands answers "does a remote-tracking ref reach HEAD", the guard picks one, and the reason that
choice is free is that they agree — which is a fact about git, so it lives here where it fails when
it stops being true rather than in a sentence beside the call.

Run: python3 scripts/hooks/test_amend_of_published_commit.py
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
GUARD = REPO_ROOT / ".claude/hooks/refuse/amend_of_published_commit.py"

REFUSE = 2
ALLOW = 0

# The two headlines the guard writes. A refusal that could not read and one that read are different
# claims, and only one of them is entitled to say the commit is published.
PUBLISHED = "Refusing `git commit --amend`: this commit is already published."
UNREAD = "Refusing `git commit --amend`: git could not say whether this commit is published."

GIT_IDENTITY = {
    "GIT_AUTHOR_NAME": "hooks", "GIT_AUTHOR_EMAIL": "hooks@velvet.test",
    "GIT_COMMITTER_NAME": "hooks", "GIT_COMMITTER_EMAIL": "hooks@velvet.test",
}


def git(cwd, *args):
    environment = dict(os.environ)
    environment.update(GIT_IDENTITY)
    finished = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True,
                              env=environment, timeout=60)
    if finished.returncode != 0:
        raise AssertionError(f"git {' '.join(args)} in {cwd}: {finished.stderr}")
    return finished.stdout


def commit(cwd, name):
    (Path(cwd) / name).write_text(name, encoding="utf-8")
    git(cwd, "add", name)
    git(cwd, "commit", "-qm", name)


class GuardCase(unittest.TestCase):
    """A clone whose HEAD is published, and a bare repository standing in for the remote."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-amend-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        remote = self.root / "remote.git"
        seed = self.root / "seed"
        seed.mkdir()
        git(self.root, "init", "-q", "--bare", "remote.git")
        git(seed, "init", "-q", ".")
        commit(seed, "one")
        git(seed, "remote", "add", "origin", str(remote))
        git(seed, "push", "-q", "origin", "HEAD:refs/heads/main")
        self.clone = self.root / "clone"
        git(self.root, "clone", "-q", str(remote), "clone")

    def answer(self, command, cwd=None):
        """(exit code, whatever the guard wrote) for one Bash command."""
        payload = json.dumps({"tool_name": "Bash", "cwd": str(cwd or self.clone),
                              "tool_input": {"command": command}})
        finished = subprocess.run([sys.executable, "-B", str(GUARD)], input=payload, text=True,
                                  capture_output=True, timeout=120)
        return finished.returncode, finished.stderr

    def verdict(self, command, cwd=None):
        """The guard's exit code. Only for the commands it lets through — see `refused`."""
        return self.answer(command, cwd)[0]

    def refusal(self, command, cwd=None):
        return self.answer(command, cwd)[1]

    def refused(self, command, cwd=None):
        """(exit code, the first line of what was written) for a command expected to be refused.

        Not the exit code alone. `python3` handed a script it cannot open exits 2, which is exactly
        what a refusal exits, so a case comparing the code by itself passes where the guard is not
        on disk — which is its state on the merge base, and would be its state if it were deleted.
        The headline is what a missing file cannot produce.
        """
        code, text = self.answer(command, cwd)
        lines = text.splitlines()
        return code, (lines[0] if lines else "")


class PublishedHeadTests(GuardCase):
    def test_Given_AHeadAtTheRemoteTip_When_AnAmendIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        answer = self.refused("git commit --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AHeadAtTheRemoteTip_When_AnAmendCarryingNoEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        answer = self.refused("git commit --amend --no-edit")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AnAmendInASecondSegment_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — a guard reading only the first command of the line sees a directory change here.
        # Act
        answer = self.refused("cd /tmp && git commit --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AnAmendNamingTheTreeWithDashC_When_ItIsPosed_Then_ThatTreeIsWhatIsRead(self):
        # Arrange — the shell sits in a directory git cannot place, so an answer at all is one
        # taken from the named tree. Both halves ride in the comparison: read from the cwd instead,
        # the refusal would be the unreadable one and the exit code the same.
        outside = self.root / "outside"
        outside.mkdir()

        # Act
        text = self.refusal(f"git -C {self.clone} commit --amend", cwd=outside)

        # Assert
        self.assertEqual(("origin/main" in text, "could not say" in text), (True, False))

    def test_Given_AHeadAtTheRemoteTip_When_AnAmendIsRefused_Then_ItNamesTheRefThatReachesIt(self):
        # Arrange / Act
        text = self.refusal("git commit --amend")

        # Assert — the headline rides along, since the two refusals differ in what they claim and
        # only one of them is entitled to say the commit is published.
        self.assertEqual(("origin/main" in text, text.splitlines()[0]), (True, PUBLISHED))

    def test_Given_SeveralRemoteBranchesReachingHead_When_AnAmendIsRefused_Then_ItNamesTheUpstream(self):
        # Arrange — the clone tracks origin/main, and a second branch reaches the same commit while
        # sorting ahead of it. Named by position rather than by upstream, the refusal points the
        # reader at a branch they are not on.
        git(self.root / "seed", "push", "-q", "origin", "HEAD:refs/heads/aaa-elsewhere")
        git(self.clone, "fetch", "-q", "origin")

        # Act — the abbreviated SHA is dropped, since which commit it is is another case's subject.
        named = self.refusal("git commit --amend").splitlines()[2].partition(" is reachable from ")[2]

        # Assert — the other branch rides in the comparison, since a reading that found only
        # origin/main would name it too and pin nothing about the choice.
        self.assertEqual(
            ("aaa-elsewhere" in git(self.clone, "branch", "-r", "--contains", "HEAD"), named),
            (True, "origin/main and 1 other remote branch"))

    def test_Given_AHeadReachedOnlyByADeletedRemoteBranch_When_AnAmendIsPosed_Then_ItIsRefused(self):
        # Arrange — the branch is gone from the remote and this clone never pruned, so the ref that
        # reaches HEAD is stale. It was still published, which is what the refusal is about.
        git(self.clone, "checkout", "-q", "-b", "side")
        commit(self.clone, "two")
        git(self.clone, "push", "-q", "origin", "HEAD:refs/heads/side")
        git(self.root / "remote.git", "update-ref", "-d", "refs/heads/side")
        git(self.clone, "fetch", "-q", "origin")

        # Act
        answer = self.refused("git commit --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))


class UnpublishedHeadTests(GuardCase):
    def test_Given_AnUnpushedCommitOnTop_When_AnAmendIsPosed_Then_ItIsAllowed(self):
        # Arrange — the ordinary amend, and the case refusing would cost more than the defect.
        commit(self.clone, "two")

        # Act
        code = self.verdict("git commit --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ADetachedHeadAtAnUnpushedCommit_When_AnAmendIsPosed_Then_ItIsAllowed(self):
        # Arrange
        git(self.clone, "checkout", "-q", "--detach")
        commit(self.clone, "two")

        # Act
        code = self.verdict("git commit --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AWorktreeOnAnUnpushedCommit_When_AnAmendIsPosed_Then_ItIsAllowed(self):
        # Arrange — where this repository's branch work happens, and where the commit under the
        # amend is unpublished while its parent is not.
        worktree = self.root / "worktree"
        git(self.clone, "worktree", "add", "-q", "-b", "side", str(worktree))
        commit(worktree, "two")

        # Act
        code = self.verdict("git commit --amend", cwd=worktree)

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ARemoteThatMovedUnderAnUnpushedCommit_When_AnAmendIsPosed_Then_ItIsAllowed(self):
        # Arrange — a fetch that advanced origin/main past this branch. Nothing reaches HEAD, and
        # the refs the fetch brought in are what a reading over "is the branch behind" would take.
        seed = self.root / "seed"
        commit(seed, "upstream")
        git(seed, "push", "-q", "origin", "HEAD:refs/heads/main")
        commit(self.clone, "two")
        git(self.clone, "fetch", "-q", "origin")

        # Act
        code = self.verdict("git commit --amend")

        # Assert
        self.assertEqual(code, ALLOW)


class CommandReadingTests(GuardCase):
    def test_Given_AnOrdinaryCommit_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.verdict("git commit -m one")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ACommitMessageSpellingTheFlag_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange — `-m` takes the next token, so this names a message and not an amend.
        # Act
        code = self.verdict("git commit -m --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ALongMessageFlagTakingTheFlagAsItsValue_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange — the long spelling walks a different branch from `-m`, and each has to skip the
        # token it takes.
        # Act
        code = self.verdict("git commit --message --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_APathspecSpellingTheFlag_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange — `--` ends the options, so what follows is a path.
        # Act
        code = self.verdict("git commit -- --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ACommandThatIsNotACommit_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.verdict("git log --oneline --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AnAmendPosedUnderAToolNothingRoutes_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange
        payload = json.dumps({"tool_name": "Read", "cwd": str(self.clone),
                              "tool_input": {"command": "git commit --amend"}})

        # Act
        finished = subprocess.run([sys.executable, "-B", str(GUARD)], input=payload, text=True,
                                  capture_output=True, timeout=120)

        # Assert
        self.assertEqual(finished.returncode, ALLOW)


class UnreadableTreeTests(GuardCase):
    def test_Given_ADirectoryInNoRepository_When_AnAmendIsPosed_Then_ItIsRefused(self):
        # Arrange — whether git placed the directory rides in the comparison, since a temporary
        # path that turned out to sit inside some repository reaches the refusal by the ordinary
        # route and the case would pin nothing.
        outside = self.root / "outside"
        outside.mkdir()
        placed = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=outside,
                                capture_output=True, text=True, timeout=60).returncode == 0

        # Act
        code, headline = self.refused("git commit --amend", cwd=outside)

        # Assert
        self.assertEqual((placed, code, headline), (False, REFUSE, UNREAD))

    def test_Given_ATreeNamedByAnUnexpandedOperand_When_AnAmendIsPosed_Then_ItIsRefused(self):
        # Arrange / Act — the guard is handed the command before the shell rewrites it, so the tree
        # it would resolve is a directory named `$WORKTREE`.
        answer = self.refused("git -C $WORKTREE commit --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, UNREAD))

    def test_Given_AnUnreadableTree_When_AnAmendIsRefused_Then_ItSaysTheReadingIsWhatFailed(self):
        # Arrange — a guard that blocks because it could not read, saying what a guard that read
        # says, sends the reader looking for a push nobody made.
        outside = self.root / "outside"
        outside.mkdir()

        # Act
        headline = self.refusal("git commit --amend", cwd=outside).splitlines()[0]

        # Assert
        self.assertEqual(headline, UNREAD)


class PredicateAgreementTests(GuardCase):
    """The two commands that answer the guard's question, held to the same answers.

    Not about the guard: it calls one of them. What this fails on is the day they diverge, which is
    the day the choice between them stops being free and the comment saying so stops being true.
    """

    def refs(self, cwd, args):
        finished = subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True,
                                  timeout=60)
        return finished.returncode, sorted(finished.stdout.split())

    def both(self, cwd):
        return (self.refs(cwd, ["branch", "-r", "--contains", "HEAD", "--format=%(refname)"]),
                self.refs(cwd, ["for-each-ref", "--contains", "HEAD", "--format=%(refname)",
                                "refs/remotes/"]))

    # GREEN_ON_BASE(characterization): git answers both commands the same way on either tree.
    # The branch adds no ref and no reading git takes, so what this pins is the agreement the choice between them rests on.
    def test_Given_APublishedHead_When_BothCommandsAreAsked_Then_TheyAnswerTheSame(self):
        # Arrange / Act
        porcelain, plumbing = self.both(self.clone)

        # Assert — the answer rides in the comparison, so a pair that agreed on nothing at all
        # would not pass for agreement about a published head.
        self.assertEqual((porcelain, plumbing[1] != []), (plumbing, True))

    # GREEN_ON_BASE(characterization): the agreement holds where no ref reaches HEAD.
    # Same standing as the published case above.
    def test_Given_AnUnpublishedHead_When_BothCommandsAreAsked_Then_TheyAnswerTheSame(self):
        # Arrange
        commit(self.clone, "two")

        # Act
        porcelain, plumbing = self.both(self.clone)

        # Assert
        self.assertEqual((porcelain, plumbing[1]), (plumbing, []))

    # GREEN_ON_BASE(characterization): a detached HEAD is where the two were expected to part.
    # They do not, and that is the reading the guard's comment names this file for.
    def test_Given_ADetachedHead_When_BothCommandsAreAsked_Then_TheyAnswerTheSame(self):
        # Arrange — the case the two were expected to differ over.
        git(self.clone, "checkout", "-q", "--detach")

        # Act
        porcelain, plumbing = self.both(self.clone)

        # Assert
        self.assertEqual((porcelain, plumbing[1] != []), (plumbing, True))

    # GREEN_ON_BASE(characterization): a worktree gets one answer from both commands.
    # Where this repository's branch work happens, so it is the shape worth holding.
    def test_Given_AWorktree_When_BothCommandsAreAsked_Then_TheyAnswerTheSame(self):
        # Arrange
        worktree = self.root / "worktree"
        git(self.clone, "worktree", "add", "-q", "--detach", str(worktree), "origin/main")

        # Act
        porcelain, plumbing = self.both(worktree)

        # Assert
        self.assertEqual((porcelain, plumbing[1] != []), (plumbing, True))

    # GREEN_ON_BASE(characterization): both commands fail alike on an unborn HEAD.
    # The exit code is what the guard reads to decide it could not answer.
    def test_Given_AHeadGitCannotResolve_When_BothCommandsAreAsked_Then_TheyFailTheSameWay(self):
        # Arrange — an unborn HEAD, where a guard reading the exit code has to know which of the
        # two it is reading.
        unborn = self.root / "unborn"
        unborn.mkdir()
        git(unborn, "init", "-q", ".")

        # Act
        porcelain, plumbing = self.both(unborn)

        # Assert
        self.assertEqual((porcelain, plumbing[0] != 0), (plumbing, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

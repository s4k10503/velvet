#!/usr/bin/env python3
"""Unit tests for amend_of_published_commit.py.

Every case runs the guard as a process against a real pair of repositories, because what it decides
is a question put to git and a stubbed answer would be this file deciding it instead.

`PredicateAgreementTests` and `GitOptionGrammarTests` are the odd ones out: they hold git rather
than the guard. Either of two commands answers "does a remote-tracking ref reach HEAD", the guard
picks one, and the reason that choice is free is that they agree; and `lib/shell_commands.py` keeps
a table of the `git commit` options that swallow the token after them. Both are facts about git, so
they live here where they fail when they stop being true rather than in a sentence beside the call.

Run: python3 scripts/hooks/test_amend_of_published_commit.py
"""

import importlib.util
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
HOOK_LIBRARY = REPO_ROOT / ".claude/hooks/lib"

REFUSE = 2
ALLOW = 0

# The two headlines the guard writes. A refusal that could not read and one that read are different
# claims, and only one of them is entitled to say the commit is published.
PUBLISHED = "Refusing `git commit --amend`: this commit is already published."
UNREAD = "Refusing `git commit --amend`: git could not say whether this commit is published."

MAIN = "main"

# The machine's own default branch is forced to a name nothing here uses, so a `git init` whose
# branch matters has to say so and cannot pass on a working machine by accident. Left to the
# default, this fixture built a remote whose HEAD named a branch the seed never pushed and the
# clone came out unborn: nine cases failed on a CI runner and none here, since git ships `master`
# and this repository's machines are configured for `main`.
GIT_ENVIRONMENT = {
    "GIT_AUTHOR_NAME": "hooks", "GIT_AUTHOR_EMAIL": "hooks@velvet.test",
    "GIT_COMMITTER_NAME": "hooks", "GIT_COMMITTER_EMAIL": "hooks@velvet.test",
    "GIT_CONFIG_COUNT": "1",
    "GIT_CONFIG_KEY_0": "init.defaultBranch", "GIT_CONFIG_VALUE_0": "velvet-unnamed-default",
}


def git(cwd, *args):
    environment = dict(os.environ)
    environment.update(GIT_ENVIRONMENT)
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
        git(self.root, "init", "-q", "--bare", "-b", MAIN, "remote.git")
        git(seed, "init", "-q", "-b", MAIN, ".")
        commit(seed, "one")
        git(seed, "remote", "add", "origin", str(remote))
        git(seed, "push", "-q", "origin", f"HEAD:refs/heads/{MAIN}")
        self.clone = self.root / "clone"
        git(self.root, "clone", "-q", str(remote), "clone")
        # The guard is run from a directory that is not the event's, so a case posing a relative
        # selector measures the guard rather than wherever this file happened to be launched from.
        # A hook process here was measured sitting in the session's own directory while the tool
        # call it was handed ran in a worktree, which is the shape this reproduces.
        self.hook_home = self.root / "hookhome"
        self.hook_home.mkdir()

    def answer(self, command, cwd=None, hook_home=None):
        """(exit code, whatever the guard wrote) for one Bash command."""
        payload = json.dumps({"tool_name": "Bash", "cwd": str(cwd or self.clone),
                              "tool_input": {"command": command}})
        finished = subprocess.run([sys.executable, "-B", str(GUARD)], input=payload, text=True,
                                  capture_output=True, timeout=120,
                                  cwd=str(hook_home or self.hook_home))
        return finished.returncode, finished.stderr

    def verdict(self, command, cwd=None, hook_home=None):
        """The guard's exit code. Only for the commands it lets through — see `refused`."""
        return self.answer(command, cwd, hook_home)[0]

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

    def test_Given_AHeadAtTheRemoteTip_When_AnAbbreviatedAmendIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        answers = tuple(self.refused(f"git commit {flag}") for flag in ("--am", "--ame", "--amen"))

        # Assert
        self.assertEqual(answers, ((REFUSE, PUBLISHED),) * 3)

    def test_Given_AnAmendBehindAShortSigningFlag_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — `-S` takes a key id only attached, so it swallows nothing and `--amend` behind
        # it is the amend. Read as value-taking, this is the spelling anyone with `commit.gpgsign`
        # habits types, and it rewrote published history with no refusal.
        # Act
        answer = self.refused("git commit -S --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AnAmendBehindALongSigningFlag_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — the long spelling walks a different branch of the reader from the short one.
        # Act
        answer = self.refused("git commit --gpg-sign --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AnAmendBehindASigningFlagEndingAShortGroup_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — last in a short group is the position a value-taking flag reaches the next
        # token from, so it is the one a wrong table swallows `--amend` at.
        # Act
        answer = self.refused("git commit -aS --amend")

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

    def test_Given_AnAmendNamingTheTreeWithGitDir_When_ItIsPosed_Then_ThatTreeIsWhatIsRead(self):
        # Arrange — the same intent as `-C`, spelled the way a command reaching past its own cwd
        # spells it. Both halves ride in the comparison for the reason the `-C` case gives.
        outside = self.root / "outside"
        outside.mkdir()

        # Act
        text = self.refusal(f"git --git-dir={self.clone}/.git commit --amend", cwd=outside)

        # Assert
        self.assertEqual(("origin/main" in text, "could not say" in text), (True, False))

    def test_Given_AnAmendNamingTheTreeWithAGitDirAssignment_When_ItIsPosed_Then_ThatTreeIsRead(self):
        # Arrange — an environment assignment ahead of the command, which the reader walks past to
        # find the program.
        outside = self.root / "outside"
        outside.mkdir()

        # Act
        text = self.refusal(f"GIT_DIR={self.clone}/.git git commit --amend", cwd=outside)

        # Assert
        self.assertEqual(("origin/main" in text, "could not say" in text), (True, False))

    def test_Given_AGitDirRelativeToDashC_When_ItIsPosed_Then_ItResolvesAgainstDashC(self):
        # Arrange / Act — the git directory is relative, so it names the published repository only
        # once `-C` has been replayed too. Both halves ride in the comparison for the reason the
        # `-C` case above gives.
        text = self.refusal(f"git -C {self.root} --git-dir=clone/.git commit --amend")

        # Assert
        self.assertEqual(("origin/main" in text, "could not say" in text), (True, False))

    def test_Given_ARelativeDashCReachingAPublishedTree_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — `../decoy` names a published clone from the directory the command runs in, and
        # an unpublished repository from the one this hook process sits in. Resolved against the
        # latter, the amend of a published commit went through with no refusal at all.
        inner = self.root / "session" / "inner"
        inner.mkdir(parents=True)
        git(self.root / "session", "clone", "-q", str(self.root / "remote.git"), "decoy")
        elsewhere = self.root / "decoy"
        elsewhere.mkdir()
        git(elsewhere, "init", "-q", "-b", MAIN, ".")
        commit(elsewhere, "elsewhere")

        # Act
        code, text = self.answer("git -C ../decoy commit --amend", cwd=inner)

        # Assert — the SHA rides in the comparison because exit 2 is also what a reading that
        # answered nothing exits, and what a `python3` that could not open the guard exits; only the
        # SHA says the published tree is the one that was read.
        published = git(self.root / "session" / "decoy", "rev-parse", "--short", "HEAD").strip()
        self.assertEqual((code, published in text), (REFUSE, True))

    def test_Given_AnAmendCarryingBothDashCAndGitDir_When_GitDirIsPublished_Then_ItIsRefused(self):
        # Arrange
        worktree = self.root / "worktree"
        git(self.clone, "worktree", "add", "-q", "-b", "side", str(worktree))
        commit(worktree, "two")

        # Act
        answer = self.refused(f"git -C {worktree} --git-dir={self.clone}/.git commit --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

    def test_Given_AnAmendCarryingBothDashCAndGitDir_When_GitDirIsUnpublished_Then_ItIsAllowed(self):
        # Arrange
        unpublished = self.root / "unpublished"
        unpublished.mkdir()
        git(unpublished, "init", "-q", "-b", MAIN, ".")
        commit(unpublished, "one")

        # Act
        code = self.verdict(
            f"git -C {self.clone} --git-dir={unpublished}/.git commit --amend")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AnAmendBehindAnAttachedSigningFlag_When_ItIsPosed_Then_ItIsRefused(self):
        # Arrange — `--gpg-sign=<key>` is the spelling that does take a value, and it takes it
        # attached, so `--amend` behind it is still the amend.
        # Act
        answer = self.refused("git commit --gpg-sign=DEADBEEF --amend")

        # Assert
        self.assertEqual(answer, (REFUSE, PUBLISHED))

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

    def test_Given_ADashCNamingTheEventsOwnTree_When_TheHookSitsInAPublishedOne_Then_ItIsAllowed(self):
        # Arrange — `-C .` is the directory the command runs in, and this hook process sits in the
        # published clone. Read from there, the refusal named a SHA and a ref out of a repository
        # the command never touched.
        side = self.root / "side"
        side.mkdir()
        git(side, "init", "-q", "-b", MAIN, ".")
        commit(side, "two")
        published_elsewhere = self.clone

        # Act
        code = self.verdict("git -C . commit --amend", cwd=side, hook_home=published_elsewhere)

        # Assert — the hook home is read for the comparison rather than named again, since a home
        # swapped for one that answers nothing would otherwise leave this passing with the reading
        # still taken in the wrong place.
        self.assertEqual(
            (git(published_elsewhere, "branch", "-r", "--contains", "HEAD").strip() != "", code),
            (True, ALLOW))

    def test_Given_ARemoteThatMovedUnderAnUnpushedCommit_When_AnAmendIsPosed_Then_ItIsAllowed(self):
        # Arrange — a fetch that advanced origin/main past this branch. Nothing reaches HEAD, and
        # the refs the fetch brought in are what a reading over "is the branch behind" would take.
        seed = self.root / "seed"
        commit(seed, "upstream")
        git(seed, "push", "-q", "origin", f"HEAD:refs/heads/{MAIN}")
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

    def test_Given_TheNegatedAmendAbbreviation_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        codes = tuple(self.verdict(f"git commit {flag}")
                      for flag in ("--no-am", "--no-ame", "--no-amen", "--no-amend"))

        # Assert
        self.assertEqual(codes, (ALLOW,) * 4)

    def test_Given_TheAmbiguousAmendPrefix_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.verdict("git commit --a")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ATokenPastTheAmendSpelling_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — the case above poses the floor and this one the ceiling: read as any token
        # opening `--am`, both of these are amends, and the characterization below has git refusing
        # to run either.
        codes = tuple(self.verdict(f"git commit {flag}") for flag in ("--amendment", "--amend=1"))

        # Assert
        self.assertEqual(codes, (ALLOW,) * 2)

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

    def test_Given_ADashCInNoRepositoryBesideAReadableGitDir_When_ItIsRefused_Then_OnlyTheOneGitBlamedIsNamed(self):
        # Arrange — the git directory resolves on its own, so naming it beside the `-C` puts the
        # selector that was fine next to the one that was not. The headline rides along, since a
        # reading that took the git directory in place of `-C` reaches the clone and calls it
        # published.
        missing = self.root / "nonexistent"

        # Act
        text = self.refusal(f"git -C {missing} --git-dir={self.clone}/.git commit --amend")

        # Assert
        self.assertEqual(
            (str(missing) in text, f"--git-dir={self.clone}/.git" in text, "could not say" in text),
            (True, False, True))

    def test_Given_AGitDirNamingNoRepository_When_ItIsRefused_Then_GitsOwnMessageIsShown(self):
        # Arrange — the typo a contributor makes, from a directory with nothing wrong with it. git
        # says which selector it refused and why; the guard discarded that and printed a `-C` of
        # its own beside the one that was written.
        absent = self.root / "nonexistent"

        # Act
        detail = self.refusal(f"git --git-dir={absent} commit --amend").splitlines()[2]

        # Assert
        self.assertEqual(detail, f"  --git-dir={absent}: "
                                 f"fatal: not a git repository: '{absent}'")

    def test_Given_AGitDirNamingNoRepository_When_ItIsRefused_Then_NoDashCIsNamed(self):
        # Arrange — the command carries no `-C`, and a reading that supplies one from the event's
        # directory shows the contributor a selector they did not write.
        absent = self.root / "nonexistent"

        # Act
        text = self.refusal(f"git --git-dir={absent} commit --amend")

        # Assert — the headline rides along, since a run that refused for some other reason would
        # name no `-C` either.
        self.assertEqual(("-C" in text, text.splitlines()[0]), (False, UNREAD))

    def test_Given_AnUnbornHead_When_AnAmendIsRefused_Then_ItSaysThereIsNothingToAmend(self):
        # Arrange — no commit has been made, so there is nothing for an amend to replace. Sent to
        # look for a push instead, the reader goes looking for one nobody made.
        unborn = self.root / "unborn"
        unborn.mkdir()
        git(unborn, "init", "-q", "-b", MAIN, ".")

        # Act
        text = self.refusal("git commit --amend", cwd=unborn)

        # Assert
        self.assertIn("Commit first: that branch has nothing on it to amend.", text)

    def test_Given_AnUnreadableTree_When_AnAmendIsRefused_Then_ItDoesNotSayAnAmendIsAllowed(self):
        # Arrange — this path refuses whatever the answer would have been, so a footer saying an
        # unpushed amend is not refused is false exactly where it prints.
        outside = self.root / "outside"
        outside.mkdir()

        # Act
        text = self.refusal("git commit --amend", cwd=outside)

        # Assert — the headline rides along, since a refusal that read the tree carries no such
        # sentence either and would otherwise pass this.
        self.assertEqual(("is not refused" in text, text.splitlines()[0]), (False, UNREAD))

    def test_Given_AnUnexpandedTree_When_AnAmendIsRefused_Then_ItSaysTheShellHasNotRunYet(self):
        # Arrange / Act — the operand is still a variable, so no reading of it could have answered
        # about a repository whatever directory it was taken from.
        text = self.refusal("git -C $WORKTREE commit --amend")

        # Assert
        self.assertIn("a selector spelled with a variable is not a path yet", text)


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


class GitOptionGrammarTests(unittest.TestCase):
    """`COMMIT_VALUE_FLAGS` held to what git does with the token after each flag.

    Not about the guard: it reads the table. The table says which options swallow their neighbour,
    which is git's to decide, and a sentence beside it asserting so goes stale the day git changes
    its mind. This is where that fails instead.

    An option is posed by putting `--amend` behind it and asking whether the commit was replaced or
    a new one made. `-S` read as value-taking is what let `git commit -S --amend` through.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-grammar-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        git(self.root, "init", "-q", "-b", MAIN, ".")
        commit(self.root, "one")
        commit(self.root, "two")

    def amends_behind(self, *flag):
        """Whether git read `--amend` as the amend when it sits behind `flag`.

        Something is always staged first, so a commit that did not happen is the flag's doing and
        not an empty index.
        """
        before = git(self.root, "rev-list", "--count", "HEAD").strip()
        self.edits = getattr(self, "edits", 0) + 1
        (self.root / "two").write_text(f"edit {self.edits}", encoding="utf-8")
        git(self.root, "add", "two")
        finished = subprocess.run(
            ["git", "commit", *flag, "--amend", "--no-edit", "--no-gpg-sign"],
            cwd=self.root, capture_output=True, text=True, timeout=60,
            env={**os.environ, **GIT_ENVIRONMENT})
        after = git(self.root, "rev-list", "--count", "HEAD").strip()
        return finished.returncode == 0 and after == before

    # GREEN_ON_BASE(characterization): git reads `-S --amend` as an amend on either tree.
    # The branch changes a table that reads git, never git, so this answers the same on the base —
    # which is the point of it: what the branch got wrong was the reading, not the behaviour.
    def test_Given_TheShortSigningFlag_When_AnAmendFollowsIt_Then_GitReadsTheAmend(self):
        # Arrange / Act — swallowed as a key id, `--amend` would leave a third commit behind.
        amended = self.amends_behind("-S")

        # Assert
        self.assertTrue(amended)

    # GREEN_ON_BASE(characterization): the long spelling reads the same way on either tree.
    # Same standing as the short one above.
    def test_Given_TheLongSigningFlag_When_AnAmendFollowsIt_Then_GitReadsTheAmend(self):
        # Arrange / Act
        amended = self.amends_behind("--gpg-sign")

        # Assert
        self.assertTrue(amended)

    # GREEN_ON_BASE(characterization): the branch changes the guard's reading, not Git's grammar.
    def test_Given_EachUnambiguousAmendAbbreviation_When_ItIsPosed_Then_GitReadsTheAmend(self):
        # Arrange / Act
        amended = tuple(self.amends_behind(flag) for flag in ("--am", "--ame", "--amen"))

        # Assert
        self.assertEqual(amended, (True, True, True))

    # GREEN_ON_BASE(characterization): the rejected boundary is Git's on either tree.
    def test_Given_TheShorterAmendPrefix_When_ItIsPosed_Then_GitRejectsItAsAmbiguous(self):
        # Arrange / Act
        finished = subprocess.run(
            ["git", "commit", "--a", "--allow-empty", "--no-edit"], cwd=self.root,
            capture_output=True, text=True, timeout=60, env={**os.environ, **GIT_ENVIRONMENT})

        # Assert
        self.assertEqual((finished.returncode != 0, "ambiguous option" in finished.stderr),
                         (True, True))

    # GREEN_ON_BASE(characterization): git rejects a token past the amend spelling on either tree.
    # That is what lets the guard allow one. Same standing as the two above.
    def test_Given_ATokenPastTheAmendSpelling_When_ItIsPosed_Then_GitRejectsTheOption(self):
        # Arrange — what git said rides with the exit code for the reason the ambiguous case above
        # gives, and here it is load-bearing rather than tidy: `git commit --allow-empty --no-edit`
        # exits 1 on its own for want of a message, so an exit code alone reads the same under `-q`
        # as under either of these, and pins nothing about the amend spelling.
        posed = (("--amendment", "unknown option"), ("--amend=1", "takes no value"))

        # Act
        rejected = tuple(
            (finished.returncode != 0, marker in finished.stderr)
            for finished, marker in (
                (subprocess.run(["git", "commit", flag, "--allow-empty", "--no-edit"],
                                cwd=self.root, capture_output=True, text=True, timeout=60,
                                env={**os.environ, **GIT_ENVIRONMENT}), marker)
                for flag, marker in posed))

        # Assert
        self.assertEqual(rejected, ((True, True), (True, True)))

    # GREEN_ON_BASE(characterization): git takes `--amend` behind `-m` as the message on either tree.
    # Same standing as the two above.
    def test_Given_TheMessageFlag_When_AnAmendFollowsIt_Then_GitTakesItAsTheMessage(self):
        # Arrange / Act — the converse, without which the case above would pass on a git that read
        # no option at all as value-taking.
        amended = self.amends_behind("-m")

        # Assert
        self.assertFalse(amended)

    # GREEN_ON_BASE(characterization): every flag the table holds behaves this way on either tree.
    # Same standing as the three above.
    def test_Given_EveryFlagTheTableHolds_When_AnAmendFollowsIt_Then_GitDoesNotAmend(self):
        # Arrange — spelled here rather than read from the table, so a member the table loses is
        # still asked about. What each has to establish is only that `--amend` behind it is not the
        # amend; whether git swallowed it or rejected the line is the same answer for the reader.
        held = ["-m", "--message", "-F", "--file", "-c", "--reedit-message",
                "-C", "--reuse-message", "--fixup", "--squash", "--author", "--date",
                "-t", "--template", "--cleanup", "--trailer", "--pathspec-from-file"]

        # Act
        amended = sorted(flag for flag in held if self.amends_behind(flag))

        # Assert — the count rides along, since an empty list of flags amends nothing either.
        self.assertEqual((len(held), amended), (17, []))

    # GREEN_ON_BASE(characterization): the attached-only flags amend on either tree.
    # Same standing as the four above.
    def test_Given_EveryFlagValuedOnlyWhenAttached_When_AnAmendFollowsIt_Then_GitAmends(self):
        # Arrange — the flags that belong out of the table. `-u` was already out and is asked
        # alongside `-S`, since it is the member that showed the distinction was one the table
        # could express.
        attached_only = ["-S", "--gpg-sign", "-u", "--untracked-files"]

        # Act
        swallowed = sorted(flag for flag in attached_only if not self.amends_behind(flag))

        # Assert
        self.assertEqual((len(attached_only), swallowed), (4, []))

    def test_Given_TheTableAsItStands_When_EachMemberIsAskedOfGit_Then_GitSwallowsTheAmend(self):
        # Arrange — read from the table rather than spelled, which is the one direction the spelled
        # case above cannot cover: a flag wrongly added is asked about by no list anybody wrote, and
        # the guard then reads `git commit <flag> --amend` as carrying no amend at all. That is the
        # defect this branch fixed for `-S`, and `-u` sat one edit away from it.
        #
        # Imported here rather than at module scope: a tree whose `shell_commands.py` keeps no such
        # table would fail the import and take every case in this file down with it, and the base is
        # such a tree.
        sys.path.insert(0, str(HOOK_LIBRARY))
        from shell_commands import COMMIT_VALUE_FLAGS
        held = sorted(COMMIT_VALUE_FLAGS)

        # Act
        amended = sorted(flag for flag in held if self.amends_behind(flag))

        # Assert — the member count rides along, since an empty table amends nothing either.
        self.assertEqual((len(held) > 0, amended), (True, []))


class UnreadableCauseTests(unittest.TestCase):
    """What git writes when the guard's reading fails, which is what the refusal reads a cause from.

    Not about the guard: it matches git's own words rather than classifying the failure itself, so
    the message it prints names the selector git refused instead of both of them. The alternative
    was a sentence beside the matcher asserting that git says these things, and a sentence goes
    stale the day git changes its mind.
    """

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-cause-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.good = self.root / "good"
        git(self.root, "init", "-q", "-b", MAIN, "good")
        commit(self.good, "one")
        self.unborn = self.root / "unborn"
        self.unborn.mkdir()
        git(self.unborn, "init", "-q", "-b", MAIN, ".")
        self.plain = self.root / "plain"
        self.plain.mkdir()
        self.missing = self.root / "nonexistent"

    def read(self, *selectors, cwd=None):
        """What git wrote when the guard's own reading was taken with these selectors."""
        finished = subprocess.run(
            ["git", *selectors, "for-each-ref", "--contains", "HEAD", "--format=%(refname)",
             "refs/remotes/"],
            cwd=cwd or self.root, capture_output=True, text=True, timeout=60,
            env={**os.environ, **GIT_ENVIRONMENT})
        return finished.stderr

    # GREEN_ON_BASE(characterization): git quotes the working tree it could not enter, on either tree.
    # The branch reads that name out of the message rather than deciding it, so the reading is the
    # branch's and what it reads is not.
    def test_Given_ADashCGitCannotEnter_When_TheReadingIsTaken_Then_ItQuotesThatDirectory(self):
        # Arrange / Act
        said = self.read("-C", str(self.missing), f"--git-dir={self.good}/.git")

        # Assert — the selector that resolved rides in the comparison, since a message quoting both
        # would pick out neither.
        self.assertEqual((f"'{self.missing}'" in said, f"'{self.good}/.git'" in said),
                         (True, False))

    # GREEN_ON_BASE(characterization): git quotes the git directory it could not open, on either tree.
    # Same standing as the working-tree direction above, and posed because a guard naming one
    # selector has to name whichever of the two failed.
    def test_Given_AGitDirThatIsNoRepository_When_TheReadingIsTaken_Then_ItQuotesThatDirectory(self):
        # Arrange / Act
        said = self.read("-C", str(self.good), f"--git-dir={self.missing}")

        # Assert
        self.assertEqual((f"'{self.missing}'" in said, f"'{self.good}'" in said), (True, False))

    # GREEN_ON_BASE(characterization): an unborn HEAD is a malformed object name to git, on either tree.
    # It is the reading behind the refusal that says there is nothing to amend yet.
    def test_Given_AnUnbornHead_When_TheReadingIsTaken_Then_GitCallsTheObjectNameMalformed(self):
        # Arrange / Act
        said = self.read(cwd=self.unborn)

        # Assert
        self.assertIn("malformed object name HEAD", said)

    # GREEN_ON_BASE(characterization): git says a directory in no repository is not one, on either tree.
    # It is the reading behind the refusal that says to pose the amend from inside one.
    def test_Given_ADirectoryInNoRepository_When_TheReadingIsTaken_Then_GitSaysItIsNotOne(self):
        # Arrange — whether git placed the directory rides in the comparison, since a temporary path
        # that turned out to sit inside some repository would answer and pin nothing.
        placed = subprocess.run(["git", "rev-parse", "--git-common-dir"], cwd=self.plain,
                                capture_output=True, text=True, timeout=60).returncode == 0

        # Act
        said = self.read(cwd=self.plain)

        # Assert
        self.assertEqual((placed, "not a git repository" in said), (False, True))

    def test_Given_TheMarkerTableAsItStands_When_EachIsPosed_Then_GitWritesThatMarker(self):
        # Arrange — read from the guard rather than spelled, which is the one direction the cases
        # above cannot cover: a marker added without a measurement is asked about by no list anybody
        # wrote, and the action beside it is then printed for a cause git never reports.
        #
        # Imported here rather than at module scope, for the reason `GitOptionGrammarTests` gives of
        # the option table: a tree without the guard would fail the import and take every case in
        # this file down with it.
        specification = importlib.util.spec_from_file_location("amend_guard", GUARD)
        guard = importlib.util.module_from_spec(specification)
        specification.loader.exec_module(guard)
        said = (self.read(cwd=self.unborn), self.read("-C", str(self.missing)),
                self.read(cwd=self.plain))
        markers = [marker for marker, _ in guard.UNREADABLE_ACTIONS]

        # Act
        unmeasured = [marker for marker in markers
                      if not any(marker in message for message in said)]

        # Assert — the table being non-empty rides along, since an empty one leaves nothing
        # unmeasured either.
        self.assertEqual((len(markers) > 0, unmeasured), (True, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)

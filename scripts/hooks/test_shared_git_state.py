#!/usr/bin/env python3
"""Unit tests for the one move .claude/hooks/refuse/shared_git_state.py lets out.

The guard refuses `checkout`, `switch` and `stash` because they retarget state another worktree is
standing on. With nothing exempted two rules of this repository contradicted each other:
`scripts/pr/settle.py` will not merge a pull request while a worktree holds its branch, and the
command that moves a worktree off a branch was the one refused. So a checkout of the branch git
records as the default is allowed, and the cases here separate that move from what still takes the
refusal.

The fixture's base is `trunk` and it carries an ordinary branch called `main` beside it, so a guard
that spelled the base out instead of reading git's record answers both of those the wrong way round.

Two cases pose git rather than the guard. What the exemption rests on — that git declines a checkout
which would lose a local change, and declines a branch a second worktree already holds — is a fact
about git rather than about this repository, and a comment asserting it would go stale with nothing
failing. They are here instead.

Run: python3 scripts/hooks/test_shared_git_state.py
"""

import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/shared_git_state.py"

REFUSED = 2
ALLOWED = 0

# The fixture's own base, chosen so no case can pass against a guard that spells `main` out.
BASE = "trunk"

# A legal branch name that is also the shell's spelling of a substitution.
VARIABLE = "$BRANCH"

# `--template=` keeps a template directory on the machine running this out of the fixture, and the
# identity lets the commits below succeed on a machine that has configured none.
NEUTRAL = ("-c", "user.email=t@velvet", "-c", "user.name=t", "-c", "commit.gpgsign=false")


def git(cwd, *arguments):
    """A failed arrangement is not a verdict, so one must not reach an assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *arguments], check=True, capture_output=True,
                          text=True, timeout=60)


def ask(command, cwd):
    """What the guard does with `command`, as the tool call would reach it."""
    event = {"tool_name": "Bash", "cwd": str(cwd), "tool_input": {"command": command}}
    return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                          capture_output=True, text=True, timeout=120)


def seeded_remote(root):
    """A bare repository holding `trunk`, `main`, `feat/x` and `sibling`, all at one commit."""
    remote = root / "origin.git"
    subprocess.run(["git", "init", "-q", "--bare", "--template=", "-b", BASE, str(remote)],
                   check=True, capture_output=True, timeout=60)
    seed = root / "seed"
    subprocess.run(["git", "init", "-q", "--template=", "-b", BASE, str(seed)],
                   check=True, capture_output=True, timeout=60)
    (seed / "carried.txt").write_text("carried\n", encoding="utf-8")
    (seed / "differs.txt").write_text("base\n", encoding="utf-8")
    git(seed, "add", "carried.txt", "differs.txt")
    git(seed, *NEUTRAL, "commit", "-qm", "root")
    git(seed, "remote", "add", "origin", str(remote))
    for ref in (BASE, f"{BASE}:main", f"{BASE}:feat/x", f"{BASE}:sibling"):
        git(seed, "push", "-q", "origin", ref)
    shutil.rmtree(seed)
    return remote


class BaseReturnTests(unittest.TestCase):
    """A worktree standing on a feature branch cut from `trunk`, which git records as the default."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-"))
        remote = seeded_remote(self.root)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        git(self.repo, "branch", "main", "origin/main")
        git(self.repo, "branch", "feat/x", "origin/feat/x")
        git(self.repo, "branch", "sibling", "origin/sibling")
        git(self.repo, "checkout", "-q", "-b", "work")
        self.other = self.root / "other"
        git(self.repo, "worktree", "add", "-q", "-b", "held", str(self.other))

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_Given_TheBranchGitRecordsAsTheBase_When_ItIsCheckedOut_Then_ItIsAllowed(self):
        # Arrange — which branch the tree stands on rides in the comparison, because from the base
        # itself the command moves nothing and the case would then be named for a deadlock it never
        # posed.
        standing_on = git(self.repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()

        # Act
        result = ask(f"git checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, standing_on), (ALLOWED, "work"))

    def test_Given_TheBranchGitRecordsAsTheBase_When_ItIsSwitchedTo_Then_ItIsAllowed(self):
        # Arrange — `git switch` is the same move as the checkout above, and leaving one refused
        # while the other is allowed would be a distinction the caller cannot act on.
        # Act
        result = ask(f"git switch {BASE}", self.repo)

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def test_Given_ATrackedFileModified_When_TheRecordedBaseIsCheckedOut_Then_ItIsAllowed(self):
        # Arrange — a dirty tree, which is what a cleanliness rule here would have to judge and this
        # guard declines to; the modification git itself declines is posed in
        # `GitOwnCheckoutRefusalTests`. Whether the tree is really dirty rides in the comparison:
        # without it the case passes over a clean tree, and is then named for a condition nothing
        # here set up.
        (self.repo / "carried.txt").write_text("uncommitted\n", encoding="utf-8")
        modified = bool(git(self.repo, "status", "--porcelain").stdout.strip())

        # Act
        result = ask(f"git checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, modified), (ALLOWED, True))

    def test_Given_AMoveIntoTheWorktreeAheadOfIt_When_TheBaseIsCheckedOut_Then_ItIsAllowed(self):
        # Arrange — the tool call starts outside the repository, which is the shape a session types
        # when it names a worktree once and moves into it. The base has to be read where the command
        # will run, not where the call started.
        # Act
        result = ask(f"cd {self.repo} && git checkout {BASE}", self.root)

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def test_Given_ARefusedCheckout_When_TheBaseIsRecorded_Then_TheRefusalNamesTheBranchToReturnTo(self):
        # Arrange — an exit nobody is told about deadlocks a reader exactly as no exit would.
        # Act
        result = ask("git checkout sibling", self.repo)

        # Assert
        self.assertIn(f"`git checkout {BASE}`", result.stderr)

    # GREEN_ON_BASE(characterization): the exemption is keyed on the branch git records as the base
    # rather than on the name `main`, and this is the case that says so — the fixture's base is
    # `trunk`, so a guard reaching for a literal lets this through while refusing the case above it.
    def test_Given_ABranchNamedMainThatIsNotTheRecordedBase_When_ItIsCheckedOut_Then_ItIsRefused(self):
        # Act
        result = ask("git checkout main", self.repo)

        # Assert
        self.assertEqual(result.returncode, REFUSED)

    # GREEN_ON_BASE(characterization): a feature branch stays refused, which is what the widening
    # must not take with it. The base refuses it for the reason it refuses every branch operand, and
    # a run there cannot tell that apart from the exemption declining to fire.
    def test_Given_AFeatureBranch_When_ItIsCheckedOut_Then_ItIsRefused(self):
        # Act
        result = ask("git checkout feat/x", self.repo)

        # Assert
        self.assertEqual(result.returncode, REFUSED)

    # GREEN_ON_BASE(characterization): a flag keeps the refusal however the operand is spelled.
    # `-b` carrying the base's own name is the spelling an exemption reading only the non-flag
    # operands would let through, and it creates a branch rather than moving onto one.
    def test_Given_ABranchCreationCarryingTheBaseName_When_ItIsCheckedOut_Then_ItIsRefused(self):
        # Act
        result = ask(f"git checkout -b {BASE}", self.repo)

        # Assert
        self.assertEqual(result.returncode, REFUSED)

    # GREEN_ON_BASE(characterization): a detach at the base is not a return to it.
    # The exemption declines every flag rather than the ones that create a branch, so this is the
    # case a narrower reading of which flags are harmless would let through.
    def test_Given_ADetachCarryingTheBaseName_When_ItIsCheckedOut_Then_ItIsRefused(self):
        # Act
        result = ask(f"git checkout --detach {BASE}", self.repo)

        # Assert
        self.assertEqual(result.returncode, REFUSED)

    # GREEN_ON_BASE(characterization): reaching into another worktree keeps the refusal.
    # Leaving your own branch is what you typed; a `-C` leaves somebody else's, which is the harm
    # the rest of this guard exists to stop.
    def test_Given_AnotherWorktreeNamedByDashC_When_TheBaseIsCheckedOutThere_Then_ItIsRefused(self):
        # Arrange — what stands at the end of the `-C`. It rides in the comparison because the guard
        # answers off the token, so a case pointing at nothing would read the same and be named for
        # a worktree that was never there.
        standing_on = git(self.other, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()

        # Act
        result = ask(f"git -C {self.other} checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, standing_on), (REFUSED, "held"))

    # GREEN_ON_BASE(characterization): a git directory named on the command keeps the refusal too.
    # It is the other spelling of the case above, and an exemption reading only the working
    # directory a command names would refuse one of the pair and allow the other.
    def test_Given_AGitDirectoryNamedOnTheCommand_When_TheBaseIsCheckedOut_Then_ItIsRefused(self):
        # Arrange — a real git directory rather than a path that only looks like one, for the reason
        # the case above gives about what the `-C` points at.
        named = git(self.root / "origin.git", "rev-parse", "--is-bare-repository").stdout.strip()

        # Act
        result = ask(f"git --git-dir={self.root}/origin.git checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, named), (REFUSED, "true"))

    # GREEN_ON_BASE(characterization): a stash refusal offers no move out, since a stash is not a
    # move onto a branch. Remove the `refused == "stash"` term and the refusal starts advertising a
    # branch move as the remedy for something that never asked for one.
    def test_Given_AStash_When_ItIsRefused_Then_NoMoveOutIsOffered(self):
        # Arrange — the subcommand this change left alone, whose refusal shares its text with the
        # one that did change. The verdict rides in the comparison so that a stash the guard stopped
        # refusing would not read here as a stash it merely stopped advising.
        # Act
        result = ask("git stash", self.repo)

        # Assert
        self.assertEqual((result.returncode, f"`git checkout {BASE}`" in result.stderr),
                         (REFUSED, False))

    # GREEN_ON_BASE(characterization): an operand the shell has not expanded keeps the refusal.
    # The exemption compares literal text against the recorded name, so this is what fails if a
    # later reading resolves the operand before comparing it.
    def test_Given_AnOperandTheShellHasNotExpanded_When_ItIsCheckedOut_Then_ItIsRefused(self):
        # Act
        result = ask("git checkout $BRANCH", self.repo)

        # Assert
        self.assertEqual(result.returncode, REFUSED)


class UnrecordedBaseTests(unittest.TestCase):
    """A repository whose remote HEAD is not recorded, which is what `git remote set-head -d` leaves
    and what the guard has no base to read in."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-none-"))
        remote = seeded_remote(self.root)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        git(self.repo, "branch", "main", "origin/main")
        git(self.repo, "checkout", "-q", "-b", "work")
        git(self.repo, "remote", "set-head", "origin", "-d")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_Given_ARemoteHeadThatIsNotRecorded_When_ABranchIsCheckedOut_Then_ItIsRefusedSayingSo(self):
        # Arrange — the reader has to tell a branch that is not the base from a repository where
        # nothing said what the base is, since the two need different things done about them. The
        # verdict rides in the same comparison because a fail-closed answer pinned by a substring of
        # prose alone is one rewording away from pinning nothing.
        # Act
        result = ask("git checkout main", self.repo)

        # Assert
        self.assertEqual((result.returncode, "git remote set-head origin -a" in result.stderr),
                         (REFUSED, True))


class VariableNamedBaseTests(unittest.TestCase):
    """A repository whose recorded default branch is spelled the way a shell variable is, so the
    literal a command carries and the branch it will reach come apart."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-var-"))
        remote = self.root / "origin.git"
        subprocess.run(["git", "init", "-q", "--bare", "--template=", "-b", VARIABLE, str(remote)],
                       check=True, capture_output=True, timeout=60)
        seed = self.root / "seed"
        subprocess.run(["git", "init", "-q", "--template=", "-b", VARIABLE, str(seed)],
                       check=True, capture_output=True, timeout=60)
        (seed / "carried.txt").write_text("carried\n", encoding="utf-8")
        git(seed, "add", "carried.txt")
        git(seed, *NEUTRAL, "commit", "-qm", "root")
        git(seed, "remote", "add", "origin", str(remote))
        git(seed, "push", "-q", "origin", VARIABLE)
        shutil.rmtree(seed)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        git(self.repo, "checkout", "-q", "-b", "work")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    # GREEN_ON_BASE(characterization): an operand the shell will rewrite keeps the refusal even where
    # its literal text is the recorded branch's own name. Remove the `unexpanded` term from
    # `returns_to_base` and this is what lets a substitution through under a name it never had.
    def test_Given_ABaseBranchSpeltLikeAVariable_When_ThatLiteralIsCheckedOut_Then_ItIsRefused(self):
        # Arrange — git records the operand's own text as the default branch, which is what a
        # comparison of literals reads as a match.
        recorded = git(self.repo, "symbolic-ref", "refs/remotes/origin/HEAD").stdout.strip()

        # Act
        result = ask(f"git checkout {VARIABLE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, recorded),
                         (REFUSED, f"refs/remotes/origin/{VARIABLE}"))


class GitOwnCheckoutRefusalTests(unittest.TestCase):
    """What git declines on its own, which is why the guard declines to have a second opinion."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-git-"))
        remote = seeded_remote(self.root)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        git(self.repo, "checkout", "-q", "-b", "work")
        (self.repo / "differs.txt").write_text("work\n", encoding="utf-8")
        git(self.repo, "add", "differs.txt")
        git(self.repo, *NEUTRAL, "commit", "-qm", "work")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def attempt(self, *arguments):
        return subprocess.run(["git", "-C", str(self.repo), *arguments], capture_output=True,
                              text=True, timeout=60)

    # GREEN_ON_BASE(characterization): git refuses a plain checkout that would lose a local change.
    # That is why the guard holds no opinion about a clean tree, and it is git's behaviour rather
    # than this branch's, so the base holds it too.
    def test_Given_ATrackedFileTheBaseWouldOverwrite_When_TheBaseIsCheckedOut_Then_GitRefuses(self):
        # Arrange — `differs.txt` is committed differently on each branch, so the modification below
        # cannot be carried across and git has to decide between the two.
        (self.repo / "differs.txt").write_text("uncommitted\n", encoding="utf-8")

        # Act
        result = self.attempt("checkout", BASE)

        # Assert
        self.assertIn("would be overwritten by checkout", result.stderr)

    # GREEN_ON_BASE(characterization): git refuses a branch a second worktree already holds.
    # The exemption rests on that: it can move a worktree onto the base, and the collision two
    # worktrees on one branch would cause is git's refusal rather than a rule written here.
    def test_Given_ABranchAnotherWorktreeHolds_When_ItIsCheckedOut_Then_GitRefuses(self):
        # Arrange — a second worktree standing on the base, which is where a return to it lands.
        git(self.repo, "worktree", "add", "-q", str(self.root / "parked"), BASE)

        # Act
        result = self.attempt("checkout", BASE)

        # Assert
        self.assertIn("is already used by worktree at", result.stderr)


if __name__ == "__main__":
    unittest.main()

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

Some cases pose git rather than the guard. What the exemption rests on — that git declines a checkout
which would lose a local change, declines a branch a second worktree already holds, lists the primary
working tree first, declines to remove that tree even under `--force`, and under a bare `--git-dir`
moves a repository's HEAD without moving its files — is a fact about git rather than about this
repository, and a comment asserting any of it would go stale with nothing failing. Those cases are
here instead, in the `GitOwn...` classes below.

Run: python3 scripts/hooks/test_shared_git_state.py
"""

import json
import os
import shlex
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

# A legal branch name, and a legal directory name, that is also the shell's spelling of a
# substitution.
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

    # GREEN_ON_BASE(characterization): a `-C` onto a linked worktree keeps the refusal.
    # Those are the trees this repository hands to agents, so one is likely to have somebody
    # standing in it; exempt every `-C` rather than the primary alone and this tree gets moved.
    def test_Given_AnotherWorktreeNamedByDashC_When_TheBaseIsCheckedOutThere_Then_ItIsRefused(self):
        # Arrange — what stands at the end of the `-C`. It rides in the comparison because the guard
        # answers off the token, so a case pointing at nothing would read the same and be named for
        # a worktree that was never there.
        standing_on = git(self.other, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()

        # Act
        result = ask(f"git -C {self.other} checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, standing_on), (REFUSED, "held"))

    def test_Given_ThePrimaryCheckoutNamedByDashC_When_TheBaseIsCheckedOutThere_Then_ItIsAllowed(self):
        # Arrange — the call runs from the linked worktree and reaches into the primary, the shape a
        # caller types who is standing in their own tree and has to free the primary's branch. Which
        # branch the primary stands on rides in the comparison: from the base itself the command
        # frees nothing, and the case would then be named for a deadlock it never posed.
        standing_on = git(self.repo, "rev-parse", "--abbrev-ref", "HEAD").stdout.strip()

        # Act
        result = ask(f"git -C {self.repo} checkout {BASE}", self.other)

        # Assert
        self.assertEqual((result.returncode, standing_on), (ALLOWED, "work"))

    def test_Given_ThePrimaryCheckoutNamedByDashC_When_TheBaseIsSwitchedToThere_Then_ItIsAllowed(self):
        # Arrange — `git switch` reaches `returns_to_base` by the same call as the checkout above,
        # and a widening that took one and not the other would be a distinction the caller cannot
        # act on.
        # Act
        result = ask(f"git -C {self.repo} switch {BASE}", self.other)

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    # GREEN_ON_BASE(characterization): another repository's primary keeps the refusal.
    # The listing the exemption reads is taken in the tree the caller stands in, so a primary
    # outside this repository's worktrees is in no listing it takes; read the listing in the tree
    # the `-C` names instead and this is the stranger's checkout that gets moved.
    def test_Given_APrimaryOfAnotherRepository_When_TheBaseIsCheckedOutThere_Then_ItIsRefused(self):
        # Arrange — a second clone, whose own listing calls it a primary. That reading rides in the
        # comparison, because a `-C` onto a path that was no worktree at all would be refused for a
        # reason this case is not about.
        foreign = self.root / "foreign"
        subprocess.run(["git", "clone", "-q", "--template=", str(self.root / "origin.git"),
                        str(foreign)], check=True, capture_output=True, timeout=60)
        listed = git(foreign, "worktree", "list", "--porcelain").stdout.splitlines()[0]

        # Act
        result = ask(f"git -C {foreign} checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, listed),
                         (REFUSED, f"worktree {os.path.realpath(foreign)}"))

    # GREEN_ON_BASE(characterization): a git directory named on the command keeps the refusal.
    # `GitOwnGitDirectoryTests` holds what such a command does instead of returning a tree to the
    # base, which is why the `-C` widening stops short of this spelling.
    def test_Given_AGitDirectoryNamedOnTheCommand_When_TheBaseIsCheckedOut_Then_ItIsRefused(self):
        # Arrange — a real git directory rather than a path that only looks like one, so the case
        # cannot be named for a git directory that was never there.
        named = git(self.root / "origin.git", "rev-parse", "--is-bare-repository").stdout.strip()

        # Act
        result = ask(f"git --git-dir={self.root}/origin.git checkout {BASE}", self.repo)

        # Assert
        self.assertEqual((result.returncode, named), (REFUSED, "true"))

    # GREEN_ON_BASE(characterization): the environment's spelling of it keeps the refusal too.
    # One term in `returns_to_base` answers both spellings, and a widening that read only the flag
    # would refuse one of the pair and exempt the other.
    def test_Given_AGitDirectoryNamedByTheEnvironment_When_TheBaseIsCheckedOut_Then_ItIsRefused(self):
        # Arrange — the directory the flag case names, so that the two differ in the spelling and in
        # nothing else, and neither can be named for a git directory that was never there.
        named = git(self.root / "origin.git", "rev-parse", "--is-bare-repository").stdout.strip()

        # Act
        result = ask(f"GIT_DIR={self.root}/origin.git git checkout {BASE}", self.repo)

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


def clone_at(root, name):
    """A clone of a freshly seeded remote at `root/name`, standing on `work`, with a linked worktree
    called `other` beside it — the two ends of a `-C` that reaches from a linked tree to the primary.
    """
    remote = seeded_remote(root)
    repo = root / name
    subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(repo)],
                   check=True, capture_output=True, timeout=60)
    git(repo, "checkout", "-q", "-b", "work")
    other = root / "other"
    git(repo, "worktree", "add", "-q", "-b", "held", str(other))
    return repo, other


class NewlineNamedPathTests(unittest.TestCase):
    """A repository whose primary checkout sits at a path holding a newline, which is the byte a
    listing read line by line would end the path on."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-newline-"))
        self.repo, self.other = clone_at(self.root, "nl\nname")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def test_Given_APrimaryAtAPathHoldingANewline_When_ItIsNamedByDashC_Then_ItIsAllowed(self):
        # Arrange — that the path really holds the byte rides in the comparison, because a fixture
        # whose directory name had lost it would read here as a listing delimited correctly.
        holds_one = "\n" in str(self.repo)

        # Act
        result = ask(f"git -C {shlex.quote(str(self.repo))} checkout {BASE}", self.other)

        # Assert
        self.assertEqual((result.returncode, holds_one), (ALLOWED, True))


class VariableNamedPathTests(unittest.TestCase):
    """A repository whose primary checkout sits at a path spelled the way a shell variable is, so the
    `-C` literal a command carries and the directory it will reach come apart."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-path-"))
        self.repo, self.other = clone_at(self.root, VARIABLE)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    # GREEN_ON_BASE(characterization): a `-C` the shell will rewrite keeps the refusal.
    # Its literal text resolves to the primary checkout here, so removing the `unexpanded` term from
    # `aims_at_primary` exempts a command whose substitution reaches somewhere else entirely.
    def test_Given_APrimaryAtAPathSpeltLikeAVariable_When_ThatLiteralIsNamedByDashC_Then_ItIsRefused(self):
        # Arrange — git lists the operand's own text as the primary, which is what a comparison of
        # resolved literals reads as a match.
        listed = git(self.other, "worktree", "list", "--porcelain").stdout.splitlines()[0]

        # Act
        result = ask(f"git -C {self.repo} checkout {BASE}", self.other)

        # Assert
        self.assertEqual((result.returncode, listed),
                         (REFUSED, f"worktree {os.path.realpath(self.repo)}"))


class GitOwnPrimaryWorktreeTests(unittest.TestCase):
    """What git says about the primary working tree: which record of a listing it is, and that git
    declines to remove it — so freeing its branch is a move onto another branch, not a removal."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-primary-"))
        remote = seeded_remote(self.root)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        self.other = self.root / "other"
        git(self.repo, "worktree", "add", "-q", "-b", "held", str(self.other))

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    # GREEN_ON_BASE(characterization): the listing puts the primary first.
    # That is git's behaviour rather than this branch's, so the base holds it too. Read the last
    # record rather than the first and the exemption starts answering about a linked worktree.
    def test_Given_ALinkedWorktreeSortingAheadOfIt_When_TheListingIsTaken_Then_ThePrimaryIsFirst(self):
        # Arrange — `other` sorts ahead of `repo`, so a listing ordered by path alone answers this
        # the other way round. Taken from the linked worktree, where a caller who needs the primary
        # freed is standing.
        # Act
        listed = [line.split(" ", 1)[1] for line
                  in git(self.other, "worktree", "list", "--porcelain").stdout.splitlines()
                  if line.startswith("worktree ")]

        # Assert
        self.assertEqual((listed[0], len(listed)), (os.path.realpath(self.repo), 2))

    # GREEN_ON_BASE(characterization): git declines to remove the primary working tree.
    # `--force` does not reach it either. That is git's behaviour rather than this branch's, so the
    # base holds it too, and it is why freeing that branch has to be a move onto another one.
    def test_Given_ThePrimaryWorkingTree_When_ItsRemovalIsForced_Then_GitRefuses(self):
        # Arrange — asked from the linked worktree, which is where the caller who needs the primary
        # freed is standing.
        # Act
        result = subprocess.run(["git", "-C", str(self.other), "worktree", "remove", "--force",
                                 str(self.repo)], capture_output=True, text=True, timeout=60)

        # Assert
        self.assertIn("is a main working tree", result.stderr)


class GitOwnGitDirectoryTests(unittest.TestCase):
    """What a `--git-dir` with no `--work-tree` beside it does, which is why the exemption stops at
    `-C` rather than taking every spelling that names a tree."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-shared-state-dir-"))
        remote = seeded_remote(self.root)
        self.repo = self.root / "repo"
        subprocess.run(["git", "clone", "-q", "--template=", str(remote), str(self.repo)],
                       check=True, capture_output=True, timeout=60)
        git(self.repo, "checkout", "-q", "-b", "work")
        (self.repo / "differs.txt").write_text("work\n", encoding="utf-8")
        git(self.repo, "add", "differs.txt")
        git(self.repo, *NEUTRAL, "commit", "-qm", "work")
        self.elsewhere = self.root / "elsewhere"
        self.elsewhere.mkdir()

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    # GREEN_ON_BASE(characterization): a bare `--git-dir` leaves the named tree's files behind.
    # It moves that repository's HEAD and index and returns no working tree to the base, so there is
    # nothing for the `-C` exemption to be asked about. Git's behaviour, so the base holds it too.
    def test_Given_AGitDirectoryWithNoWorkTree_When_TheBaseIsCheckedOut_Then_TheNamedTreeIsLeftBehind(self):
        # Arrange — `differs.txt` is committed differently on each branch, so a checkout that took
        # the named tree with it would leave the status below with nothing to report.
        # Act
        subprocess.run(["git", f"--git-dir={self.repo}/.git", "checkout", BASE],
                       cwd=str(self.elsewhere), capture_output=True, text=True, timeout=60)

        # Assert
        self.assertEqual(git(self.repo, "status", "--porcelain").stdout, " M differs.txt\n")


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

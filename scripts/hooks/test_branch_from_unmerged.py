#!/usr/bin/env python3
"""Which `git worktree add` spellings .claude/hooks/refuse/branch_from_unmerged.py reads as creating.

`BranchGuardParsingTests` holds the same table for `checkout`, `switch` and `branch`. This subcommand
is here instead because its operands answer differently: the path and the start point are two
positionals in either order around the flags, where the other three name the branch on a flag and
take a start point nowhere else. Read the way a flag's value is read, `git worktree add -b topic
../tree` names `../tree` as an explicit start point — an allowance — and the branch-from-the-tip
shape the guard exists to stop passes through the guard's own exemption.

The guard's failure mode is silence: a spelling it does not recognise exits 0 with no output, which
is what a guard with nothing to say also does. So the table asserts the reading, and a case beside it
asks the whole guard, since a reading nothing acts on refuses nothing.

`UndecodableHeadTests` holds the guard over a HEAD git names with a byte UTF-8 does not map. A strict
decode raised there, and the hook exited 1, which lets the creation through.

`UnreadHeadTests` holds it where git reads no HEAD at all: the refusal named a detached HEAD at an
empty SHA there, and a deferred creation recorded a base with no commit in it.

Run: python3 scripts/hooks/test_branch_from_unmerged.py
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
HOOK = REPO_ROOT / ".claude/hooks/refuse/branch_from_unmerged.py"

REFUSED = 2
ALLOWED = 0

UNDECODABLE_BRANCH = b"bad\xffbranch"
PROBE_BRANCH = "tooling/undecodable-head-probe"

# What a reading that named no creation at all answers with, so a table row records that rather than
# a case dying on an empty list — an exception is not a disagreement, here or to base_red_check.py.
NOTHING_READ = "no creation read"

# The comma-joined names the guard should extract, empty for no creation.
TABLE = (
    ("git worktree add ../tree -b topic", "topic"),
    ("git worktree add -b topic ../tree", "topic"),
    ("git worktree add -btopic ../tree", "topic"),
    ("git worktree add -B topic ../tree", "topic"),
    ("git worktree add -f --lock -b topic ../tree", "topic"),
    ("git worktree add --reason 'held for review' -b topic ../tree", "topic"),
    ("git worktree add \"../tree\" -b \"topic\"", "topic"),
    ("git -C /elsewhere worktree add -b topic ../tree", "topic"),
    ("git worktree add ../tree -b first && git worktree add ../other -b second", "first,second"),

    ("git worktree add ../tree", ""),
    ("git worktree add ../tree origin/main", ""),
    ("git worktree add --detach ../tree", ""),
    ("git worktree add --orphan -b topic ../tree", ""),
    ("git worktree list", ""),
    ("git worktree remove --force ../tree", ""),
    ("git commit -m \"git worktree add ../tree -b nope\"", ""),
)

# The start point each of these names, or None where it names none. The table above cannot say this:
# a spelling read as creating from HEAD and one read as creating from a named commit both answer with
# the branch name, and which of the two it is decides the verdict.
START_POINTS = (
    ("git worktree add ../tree -b topic", None),
    ("git worktree add -b topic ../tree", None),
    ("git worktree add ../tree -b topic origin/main", "origin/main"),
    ("git worktree add -b topic ../tree origin/main", "origin/main"),
    ("git worktree add --reason 'held' -b topic ../tree origin/main", "origin/main"),
)


def load_guard():
    """Imports the hook by path, since .claude holds no packages."""
    spec = importlib.util.spec_from_file_location("branch_guard", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


guard = load_guard()


def git(cwd, *args):
    """A failed arrangement is not a verdict, so one must not reach the assertion as an answer."""
    return subprocess.run(["git", "-C", str(cwd), *args], check=True, capture_output=True,
                          text=True, timeout=60)


def commit(cwd, message):
    git(cwd, "-c", "user.email=t@velvet", "-c", "user.name=t", "commit", "-q", "--allow-empty",
        "-m", message)


class WorktreeCreationTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-branch-guard-"))
        self.origin = self.root / "origin.git"
        self.project = self.root / "project"
        self.wt = self.root / "wt"
        subprocess.run(["git", "init", "-q", "--bare", str(self.origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "remote", "add", "origin", str(self.origin))
        commit(self.project, "first")
        first = git(self.project, "rev-parse", "HEAD").stdout.strip()
        commit(self.project, "work that never landed")
        git(self.project, "branch", "feature")
        git(self.project, "reset", "-q", "--hard", first)
        git(self.project, "push", "-q", "origin", "main")
        git(self.project, "fetch", "-q", "origin")
        git(self.project, "worktree", "add", "-q", str(self.wt), "feature")

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def ask(self, command):
        """What the guard does with `command` run in the worktree on unmerged work."""
        event = {"tool_name": "Bash", "cwd": str(self.wt), "tool_input": {"command": command}}
        environment = dict(os.environ)
        environment["CLAUDE_PROJECT_DIR"] = str(self.project)
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def test_Given_TheWorktreeAddTable_When_TheGuardParsesEach_Then_ItNamesTheBranchesCreated(self):
        # Arrange / Act
        read = [(command, ",".join(made[0] for made in guard.creations(command)))
                for command, _ in TABLE]

        # Assert
        self.assertEqual([row for row in read if row not in TABLE], [],
                         "a spelling the guard does not read as a creation is refused by nothing")

    def test_Given_TheWorktreeAddTable_When_TheGuardParsesEach_Then_ItNamesTheStartPointsGiven(self):
        # Arrange / Act
        read = [(command, next((made[1] for made in guard.creations(command)), NOTHING_READ))
                for command, _ in START_POINTS]

        # Assert — a path read as a start point is an allowance, so this is the half of the reading
        # that decides the verdict rather than the half that names the branch.
        self.assertEqual([row for row in read if row not in START_POINTS], [],
                         "a path read as a start point exempts the creation it should refuse")

    def test_Given_AWorktreeAddFromAnUnmergedTip_When_TheGuardIsAsked_Then_ItIsRefused(self):
        # Arrange — the worktree is on a branch main does not contain, which is the state the first
        # arm fires in, and the command names no start point.
        result = self.ask("git worktree add ../tree -b tooling/remedy-probe")

        # Act / Assert
        self.assertEqual((result.returncode,
                          "not main and not a commit main contains" in result.stderr),
                         (REFUSED, True))

    # GREEN_ON_BASE(characterization): a named start point is an allowance the base already gave.
    # Without this control beside the case above, refusing every worktree creation would satisfy it.
    def test_Given_AWorktreeAddFromOriginMain_When_TheGuardIsAsked_Then_ItIsAllowed(self):
        # Arrange / Act
        result = self.ask("git worktree add ../tree -b tooling/remedy-probe origin/main")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)


class UndecodableHeadTests(unittest.TestCase):
    """HEAD on a branch whose name holds a byte UTF-8 does not map. `git check-ref-format` accepts
    the name, and a packed ref carries it where APFS refuses a loose ref's file, with `Illegal byte
    sequence`."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-branch-bytes-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.project = self.root / "project"
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        commit(self.project, "on main")
        self.on_main = git(self.project, "rev-parse", "HEAD").stdout.strip()
        commit(self.project, "work main does not contain")
        self.unmerged = git(self.project, "rev-parse", "HEAD").stdout.strip()
        git(self.project, "reset", "-q", "--hard", self.on_main)

    def check_out_undecodable(self, commit_id):
        refs = self.project / ".git"
        with open(refs / "packed-refs", "ab") as packed:
            packed.write(commit_id.encode("ascii") + b" refs/heads/" + UNDECODABLE_BRANCH + b"\n")
        (refs / "HEAD").write_bytes(b"ref: refs/heads/" + UNDECODABLE_BRANCH + b"\n")

    def head_is_undecodable(self):
        """Whether git names HEAD with the byte, read as bytes rather than through the decode under
        test."""
        raw = subprocess.run(["git", "-C", str(self.project), "rev-parse", "--abbrev-ref", "HEAD"],
                             capture_output=True, check=True, timeout=60).stdout
        try:
            raw.decode("utf-8")
        except UnicodeDecodeError:
            return True
        return False

    def ask(self, command):
        """What the guard does with `command`, under a HOME holding no deferral that could decide it
        instead."""
        event = {"tool_name": "Bash", "cwd": str(self.project), "tool_input": {"command": command}}
        environment = dict(os.environ, HOME=str(self.root))
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def test_Given_AHeadNamedWithAByteThatIsNotUTF8_When_ABranchIsCutFromUnmergedWork_Then_ItIsRefused(self):
        # Arrange — the gate rides in the comparison because a name that decoded would leave
        # nothing to fail on, and this case green having posed nothing.
        self.check_out_undecodable(self.unmerged)
        arranged = self.head_is_undecodable()

        # Act
        result = self.ask(f"git branch {PROBE_BRANCH}")

        # Assert
        self.assertEqual((arranged, result.returncode,
                          "not main and not a commit main contains" in result.stderr),
                         (True, REFUSED, True))

    def test_Given_AHeadNamedWithAByteThatIsNotUTF8_When_ABranchIsCutFromMainsCommit_Then_ItIsAllowed(self):
        # Arrange — the other side of the same name, so a reading that survives the byte by
        # refusing whatever it is asked is not what makes the case above pass.
        self.check_out_undecodable(self.on_main)
        arranged = self.head_is_undecodable()

        # Act
        result = self.ask(f"git branch {PROBE_BRANCH}")

        # Assert
        self.assertEqual((arranged, result.returncode), (True, ALLOWED))


UNREAD_BRANCH = "tooling/unread-head-probe"
SESSION = "probe-0001"


class UnreadHeadTests(unittest.TestCase):
    """A directory that is not a repository, so git reads no HEAD in it."""

    def setUp(self):
        self.home = Path(tempfile.mkdtemp(prefix="velvet-branch-unread-"))
        self.addCleanup(shutil.rmtree, self.home, ignore_errors=True)
        self.tree = self.home / "tree"
        self.tree.mkdir()

    def ask(self, command):
        event = {"tool_name": "Bash", "cwd": str(self.tree), "tool_input": {"command": command}}
        environment = dict(os.environ, HOME=str(self.home), CLAUDE_CODE_SESSION_ID=SESSION)
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def test_Given_NoHeadGitCanRead_When_ABranchIsCreated_Then_TheRefusalNamesTheReadingRatherThanAHead(self):
        # Arrange / Act
        result = self.ask(f"git branch {UNREAD_BRANCH}")

        # Assert
        self.assertEqual((result.returncode, "detached at" in result.stderr,
                          "git rev-parse --abbrev-ref HEAD" in result.stderr),
                         (REFUSED, False, True))

    def test_Given_NoHeadGitCanRead_When_ADeferredBranchIsCreated_Then_NoBaseIsRecorded(self):
        # Arrange
        (self.home / ".velvet-pr-deferrals").write_text(
            f"{UNREAD_BRANCH} stacking on purpose {int(time.time())} {SESSION}\n", encoding="utf-8")
        bases = self.home / ".velvet-branch-bases"

        # Act
        result = self.ask(f"git branch {UNREAD_BRANCH}")

        # Assert
        self.assertEqual((result.returncode, bases.read_text() if bases.exists() else None),
                         (ALLOWED, None))


SECURITY = ("# Security Policy\n\n## Supported versions\n\n| Version | Supported |\n"
             "| ------- | --------- |\n| 3.0.x   | ✅        |\n| 2.1.x   | ✅        |\n")


def velvet_origin(root, security=SECURITY):
    """A checkout whose origin/main declares 3.0.0 with nothing breaking waiting, and whose
    SECURITY.md, unless `security` is None, supports 3.0.x and 2.1.x, so the line main maintains is
    2.x and 3.x is its own series. 2.x is on origin, cut where main stands."""
    origin, project = root / "origin.git", root / "project"
    subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
    subprocess.run(["git", "init", "-q", "-b", "main", str(project)], check=True, timeout=60)
    git(project, "remote", "add", "origin", str(origin))
    package = project / "Packages/com.velvet.core/package.json"
    package.parent.mkdir(parents=True)
    package.write_text(json.dumps({"name": "com.velvet.core", "version": "3.0.0"}))
    (package.parent / "CHANGELOG.md").write_text(
        "# Changelog\n\n## [Unreleased]\n\n## [3.0.0] - 2026-11-14\n\n- A change.\n")
    if security is not None:
        (project / "SECURITY.md").write_text(security, encoding="utf-8")
    git(project, "add", "-A")
    commit(project, "chore(velvet): release v3.0.0")
    git(project, "push", "-q", "origin", "main", "main:refs/heads/2.x")
    git(project, "fetch", "-q", "origin")
    return project


def ask_in(root, project, command):
    event = {"tool_name": "Bash", "cwd": str(project), "tool_input": {"command": command}}
    environment = dict(os.environ, HOME=str(root))
    environment.pop("CLAUDE_PROJECT_DIR", None)
    return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                          capture_output=True, text=True, env=environment, timeout=120)


class LineCutTests(unittest.TestCase):
    """`velvet_origin`'s checkout. Each creation names a start point, which the guard allows on its
    own, and a push is read for its line alone, so a refusal here is the line's."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-line-cut-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.project = velvet_origin(self.root)

    def refused(self, command):
        """What the guard said in refusing `command`, or "" where it let it through."""
        result = ask_in(self.root, self.project, command)
        return result.stderr if result.returncode == REFUSED else ""

    def test_Given_MainsOwnSeries_When_ABranchIsRenamedToItAndPushed_Then_ItIsRefused(self):
        # Act
        said = self.refused("git branch -m topic 3.x && git push -u origin HEAD")

        # Assert
        self.assertIn("Refusing to create `3.x`: 3.x is not a line main maintains", said)

    def test_Given_MainsOwnSeries_When_ABranchIsCopiedToIt_Then_ItIsRefused(self):
        # Act
        said = self.refused("git branch -c main 3.x")

        # Assert
        self.assertIn("Refusing to create `3.x`: 3.x is not a line main maintains", said)

    def test_Given_MainsOwnSeries_When_AnOrphanIsCheckedOutAsIt_Then_ItIsRefused(self):
        # Act
        said = self.refused("git checkout --orphan 3.x")

        # Assert
        self.assertIn("Refusing to create `3.x`: 3.x is not a line main maintains", said)

    def test_Given_MainsOwnSeries_When_AWorktreeIsAddedAtAPathNamedForIt_Then_ItIsRefused(self):
        # Act
        said = self.refused("git worktree add ../3.x && git -C ../3.x push -u origin HEAD")

        # Assert
        self.assertIn("Refusing to create `3.x`: 3.x is not a line main maintains", said)

    def test_Given_TheCurrentBranchIsMainsOwnSeries_When_HeadIsPushed_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "switch", "-q", "-c", "3.x")

        # Act
        said = self.refused("git push -u origin HEAD")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_TheCurrentBranchIsMainsOwnSeries_When_APushNamesNoRefspec_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "switch", "-q", "-c", "3.x")

        # Act
        said = self.refused("git push")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_ALocalBranchForMainsOwnSeries_When_EveryBranchIsPushed_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "branch", "3.x")

        # Act
        said = self.refused("git push --all origin")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_MainsCommitsNotOnTheLine_When_MainIsPushedToTheLine_Then_ItIsRefused(self):
        # Arrange — main moves past where 2.x was cut.
        commit(self.project, "fix(velvet): main's own fix")
        main_fix = git(self.project, "rev-parse", "HEAD").stdout.strip()
        git(self.project, "push", "-q", "origin", "main")

        # Act
        said = self.refused("git push origin main:2.x")

        # Assert
        self.assertIn(f"Refusing to push to `2.x`: it carries 1 commit of main that 2.x does not "
                      f"hold, starting with {main_fix[:7]}", said)

    def test_Given_OriginsRepositoryNamedByItsPath_When_MainsOwnSeriesIsPushedThere_Then_ItIsRefused(self):
        # Act
        said = self.refused(f"git push {self.root / 'origin.git'} HEAD:refs/heads/3.x")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_OriginsRepositoryNamedByAFileUrl_When_MainsOwnSeriesIsPushedThere_Then_ItIsRefused(self):
        # Act
        said = self.refused(f"git push file://{self.root / 'origin.git'} HEAD:refs/heads/3.x")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_ASecondRemoteForOriginsRepository_When_MainsOwnSeriesIsPushedThere_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "remote", "add", "gh", str(self.root / "origin.git"))

        # Act
        said = self.refused("git push gh HEAD:refs/heads/3.x")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    # GREEN_ON_BASE(characterization): the base reads no push, so it lets this one through.
    # What it pins is that another repository's lines are not origin's: reading every remote as
    # origin reddens it.
    def test_Given_ARepositoryThatIsNotOrigins_When_MainsOwnSeriesIsPushedThere_Then_ItIsAllowed(self):
        # Arrange
        subprocess.run(["git", "init", "-q", "--bare", str(self.root / "fork.git")], check=True,
                       timeout=60)

        # Act
        result = ask_in(self.root, self.project,
                        f"git push {self.root / 'fork.git'} HEAD:refs/heads/3.x")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    # GREEN_ON_BASE(characterization): the base reads no push, so it lets this one through.
    # What it pins is that a tree with no origin is not this repository: reading it as one that
    # cannot be told reddens it.
    def test_Given_ATreeWithNoOrigin_When_ALineIsPushedToAPath_Then_ItIsAllowed(self):
        # Arrange
        git(self.project, "remote", "remove", "origin")

        # Act
        result = ask_in(self.root, self.project,
                        f"git push {self.root / 'fork.git'} HEAD:refs/heads/3.x")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def test_Given_ATreeThatIsNotThere_When_ALineIsPushedFromIt_Then_ItIsRefusedAsUntold(self):
        # Act
        said = self.refused(f"git -C {self.root / 'missing'} push origin HEAD:refs/heads/3.x")

        # Assert
        self.assertIn("whether the push reaches origin's repository could not be told", said)

    # GREEN_ON_BASE(characterization): the base lets a named start point through whatever its name.
    # What it pins is that a tree with no origin is not this repository: refusing a line where
    # origin cannot be read reddens it.
    def test_Given_ATreeWithNoOrigin_When_ALineIsCreated_Then_ItIsAllowed(self):
        # Arrange
        git(self.project, "remote", "remove", "origin")

        # Act
        result = ask_in(self.root, self.project, "git branch 3.x HEAD")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    # GREEN_ON_BASE(characterization): the base lets a named start point through whatever its name.
    # What it pins is that an origin with no `main` is not this repository: refusing a line where
    # origin/main cannot be fetched reddens it.
    def test_Given_AnOriginWithNoMain_When_ALineIsCreated_Then_ItIsAllowed(self):
        # Arrange — origin carries a branch of another name only.
        trunk = self.root / "trunk.git"
        subprocess.run(["git", "init", "-q", "--bare", str(trunk)], check=True, timeout=60)
        git(self.project, "push", "-q", str(trunk), "main:refs/heads/trunk")
        git(self.project, "remote", "set-url", "origin", str(trunk))

        # Act
        result = ask_in(self.root, self.project, "git branch 3.x HEAD")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def test_Given_ATreeThatIsNotThere_When_MainsOwnSeriesIsCreatedInIt_Then_ItIsRefusedAsUnread(self):
        # Act
        said = self.refused(f"git -C {self.root / 'missing'} branch 3.x origin/main")

        # Assert
        self.assertIn("Refusing to create `3.x`: which lines main maintains could not be read", said)

    def test_Given_AnOriginThatCannotBeFetched_When_ALineIsCreated_Then_ItIsRefusedAsUnread(self):
        # Arrange
        git(self.project, "remote", "set-url", "origin", str(self.root / "gone.git"))

        # Act
        said = self.refused("git branch 2.x origin/main")

        # Assert
        self.assertIn("Refusing to create `2.x`: which lines main maintains could not be read", said)

    def test_Given_ALocalLineAGlobMatches_When_TheGlobIsPushed_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "branch", "3.x")

        # Act
        said = self.refused("git push origin 'refs/heads/*:refs/heads/*'")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    def test_Given_ABranchThatIsNoLineAGlobWritesToALine_When_TheGlobIsPushed_Then_ItIsRefused(self):
        # Arrange
        git(self.project, "branch", "x")

        # Act
        said = self.refused("git push origin 'refs/heads/x*:refs/heads/3.x*'")

        # Assert
        self.assertIn("Refusing to push `3.x`: 3.x is not a line main maintains", said)

    # GREEN_ON_BASE(characterization): the base reads no push, so it lets this one through.
    # What it pins is that a glob names only what it matches: reading a glob as every local line
    # reddens it.
    def test_Given_ALocalLineAGlobDoesNotMatch_When_TheGlobIsPushed_Then_ItIsAllowed(self):
        # Arrange — a destination that would name 1.x, so the source side alone keeps it out.
        git(self.project, "branch", "1.x")
        git(self.project, "branch", "feat/knob")

        # Act
        result = ask_in(self.root, self.project, "git push origin 'refs/heads/feat/*:refs/heads/*'")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    # GREEN_ON_BASE(characterization): the base reads no push, so it lets this one through.
    # What it pins is that a fix the line takes is let through: refusing every commit a push brings
    # reddens it.
    def test_Given_AFixOfTheLinesOwn_When_ItIsPushedToTheLine_Then_ItIsAllowed(self):
        # Arrange
        git(self.project, "switch", "-q", "-c", "fix", "origin/2.x")
        commit(self.project, "fix(velvet): the line's own fix")

        # Act
        result = ask_in(self.root, self.project, "git push origin fix:2.x")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def ask(self, command):
        event = {"tool_name": "Bash", "cwd": str(self.project), "tool_input": {"command": command}}
        environment = dict(os.environ, HOME=str(self.root))
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def test_Given_MainsOwnSeries_When_ItsLineIsCreated_Then_ItIsRefused(self):
        # Act
        result = self.ask("git branch 3.x origin/main")

        # Assert
        self.assertIn("3.x is not a line main maintains: main is building 3.x",
                      result.stderr if result.returncode == REFUSED else "")

    def test_Given_MainsOwnSeries_When_ItsLineIsPushed_Then_ItIsRefused(self):
        # Act
        result = self.ask("git push origin origin/main:refs/heads/3.x")

        # Assert
        self.assertIn("3.x is not a line main maintains: main is building 3.x",
                      result.stderr if result.returncode == REFUSED else "")

    # GREEN_ON_BASE(characterization): the base lets a named start point through whatever its name.
    # What it pins is that the line main maintains is not refused: refusing every line reddens it.
    def test_Given_TheLineMainMaintains_When_ItIsCreated_Then_ItIsAllowed(self):
        # Act
        result = self.ask("git branch 2.x origin/main")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    # GREEN_ON_BASE(characterization): the base reads no push at all, so it lets this one through.
    # What it pins is that deleting a line is no push to one: reading `--delete` as one reddens it.
    def test_Given_MainsOwnSeries_When_ItsLineIsDeleted_Then_ItIsAllowed(self):
        # Act
        result = self.ask("git push origin --delete 3.x")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)


def repository(url):
    """`line_branches.repository` for `url`, imported where it is asked rather than with this
    module, which the cases above load without."""
    sys.path.insert(0, str(REPO_ROOT / ".claude/hooks/lib"))
    import line_branches
    return line_branches.repository(url, ".")


# This repository's origin, spelled each way a push can name it.
ORIGIN_SPELLINGS = ("https://github.com/s4k10503/velvet.git", "https://github.com/S4K10503/velvet",
                    "git@github.com:s4k10503/velvet.git", "ssh://git@github.com/s4k10503/velvet.git")


class RepositorySpellingTests(unittest.TestCase):
    def test_Given_OriginSpelledEachWayAPushCanName_When_Read_Then_EachIsOneRepository(self):
        # Act
        read = {repository(url) for url in ORIGIN_SPELLINGS}

        # Assert
        self.assertEqual(len(read), 1)

    def test_Given_AForkOfTheSameName_When_Read_Then_ItIsNotOrigins(self):
        # Act
        read = repository("git@github.com:someone/velvet.git")

        # Assert
        self.assertNotEqual(read, repository(ORIGIN_SPELLINGS[0]))


class LineTreeTests(unittest.TestCase):
    """Trees the line reading cannot ask: one that is not this repository, and one it cannot read."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-line-tree-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    # GREEN_ON_BASE(characterization): the base reads no push, so it lets this one through.
    # What it pins is that a tree whose main holds no Velvet package is left alone: dropping the
    # package check reddens it.
    def test_Given_ATreeThatIsNotThisRepository_When_ALineIsPushed_Then_ItIsAllowed(self):
        # Arrange
        origin, project = self.root / "origin.git", self.root / "elsewhere"
        subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(project)], check=True, timeout=60)
        git(project, "remote", "add", "origin", str(origin))
        commit(project, "a project of its own")
        git(project, "push", "-q", "origin", "main")

        # Act
        result = ask_in(self.root, project, "git push origin HEAD:refs/heads/1.x")

        # Assert
        self.assertEqual(result.returncode, ALLOWED)

    def test_Given_AMainWhoseSecurityPolicyIsMissing_When_ALineIsPushed_Then_WhatGitSaidAndAWayOutAreNamed(self):
        # Arrange
        project = velvet_origin(self.root, security=None)

        # Act
        result = ask_in(self.root, project, "git push origin HEAD:refs/heads/2.x")

        # Assert
        self.assertIn("'SECURITY.md' does not exist in 'origin/main'\n\nFetch origin and retry",
                      result.stderr if result.returncode == REFUSED else "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

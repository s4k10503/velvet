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

Run: python3 scripts/hooks/test_branch_from_unmerged.py
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
HOOK = REPO_ROOT / ".claude/hooks/refuse/branch_from_unmerged.py"

REFUSED = 2
ALLOWED = 0

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


if __name__ == "__main__":
    unittest.main(verbosity=2)

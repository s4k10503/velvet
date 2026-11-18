#!/usr/bin/env python3
"""Tests for the hook refusing a `gh pr merge` whose milestone does not hold.

Posed inside `pull_request_base_check.py`'s worlds, whose `origin/main` builds 3 and whose `2.x` line
last published 1.0.0, with its stub gh answering the pull request, its files and the open
milestones. `scripts/pr/test_milestone.py` owns the decision; these hold the hook to asking it.

Run: python3 scripts/hooks/test_merge_off_milestone.py
"""

import importlib.util
import inspect
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import pull_request_base_check as worlds  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/merge_off_milestone.py"
REFUSED, ALLOWED = 2, 0

# A head off 2.x changing a file of the package, which the hook reads off git.
PACKAGE_HEAD = "package-topic"
# What the 2.x line of the world publishes next, and one past it.
LINE_MILESTONES = [{"number": 11, "title": "v2.0.0", "state": "open"},
                   {"number": 12, "title": "v2.1.0", "state": "open"},
                   {"number": 7, "title": "Infrastructure", "state": "open"}]


class MergeOffMilestoneTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root = Path(tempfile.mkdtemp(prefix="velvet-milestone-hook-")).resolve()
        cls.home = cls.root / "home"
        cls.home.mkdir()
        cls.project = worlds.build_world(cls.root / "world", unpublished_on_maintenance=False)
        # A fragment on main and on the line, and a public API on the line, so a merge takes every
        # git read the hook can: a fragment's contents and the API file are read where a tree has
        # them.
        fragment = {"Packages/com.velvet.core/Changelog~/unreleased/kept.md":
                    "### Fixed\n\n- A fix.\n"}
        write_and_commit(cls.project, fragment)
        worlds.in_worktree(cls.project, cls.root, "2.x", lambda work: write_and_commit(
            work, {**fragment, "Packages/com.velvet.core/PublicAPI.txt": "method Velvet.V.Old()\n"}))
        worlds.git(cls.project, "push", "-q", "origin", "main", "2.x")
        worlds.git(cls.project, "fetch", "-q", "origin")
        worlds.git(cls.project, "branch", PACKAGE_HEAD, "origin/2.x")

        def change_the_package(work):
            fix = work / "Packages/com.velvet.core/Runtime/Fix.cs"
            fix.parent.mkdir(parents=True, exist_ok=True)
            fix.write_text("class Fix {}\n", encoding="utf-8")
            worlds.commit(work, "a fix to the package")

        worlds.in_worktree(cls.project, cls.root, PACKAGE_HEAD, change_the_package)
        worlds.git(cls.project, "push", "-q", "origin", PACKAGE_HEAD)
        # Where GitHub keeps a fork's head on the base repository, under a branch name origin lacks.
        worlds.git(cls.project, "push", "-q", "origin", f"{PACKAGE_HEAD}:refs/pull/1/head")
        cls.bin = cls.root / "bin"
        cls.bin.mkdir()
        (cls.bin / "gh").write_text(worlds.STUB_GH, encoding="utf-8")
        (cls.bin / "gh").chmod(0o755)
        # Every git the hook runs, logged and then run: the count of reads is what the budget holds.
        cls.git_log = cls.root / "git.log"
        (cls.bin / "git").write_text(f'#!/bin/sh\necho "$*" >> "{cls.git_log}"\n'
                                     f'exec "{shutil.which("git")}" "$@"\n', encoding="utf-8")
        (cls.bin / "git").chmod(0o755)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.root, ignore_errors=True)

    def merge(self, title="Infrastructure", head="topic", milestones=LINE_MILESTONES,
              command=worlds.COMMAND, gh_fails=False, fork=False):
        """(exit code, stderr) of the hook posed a merge onto 2.x of `head` under `title`, from a
        fork whose branch origin has not got where `fork` is set."""
        pull = worlds.at(self.project, head, "2.x")
        pull["milestone"] = {"number": 7, "title": title, "state": "open"} if title else None
        if fork:
            pull["head"] = {**pull["head"], "ref": "fork-only", "repo": {"full_name": "fork/velvet"}}
        environment = dict(os.environ, HOME=str(self.home),
                           PATH=str(self.bin) + os.pathsep + os.environ.get("PATH", ""),
                           VELVET_BASE_CHECK_PULLS=json.dumps({"1": pull}),
                           VELVET_BASE_CHECK_RUNS=json.dumps(worlds.push_runs(self.project)),
                           VELVET_BASE_CHECK_MILESTONES=json.dumps(milestones))
        environment[worlds.UNMODELLED] = str(self.root / "unmodelled")
        environment.pop("CLAUDE_PROJECT_DIR", None)
        if gh_fails:
            environment["VELVET_BASE_CHECK_PULLS"] = "{}"
        event = json.dumps({"tool_name": "Bash", "cwd": str(self.project),
                            "tool_input": {"command": command}})
        done = subprocess.run([sys.executable, "-B", str(HOOK)], input=event, text=True,
                              capture_output=True, cwd=str(self.project), env=environment,
                              timeout=120)
        return done.returncode, done.stderr

    def refusal(self, **asked):
        """The hook's refusal text, or "" where it did not refuse."""
        code, said = self.merge(**asked)
        return said if code == REFUSED else ""

    def test_Given_NoMilestone_When_Merged_Then_TheEditThatSetsOneIsNamed(self):
        # Act / Assert
        self.assertIn("`gh pr edit 1 --milestone Infrastructure`", self.refusal(title=None))

    def test_Given_APackageChangeUnderInfrastructure_When_Merged_Then_TheLinesNextVersionIsNamed(self):
        # Act / Assert
        self.assertIn("`gh pr edit 1 --milestone v2.0.0`", self.refusal(head=PACKAGE_HEAD))

    def test_Given_APackageChangeUnderALaterVersion_When_Merged_Then_ItWaitsForTheNextOne(self):
        # Act / Assert
        self.assertIn("the next version 2.x publishes is v2.0.0",
                      self.refusal(title="v2.1.0", head=PACKAGE_HEAD))

    def test_Given_APackageChangeUnderTheNextVersion_When_Merged_Then_ItAllows(self):
        # Act
        code, _ = self.merge(title="v2.0.0", head=PACKAGE_HEAD)

        # Assert
        self.assertEqual(code, ALLOWED)

    def test_Given_AChangeOutsideThePackageUnderInfrastructure_When_Merged_Then_ItAllows(self):
        # Act
        code, _ = self.merge()

        # Assert
        self.assertEqual(code, ALLOWED)

    def test_Given_APullRequestGhCannotRead_When_Merged_Then_ItRefusesAsUnjudged(self):
        # Act / Assert
        self.assertIn("could not be judged", self.refusal(gh_fails=True))

    def test_Given_AnUnexpandedOperand_When_Merged_Then_ItRefuses(self):
        # Act / Assert
        self.assertIn("has not expanded yet",
                      self.refusal(command="gh pr merge $PR --squash --delete-branch"))

    def test_Given_ACommandMergingNothing_When_Posed_Then_ItAllows(self):
        # Act
        code, _ = self.merge(title=None, command="gh pr view 1")

        # Assert
        self.assertEqual(code, ALLOWED)

    def test_Given_AForksPackageChange_When_Merged_Then_ItsHeadIsFetchedByItsPullRequestsRef(self):
        # Act — its branch is not on origin, so a fetch naming the branch fails and refuses.
        code, _ = self.merge(title="v2.0.0", head=PACKAGE_HEAD, fork=True)

        # Assert
        self.assertEqual(code, ALLOWED)

    def test_Given_TwoMergesInOneCommand_When_Posed_Then_ItRefuses(self):
        # Act / Assert
        self.assertIn("merges 2 pull requests",
                      self.refusal(command=worlds.COMMAND + " && gh pr merge 2 --squash "
                                                            "--delete-branch"))

    def test_Given_APackageMergeReadingEveryTree_When_ItsGitReadsAreCounted_Then_TheyAreTheBudgets(self):
        # Arrange — the world holds a fragment on every tree read and an API file on the line.
        self.git_log.write_text("", encoding="utf-8")
        self.merge(title="v2.0.0", head=PACKAGE_HEAD)

        # Act — the fetch and the tag listing are budgeted apart, as network reads.
        local = [line for line in self.git_log.read_text(encoding="utf-8").splitlines()
                 if subcommand(line) not in ("fetch", "ls-remote")]

        # Assert
        self.assertEqual(len(local), guard().LOCAL_READS)


def write_and_commit(work, files):
    for path, text in files.items():
        (work / path).parent.mkdir(parents=True, exist_ok=True)
        (work / path).write_text(text, encoding="utf-8")
    worlds.commit(work, "a fragment and an API file")


def subcommand(line):
    """The git subcommand a logged invocation ran, past any `-C <directory>`."""
    words = line.split()
    while words[:1] == ["-C"]:
        words = words[2:]
    return words[0] if words else ""


def guard():
    spec = importlib.util.spec_from_file_location("merge_off_milestone", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class BudgetTests(unittest.TestCase):
    """The hook's worst case against the timeout it is registered with, past which the harness
    abandons it and the merge goes ahead unguarded."""

    def test_Given_EveryReadingAtItsBound_When_Added_Then_TheyFitTheRegisteredTimeout(self):
        # Arrange
        hook = guard()
        registered = next(entry["timeout"] for entry in json.loads(
            (REPO_ROOT / ".harness/hooks.json").read_text(encoding="utf-8"))
            if entry["script"] == "refuse/merge_off_milestone.py")

        # Act — the number looked up, the pull request, the milestone pages and the tag listing.
        spent = ((3 + hook.MILESTONE_PAGES) * hook.READ_TIMEOUT + hook.GIT_TIMEOUT
                 + hook.LOCAL_READS * hook.LOCAL_TIMEOUT)

        # Assert
        self.assertLessEqual(spent, registered)

    def test_Given_TheTreeReadings_When_TheirBoundsAreRead_Then_NoneExceedsTheLocalTimeout(self):
        # Arrange
        hook = guard()
        release = hook.milestone.published_check

        # Act
        longest = max([inspect.signature(read).parameters["timeout"].default
                       for read in (release.git, hook.milestone.release_notes.fragments_at)]
                      + [hook.milestone.GIT_TIMEOUT])

        # Assert
        self.assertLessEqual(longest, hook.LOCAL_TIMEOUT)


if __name__ == "__main__":
    unittest.main(verbosity=2)

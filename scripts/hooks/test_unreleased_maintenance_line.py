#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/unreleased_maintenance_line.py.

The report exists because nothing else reads this state: `published_check.py` asks whether a closed
version went unpublished, and an entry waiting in `## [Unreleased]` belongs to no version yet. So the
cases that matter most are the silences — a `main` full of entries, a feature branch full of them,
and a line whose section is empty — because a report that fires on those is one a reader learns to
skip, and the one case it exists for goes past with them.

Each silence is asserted beside the exit code. An empty stdout is also what a tree without the hook
produces, so the three would pass on a base that does not carry it and separate nothing.

Run: python3 scripts/hooks/test_unreleased_maintenance_line.py
"""

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/report/unreleased_maintenance_line.py"

_spec = importlib.util.spec_from_file_location("maintenance_line_report", HOOK)
report_module = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(report_module)
CHANGELOG = "Packages/com.velvet.core/CHANGELOG.md"

EMPTY = """# Changelog

## [Unreleased]

## [2.1.2] - 2026-08-23

### Highlights

- A fix.
"""

WAITING = """# Changelog

## [Unreleased]

### Fixed

- A backport nobody released.

## [2.1.2] - 2026-08-23

### Highlights

- A fix.
"""


def run(cwd):
    return subprocess.run([sys.executable, str(HOOK)], cwd=str(cwd),
                          capture_output=True, text=True, timeout=30)


def git(cwd, *args):
    subprocess.run(["git", "-C", str(cwd), *args], check=True,
                   capture_output=True, text=True)


def repository(branch, changelog, merged=True):
    """An origin holding one branch, cloned so the clone carries it as a remote-tracking ref.

    `merged` is whether main has taken the branch. A line main has not taken is the state the second
    reading is for, and the default is the settled one so a case about the first reading says nothing
    about the second.
    """
    root = Path(tempfile.mkdtemp())
    origin = root / "origin"
    origin.mkdir()
    git(origin, "init", "--quiet", "--initial-branch=main")
    git(origin, "config", "user.email", "t@example.com")
    git(origin, "config", "user.name", "t")
    (origin / CHANGELOG).parent.mkdir(parents=True)
    (origin / CHANGELOG).write_text(EMPTY)
    git(origin, "add", CHANGELOG)
    git(origin, "commit", "--quiet", "-m", "main")
    if branch != "main":
        git(origin, "checkout", "--quiet", "-b", branch)
        (origin / CHANGELOG).write_text(changelog)
        git(origin, "commit", "--quiet", "--allow-empty", "-am", branch)
        git(origin, "checkout", "--quiet", "main")
    else:
        (origin / CHANGELOG).write_text(changelog)
        git(origin, "commit", "--quiet", "--allow-empty", "-am", "more")
    if merged and branch != "main":
        git(origin, "merge", "--quiet", "--no-edit", "-m", "merge forward", branch)
    clone = root / "clone"
    subprocess.run(["git", "clone", "--quiet", str(origin), str(clone)],
                   check=True, capture_output=True, text=True)
    return clone


class LineMainHasNotTaken(unittest.TestCase):
    """The second reading, asked of git's ancestry rather than of what a pull request says.

    #777 measured two per-commit readings failing, and a third that works by reading prose. This one
    is the question the practice already answers: merge the line forward, and git records that it did.
    """

    def test_Given_ALineMainHasNotMerged_When_Reported_Then_ItIsNamedWithACount(self):
        # Arrange — a line carrying a commit main cannot reach, which is what a fix made there and
        # never carried forward looks like.
        clone = repository("2.x", EMPTY, merged=False)

        # Act
        done = run(clone)

        # Assert
        self.assertIn("main does not contain origin/2.x: 1 commit(s)", done.stdout)

    def test_Given_ALineMainHasMerged_When_Reported_Then_NothingIsSaid(self):
        # Arrange — once the merge has happened there is nothing left to interpret, and a report that
        # keeps asking is one a reader learns to skip.
        clone = repository("2.x", EMPTY, merged=True)

        # Act
        done = run(clone)

        # Assert
        self.assertEqual((done.returncode, done.stdout), (0, ""))


class MaintenanceLineReport(unittest.TestCase):
    def test_Given_ALineHoldingAnUnreleasedEntry_When_Reported_Then_ItIsNamed(self):
        # Arrange
        clone = repository("2.x", WAITING)

        # Act
        done = run(clone)

        # Assert
        self.assertIn("origin/2.x holds 1 unreleased CHANGELOG entry", done.stdout)

    def test_Given_ALineWhoseSectionIsEmpty_When_Reported_Then_NothingIsSaid(self):
        # Arrange
        clone = repository("2.x", EMPTY)

        # Act
        done = run(clone)

        # Assert
        self.assertEqual((done.returncode, done.stdout), (0, ""))

    def test_Given_MainHoldingEntries_When_Reported_Then_NothingIsSaid(self):
        # Arrange — the ordinary state between releases, and the reason main is not read.
        clone = repository("main", WAITING)

        # Act
        done = run(clone)

        # Assert
        self.assertEqual((done.returncode, done.stdout), (0, ""))

    def test_Given_AFeatureBranchHoldingEntries_When_Reported_Then_NothingIsSaid(self):
        # Arrange — a branch holds unreleased entries by design; reporting those buries the one case.
        clone = repository("feat/thing", WAITING)

        # Act
        done = run(clone)

        # Assert
        self.assertEqual((done.returncode, done.stdout), (0, ""))

    def test_Given_ATreeThatIsNoRepository_When_Reported_Then_ItExitsZero(self):
        # Arrange
        elsewhere = Path(tempfile.mkdtemp())

        # Act
        done = run(elsewhere)

        # Assert
        self.assertEqual(done.returncode, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

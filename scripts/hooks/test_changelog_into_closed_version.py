#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/changelog_into_closed_version.py.

A refuse guard that stops matching is indistinguishable from one that had nothing to say — exit 0 and
no output — so the cases hold both directions: what it refuses, and the release-time edits it must
let through. The second half is where it has already been measured wrong, by walking the release
procedure by hand, which was the only reading it got.

Run: python3 scripts/hooks/test_changelog_into_closed_version.py
"""

import json
import os
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/changelog_into_closed_version.py"
CHANGELOG_REL = "Packages/com.velvet.core/CHANGELOG.md"

RELEASED = """# Changelog

## [Unreleased]

### Fixed

- Something not yet shipped.

## [Unreleased — breaking]

### Changed

- An API a caller has to edit around.

## [2.0.0] - 2026-08-02

### Fixed

- A thing that shipped.
"""


class Verdicts(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="closed-version-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.root), "init", "--quiet"],
                       check=True, capture_output=True)
        self.changelog = self.root / CHANGELOG_REL
        self.changelog.parent.mkdir(parents=True)
        self.changelog.write_text(RELEASED)

    def edit(self, old, new):
        """The guard's verdict on an Edit replacing `old` with `new`, as (exit code, stderr)."""
        event = {"tool_name": "Edit", "cwd": str(self.root),
                 "tool_input": {"file_path": str(self.changelog),
                                "old_string": old, "new_string": new}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30,
                              env=dict(os.environ, CLAUDE_PROJECT_DIR=str(self.root)))
        return done.returncode, done.stderr

    def test_Given_AnEntryFiledIntoAReleasedSection_When_Judged_Then_ItIsRefused(self):
        # Arrange — the version shipped without the line, and would ship missing it.
        code, said = self.edit("- A thing that shipped.\n",
                               "- A thing that shipped.\n- A thing that did not.\n")

        # Act / Assert
        self.assertEqual((code, "2.0.0" in said), (2, True))

    def test_Given_AReleasedHeadingLosingItsDate_When_Judged_Then_ItIsRefused(self):
        # Arrange — renaming, stripping the date and deleting the section are one edit reached three
        # ways: whatever sits under an undated heading is not compared at all.
        code, said = self.edit("## [2.0.0] - 2026-08-02\n", "## [2.0.0]\n")

        # Act / Assert
        self.assertEqual((code, "no released section" in said), (2, True))

    def test_Given_AnEditWritingASecondHeadingForOneVersion_When_Judged_Then_ItIsRefused(self):
        # Arrange — a note is rebuilt from the first heading matching its version, so only one of the
        # two is ever published and their order decides which.
        code, said = self.edit("## [Unreleased]\n", "## [Unreleased]\n\n## [2.0.0] - 2026-08-02\n")

        # Act / Assert
        self.assertEqual((code, "second" in said), (2, True))

    def test_Given_AnEntryFiledUnderUnreleased_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the ordinary edit, and the one a guard that stopped matching would also pass.
        code, said = self.edit("- Something not yet shipped.\n",
                               "- Something not yet shipped.\n- And another.\n")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_TheBreakingSectionDrainedIntoTheVersionClosing_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the release-time edit this guard has already been measured refusing. The version
        # being closed is not released yet, so nothing under it is compared.
        code, said = self.edit(
            "## [Unreleased — breaking]\n\n### Changed\n\n- An API a caller has to edit around.\n",
            "")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AHighlightsBlockOnTheVersionClosing_When_Judged_Then_ItIsLetThrough(self):
        # Arrange — the other half of closing a version, and the order CONTRIBUTING.md prescribes:
        # everything written into the open section first, the rename last.
        code, said = self.edit("## [Unreleased]\n",
                               "## [Unreleased]\n\n### Highlights\n\n- What this release leads with.\n")

        # Act / Assert
        self.assertEqual((code, said), (0, ""))

    def test_Given_AChangelogInAnotherRepository_When_Judged_Then_ItIsNotThisGuardsToRefuse(self):
        # Arrange — a genuinely foreign checkout, which is what separates out-of-scope from the
        # worktrees this repository does its branch work in.
        elsewhere = Path(tempfile.mkdtemp(prefix="closed-version-foreign-"))
        self.addCleanup(shutil.rmtree, elsewhere, ignore_errors=True)
        subprocess.run(["git", "-C", str(elsewhere), "init", "--quiet"],
                       check=True, capture_output=True)
        foreign = elsewhere / CHANGELOG_REL
        foreign.parent.mkdir(parents=True)
        foreign.write_text(RELEASED)
        event = {"tool_name": "Edit", "cwd": str(self.root),
                 "tool_input": {"file_path": str(foreign),
                                "old_string": "- A thing that shipped.\n",
                                "new_string": "- A thing that shipped.\n- A thing that did not.\n"}}
        done = subprocess.run(["python3", str(GUARD)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=30,
                              env=dict(os.environ, CLAUDE_PROJECT_DIR=str(self.root)))

        # Act / Assert
        self.assertEqual((done.returncode, done.stderr), (0, ""))


if __name__ == "__main__":
    unittest.main(verbosity=2)

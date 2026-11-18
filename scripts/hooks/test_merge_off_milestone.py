#!/usr/bin/env python3
"""Tests for the hook refusing a `gh pr merge` whose milestone does not hold.

Posed inside `pull_request_base_check.py`'s worlds, whose `origin/main` builds 3 and whose `2.x` line
last published 1.0.0, with its stub gh answering the pull request, its files and the open
milestones. `scripts/pr/test_milestone.py` owns the decision; these hold the hook to asking it.

Run: python3 scripts/hooks/test_merge_off_milestone.py
"""

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

HOOK = Path(__file__).resolve().parents[2] / ".claude/hooks/refuse/merge_off_milestone.py"
REFUSED, ALLOWED = 2, 0

PACKAGE_FILE = {"filename": "Packages/com.velvet.core/Runtime/Fix.cs"}
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
        cls.bin = cls.root / "bin"
        cls.bin.mkdir()
        (cls.bin / "gh").write_text(worlds.STUB_GH, encoding="utf-8")
        (cls.bin / "gh").chmod(0o755)

    @classmethod
    def tearDownClass(cls):
        shutil.rmtree(cls.root, ignore_errors=True)

    def merge(self, title="Infrastructure", files=(), milestones=LINE_MILESTONES,
              command=worlds.COMMAND, gh_fails=False):
        """(exit code, stderr) of the hook posed a merge onto 2.x of `topic` under `title`."""
        pull = worlds.at(self.project, "topic", "2.x")
        pull["milestone"] = {"number": 7, "title": title, "state": "open"} if title else None
        pull["files"], pull["changed_files"] = list(files), len(files)
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
        self.assertIn("`gh pr edit 1 --milestone v2.0.0`",
                      self.refusal(files=[PACKAGE_FILE]))

    def test_Given_APackageChangeUnderALaterVersion_When_Merged_Then_ItWaitsForTheNextOne(self):
        # Act / Assert
        self.assertIn("the next version 2.x publishes is v2.0.0",
                      self.refusal(title="v2.1.0", files=[PACKAGE_FILE]))

    def test_Given_APackageChangeUnderTheNextVersion_When_Merged_Then_ItAllows(self):
        # Act
        code, _ = self.merge(title="v2.0.0", files=[PACKAGE_FILE])

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


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for scripts/pr/maintenance_line.py: which names are lines, what a pull request onto one
is refused for, what git is asked about a head, and which scripts and hooks spell a line's name in a
string.

Run: python3 scripts/pr/test_maintenance_line.py
"""

import ast
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))

import maintenance_line  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]

SHA = "a" * 40
OTHER = "b" * 40


def nothing_read():
    return maintenance_line.Reading([], [], [])


class NameTests(unittest.TestCase):
    def test_Given_TheWaysALineCouldBeSpelled_When_EachIsAskedWhetherItIsALine_Then_OnlyMajorDotXIs(self):
        # Arrange
        table = {"2.x": True, "10.x": True, "3.0.x": False, "2.X": False, "v2.x": False,
                 "2.x-hotfix": False, "fix/2.x": False, "main": False, "": False}

        # Act
        disagreements = [name for name, expected in table.items()
                         if maintenance_line.is_line(name) != expected]

        # Assert
        self.assertEqual(disagreements, [])

    def test_Given_VersionNamesAndThisRepositorysBranches_When_EachIsAskedWhetherItIsALookalike_Then_OnlyTheVersionNamesAre(self):
        # Arrange — the names with a prefix are heads of pull requests opened here.
        table = {"3.0.x": True, "2.X": True, "v2.x": True, "2.*": True, "3.0": True,
                 "v3.0.1": True, "v3": True,
                 "2.x": False, "release/2.1.1": False, "backport/2.1.2": False,
                 "fix/2.x-retire-presence-state": False, "chore/release-3.0.0": False,
                 "dependabot/github_actions/2.x/github-actions-9069ad7eea": False,
                 "1672": False, "main": False}

        # Act
        disagreements = [name for name, expected in table.items()
                         if maintenance_line.lookalike(name) != expected]

        # Assert
        self.assertEqual(disagreements, [])


class PullRequestReasonTests(unittest.TestCase):
    def test_Given_AFeatureTitleOntoALine_When_Reasoned_Then_TheTitleIsRefused(self):
        # Act
        said = maintenance_line.pull_request_reasons("2.x", "topic", "feat(velvet): add a knob")

        # Assert
        self.assertIn("its title `feat(velvet): add a knob` is a feature", " ".join(said))

    def test_Given_AFixTitleMarkedBreakingOntoALine_When_Reasoned_Then_TheTitleIsRefused(self):
        # Act
        said = maintenance_line.pull_request_reasons("2.x", "topic", "fix(velvet)!: drop a knob")

        # Assert
        self.assertIn("its title `fix(velvet)!: drop a knob` is a feature", " ".join(said))

    def test_Given_TitlesALineTakes_When_Reasoned_Then_NoneIsRefused(self):
        # Arrange — the titles of pull requests this repository landed on 2.x, and a revert of a
        # feature, which removes one rather than adding it.
        titles = ["fix(velvet): retire an AnimatePresence's state when its node stops being rendered",
                  "ci(velvet): hold game-ci/unity-test-runner at 4.3.1 on 2.x",
                  "chore(velvet): release v2.1.4",
                  "ci: bump the github-actions group with 2 updates",
                  'Revert "feat(velvet): add a knob"']

        # Act
        refused = [title for title in titles
                   if maintenance_line.pull_request_reasons("2.x", "topic", title)]

        # Assert
        self.assertEqual(refused, [])

    def test_Given_AFeatureTitleOntoMain_When_Reasoned_Then_NothingIsRefused(self):
        # Act
        said = maintenance_line.pull_request_reasons("main", "topic", "feat(velvet): add a knob")

        # Assert
        self.assertEqual(said, [])

    def test_Given_ABaseNamedLikeALine_When_Reasoned_Then_TheBaseIsNamed(self):
        # Act
        said = maintenance_line.pull_request_reasons("3.0.x", "topic", "fix(velvet): a fix")

        # Assert
        self.assertIn("its base 3.0.x is named like a maintenance line", " ".join(said))

    def test_Given_AHeadNamedLikeALine_When_Reasoned_Then_TheHeadIsNamed(self):
        # Act
        said = maintenance_line.pull_request_reasons("main", "v3", "fix(velvet): a fix")

        # Assert
        self.assertIn("its head v3 is named like a maintenance line", " ".join(said))


class HeadReasonTests(unittest.TestCase):
    def test_Given_AHeadCarryingMainsCommits_When_Reasoned_Then_TheyAreCountedAndTheFirstIsNamed(self):
        # Arrange
        reading = maintenance_line.Reading(
            [(SHA, "Merge origin/main", ""), (OTHER, "fix(velvet): main's fix", "")],
            [(OTHER, "fix(velvet): main's fix"), ("c" * 40, "fix(velvet): older")], [])

        # Act
        said = maintenance_line.head_reasons("2.x", reading)

        # Assert
        self.assertIn("it carries 2 commits of main that 2.x does not hold, starting with bbbbbbb",
                      " ".join(said))

    def test_Given_ACommitTitledAsAFeature_When_Reasoned_Then_ItIsNamed(self):
        # Arrange
        reading = maintenance_line.Reading([(SHA, "feat(velvet): add a knob", "")], [], [])

        # Act
        said = maintenance_line.head_reasons("2.x", reading)

        # Assert
        self.assertIn("starting with aaaaaaa feat(velvet): add a knob", " ".join(said))

    def test_Given_ACommitFootedAsBreaking_When_Reasoned_Then_ItIsNamed(self):
        # Arrange
        reading = maintenance_line.Reading(
            [(SHA, "fix(velvet): a fix", "BREAKING CHANGE: the knob is gone\n")], [], [])

        # Act
        said = maintenance_line.head_reasons("2.x", reading)

        # Assert
        self.assertIn("starting with aaaaaaa fix(velvet): a fix", " ".join(said))

    def test_Given_AnAddedBreakingFragment_When_Reasoned_Then_ItIsNamed(self):
        # Arrange
        path = maintenance_line.BREAKING_FRAGMENTS + "/topic.md"
        reading = maintenance_line.Reading([], [], [path])

        # Act
        said = maintenance_line.head_reasons("3.x", reading)

        # Assert
        self.assertIn(f"it adds {path}, a breaking change", " ".join(said))

    def test_Given_FixesPickedFromMain_When_Reasoned_Then_NothingIsRefused(self):
        # Arrange — the shape the four fixes this line took from main arrived in.
        reading = maintenance_line.Reading(
            [(SHA, "fix(velvet): a fix", f"(cherry picked from commit {OTHER})\n")], [], [])

        # Act
        said = maintenance_line.head_reasons("2.x", reading)

        # Assert
        self.assertEqual(said, [])


def git(cwd, *args):
    return subprocess.run(["git", "-C", str(cwd), "-c", "user.email=t@velvet", "-c", "user.name=t",
                           *args], check=True, capture_output=True, text=True,
                          timeout=60).stdout.strip()


class ReadTests(unittest.TestCase):
    """`main` released 1.0.0 at `cut`, where `2.x` was cut; `main` then took `feature`, and `2.x`
    took a fix of its own."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-line-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        origin, self.project = self.root / "origin.git", self.root / "project"
        subprocess.run(["git", "init", "-q", "--bare", str(origin)], check=True, timeout=60)
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        git(self.project, "remote", "add", "origin", str(origin))
        (self.project / "notes.txt").write_text("1.0.0\n", encoding="utf-8")
        git(self.project, "add", "notes.txt")
        git(self.project, "commit", "-q", "-m", "chore(velvet): release v1.0.0")
        git(self.project, "branch", "2.x")
        (self.project / "feature.txt").write_text("a knob\n", encoding="utf-8")
        git(self.project, "add", "feature.txt")
        git(self.project, "commit", "-q", "-m", "feat(velvet): add a knob")
        self.feature = git(self.project, "rev-parse", "HEAD")
        git(self.project, "switch", "-q", "2.x")
        git(self.project, "commit", "-q", "--allow-empty", "-m", "fix(velvet): the line's own fix")
        git(self.project, "push", "-q", "origin", "main", "2.x")
        git(self.project, "switch", "-q", "-c", "topic", "origin/2.x")

    def head(self):
        git(self.project, "push", "-q", "origin", "topic")
        return git(self.project, "rev-parse", "HEAD")

    def test_Given_AHeadThatMergedMainIn_When_Read_Then_MainsCommitIsWhatItBringsOfMain(self):
        # Arrange
        git(self.project, "merge", "-q", "--no-edit", "origin/main")

        # Act
        reading = maintenance_line.read(self.project, "2.x", self.head(), 60)

        # Assert
        self.assertEqual([sha for sha, _ in reading.from_main], [self.feature])

    def test_Given_AHeadPickingMainsCommitWithItsTrailer_When_Read_Then_ItBringsNothingOfMain(self):
        # Arrange
        git(self.project, "cherry-pick", "-x", self.feature)

        # Act
        reading = maintenance_line.read(self.project, "2.x", self.head(), 60)

        # Assert
        self.assertEqual(reading.from_main, [])

    def test_Given_AHeadAddingABreakingFragment_When_Read_Then_ItIsListed(self):
        # Arrange
        path = maintenance_line.BREAKING_FRAGMENTS + "/topic.md"
        (self.project / path).parent.mkdir(parents=True)
        (self.project / path).write_text("### Removed\n\n- The knob.\n", encoding="utf-8")
        git(self.project, "add", path)
        git(self.project, "commit", "-q", "-m", "fix(velvet): a fix")

        # Act
        reading = maintenance_line.read(self.project, "2.x", self.head(), 60)

        # Assert
        self.assertEqual(reading.fragments, [path])

    def test_Given_AHeadsOwnCommit_When_Read_Then_ItsBodyIsRead(self):
        # Arrange
        git(self.project, "commit", "-q", "--allow-empty", "-m", "fix(velvet): a fix",
            "-m", "BREAKING CHANGE: the knob is gone")

        # Act
        reading = maintenance_line.read(self.project, "2.x", self.head(), 60)

        # Assert
        self.assertEqual([body.strip() for _, _, body in reading.commits],
                         ["BREAKING CHANGE: the knob is gone"])


# A line's name spelled into a string: an escaped dot or a bracketed one before the `x`, or an `x`
# appended to a format field or concatenated on.
def spells_a_line(text):
    import re
    return bool(re.search(r"(?:\\\.|\[\.\]|\}\.|^\.)x(?!\w)", text))


def strings_of(tree):
    """Every string constant of a module, its docstrings left out, and each piece of an f-string."""
    docstrings = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
            body = node.body
            if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant):
                docstrings.add(id(body[0].value))
    return [node.value for node in ast.walk(tree)
            if isinstance(node, ast.Constant) and isinstance(node.value, str)
            and id(node) not in docstrings]


# Reads SECURITY.md's supported-version table, whose rows are a release's series — `3.0.x`, `2.1.x` —
# rather than branches.
NOT_A_LINE_READER = {"scripts/release/supported_versions_check.py"}


class SpellingTests(unittest.TestCase):
    def test_Given_TheScriptsAndHooks_When_TheirStringsAreReadForALinesName_Then_OnlyTheOwnerSpellsOne(self):
        # Arrange — the owner is among what is read, so a scan that found nothing anywhere fails too.
        sources = [path for root in ("scripts", ".harness/hooks")
                   for path in sorted((REPO_ROOT / root).rglob("*.py"))
                   if not path.name.startswith("test_") and "__pycache__" not in path.parts]

        # Act
        spelling = [path.relative_to(REPO_ROOT).as_posix() for path in sources
                    if any(spells_a_line(text)
                           for text in strings_of(ast.parse(path.read_text(encoding="utf-8"))))]

        # Assert
        self.assertEqual(sorted(set(spelling) - NOT_A_LINE_READER),
                         [Path(maintenance_line.__file__).resolve().relative_to(REPO_ROOT).as_posix()])


if __name__ == "__main__":
    unittest.main(verbosity=2)

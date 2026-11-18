#!/usr/bin/env python3
"""Unit tests for milestone.py: every refusal a merge or a release meets, and the way out it names.

Run: python3 scripts/test_quality/run_suite.py scripts/pr/test_milestone.py
"""

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import milestone  # noqa: E402

M = milestone.Milestone
# The repository as it stood when this was written: main builds 3, the 2.x line is cut, and v3.0.1
# is the newest release.
OPEN = [M(5, "v3.0.2"), M(2, "v3.1.0"), M(4, "v3.2.0"), M(3, "v4.0.0"), M(7, "Infrastructure")]
PUBLISHED = {"2.1.4", "3.0.0", "3.0.1"}
TODAY = milestone.Repository(OPEN, PUBLISHED, 3)


def pull(title="v3.0.2", state="open", base="main", landing=None, name="fix(velvet): a fix"):
    return milestone.PullRequest(title, state, base, landing or base, "topic", "a" * 40, 1, False,
                                 name)


def decided(title="v3.0.2", package=True, change=milestone.NOTHING, repository=TODAY, base="main",
            state="open", name="fix(velvet): a fix", number=7):
    return milestone.merge_reasons(number, pull(title, state, base, name=name), package, change,
                                   repository)


class PullRequestReasonTests(unittest.TestCase):
    def test_Given_APackageChangeWithNoMilestone_When_Decided_Then_TheNextVersionIsTheWayOut(self):
        # Act
        reasons = decided(title=None, state=None)

        # Assert
        self.assertIn("carries no milestone", reasons[0] if reasons else "")

    def test_Given_APackageChangeWithNoMilestone_When_Decided_Then_TheEditNamesTheNextVersion(self):
        # Act
        reasons = decided(title=None, state=None)

        # Assert
        self.assertIn("`gh pr edit 7 --milestone v3.0.2`", reasons[0] if reasons else "")

    def test_Given_AChangeOutsideThePackageWithNoMilestone_When_Decided_Then_InfrastructureIsNamed(self):
        # Act
        reasons = decided(title=None, state=None, package=False)

        # Assert
        self.assertIn("`gh pr edit 7 --milestone Infrastructure`", reasons[0] if reasons else "")

    def test_Given_AClosedMilestone_When_Decided_Then_AnOpenOneIsAskedFor(self):
        # Act
        reasons = decided(title="v3.0.1", state="closed")

        # Assert
        self.assertIn("its milestone v3.0.1 is closed: set an open one with "
                      "`gh pr edit 7 --milestone v3.0.2`", reasons[0] if reasons else "")

    def test_Given_AMilestoneThatIsNeitherKind_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = decided(title="Someday", package=False)

        # Assert
        self.assertIn("neither a version (vMAJOR.MINOR.PATCH) nor Infrastructure",
                      reasons[0] if reasons else "")


class HeadReasonTests(unittest.TestCase):
    def test_Given_APackageChangeUnderInfrastructure_When_Decided_Then_TheVersionIsAskedFor(self):
        # Act
        reasons = decided(title="Infrastructure")

        # Assert
        self.assertIn("carries Infrastructure, which is for a change outside it: set the version it "
                      "ships in with `gh pr edit 7 --milestone v3.0.2`", reasons[0] if reasons else "")

    def test_Given_AChangeOutsideThePackageUnderInfrastructure_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(decided(title="Infrastructure", package=False), [])

    def test_Given_AChangeOutsideThePackageUnderALaterVersion_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(decided(title="v3.1.0", package=False), [])

    def test_Given_APackageChangeUnderTheNextVersion_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(decided(), [])

    def test_Given_APackageChangeUnderALaterVersion_When_Decided_Then_ItWaitsForTheNextOne(self):
        # Act
        reasons = decided(title="v3.1.0")

        # Assert
        self.assertIn("the next version main publishes is v3.0.2: wait for v3.0.2 to be published "
                      "and its milestone closed, or ship it in v3.0.2 with "
                      "`gh pr edit 7 --milestone v3.0.2`", reasons[0] if reasons else "")

    def test_Given_APackageChangeOntoABranchNoReleaseIsCutFrom_When_Decided_Then_ALaterVersionHolds(self):
        # Arrange — a chain member onto another pull request's branch lands nothing on main yet.
        # Act / Assert
        self.assertEqual(decided(title="v3.1.0", base="refactor/below"), [])

    def test_Given_TheLowestOpenVersionAlreadyPublished_When_Decided_Then_ClosingItIsTheWayOut(self):
        # Arrange — the release workflow closes it; this is the state where that did not happen.
        repository = TODAY._replace(open=[M(8, "v3.0.1")] + OPEN)

        # Act
        reasons = decided(repository=repository)

        # Assert
        self.assertIn("v3.0.1, the lowest open version milestone main publishes, is published "
                      "already: close it with "
                      "`gh api -X PATCH repos/{owner}/{repo}/milestones/8 -f state=closed`",
                      reasons[0] if reasons else "")

    def test_Given_TheLowestOpenVersionSkippingOne_When_Decided_Then_TheFollowersAreNamed(self):
        # Arrange — v3.0.2 closed unpublished, which leaves v3.2.0 behind v3.1.0's gap.
        repository = TODAY._replace(open=[M(4, "v3.2.0"), M(3, "v4.0.0")])

        # Act
        reasons = decided(title="v3.2.0", repository=repository)

        # Assert
        self.assertIn("does not follow v3.0.1, the newest release below it, so publishing it skips "
                      "a version: open the milestone of whichever of v3.0.2, v3.1.0, v4.0.0",
                      reasons[0] if reasons else "")

    def test_Given_ALineWithNoOpenVersionMilestone_When_Decided_Then_OpeningOneIsTheWayOut(self):
        # Act
        reasons = decided(title="v3.0.2", base="2.x")

        # Assert
        self.assertIn("no open milestone names the next version 2.x publishes: open it with "
                      "`gh api repos/{owner}/{repo}/milestones -f title=v<version>`, the version "
                      "`python3 scripts/release/next_version.py --rev origin/2.x` prints",
                      reasons[0] if reasons else "")

    def test_Given_ALinesOwnNextVersion_When_ItsPullRequestIsDecided_Then_NothingBlocks(self):
        # Arrange
        repository = TODAY._replace(open=OPEN + [M(9, "v2.1.5")])

        # Act / Assert
        self.assertEqual(decided(title="v2.1.5", base="2.x", repository=repository), [])

    def test_Given_ALinesMilestoneBelowMains_When_MainsPullRequestIsDecided_Then_ItIsNotMainsNext(self):
        # Arrange — v2.1.5 is the lowest open version overall, and the line's rather than main's.
        repository = TODAY._replace(open=OPEN + [M(9, "v2.1.5")])

        # Act / Assert
        self.assertEqual(decided(repository=repository), [])

    def test_Given_MainBuildingTheNextMajor_When_Decided_Then_ItsNextVersionIsThatMajor(self):
        # Arrange — a breaking entry on main makes it build 4, and the v3 milestones a line's.
        repository = TODAY._replace(series=4)

        # Act / Assert
        self.assertEqual(decided(title="v4.0.0", repository=repository), [])


class BumpReasonTests(unittest.TestCase):
    """Onto a branch no release is cut from, so the milestone is asked about nothing but its bump."""

    def bumped(self, title, change=milestone.NOTHING, name="fix(velvet): a fix"):
        return decided(title=title, change=change, base="refactor/below", name=name)

    def test_Given_ABreakingEntryUnderAPatchMilestone_When_Decided_Then_TheMajorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", milestone.Change((), "A break.", None))

        # Assert
        self.assertIn("adds the breaking entry `A break.`, which ships in a major at least, and "
                      "carries v3.0.2, a patch: carry v4.0.0 with `gh pr edit 7 --milestone v4.0.0`",
                      reasons[0] if reasons else "")

    def test_Given_ABreakingEntryUnderAMinorMilestone_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = self.bumped("v3.1.0", milestone.Change((), "A break.", None))

        # Assert
        self.assertIn("carries v3.1.0, a minor", reasons[0] if reasons else "")

    def test_Given_ABreakingEntryUnderAMajorMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v4.0.0", milestone.Change((), "A break.", None)), [])

    def test_Given_AnAddedEntryUnderAPatchMilestone_When_Decided_Then_TheMinorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", milestone.Change((), None, "A utility."))

        # Assert
        self.assertIn("carry v3.1.0 with `gh pr edit 7 --milestone v3.1.0`",
                      reasons[0] if reasons else "")

    def test_Given_AnAddedEntryUnderAMinorMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v3.1.0", milestone.Change((), None, "A utility.")), [])

    def test_Given_AFeatureTitleUnderAPatchMilestone_When_Decided_Then_TheMinorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", name="feat(velvet): a utility")

        # Assert
        self.assertIn("is titled as a feature", reasons[0] if reasons else "")

    def test_Given_AFixTitleUnderAPatchMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v3.0.2"), [])


class ClosingReasonTests(unittest.TestCase):
    def test_Given_AReleaseClosingAnotherVersionThanItsMilestone_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = decided(title="v3.0.2", change=milestone.Change(("3.1.0",), None, None))

        # Assert
        self.assertIn("it closes 3.1.0 in the CHANGELOG under the milestone v3.0.2",
                      reasons[0] if reasons else "")

    def test_Given_AReleaseClosingItsMilestonesVersion_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(decided(change=milestone.Change(("3.0.2",), None, None)), [])


class ReleaseReasonTests(unittest.TestCase):
    def test_Given_TheNextVersion_When_ReleaseIsAsked_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(milestone.release_reasons("3.0.2", TODAY), [])

    def test_Given_AVersionAboveAnOpenOne_When_ReleaseIsAsked_Then_TheOneBelowIsNamed(self):
        # Act
        reasons = milestone.release_reasons("3.1.0", TODAY)

        # Assert
        self.assertEqual(reasons, [
            "v3.0.2 is open below v3.1.0 among the versions main publishes: publish v3.0.2 first, "
            "or close its milestone with `gh api -X PATCH repos/{owner}/{repo}/milestones/5 -f "
            "state=closed` (CONTRIBUTING.md's releasing section)"])

    def test_Given_AVersionThatSkipsOne_When_ReleaseIsAsked_Then_TheFollowersAreNamed(self):
        # Arrange
        repository = TODAY._replace(open=[M(6, "v3.0.3")])

        # Act
        reasons = milestone.release_reasons("3.0.3", repository)

        # Assert
        self.assertIn("v3.0.3 does not follow v3.0.1, the newest release below it",
                      reasons[0] if reasons else "")

    def test_Given_AVersionNoOpenMilestoneNames_When_ReleaseIsAsked_Then_OpeningItIsTheWayOut(self):
        # Arrange
        repository = TODAY._replace(open=[M(2, "v3.1.0")])

        # Act
        reasons = milestone.release_reasons("3.0.2", repository)

        # Assert
        self.assertIn("`gh api repos/{owner}/{repo}/milestones -f title=v3.0.2`",
                      reasons[0] if reasons else "")

    def test_Given_APublishedMilestoneLeftOpenBelow_When_ReleaseIsAsked_Then_ClosingItIsNamed(self):
        # Arrange
        repository = TODAY._replace(open=[M(8, "v3.0.1")] + OPEN)

        # Act
        reasons = milestone.release_reasons("3.0.2", repository)

        # Assert
        self.assertIn("v3.0.1 is published and its milestone is still open: close it with "
                      "`gh api -X PATCH repos/{owner}/{repo}/milestones/8 -f state=closed`",
                      reasons[0] if reasons else "")

    def test_Given_ALinesPatchBesideMainsOpenMilestones_When_ReleaseIsAsked_Then_NothingBlocks(self):
        # Arrange
        repository = TODAY._replace(open=OPEN + [M(9, "v2.1.5")])

        # Act / Assert
        self.assertEqual(milestone.release_reasons("2.1.5", repository), [])


def pages(*sizes):
    """An API answering each listing page with the next of `sizes` entries, recording each path."""
    asked = []

    def api(path, method="GET"):
        asked.append((method, path))
        return [{"number": len(asked) * 1000 + entry, "title": "Infrastructure",
                 "filename": f"scripts/{entry}.py"} for entry in range(sizes[len(asked) - 1])]
    return api, asked


class ReadingTests(unittest.TestCase):
    def test_Given_AListingFillingItsFirstPage_When_Read_Then_TheSecondIsAskedFor(self):
        # Arrange
        api, _ = pages(milestone.PAGE, 3)

        # Act / Assert
        self.assertEqual(len(milestone.listed(api, "milestones?state=open")), milestone.PAGE + 3)

    def test_Given_AFileMovedOutOfThePackage_When_ItsPathsAreRead_Then_ThePackageIsChanged(self):
        # Arrange
        def api(path, method="GET"):
            return [{"filename": "scripts/moved.py",
                     "previous_filename": "Packages/com.velvet.core/Runtime/Moved.cs"}]

        # Act / Assert
        self.assertTrue(milestone.changes_package(milestone.changed_paths(api, 7, 1)))

    def test_Given_AFileListingShorterThanTheCountReported_When_Read_Then_ItIsUnread(self):
        # Arrange
        api, _ = pages(2)

        # Act / Assert
        with self.assertRaises(milestone.Unread):
            milestone.changed_paths(api, 7, 5)

    def test_Given_AStackMember_When_ItsPullRequestIsRead_Then_ItLandsOnTheStacksBase(self):
        # Arrange
        def api(path, method="GET"):
            return {"head": {"ref": "upper", "sha": "a" * 40, "repo": {"full_name": "o/r"}},
                    "base": {"ref": "lower", "repo": {"full_name": "o/r"}},
                    "stack": {"number": 3, "base": {"ref": "main"}}, "changed_files": 1}

        # Act / Assert
        self.assertEqual(milestone.pull_request(api, 7).landing, "main")

    def test_Given_APublishedVersionsMilestone_When_Closed_Then_ItsNumberIsPatched(self):
        # Arrange
        asked = []

        def api(path, method="GET", fields=()):
            asked.append(" ".join((method, path, *fields)))
            return [{"number": 5, "title": "v3.0.2"}, {"number": 2, "title": "v3.1.0"}]

        # Act
        milestone.close(api, "3.0.2")

        # Assert
        self.assertEqual(asked[-1], "PATCH milestones/5 state=closed")


class RerunTests(unittest.TestCase):
    HEAD = "a" * 40

    def api(self, status="completed", heads=None, runs=True):
        """An API over one pull request at HEAD whose generators run is in `status`; `heads` is
        what each reading of the pull request answers, in turn."""
        heads = list(heads or [self.HEAD, self.HEAD])
        posted = []

        def ask(path, method="GET", fields=()):
            if method == "POST":
                posted.append(path)
                return None
            if path.startswith("pulls/"):
                sha = heads.pop(0) if len(heads) > 1 else heads[0]
                return {"head": {"ref": "topic", "sha": sha}, "base": {"ref": "main"}}
            if path.startswith("actions/workflows/"):
                return {"workflow_runs": [{"id": 11, "run_number": 4, "status": status}]
                        if runs else []}
            return {"jobs": [{"id": 21, "name": "Source generators (dotnet)"},
                             {"id": 22, "name": milestone.JOB}]}
        return ask, posted

    def test_Given_AFinishedRun_When_RerunIsAsked_Then_OnlyTheMilestoneJobRunsAgain(self):
        # Arrange
        api, posted = self.api()

        # Act
        milestone.rerun(api, 7, deadline=0, now=lambda: 0)

        # Assert
        self.assertEqual(posted, ["actions/jobs/22/rerun"])

    def test_Given_ARunStillGoingAtTheDeadline_When_RerunIsAsked_Then_TheRerunCommandIsNamed(self):
        # Arrange
        api, _ = self.api(status="in_progress")

        # Act / Assert
        with self.assertRaisesRegex(milestone.Unread, r"gh run rerun 11"):
            milestone.rerun(api, 7, deadline=0, now=lambda: 0, sleep=lambda _: None)

    def test_Given_AHeadThatMovedWhileItWaited_When_RerunIsAsked_Then_NothingRunsAgain(self):
        # Arrange
        api, posted = self.api(heads=[self.HEAD, "b" * 40])

        # Act
        milestone.rerun(api, 7, deadline=0, now=lambda: 0)

        # Assert
        self.assertEqual(posted, [])

    def test_Given_NoRunAtTheHeadYet_When_RerunIsAsked_Then_NothingRunsAgain(self):
        # Arrange
        api, posted = self.api(runs=False)

        # Act
        milestone.rerun(api, 7, deadline=0, now=lambda: 0)

        # Assert
        self.assertEqual(posted, [])


def git(directory, *args):
    return subprocess.run(["git", "-C", str(directory), *args], check=True, capture_output=True,
                          text=True).stdout.strip()


CHANGELOG = """# Changelog

## [Unreleased]

## [Unreleased — breaking]

## [3.0.1] - 2026-11-17

### Highlights

- A fix.
"""


class ChangeReadingTests(unittest.TestCase):
    """What a head's fragments add, read off a repository of its own."""

    def setUp(self):
        self.directory = Path(tempfile.mkdtemp(prefix="velvet-milestone-"))
        git(self.directory, "init", "-q", "-b", "main")
        changelog = self.directory / "Packages/com.velvet.core/CHANGELOG.md"
        changelog.parent.mkdir(parents=True)
        changelog.write_text(CHANGELOG, encoding="utf-8")
        self.commit()
        self.base = git(self.directory, "rev-parse", "HEAD")

    def tearDown(self):
        subprocess.run(["rm", "-rf", str(self.directory)], check=False)

    def commit(self):
        git(self.directory, "add", "-A")
        git(self.directory, "-c", "user.email=t@velvet", "-c", "user.name=t", "-c",
            "commit.gpgsign=false", "commit", "-q", "-m", "change")

    def fragment(self, kind, text):
        path = self.directory / f"Packages/com.velvet.core/Changelog~/{kind}/topic.md"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        self.commit()
        return milestone.change(self.directory, self.base, "HEAD", {"3.0.1"})

    def test_Given_ABreakingFragment_When_TheChangeIsRead_Then_ItsEntryIsTheBreakingOne(self):
        # Act
        read = self.fragment("breaking", "### Changed\n\n- A break.\n")

        # Assert
        self.assertEqual(read.breaking, "A break.")

    def test_Given_AnAddedFragment_When_TheChangeIsRead_Then_ItsEntryIsTheAddedOne(self):
        # Act
        read = self.fragment("unreleased", "### Added\n\n- A utility.\n")

        # Assert
        self.assertEqual(read.added, "A utility.")

    def test_Given_AFixedFragment_When_TheChangeIsRead_Then_NothingAsksForABump(self):
        # Act
        read = self.fragment("unreleased", "### Fixed\n\n- A fix.\n")

        # Assert
        self.assertIsNone(milestone.needed_bump(read, "fix(velvet): a fix"))


if __name__ == "__main__":
    unittest.main(verbosity=2)

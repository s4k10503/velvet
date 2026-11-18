#!/usr/bin/env python3
"""Unit tests for milestone.py: every refusal a merge or a release meets, and the way out it names.

Run: python3 scripts/test_quality/run_suite.py scripts/pr/test_milestone.py
"""

import contextlib
import io
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parent))
import milestone  # noqa: E402

M = milestone.Milestone
# The repository as it stood when this was written: main builds 3, the 2.x line is cut, and v3.0.1
# is the newest release.
OPEN = [M(5, "v3.0.2"), M(2, "v3.1.0"), M(4, "v3.2.0"), M(3, "v4.0.0"), M(7, "Infrastructure")]
PUBLISHED = {"2.1.4", "3.0.0", "3.0.1"}
TODAY = milestone.Repository(OPEN, PUBLISHED, 3)


def pull(title="v3.0.2", state="open", base="main", landing=None):
    return milestone.PullRequest(title, state, base, landing or base, "topic", "a" * 40, False)


def adds(bump="patch", closes=()):
    """A change to the package whose derivation is `bump`, closing `closes`."""
    derived = {"major": (["- A break."], [], []), "minor": ([], ["method Velvet.V.New()"], []),
               "patch": ([], [], [])}[bump]
    return milestone.Change(True, tuple(closes), milestone.next_version.derive("0.0.0", *derived))


def decided(title="v3.0.2", package=True, change=None, repository=TODAY, base="main",
            state="open", number=7):
    change = change or (adds() if package else milestone.NOTHING)
    return milestone.merge_reasons(number, pull(title, state, base), change, repository)


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
                      "a version: open v3.0.2 with "
                      "`gh api repos/{owner}/{repo}/milestones -f title=v3.0.2` or v3.1.0 with",
                      reasons[0] if reasons else "")

    def test_Given_TheSkippedVersionsMilestoneClosed_When_Decided_Then_ReopeningItIsNamed(self):
        # Arrange — v3.0.2 closed unpublished, so its title is held: creating it again is refused.
        repository = TODAY._replace(open=[M(4, "v3.2.0")], closed=[M(5, "v3.0.2")])

        # Act
        reasons = decided(title="v3.2.0", repository=repository)

        # Assert
        self.assertIn("open v3.0.2 with "
                      "`gh api -X PATCH repos/{owner}/{repo}/milestones/5 -f state=open`",
                      reasons[0] if reasons else "")

    def test_Given_ALineWithNoOpenVersionMilestone_When_Decided_Then_OpeningOneIsTheWayOut(self):
        # Act
        reasons = decided(title="v3.0.2", base="2.x")

        # Assert
        self.assertIn("no open milestone names the next version 2.x publishes, the one "
                      "`python3 scripts/release/next_version.py --rev origin/2.x` prints: open "
                      "v2.1.5 with `gh api repos/{owner}/{repo}/milestones -f title=v2.1.5` or "
                      "v2.2.0 with", reasons[0] if reasons else "")

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

    def test_Given_NoOpenMilestoneOfMainsOwnMajor_When_Decided_Then_TheNextMajorIsMains(self):
        # Arrange — main builds 3 and every v3 milestone is closed, so the v4.0.0 pull request that
        # starts main building 4 merges under main's next version.
        repository = TODAY._replace(open=[M(3, "v4.0.0")])

        # Act / Assert
        self.assertEqual(decided(title="v4.0.0", change=adds("major"), repository=repository), [])

    def test_Given_MainBuildingTheNextMajor_When_Decided_Then_ItsNextVersionIsThatMajor(self):
        # Arrange — a breaking entry on main makes it build 4, and the v3 milestones a line's.
        repository = TODAY._replace(series=4)

        # Act / Assert
        self.assertEqual(decided(title="v4.0.0", repository=repository), [])


class BumpReasonTests(unittest.TestCase):
    """Onto a branch no release is cut from, so the milestone is asked about nothing but its bump."""

    def bumped(self, title, bump):
        return decided(title=title, change=adds(bump), base="refactor/below")

    def test_Given_ABreakingAdditionUnderAPatchMilestone_When_Decided_Then_TheMajorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", "major")

        # Assert
        self.assertIn("ships in a major", reasons[0] if reasons else "")

    def test_Given_ABreakingAdditionUnderAPatchMilestone_When_Decided_Then_TheEditToTheMajorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", "major")

        # Assert
        self.assertIn("carry v4.0.0 with `gh pr edit 7 --milestone v4.0.0`",
                      reasons[0] if reasons else "")

    def test_Given_ABreakingAdditionUnderAMinorMilestone_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = self.bumped("v3.1.0", "major")

        # Assert
        self.assertIn("carries v3.1.0, a minor", reasons[0] if reasons else "")

    def test_Given_ABreakingAdditionUnderAMajorMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v4.0.0", "major"), [])

    def test_Given_AGainedPublicMemberUnderAPatchMilestone_When_Decided_Then_TheMinorIsNamed(self):
        # Act
        reasons = self.bumped("v3.0.2", "minor")

        # Assert
        self.assertIn("carry v3.1.0 with `gh pr edit 7 --milestone v3.1.0`",
                      reasons[0] if reasons else "")

    def test_Given_AGainedPublicMemberUnderAMinorMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v3.1.0", "minor"), [])

    def test_Given_APatchUnderAPatchMilestone_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(self.bumped("v3.0.2", "patch"), [])

    def test_Given_TheMinorAskedForWithNoMilestoneOpen_When_Decided_Then_OpeningItIsNamed(self):
        # Arrange
        repository = TODAY._replace(open=[M(5, "v3.0.2")])

        # Act
        reasons = decided(title="v3.0.2", change=adds("minor"), base="refactor/below",
                          repository=repository)

        # Assert
        self.assertIn("opening it with `gh api repos/{owner}/{repo}/milestones -f title=v3.1.0`",
                      reasons[0] if reasons else "")


class ClosingReasonTests(unittest.TestCase):
    def test_Given_AReleaseClosingAnotherVersionThanItsMilestone_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = decided(title="v3.0.2", change=adds(closes=("3.1.0",)))

        # Assert
        self.assertIn("it closes 3.1.0 in the CHANGELOG under the milestone v3.0.2",
                      reasons[0] if reasons else "")

    def test_Given_AReleaseClosingItsMilestonesVersion_When_Decided_Then_NothingBlocks(self):
        # Act / Assert
        self.assertEqual(decided(change=adds(closes=("3.0.2",))), [])


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

    def test_Given_AClosedMilestoneHoldingTheTitle_When_ReleaseIsAsked_Then_ReopeningItIsNamed(self):
        # Arrange — a release published again once its tag went, whose milestone closed with it.
        repository = TODAY._replace(open=[M(5, "v3.0.2")], closed=[M(8, "v3.0.1")],
                                    published={"2.1.4", "3.0.0"})

        # Act
        reasons = milestone.release_reasons("3.0.1", repository)

        # Assert
        self.assertIn("`gh api -X PATCH repos/{owner}/{repo}/milestones/8 -f state=open`",
                      reasons[0] if reasons else "")

    def test_Given_AMajorAboveMainsSeries_When_ReleaseIsAskedWithoutItsMilestone_Then_MainIsNamed(self):
        # Arrange — main builds 3; a 4.0.0 is main's to publish, and no line's.
        repository = TODAY._replace(open=[M(5, "v3.0.2")])

        # Act
        reasons = milestone.release_reasons("4.0.0", repository)

        # Assert
        self.assertIn("where v4.0.0 is the next version main publishes", reasons[0] if reasons else "")

    def test_Given_ALinesPatchBesideMainsOpenMilestones_When_ReleaseIsAsked_Then_NothingBlocks(self):
        # Arrange
        repository = TODAY._replace(open=OPEN + [M(9, "v2.1.5")])

        # Act / Assert
        self.assertEqual(milestone.release_reasons("2.1.5", repository), [])


def pages(*sizes):
    """An API answering each listing page with the next of `sizes` entries, recording each path."""
    asked = []

    def api(path, method="GET", fields=()):
        asked.append((method, path))
        return [{"number": len(asked) * 1000 + entry, "title": "Infrastructure", "state": "open",
                 "filename": f"scripts/{entry}.py"} for entry in range(sizes[len(asked) - 1])]
    return api, asked


class ReadingTests(unittest.TestCase):
    def test_Given_AListingFillingItsFirstPage_When_Read_Then_TheSecondIsAskedFor(self):
        # Arrange
        api, _ = pages(milestone.PAGE, 3)

        # Act / Assert
        self.assertEqual(len(milestone.listed(api, "milestones?state=all")), milestone.PAGE + 3)

    def test_Given_AStackMember_When_ItsPullRequestIsRead_Then_ItLandsOnTheStacksBase(self):
        # Arrange
        def api(path, method="GET", fields=()):
            return {"head": {"ref": "upper", "sha": "a" * 40, "repo": {"full_name": "o/r"}},
                    "base": {"ref": "lower", "repo": {"full_name": "o/r"}},
                    "stack": {"number": 3, "base": {"ref": "main"}}}

        # Act / Assert
        self.assertEqual(milestone.pull_request(api, 7).landing, "main")

    def test_Given_OpenAndClosedMilestones_When_Listed_Then_TheClosedAreKeptApart(self):
        # Arrange
        def api(path, method="GET", fields=()):
            return [{"number": 5, "title": "v3.0.2", "state": "open"},
                    {"number": 8, "title": "v3.0.1", "state": "closed"}]

        # Act / Assert
        self.assertEqual(milestone.milestones(api)[1], [M(8, "v3.0.1")])

    def test_Given_APublishedVersionsMilestone_When_Closed_Then_ItsNumberIsPatched(self):
        # Arrange
        asked = []

        def api(path, method="GET", fields=()):
            asked.append(" ".join((method, path, *fields)))
            return [{"number": 5, "title": "v3.0.2", "state": "open"},
                    {"number": 2, "title": "v3.1.0", "state": "open"}]

        # Act
        milestone.close(api, "3.0.2")

        # Assert
        self.assertEqual(asked[-1], "PATCH milestones/5 state=closed")


class CheckOutputTests(unittest.TestCase):
    """What the check prints for a workflow run, read through `main`."""

    def checked(self, found, run="99"):
        printed = io.StringIO()
        environment = {"GITHUB_RUN_ID": run} if run else {}
        with mock.patch.object(milestone, "check", lambda *_: found), \
                mock.patch.dict(os.environ, environment, clear=False), \
                contextlib.redirect_stderr(printed), contextlib.redirect_stdout(io.StringIO()):
            if not run:
                os.environ.pop("GITHUB_RUN_ID", None)
            milestone.main(["check", "--number", "7"])
        return printed.getvalue()

    def test_Given_ARefusalInAWorkflowRun_When_Printed_Then_ItsReRunIsNamed(self):
        # Act / Assert — the verdict stands until a push or this, whatever the milestone does.
        self.assertIn("run it again with `gh run rerun 99 --failed`, or push to the branch",
                      self.checked(["a reason"]))

    def test_Given_ARefusalOutsideAWorkflowRun_When_Printed_Then_NoReRunIsNamed(self):
        # Act / Assert
        self.assertNotIn("gh run rerun", self.checked(["a reason"], run=None))


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

API = "Packages/com.velvet.core/PublicAPI.txt"


class ChangeReadingTests(unittest.TestCase):
    """What a head changes, read off a repository of its own from where it branched."""

    def setUp(self):
        self.directory = Path(tempfile.mkdtemp(prefix="velvet-milestone-"))
        git(self.directory, "init", "-q", "-b", "main")
        self.write("Packages/com.velvet.core/CHANGELOG.md", CHANGELOG)
        self.write(API, "method Velvet.V.Old()\n")
        self.commit()
        self.base = git(self.directory, "rev-parse", "HEAD")

    def tearDown(self):
        subprocess.run(["rm", "-rf", str(self.directory)], check=False)

    def write(self, path, text):
        target = self.directory / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def commit(self):
        git(self.directory, "add", "-A")
        git(self.directory, "-c", "user.email=t@velvet", "-c", "user.name=t", "-c",
            "commit.gpgsign=false", "commit", "-q", "-m", "change")
        return git(self.directory, "rev-parse", "HEAD")

    def fragment(self, kind, text):
        self.write(f"Packages/com.velvet.core/Changelog~/{kind}/topic.md", text)
        head = self.commit()
        return milestone.change(self.directory, self.base, head, {"3.0.1"})

    def test_Given_ABreakingFragment_When_TheChangeIsRead_Then_ItDerivesAMajor(self):
        # Act / Assert
        self.assertEqual(self.fragment("breaking", "### Changed\n\n- A break.\n")
                         .derivation.bump, "major")

    def test_Given_AnAddedFragment_When_TheChangeIsRead_Then_ItDerivesAMinor(self):
        # Act / Assert
        self.assertEqual(self.fragment("unreleased", "### Added\n\n- A utility.\n")
                         .derivation.bump, "minor")

    def test_Given_AFixedFragment_When_TheChangeIsRead_Then_ItDerivesAPatch(self):
        # Act / Assert — a reading that took nothing would derive nothing, which is no patch.
        self.assertEqual(self.fragment("unreleased", "### Fixed\n\n- A fix.\n")
                         .derivation.bump, "patch")

    def test_Given_AGainedPublicMember_When_TheChangeIsRead_Then_ItDerivesAMinor(self):
        # Arrange
        self.write(API, "method Velvet.V.Old()\nmethod Velvet.V.New()\n")
        head = self.commit()

        # Act / Assert
        self.assertEqual(milestone.change(self.directory, self.base, head, {"3.0.1"})
                         .derivation.bump, "minor")

    def test_Given_AFileOutsideThePackage_When_TheChangeIsRead_Then_ThePackageIsUnchanged(self):
        # Arrange
        self.write("scripts/tool.py", "print()\n")
        head = self.commit()

        # Act / Assert
        self.assertFalse(milestone.change(self.directory, self.base, head, {"3.0.1"}).package)

    def test_Given_AFileMovedOutOfThePackage_When_TheChangeIsRead_Then_ThePackageIsChanged(self):
        # Arrange
        self.write("Packages/com.velvet.core/Runtime/Moved.cs", "class Moved {}\n")
        self.base = self.commit()
        git(self.directory, "mv", "Packages/com.velvet.core/Runtime/Moved.cs", "Moved.cs")
        head = self.commit()

        # Act / Assert
        self.assertTrue(milestone.change(self.directory, self.base, head, {"3.0.1"}).package)

    def test_Given_AHeadBehindABaseThatDrainedABreak_When_Read_Then_TheBreakIsNotItsOwn(self):
        # Arrange — the head branched while a break waited; the base has since released it.
        self.write("Packages/com.velvet.core/Changelog~/breaking/old.md",
                   "### Changed\n\n- An old break.\n")
        self.commit()
        git(self.directory, "branch", "topic")
        (self.directory / "Packages/com.velvet.core/Changelog~/breaking/old.md").unlink()
        base = self.commit()
        git(self.directory, "checkout", "-q", "topic")
        self.write("Packages/com.velvet.core/Runtime/Fix.cs", "class Fix {}\n")
        head = self.commit()

        # Act — from where the head branched it adds a fix and nothing breaking.
        read = milestone.change(self.directory, base, head, {"3.0.1"})

        # Assert
        self.assertEqual(read.derivation.bump, "patch")


if __name__ == "__main__":
    unittest.main(verbosity=2)

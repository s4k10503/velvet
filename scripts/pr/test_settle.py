#!/usr/bin/env python3
"""Unit tests for settle.py's merge decision.

Every case is a merge that went wrong, not a rule someone thought of. The decision is separated from
the readings precisely so these run without a network, since a guard exercised only against live
pull requests is exercised only in the states those happen to be in.

Run: python3 scripts/pr/test_settle.py
"""

import importlib.util
import unittest
from pathlib import Path

GREEN = "a" * 40
MOVED = "b" * 40


def load_module():
    """Imports settle by path, since scripts/pr is not a package."""
    spec = importlib.util.spec_from_file_location("settle", Path(__file__).with_name("settle.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


settle = load_module()


def reasons(before=GREEN, after=GREEN, results=None, branch="topic", base="main",
            holds_base=True, held_by_worktree=False):
    if results is None:
        results = [{"name": "Required checks (Unity)", "bucket": "pass"}]
    return settle.reasons_from(before, after, results, branch, base, holds_base, held_by_worktree)


class MergeDecisionTests(unittest.TestCase):
    def test_Given_EveryCheckPassedAndNothingElseBlocks_When_Decided_Then_ThereIsNoReason(self):
        # Act / Assert
        self.assertEqual(reasons(), [])

    def test_Given_TheHeadMovedWhileChecksWereRead_When_Decided_Then_NothingElseIsReported(self):
        # Arrange — the readings straddle a force-push, so they are not about one commit.
        results = [{"name": "Unity", "bucket": "fail"}]

        # Act
        decided = reasons(after=MOVED, results=results, holds_base=False, held_by_worktree=True)

        # Assert
        self.assertEqual(len(decided), 1)

    def test_Given_NoCheckEverRan_When_Decided_Then_ThatIsNotReadAsPending(self):
        # Arrange — an empty list is a workflow never triggered, which a force-push after a cancel leaves.
        decided = reasons(results=[])

        # Act / Assert
        self.assertTrue(any("never triggered" in reason for reason in decided))

    def test_Given_ACheckStillRunning_When_Decided_Then_ItIsNamed(self):
        # Arrange
        results = [{"name": "Unity tests (PlayMode)", "bucket": "pending"},
                   {"name": "Release notes", "bucket": "pass"}]

        # Act
        decided = reasons(results=results)

        # Assert
        self.assertEqual(decided, ["still pending at aaaaaaa: Unity tests (PlayMode)"])

    def test_Given_ACancelledCheck_When_Decided_Then_ItBlocksRatherThanCountingAsPassed(self):
        # Arrange — a superseded run and a run somebody stopped both arrive as cancel.
        results = [{"name": "Unity tests (EditMode)", "bucket": "cancel"}]

        # Act
        decided = reasons(results=results)

        # Assert
        self.assertEqual(decided, ["failing at aaaaaaa: Unity tests (EditMode)=cancel"])

    def test_Given_ASkippedCheck_When_Decided_Then_ItPasses(self):
        # Arrange — the Unity jobs skip wholesale without a licence, which is what lets a fork merge.
        results = [{"name": "Unity tests (EditMode)", "bucket": "skipping"}]

        # Act / Assert
        self.assertEqual(reasons(results=results), [])

    def test_Given_ABranchBehindItsBase_When_Decided_Then_ItBlocksThoughEveryCheckPassed(self):
        # Arrange — GitHub reports BEHIND only where the base requires up-to-date heads, which this
        # repository deliberately does not, so mergeStateStatus reads CLEAN here.
        decided = reasons(holds_base=False)

        # Act / Assert
        self.assertEqual(decided, ["does not contain origin/main: merge it in and let the checks run again"])

    def test_Given_AWorktreeHoldingTheBranch_When_Decided_Then_ItBlocksBeforeTheMergeHappens(self):
        # Arrange — --delete-branch deletes the remote, then fails on the local one, after merging.
        decided = reasons(held_by_worktree=True)

        # Act / Assert
        self.assertTrue(any("worktree holds topic" in reason for reason in decided))

    def test_Given_SeveralIndependentProblems_When_Decided_Then_EachIsReported(self):
        # Arrange — reporting one at a time costs a round of CI per reason.
        results = [{"name": "Unity", "bucket": "pending"}]

        # Act
        decided = reasons(results=results, holds_base=False, held_by_worktree=True)

        # Assert
        self.assertEqual(len(decided), 3)


class UpdateReasonsTests(unittest.TestCase):
    """The update side of the same decision: what makes bringing the base in the wrong move."""

    def test_Given_ABranchBehindTheBase_When_Decided_Then_NothingBlocksTheUpdate(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=False)

        # Assert
        self.assertEqual(reasons, [])

    def test_Given_ABranchAlreadyHoldingTheBase_When_Decided_Then_ItIsRefused(self):
        # Act — an update that merges nothing still pushes, and a push re-runs every check.
        reasons = settle.update_reasons("feat/x", "main", holds_base=True, held_by_worktree=False)

        # Assert
        self.assertEqual(len(reasons), 1)

    def test_Given_ABranchHeldByAWorktree_When_Decided_Then_ItIsRefused(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=True)

        # Assert
        self.assertEqual(len(reasons), 1)

    def test_Given_BothConditions_When_Decided_Then_EachIsReportedOnce(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=True, held_by_worktree=True)

        # Assert
        self.assertEqual(len(reasons), 2)

    def test_Given_ARefusal_When_ItsTextIsRead_Then_ItNamesTheBranch(self):
        # Act
        reasons = settle.update_reasons("feat/x", "main", holds_base=False, held_by_worktree=True)

        # Assert
        self.assertIn("feat/x", reasons[0])


class TerminalStateTests(unittest.TestCase):
    def test_Given_TheTerminalSets_When_Compared_Then_NoBucketIsInBoth(self):
        # Arrange — a bucket in both would make a failing check merge or a passing one block.
        overlap = settle.TERMINAL_PASS & settle.TERMINAL_FAIL

        # Act / Assert
        self.assertEqual((len(settle.TERMINAL_PASS) > 0, overlap), (True, set()))

    def test_Given_APendingBucket_When_ClassifiedAgainstBothSets_Then_ItIsInNeither(self):
        # Act / Assert
        self.assertNotIn("pending", settle.TERMINAL_PASS | settle.TERMINAL_FAIL)


if __name__ == "__main__":
    unittest.main(verbosity=2)

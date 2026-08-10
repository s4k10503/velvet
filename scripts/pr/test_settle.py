#!/usr/bin/env python3
"""Unit tests for settle.py's merge decision.

The decision is separated from the readings precisely so these run without a network, since a guard
exercised only against live pull requests is exercised only in the states those happen to be in.

Run: python3 scripts/pr/test_settle.py
"""

import contextlib
import importlib.util
import io
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

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
            holds_base=True, held_by_worktree=False, unpublished_release=None):
    if results is None:
        results = [{"name": "Required checks (Unity)", "bucket": "pass"}]
    return settle.reasons_from(before, after, results, branch, base, holds_base, held_by_worktree,
                               unpublished_release)


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

    def test_Given_TheBaseHoldsAnUnpublishedRelease_When_EverythingElsePasses_Then_ItStillBlocks(self):
        # Arrange — a non-empty reason from the release guard, with every other input clean.
        unpublished = "v2.0.1 was never published"

        # Act
        decided = reasons(unpublished_release=unpublished)

        # Assert
        self.assertEqual(decided, [unpublished])

    def test_Given_TheHeadMovedAndTheBaseIsUnpublished_When_Decided_Then_BothAreReported(self):
        # Arrange — the force-push voids the readings about the head, not the one about the base.
        unpublished = "v2.0.1 was never published"

        # Act
        decided = reasons(after=MOVED, unpublished_release=unpublished)

        # Assert
        self.assertEqual(len(decided), 2)

    def test_Given_NoCheckEverRan_When_Decided_Then_ThatIsNotReadAsPending(self):
        # Arrange — no bucket at all, which the module docstring's fourth precondition reads as a
        # workflow never triggered rather than as one still to come.
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
        # Arrange — the local delete would otherwise fail once the merge had already happened.
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


class RepositorySlugTests(unittest.TestCase):
    """The remote forms that have to yield owner/name, since a wrong slug 404s only at merge time.

    Five of the eight were measured wrong before this: both trailing-slash forms, both host-alias
    forms, and the one naming no owner. A bare `alias:` is what a `Host` entry in ~/.ssh/config puts
    in remote.origin.url.
    """

    def test_Given_AnScpStyleRemote_When_Parsed_Then_ItIsOwnerAndName(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("git@github.com:s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_AnHttpsRemote_When_Parsed_Then_ItIsOwnerAndName(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet"),
                         "s4k10503/velvet")

    def test_Given_AnHttpsRemoteWithATrailingSlash_When_Parsed_Then_TheOwnerIsNotDropped(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet/"),
                         "s4k10503/velvet")

    def test_Given_ADotGitRemoteWithATrailingSlash_When_Parsed_Then_TheSuffixIsStillRemoved(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("https://github.com/s4k10503/velvet.git/"),
                         "s4k10503/velvet")

    def test_Given_AnSshUrlRemote_When_Parsed_Then_TheHostIsNotCountedAsTheOwner(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("ssh://git@github.com/s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_AnSshHostAlias_When_Parsed_Then_TheAliasIsNotKeptInTheSlug(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("gh:s4k10503/velvet.git"), "s4k10503/velvet")

    def test_Given_AnSshHostAliasCarryingDashes_When_Parsed_Then_TheAliasIsNotKeptInTheSlug(self):
        # Act / Assert
        self.assertEqual(settle.repository_slug("velvet-alias:s4k10503/velvet.git"),
                         "s4k10503/velvet")

    def test_Given_ARemoteNamingNoOwner_When_Parsed_Then_ItRaisesRatherThanReturningHalfASlug(self):
        # Act / Assert
        self.assertRaises(RuntimeError, settle.repository_slug, "https://github.com/velvet")


def runs(*entries):
    """A check-runs payload carrying one entry per (name, status, conclusion) triple."""
    listed = [{"name": name, "status": status, "conclusion": conclusion}
              for name, status, conclusion in entries]
    return {"total_count": len(listed), "check_runs": listed}


# What the commit-status endpoint answered for a commit carrying no status at all: a rollup state of
# "pending" beside an empty list. Reading the rollup rather than the entries blocks such a commit.
NO_STATUSES = {"state": "pending", "total_count": 0, "statuses": []}


class CheckResultTests(unittest.TestCase):
    """The buckets the decision is made from, built out of the two payloads that carry them."""

    def test_Given_ACompletedSuccess_When_Bucketed_Then_NothingBlocks(self):
        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), NO_STATUSES)

        # Assert
        self.assertEqual(reasons(results=results), [])

    def test_Given_AFailingConclusion_When_Decided_Then_TheMergeIsRefused(self):
        # Arrange — the whole point of the table: a conclusion that must never reach TERMINAL_PASS.
        results = settle.check_results(runs(("Unity", "completed", "failure")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(reasons(results=results), ["failing at aaaaaaa: Unity=fail"])

    def test_Given_EveryMappedConclusion_When_ComparedToTheTerminalSets_Then_OnlyThreeLetAMergeThrough(self):
        # Act
        passing = sorted(name for name, bucket in settle._BUCKET.items()
                         if bucket in settle.TERMINAL_PASS)

        # Assert
        self.assertEqual(passing, ["neutral", "skipped", "success"])

    def test_Given_AConclusionTheTableDoesNotCarry_When_Bucketed_Then_ItBlocks(self):
        # Arrange — GitHub adding a conclusion must not merge unclassified.
        results = settle.check_results(runs(("Unity", "completed", "invented_by_github")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results, [{"name": "Unity", "bucket": "fail"}])

    def test_Given_ARunStillQueued_When_Bucketed_Then_ItIsPendingRatherThanFailing(self):
        # Arrange — a run carrying no conclusion yet is unfinished, not one that concluded badly.
        results = settle.check_results(runs(("Unity", "queued", None)), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results, [{"name": "Unity", "bucket": "pending"}])

    def test_Given_ANameCarryingATab_When_Bucketed_Then_ItArrivesWhole(self):
        # Arrange — the name is read as a field, not split out of one line of text.
        results = settle.check_results(runs(("Unity\ttests", "completed", "success")), NO_STATUSES)

        # Act / Assert
        self.assertEqual(results[0]["name"], "Unity\ttests")

    def test_Given_ACommitCarryingNoStatusAtAll_When_Bucketed_Then_ThePendingRollupIsNotRead(self):
        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), NO_STATUSES)

        # Assert
        self.assertEqual(len(results), 1)

    def test_Given_AFailingLegacyCommitStatus_When_Decided_Then_ItBlocksLikeACheckRun(self):
        # Arrange — a required context can be a commit status instead of an Actions check run.
        statuses = {"state": "failure", "total_count": 1,
                    "statuses": [{"context": "external/ci", "state": "failure"}]}

        # Act
        results = settle.check_results(runs(("Unity", "completed", "success")), statuses)

        # Assert
        self.assertEqual(reasons(results=results), ["failing at aaaaaaa: external/ci=fail"])

    def test_Given_APageThatDidNotCarryEveryRun_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — the run that arrived passes, so nothing else in the decision would block.
        truncated = {"total_count": 2, "check_runs": [{"name": "Unity", "status": "completed",
                                                       "conclusion": "success"}]}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results, truncated, NO_STATUSES)

    def test_Given_APageThatDidNotCarryEveryCommitStatus_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — every entry that did arrive is passing, so `whole_page` is the only thing
        # between this payload and a green merge; its docstring owns why.
        truncated = {"state": "failure", "total_count": 31,
                     "statuses": [{"context": f"external/ci-{index}", "state": "success"}
                                  for index in range(30)]}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results,
                          runs(("Unity", "completed", "success")), truncated)

    def test_Given_APageThatCarriedNoRunAtAll_When_Bucketed_Then_ItRaisesRatherThanDeciding(self):
        # Arrange — a page that dropped every entry it claims, which is the one truncation whose
        # buckets are also what a head with no workflow leaves. `whole_page` is what separates them,
        # so an empty list must not be short-circuited past it.
        truncated = {"total_count": 3, "check_runs": []}

        # Act / Assert
        self.assertRaises(RuntimeError, settle.check_results, truncated, NO_STATUSES)


class CheckReadTests(unittest.TestCase):
    """The paths the two check surfaces are read from: neither leaves its page size to the API."""

    def test_Given_BothCheckSurfaces_When_Read_Then_EachPathAsksForAPageSize(self):
        # Arrange
        asked = []
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(settle, "repository", lambda *_: "owner/name"))
            stack.enter_context(mock.patch.object(settle, "head_sha", lambda *_: GREEN))
            stack.enter_context(mock.patch.object(
                settle, "rest_json", lambda path: (asked.append(path), NO_STATUSES)[1]))

            # Act
            settle.checks(Path("."), 592)

        # Assert
        self.assertEqual(asked, [f"repos/owner/name/commits/{GREEN}/check-runs?per_page=100",
                                 f"repos/owner/name/commits/{GREEN}/status?per_page=100"])


class RepositoryReadTests(unittest.TestCase):
    """Which checkout the slug is read from, since --project points this at one that is not the cwd."""

    def test_Given_AProjectThatIsNotTheCwd_When_TheSlugIsRead_Then_ItComesFromThatCheckout(self):
        # Arrange
        with repository_holding("topic") as project:
            subprocess.run(["git", "-C", str(project), "remote", "add", "origin",
                            "https://github.com/elsewhere/other.git"], capture_output=True, check=True)

            # Act / Assert
            self.assertEqual(settle.repository(project), "elsewhere/other")


def stubbed_readings(head=GREEN, branch="topic", title="A title", body="A body"):
    """Every reading settle.merge takes, answered without a network — `repository` included.

    Left real, `repository` shells out to git in whatever directory the tests were started from. In
    a copy of scripts/ outside a checkout it raised, and it raised inside the merge call before any
    assertion about that call — reporting as an error rather than a failure, so a run that stubbed
    only the other readings looked like it had proved something and had not.
    """
    return [mock.patch.object(settle, "repository", lambda *_: "owner/name"),
            mock.patch.object(settle, "blocking_reasons",
                              lambda *_: settle.Blocking([], head, branch)),
            mock.patch.object(settle, "pull_request_text", lambda *_: (title, body))]


def merge_request(**readings):
    """The arguments settle.merge hands `gh`, with the cleanup stubbed out too."""
    sent = []
    with contextlib.ExitStack() as stack:
        for patch in stubbed_readings(**readings):
            stack.enter_context(patch)
        stack.enter_context(mock.patch.object(settle, "delete_merged_branch", lambda *_: []))
        stack.enter_context(mock.patch.object(settle, "gh",
                                              lambda *args: (sent.append(args), "")[1]))
        stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
        settle.merge(Path("."), 592, "main", dry_run=False)
    return sent[0]


class MergeRequestTests(unittest.TestCase):
    """What the merge request itself carries, which no reading of the decision would catch."""

    def test_Given_AMergeNothingBlocks_When_Sent_Then_ItCarriesTheHeadTheChecksWereReadAt(self):
        # Arrange — a push landing after the last reading has to lose, not win by arriving late.
        sent = merge_request()

        # Act / Assert
        self.assertIn("sha={}".format(GREEN), sent)

    def test_Given_AMergeNothingBlocks_When_Sent_Then_TheBodyIsThePullRequestsOwnDescription(self):
        # Arrange — left out, GitHub composes one from the branch commits and repeats their trailers.
        sent = merge_request(body="What changed and why")

        # Act / Assert
        self.assertIn("commit_message=What changed and why", sent)

    def test_Given_AMergeThatWentThrough_When_ItReturns_Then_TheBranchIsDeleted(self):
        # Arrange
        deleted = []
        with contextlib.ExitStack() as stack:
            for patch in stubbed_readings():
                stack.enter_context(patch)
            stack.enter_context(mock.patch.object(settle, "gh", lambda *args: ""))
            stack.enter_context(mock.patch.object(
                settle, "delete_merged_branch", lambda project, branch: (deleted.append(branch),
                                                                         [])[1]))
            stack.enter_context(contextlib.redirect_stdout(io.StringIO()))
            settle.merge(Path("."), 592, "main", dry_run=False)

        # Act / Assert
        self.assertEqual(deleted, ["topic"])

    def test_Given_AMergeThatWentThrough_When_ItReturns_Then_ItSaysTheMergeHappened(self):
        # Arrange — a dry run that decided nothing and a merge that landed both exit 0.
        printed = io.StringIO()
        with contextlib.ExitStack() as stack:
            for patch in stubbed_readings():
                stack.enter_context(patch)
            stack.enter_context(mock.patch.object(settle, "gh", lambda *args: ""))
            stack.enter_context(mock.patch.object(settle, "delete_merged_branch", lambda *_: []))
            stack.enter_context(contextlib.redirect_stdout(printed))
            settle.merge(Path("."), 592, "main", dry_run=False)

        # Act / Assert
        self.assertIn("PR#592 merged:", printed.getvalue())


@contextlib.contextmanager
def repository_holding(branch):
    """A throwaway repository on `main` with `branch` also present, since deletion needs a real one."""
    with tempfile.TemporaryDirectory(prefix="settle-test-") as directory:
        project = Path(directory)
        git = ["git", "-C", str(project), "-c", "user.email=t@example.com", "-c", "user.name=t"]
        subprocess.run(["git", "init", "-b", "main", str(project)], capture_output=True, check=True)
        subprocess.run(git + ["commit", "--allow-empty", "-m", "init"],
                       capture_output=True, check=True)
        subprocess.run(git + ["branch", branch], capture_output=True, check=True)
        yield project


def local_branches(project):
    listing = subprocess.run(["git", "-C", str(project), "for-each-ref", "--format=%(refname:short)",
                              "refs/heads"], capture_output=True, text=True, check=True)
    return sorted(listing.stdout.split())


class LocalBranchDeletionTests(unittest.TestCase):
    """The half `gh pr merge --delete-branch` did and a REST ref delete cannot reach."""

    def test_Given_AMergedBranchPresentLocally_When_Deleted_Then_ItIsGoneFromTheCheckout(self):
        # Arrange
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: ""):
                settle.delete_merged_branch(project, "topic")

            # Assert
            self.assertEqual(local_branches(project), ["main"])

    def test_Given_ABranchNeverCheckedOutLocally_When_Deleted_Then_NothingIsReported(self):
        # Arrange — work done from a detached worktree leaves no local ref, which is not a failure.
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: ""):
                failures = settle.delete_merged_branch(project, "never-existed")

            # Assert
            self.assertEqual(failures, [])

    def test_Given_ARemoteDeleteThatFailed_When_ItReturns_Then_TheFailureIsReportedNotSwallowed(self):
        # Arrange
        with repository_holding("topic") as project:
            # Act
            with mock.patch.object(settle, "delete_remote_ref", lambda *_: "HTTP 403"):
                failures = settle.delete_merged_branch(project, "topic")

            # Assert
            self.assertEqual(failures, ["the remote branch topic survived: HTTP 403"])


class BranchDeletionTests(unittest.TestCase):
    def test_Given_ARefDeleteThatFoundNothing_When_Read_Then_ItIsNotReportedAsAFailure(self):
        # Arrange — the repository deletes the head on merge, so this DELETE usually arrives second.
        stderr = "gh: Reference does not exist (HTTP 422)"

        # Act / Assert
        self.assertTrue(settle.reference_already_gone(stderr))

    def test_Given_ARefDeleteRefusedForAnyOtherReason_When_Read_Then_ItIsReported(self):
        # Act / Assert
        self.assertFalse(settle.reference_already_gone("gh: Resource not accessible (HTTP 403)"))


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

#!/usr/bin/env python3
"""Unit tests for .claude/hooks/refuse/merge_unproven_head.py's verdict on a head's own checks.

Nothing posed this guard a check list before. The harnesses over this directory ask what a guard
does when a reading fails, where its command is placed and which base it judges, and the defects
below sat on `main` with all of them green. So what is posed here is the list itself, against a `gh`
on PATH that answers.

The guard reads the head, then the checks, then the head again, and a failure past the first of those
reached no refusal at all. A second head reading that failed made the guard format None and raise; a
list entry carrying no bucket raised on the lookup; and a check list printed under a non-zero exit
was taken for no list and refused as a workflow that was never triggered, which is a sentence about a
state that had not happened. `lib/repository.py` owns what a raising hook costs.

`skipping` is a verdict of its own here rather than a shade of `pass`, because this repository's
Unity jobs are skipped for want of a licence, and a guard refusing a skipped check would refuse the
merge wherever that happens -- which is the ordinary state of a fork.

Run: python3 scripts/hooks/test_merge_unproven_head.py
"""

import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/refuse/merge_unproven_head.py"

REFUSED = 2
ALLOWED = 0

MERGE = "gh pr merge 7 --squash --delete-branch"

HEAD = "abcdef1234567890"
MOVED = "0123456789abcdef"

# The four readings the guard makes, so a verdict of `ALLOWED` can be told from a guard that never
# recognised the command and read nothing.
READINGS = 4

# `gh` as this fixture answers it: the head with its labels, the check list, the head's workflow runs,
# then the head again, told apart by a call count kept on disk. An empty `VELVET_HEAD_AGAIN` is the
# second head reading failing.
STUB_GH = """#!/bin/sh
calls=0
[ -r "$VELVET_CALLS" ] && read calls < "$VELVET_CALLS"
calls=$((calls + 1))
printf '%s\\n' "$calls" > "$VELVET_CALLS"
[ "$1" = api ] && { printf '%s' "$VELVET_RUNS"; exit 0; }
case "$2" in
  view)
    [ "$calls" -eq 1 ] && { printf '{"headRefOid":"%s","labels":%s}\\n' "$VELVET_HEAD" "$VELVET_LABELS"; exit 0; }
    [ -z "$VELVET_HEAD_AGAIN" ] && { printf 'gh: HTTP 502\\n' >&2; exit 1; }
    printf '%s\\n' "$VELVET_HEAD_AGAIN_PAYLOAD"; exit 0 ;;
  checks)
    printf '%s' "$VELVET_CHECKS"
    exit "$VELVET_CHECKS_EXIT" ;;
esac
exit 0
"""

PASSING = json.dumps([{"name": "Required checks (Unity)", "bucket": "pass"}])
SKIPPED = json.dumps([{"name": "Required checks (Unity)", "bucket": "skipping"}])
PENDING = json.dumps([{"name": "Required checks (Unity)", "bucket": "pending"}])
BUCKETLESS = json.dumps([{"name": "Required checks (Unity)"}])

UNLABELLED = json.dumps([])
LABELLED = json.dumps([{"name": "automerge"}])


def workflow_runs(*campaigns, path=".github/workflows/mutation.yml"):
    """The head's workflow runs: Test's, and one of `path` per (number, status, conclusion)."""
    listed = [{"run_number": 40, "path": ".github/workflows/test.yml", "status": "completed",
               "conclusion": "success"}]
    listed += [{"run_number": number, "path": path, "status": status, "conclusion": conclusion}
               for number, status, conclusion in campaigns]
    return json.dumps({"total_count": len(listed), "workflow_runs": listed})


class HeadCheckVerdictTests(unittest.TestCase):
    """Putting `MERGE` to the guard against a `gh` that answers as each case arranges."""

    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-unproven-head-"))
        self.binaries = self.root / "bin"
        self.binaries.mkdir()
        (self.binaries / "gh").write_text(STUB_GH, encoding="utf-8")
        (self.binaries / "gh").chmod(0o755)
        self.calls = self.root / "calls"

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def ask(self, checks=PASSING, head_again=HEAD, checks_exit=0, head_again_payload=None,
            labels=UNLABELLED, runs=None):
        """What the guard does with `MERGE`, against a `gh` answering as given."""
        environment = dict(os.environ)
        environment["PATH"] = str(self.binaries) + os.pathsep + environment.get("PATH", "")
        environment.update(VELVET_CALLS=str(self.calls), VELVET_HEAD=HEAD,
                           VELVET_HEAD_AGAIN=head_again, VELVET_CHECKS=checks,
                           VELVET_CHECKS_EXIT=str(checks_exit), VELVET_LABELS=labels,
                           VELVET_RUNS=workflow_runs() if runs is None else runs,
                           VELVET_HEAD_AGAIN_PAYLOAD=(head_again_payload if head_again_payload is not None
                                                      else json.dumps({"headRefOid": head_again})))
        event = {"tool_name": "Bash", "cwd": str(self.root), "tool_input": {"command": MERGE}}
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(event),
                              capture_output=True, text=True, env=environment, timeout=120)

    def consulted(self):
        """How many times the guard asked gh anything."""
        return int(self.calls.read_text().strip()) if self.calls.exists() else 0

    # GREEN_ON_BASE(characterization): the base refuses an empty check list too.
    # It is the verdict the unread-list arm beside it must not swallow.
    def test_Given_ACheckListNamingNoCheck_When_TheMergeIsAsked_Then_ItIsRefusedAsNeverTriggered(self):
        # Arrange / Act
        result = self.ask(checks="[]")

        # Assert — the sentence rides in the comparison because the other arms refuse too, and
        # each of them names a state this one is not.
        self.assertEqual((result.returncode, f"no check ran for {HEAD[:7]}" in result.stderr),
                         (REFUSED, True))

    # GREEN_ON_BASE(characterization): the base lets a passing head through.
    # That is the side no refusal added here may take.
    def test_Given_EveryCheckPassing_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange / Act
        result = self.ask(checks=PASSING)

        # Assert — the reading count rides in the comparison because a guard that recognised nothing
        # reports `ALLOWED` too, having asked gh nothing at all.
        self.assertEqual((result.returncode, self.consulted()), (ALLOWED, READINGS))

    # GREEN_ON_BASE(characterization): the base lets a skipped head through as well.
    # That is the merge a fork asks for, its Unity jobs skipped for want of a licence.
    def test_Given_EveryCheckSkipped_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange / Act
        result = self.ask(checks=SKIPPED)

        # Assert — the reading count rides along for the reason the case above states.
        self.assertEqual((result.returncode, self.consulted()), (ALLOWED, READINGS))

    # GREEN_ON_BASE(characterization): the base names a moved head too.
    # It is the arm the unread head is now placed ahead of.
    def test_Given_AHeadThatMovedWhileItsChecksWereRead_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act — the checks pass, so nothing but the move is left to refuse on.
        result = self.ask(checks=PASSING, head_again=MOVED)

        # Assert
        self.assertEqual((result.returncode,
                          f"head moved from {HEAD[:7]} to {MOVED[:7]}" in result.stderr),
                         (REFUSED, True))

    def test_Given_AHeadReadingThatFailsAfterTheChecks_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act — gh answers the head and the checks and then fails, which the unreadable-
        # state harness does not pose: it breaks every call, and the first one failing takes the
        # guard out through the arm that declines to invent an answer.
        result = self.ask(checks=PASSING, head_again="")

        # Assert — the sentence rides in the comparison because a reading that spelled the unread
        # head into the moved-head arm would refuse too, naming a move that did not happen.
        self.assertEqual((result.returncode, "could not be read again" in result.stderr),
                         (REFUSED, True))

    def test_Given_ASecondHeadResponseWithoutAnOid_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act
        result = self.ask(head_again_payload='{"unrelated": "value"}')

        # Assert
        self.assertEqual((result.returncode, "could not be read again" in result.stderr),
                         (REFUSED, True))

    def test_Given_ACheckListPrintedUnderANonZeroExit_When_TheMergeIsAsked_Then_TheUnpassedCheckIsNamed(self):
        # Arrange / Act — the list names a check that has not passed, and the call reporting it
        # exits non-zero.
        result = self.ask(checks=PENDING, checks_exit=8)

        # Assert
        self.assertEqual((result.returncode,
                          f"not passing at {HEAD[:7]}: Required checks (Unity)" in result.stderr),
                         (REFUSED, True))

    def test_Given_ACheckListEntryCarryingNoBucket_When_TheMergeIsAsked_Then_TheUnreadListIsNamed(self):
        # Arrange / Act — a list gh printed and this cannot decide from, which is neither a check
        # that has not passed nor a head no workflow ran for.
        result = self.ask(checks=BUCKETLESS)

        # Assert
        self.assertEqual((result.returncode,
                          f"the check list for {HEAD[:7]} could not be read" in result.stderr),
                         (REFUSED, True))

    def test_Given_AChecksReadingThatPrintedNoList_When_TheMergeIsAsked_Then_TheUnreadListIsNamed(self):
        # Arrange / Act — nothing on stdout, which is a list nobody read rather than a head nobody
        # ran a workflow for.
        result = self.ask(checks="", checks_exit=1)

        # Assert
        self.assertEqual((result.returncode,
                          f"the check list for {HEAD[:7]} could not be read" in result.stderr),
                         (REFUSED, True))


    def test_Given_ALabelledHeadNoCampaignRanOn_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act — every check `gh pr checks` lists passes.
        result = self.ask(labels=LABELLED)

        # Assert
        self.assertEqual((result.returncode,
                          f"no mutation.yml run has measured {HEAD[:7]}" in result.stderr),
                         (REFUSED, True))

    def test_Given_ALabelledHeadWhoseCampaignPassed_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange / Act
        result = self.ask(labels=LABELLED, runs=workflow_runs((2, "completed", "success")))

        # Assert — the reading count rides along for the reason the passing case above states.
        self.assertEqual((result.returncode, self.consulted()), (ALLOWED, READINGS))

    def test_Given_ALabelledHeadWhoseOnlyCampaignNamedRunIsAnotherWorkflows_When_Asked_Then_ItIsRefused(self):
        # Arrange / Act — a skipped run of another workflow, which a campaign job's name could label.
        result = self.ask(labels=LABELLED, runs=workflow_runs(
            (2, "completed", "skipped"), path=".github/workflows/test.yml"))

        # Assert
        self.assertEqual((result.returncode,
                          f"no mutation.yml run has measured {HEAD[:7]}" in result.stderr),
                         (REFUSED, True))

    def test_Given_AnUnlabelledHeadWhoseCampaignFailed_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act — a campaign that ran on the head has to pass whether or not one was owed.
        result = self.ask(runs=workflow_runs((2, "completed", "failure")))

        # Assert
        self.assertEqual((result.returncode,
                          f"its mutation.yml run on {HEAD[:7]} failed" in result.stderr),
                         (REFUSED, True))

    def test_Given_AnUnlabelledHeadWhoseCampaignIsRunning_When_TheMergeIsAsked_Then_ItIsRefused(self):
        # Arrange / Act — a dispatched run carries no check `gh pr checks` lists.
        result = self.ask(runs=workflow_runs((2, "queued", None)))

        # Assert
        self.assertEqual((result.returncode,
                          f"its mutation.yml run on {HEAD[:7]} has not finished" in result.stderr),
                         (REFUSED, True))

    def test_Given_ANewerCampaignThatPassedListedFirst_When_TheMergeIsAsked_Then_ItIsLetThrough(self):
        # Arrange / Act — the older one was cancelled on the same head, and a reader taking the
        # page's last entry takes it.
        result = self.ask(labels=LABELLED, runs=workflow_runs((3, "completed", "success"),
                                                              (2, "completed", "cancelled")))

        # Assert
        self.assertEqual((result.returncode, self.consulted()), (ALLOWED, READINGS))

    def test_Given_WorkflowRunsThatCameBackPartial_When_TheMergeIsAsked_Then_TheUnreadRunsAreNamed(self):
        # Arrange / Act — the page claims a run it did not carry, which may be the campaign.
        runs = json.dumps({"total_count": 2, "workflow_runs": []})
        result = self.ask(runs=runs)

        # Assert
        self.assertEqual((result.returncode,
                          f"the workflow runs of {HEAD[:7]} could not all be read" in result.stderr),
                         (REFUSED, True))

    def test_Given_LabelsThatCameBackUnreadable_When_TheMergeIsAsked_Then_TheyAreNamed(self):
        # Arrange / Act
        result = self.ask(labels=json.dumps("automerge"))

        # Assert
        self.assertEqual((result.returncode, "its labels could not be read" in result.stderr),
                         (REFUSED, True))

if __name__ == "__main__":
    unittest.main()

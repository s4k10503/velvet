#!/usr/bin/env python3
"""Unit tests for unreadable_state_check.py, plus the check over this repository's own guards.

Each synthetic guard below is one shape a failed reading takes in `.claude/hooks/refuse` today: a
`return 0` after an empty read, a helper answering None that the caller reads as "nothing found", a
successful call that printed nothing, and — the one a reading over the source gets wrong — a git
exit code of 1 that is a legitimate negative answer rather than a failure.

The Stop guards are held to the same declaration and to one thing more, which is the shape their own
defect took: blocking is not enough if what the block says is a claim about the subject rather than
about the reading. The last class runs a guard from the tree rather than a synthetic one, because
the branch it exercises refuses every editing tool in every session and, until this branch, offered
no deferral to get past it.

Run: python3 scripts/hooks/test_unreadable_state_check.py
"""

import importlib.util
import json
import shutil
import tempfile
import time
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports unreadable_state_check by path, since scripts/hooks is not a package."""
    spec = importlib.util.spec_from_file_location(
        "unreadable_state_check", Path(__file__).with_name("unreadable_state_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check = load_module()

PREAMBLE = '''#!/usr/bin/env python3
"""A synthetic guard."""
import json
import subprocess
import sys

HOOK_TOOLS = {"Bash"}
'''

# `capture_output` keeps the stub's own stderr off this process's, so a failure the check reports
# reads as the check's own line rather than as the stub's message.
READ_GH = 'subprocess.run(["gh", "api", "x"], capture_output=True, text=True)'
READ_GIT = 'subprocess.run(["git", "rev-parse", "x"], capture_output=True, text=True)'


def guard(policy, body, reason="", probe='{"command": "probe"}'):
    return (PREAMBLE
            + (f"\n# {reason}\n" if reason else "\n")
            + f'UNREADABLE_POLICY = "{policy}"\n'
            + f"UNREADABLE_PROBE = {probe}\n\n\n"
            + "def main():\n"
            + "    event = json.load(sys.stdin)\n"
            + "    if event.get('tool_name') not in HOOK_TOOLS:\n"
            + "        return 0\n"
            + body
            + "\n\nsys.exit(main())\n")


# Reads the pull request's head and treats an empty answer as nothing to refuse — stale_merge.py's
# shape before this check existed.
FAILS_OPEN = f"""    head = {READ_GH}.stdout.strip()
    if not head:
        return 0
    return 2
"""

HOLDS = f"""    head = {READ_GH}.stdout.strip()
    if not head:
        return 2
    return 2
"""

# Refuses on an error and reads a successful empty answer as an answer, which is what a renamed
# JSON field leaves behind: the command succeeds and `--jq` selects nothing.
HOLDS_ON_ERROR_ONLY = f"""    done = {READ_GH}
    if done.returncode != 0:
        return 2
    return 0 if not done.stdout.strip() else 2
"""

READS_NOTHING = "    return 2\n"

STAYS_OUT = "    return 0\n"

READS_GIT = f"""    done = {READ_GIT}
    return 2 if done.returncode != 0 else 0
"""

# shared_git_state.py's shape: git exiting 1 is `rev-parse --verify --quiet` saying the ref is not
# there, which is an answer, and the guard's refusal is what it does with it.
NEGATIVE_IS_AN_ANSWER = f"""    done = {READ_GIT}
    return 0 if done.returncode == 1 else 2
"""

ALLOWS = f"""    {READ_GH}
    return 0
"""


def directory(**guards):
    root = Path(tempfile.mkdtemp(prefix="velvet-unreadable-tests-"))
    for name, source in guards.items():
        (root / (name + ".py")).write_text(source, encoding="utf-8")
    return root


def faults(root):
    return check.faults(root, root, floor=0)


class DeclarationTests(unittest.TestCase):
    def test_Given_AGuardDeclaringNothing_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(quiet=PREAMBLE + "\nsys.exit(0)\n")

        # Act
        found = [line for line in faults(root) if "UNREADABLE_POLICY must be one of" in line]

        # Assert
        self.assertEqual(len(found), 1, faults(root))

    def test_Given_AGuardDeclaringAllowWithNoCommentAboveIt_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange — the sibling is there so the missing comment is the only fault left.
        root = directory(lenient=guard("allow", ALLOWS), holds=guard("refuse", HOLDS))

        # Act
        found = [line for line in faults(root) if "no comment above it" in line]

        # Assert
        self.assertEqual(len(found), 1, faults(root))


class VerdictTests(unittest.TestCase):
    def test_Given_AGuardReadingAnEmptyAnswerAsNothingToRefuse_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(quiet=guard("refuse", FAILS_OPEN))

        # Act
        found = [line for line in faults(root) if 'answers "allow"' in line]

        # Assert — every mode that reaches the read reports it, so the count rides along.
        self.assertEqual(len(found), 2, faults(root))

    def test_Given_TheSameGuardRefusingOnThatAnswer_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(quiet=guard("refuse", HOLDS))

        # Act
        found = faults(root)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AGuardHoldingOnlyWhenTheReadErrors_When_TheCheckRuns_Then_TheEmptyAnswerIsReported(self):
        # Arrange
        root = directory(partial=guard("refuse", HOLDS_ON_ERROR_ONLY))

        # Act
        found = [line for line in faults(root) if "gh-empty" in line]

        # Assert
        self.assertEqual(len(found), 1, faults(root))

    def test_Given_AGuardWhoseRefusalIsWhatGitsNegativeAnswerMeans_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(negative=guard("refuse", NEGATIVE_IS_AN_ANSWER))

        # Act
        found = faults(root)

        # Assert
        self.assertEqual(found, [])


class ScopeTests(unittest.TestCase):
    def test_Given_AGuardDeclaringNoneThatConsultsGit_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(reader=guard("none", READS_GIT))

        # Act
        found = [line for line in faults(root) if 'declares "none"' in line]

        # Assert
        self.assertEqual(len(found), 1, faults(root))

    def test_Given_AGuardDeclaringNoneThatConsultsNeither_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(textual=guard("none", READS_NOTHING))

        # Act
        found = faults(root)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AGuardWhoseProbeReachesNeitherProgram_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange — a "refuse" that no reading takes part in says nothing about an unreadable state.
        root = directory(textual=guard("refuse", READS_NOTHING))

        # Act
        found = [line for line in faults(root) if "never happens" in line]

        # Assert
        self.assertEqual(len(found), 1, faults(root))


class BackingTests(unittest.TestCase):
    def test_Given_AGuardDeclaringAllowAndNoSiblingRefusingItsProbe_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(lenient=guard("allow", ALLOWS, reason="nothing holds this"),
                         elsewhere=guard("none", STAYS_OUT, probe='{"command": "other"}'))

        # Act
        found = [line for line in faults(root) if "guarded by nothing at all" in line]

        # Assert
        self.assertEqual(len(found), 2, faults(root))

    def test_Given_AGuardDeclaringAllowAndASiblingRefusingItsProbe_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(lenient=guard("allow", ALLOWS, reason="the sibling holds this"),
                         holds=guard("refuse", HOLDS))

        # Act
        found = faults(root)

        # Assert
        self.assertEqual(found, [])


STOP_PREAMBLE = '''#!/usr/bin/env python3
"""A synthetic Stop guard."""
import subprocess
import sys
'''

STOP_READ = 'done = subprocess.run(["gh", "pr", "list"], capture_output=True, text=True)'


def stop_guard(policy, body, allows=None, reason=""):
    return (STOP_PREAMBLE
            + f'\n\nUNREADABLE_POLICY = "{policy}"\n'
            + ("" if allows is None
               else (f"\n# {reason}\n" if reason else "\n") + f"UNREADABLE_ALLOWS = {allows}\n")
            + "\n\ndef main():\n"
            + body
            + "\n\nsys.exit(main())\n")


# Lets the session end when nothing answered, which is what a cleared subject also does.
STOP_FAILS_OPEN = f"""    {STOP_READ}
    if done.returncode != 0:
        return 0
    return 2
"""

# Blocks, and describes the subject rather than the reading — the shape both Stop guards shipped.
STOP_BLOCKS_MUTE = f"""    {STOP_READ}
    if done.returncode != 0:
        print("nothing here says they are settled", file=sys.stderr)
        return 2
    return 0
"""

STOP_BLOCKS_SAYING = f"""    {STOP_READ}
    if done.returncode != 0:
        print("{check.SELF_REPORT} them", file=sys.stderr)
        return 2
    return 0
"""

STOP_READS_NOTHING = "    return 2\n"

# Blocks when nothing answered, and lets the session end when the listing did — open_backlog.py's
# shape, which is right there and would be a silent pass anywhere else.
STOP_PARTIAL_ALLOWS = f"""    listing = subprocess.run(["gh", "api", "pulls"], capture_output=True, text=True)
    if listing.returncode == 0:
        return 0
    print("{check.SELF_REPORT} them", file=sys.stderr)
    return 2
"""


def stop_faults(root):
    return check.stop_faults(root, root, floor=0)


class StopGuardTests(unittest.TestCase):
    def test_Given_AStopGuardEndingTheSessionWhenNothingAnswered_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(lenient=stop_guard("refuse", STOP_FAILS_OPEN))

        # Act
        found = [line for line in stop_faults(root) if 'answers "allow"' in line]

        # Assert — a number rather than the table's own length, which would shrink with it and
        # agree all the way down to the one mode that missed the defect this branch is about.
        self.assertEqual(len(found), 2, stop_faults(root))

    def test_Given_AStopGuardBlockingWithoutSayingTheReadingFailed_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(mute=stop_guard("refuse", STOP_BLOCKS_MUTE))

        # Act
        found = [line for line in stop_faults(root) if "fact about its subject" in line]

        # Assert — a number rather than the table's own length, which would shrink with it and
        # agree all the way down to the one mode that missed the defect this branch is about.
        self.assertEqual(len(found), 2, stop_faults(root))

    def test_Given_AStopGuardThatSaysTheReadingFailed_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(plain=stop_guard("refuse", STOP_BLOCKS_SAYING))

        # Act
        found = stop_faults(root)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AStopGuardDeclaringNothing_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(quiet=STOP_PREAMBLE + "\nsys.exit(0)\n")

        # Act
        found = [line for line in stop_faults(root) if "UNREADABLE_POLICY must be one of" in line]

        # Assert
        self.assertEqual(len(found), 1, stop_faults(root))

    def test_Given_AStopGuardThatReadsNeitherProgram_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange — a "refuse" no reading takes part in says nothing about an unreadable state.
        root = directory(textual=stop_guard("refuse", STOP_READS_NOTHING))

        # Act
        found = [line for line in stop_faults(root) if "never reaches gh" in line]

        # Assert
        self.assertEqual(len(found), 1, stop_faults(root))

    def test_Given_AStopGuardDeclaringAnExemptionWithNoCommentAboveIt_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange
        root = directory(lenient=stop_guard("refuse", STOP_BLOCKS_SAYING,
                                            allows='("gh-graphql-error",)'))

        # Act
        found = [line for line in stop_faults(root) if "no comment above it" in line]

        # Assert
        self.assertEqual(len(found), 1, stop_faults(root))

    def test_Given_AnExemptionNoSiblingRefusesUnder_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange — the exemption alone would let the session end with the failed reading unsaid.
        root = directory(lenient=stop_guard("refuse", STOP_PARTIAL_ALLOWS,
                                            allows='("gh-graphql-error",)',
                                            reason="the sibling refuses there"))

        # Act
        found = [line for line in stop_faults(root) if "no sibling refuses there" in line]

        # Assert
        self.assertEqual(len(found), 1, stop_faults(root))

    def test_Given_AnExemptionWithASiblingRefusingUnderIt_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange
        root = directory(lenient=stop_guard("refuse", STOP_PARTIAL_ALLOWS,
                                            allows='("gh-graphql-error",)',
                                            reason="the sibling refuses there"),
                         holds=stop_guard("refuse", STOP_BLOCKS_SAYING))

        # Act
        found = stop_faults(root)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnExemptionForAModeTheGuardRefusesIn_When_TheCheckRuns_Then_ItIsReportedStale(self):
        # Arrange
        root = directory(lenient=stop_guard("refuse", STOP_BLOCKS_SAYING,
                                            allows='("gh-graphql-error",)',
                                            reason="claimed, and not what it does"))

        # Act
        found = [line for line in stop_faults(root) if "the declaration is stale" in line]

        # Assert
        self.assertEqual(len(found), 1, stop_faults(root))

    def test_Given_ThisRepositorysStopGuards_When_NoReadingAnswers_Then_EachAnswersWhatItDeclares(self):
        # Arrange
        stop_directory = REPO_ROOT / check.STOP_DIRECTORY

        # Act
        found = check.stop_faults(stop_directory, REPO_ROOT)

        # Assert — the guard count rides along, because an empty directory disagrees with nothing.
        self.assertEqual((len(check.guards(stop_directory)) >= check.STOP_FLOOR, found),
                         (True, []))


class RepositoryTests(unittest.TestCase):
    def test_Given_ThisRepositorysGuards_When_EachIsPosedItsOwnProbe_Then_EachAnswersWhatItDeclares(self):
        # Arrange
        refuse_directory = REPO_ROOT / check.REFUSE_DIRECTORY

        # Act
        found = check.faults(refuse_directory, REPO_ROOT)

        # Assert — the guard count rides along, because an empty directory disagrees with nothing.
        self.assertEqual((len(check.guards(refuse_directory)) >= check.GUARD_FLOOR, found),
                         (True, []))


# `gh pr checks --json` exits 1 on a head with no check at all, the same code it exits when it could
# not read, so a guard reading the code alone cannot tell a pull request in its first minutes from an
# exhausted quota. These stubs pose both, with the rollup answering or not.
NO_CHECKS_AND_A_ROLLUP = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  "pr checks") echo "no checks reported on the 'topic' branch" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*)
                   case "$*" in
                     *--jq*) echo 0 ;;
                     *) echo '{"statusCheckRollup":[]}' ;;
                   esac ;;
                 *) echo CLEAN ;;
               esac ;;
  *)           exit 0 ;;
esac
"""

# The rollup answers, and what it answers is not a list. This stub applies `--jq` the way gh would,
# so a guard that asks for `| length` is handed the 0 jq gives for a null and a guard that reads the
# payload is handed the null itself — the two readings of one answer, which is the whole question.
NO_CHECKS_AND_A_NULL_ROLLUP = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  "pr checks") echo "no checks reported on the 'topic' branch" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*)
                   case "$*" in
                     *--jq*) echo 0 ;;
                     *) echo '{"statusCheckRollup":null}' ;;
                   esac ;;
                 *) echo CLEAN ;;
               esac ;;
  *)           exit 0 ;;
esac
"""

NO_CHECKS_AND_NO_ROLLUP = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  *)           echo "GraphQL: API rate limit already exceeded" >&2; exit 1 ;;
esac
"""


# The rollup fails while the merge state answers, which is where reading a failed check read as an
# empty one stops merely mis-wording and starts asserting a fact nothing established.
NO_CHECKS_AND_A_MERGE_STATE = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  "pr checks") echo "GraphQL: API rate limit already exceeded" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*) echo "GraphQL: API rate limit already exceeded" >&2; exit 1 ;;
                 *) echo CLEAN ;;
               esac ;;
  *)           exit 0 ;;
esac
"""


# The head really carries no check — the rollup says so — and the merge state read then fails. This
# is the one state that reaches `merge_state`'s own answer, and where an empty string standing for
# both "GitHub named none" and "the read failed" makes an unread pull request settled.
NO_CHECKS_AND_NO_MERGE_STATE = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  "pr checks") echo "no checks reported on the 'topic' branch" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*) echo '{"statusCheckRollup":[]}' ;;
                 *) echo "GraphQL: API rate limit already exceeded" >&2; exit 1 ;;
               esac ;;
  *)           exit 0 ;;
esac
"""


# The merge state read succeeds and names nothing, which is what a renamed field leaves behind. It
# is the one state that reaches EXPECTED_WITHOUT_CHECKS with an empty string in hand.
NO_CHECKS_AND_AN_UNNAMED_MERGE_STATE = """#!/bin/sh
case "$1 $2" in
  "pr list")   echo 51 ;;
  "pr checks") echo "no checks reported on the 'topic' branch" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*) echo '{"statusCheckRollup":[]}' ;;
                 *) exit 0 ;;
               esac ;;
  *)           exit 0 ;;
esac
"""


class ZeroCheckTests(unittest.TestCase):
    """A head no workflow has run for yet, which `gh pr checks` reports by failing.

    Read as a failed reading it blocks every pull request in its first minutes and takes the whole
    zero-check branch out of reach; read as an answer without asking anything else, an exhausted
    quota walks back in as "no checks".
    """

    GUARD = REPO_ROOT / ".claude/hooks/stop/unsettled_pr.py"

    def said(self, stub):
        home = Path(tempfile.mkdtemp(prefix="velvet-zero-check-home-"))
        try:
            _, _, errors, _ = check.run_guard(self.GUARD, check.STOP_PAYLOAD, "probe", REPO_ROOT,
                                              home, {"probe": {"gh": stub}})
            return errors
        finally:
            shutil.rmtree(home, ignore_errors=True)

    def test_Given_AHeadWithNoCheckAtAll_When_TheRollupAnswers_Then_ItIsNotCalledUnread(self):
        # Arrange — the rollup says zero, so the answer was "none" and the reading did not fail.
        said = self.said(NO_CHECKS_AND_A_ROLLUP)

        # Act / Assert — the zero-check reminder, and not the sentence for a guard that read nothing.
        self.assertEqual(("no checks apply to it" in said, check.SELF_REPORT in said), (True, False))

    def test_Given_TheSameFailureWithNoRollupEither_When_ItIsJudged_Then_ThePullRequestIsNamed(self):
        # Arrange — nothing answered at all. This one carries the per-pull-request half rather than
        # anything about `checks_of`: the two cases either side of it are what separate a failed
        # check read from an answered-empty one, and both shapes of that read leave this green. What
        # it holds is that the block names the pull request it could not read, rather than stopping
        # at the listing.
        said = self.said(NO_CHECKS_AND_NO_ROLLUP)

        # Act / Assert
        self.assertEqual((check.SELF_REPORT in said, "PR #51" in said), (True, True))

    def test_Given_AMergeStateThatAnsweredAndNamedNothing_When_Judged_Then_ItIsNotSettledEither(self):
        # Arrange — gh exits 0 and selects nothing, which is what a renamed field leaves. Listing the
        # empty string among the states that explain an absent check list made this pull request
        # settled; leaving it out sends it to the reason that reports what the state was.
        said = self.said(NO_CHECKS_AND_AN_UNNAMED_MERGE_STATE)

        # Act / Assert
        self.assertIn("merge state is unnamed", said)

    def test_Given_AHeadWithNoCheckAndAnUnreadMergeState_When_Judged_Then_ItIsNotCalledSettled(self):
        # Arrange — the rollup establishes that there are none, so the check list is an answer and
        # the merge state is the only thing left unread. An empty string standing for both "GitHub
        # named none" and "the read failed" puts this pull request in EXPECTED_WITHOUT_CHECKS and
        # ends the session on it.
        said = self.said(NO_CHECKS_AND_NO_MERGE_STATE)

        # Act / Assert
        self.assertEqual((check.SELF_REPORT in said, "its merge state" in said), (True, True))

    def test_Given_ARollupThatIsNotAList_When_ItIsRead_Then_ItIsNotTakenForNone(self):
        # Arrange — the merge state answers CLEAN, so a rollup misread as empty produces the
        # zero-check reminder about a list nothing here understood.
        said = self.said(NO_CHECKS_AND_A_NULL_ROLLUP)

        # Act / Assert
        self.assertEqual((check.SELF_REPORT in said, "no checks apply to it" in said), (True, False))

    def test_Given_AnUnreadCheckListBesideAReadableMergeState_When_Judged_Then_NoneIsNotReportedAsZero(self):
        # Arrange — the merge state answers CLEAN, so a check read taken for an empty one produces
        # the zero-check reminder: "no checks apply to it", about a list nothing here read.
        said = self.said(NO_CHECKS_AND_A_MERGE_STATE)

        # Act / Assert
        self.assertEqual((check.SELF_REPORT in said, "no checks apply to it" in said), (True, False))


# Two open pull requests, neither carrying a check, both readable. What varies between the cases
# below is only how many of them a deferral holds.
TWO_SETTLED_PULL_REQUESTS = """#!/bin/sh
case "$1 $2" in
  "pr list")   printf '1\n2\n' ;;
  "pr checks") echo "no checks reported on the 'topic' branch" >&2; exit 1 ;;
  "pr view")   case "$*" in
                 *statusCheckRollup*) echo '{"statusCheckRollup":[]}' ;;
                 *) echo BEHIND ;;
               esac ;;
  *)           exit 0 ;;
esac
"""

NOTHING_WAS_READ = "none of them was read this time"


class AllHeldTests(unittest.TestCase):
    """Every open pull request deferred means the guard read none of them.

    A deferral is checked before the pull request is, so the held list after checking each one and
    the held list after checking nothing are the same list. Which of the two it is costs no reading
    to know — the count says it — and saying it is the difference between a report about the pull
    requests and a report about what was claimed of them.
    """

    GUARD = REPO_ROOT / ".claude/hooks/stop/unsettled_pr.py"

    def said(self, deferrals):
        home = Path(tempfile.mkdtemp(prefix="velvet-all-held-home-"))
        try:
            (home / ".velvet-pr-deferrals").write_text(deferrals, encoding="utf-8")
            _, _, errors, _ = check.run_guard(self.GUARD, check.STOP_PAYLOAD, "probe", REPO_ROOT,
                                              home, {"probe": {"gh": TWO_SETTLED_PULL_REQUESTS}})
            return errors
        finally:
            shutil.rmtree(home, ignore_errors=True)

    def held(self, *numbers):
        stamp = int(time.time())
        return "".join(f"{number} waiting on a review {stamp}\n" for number in numbers)

    def test_Given_EveryOpenPullRequestHeld_When_TheSessionEnds_Then_ItSaysNoneWasRead(self):
        # Act / Assert
        self.assertIn(NOTHING_WAS_READ, self.said(self.held(1, 2)))

    def test_Given_OnlySomeHeld_When_TheSessionEnds_Then_TheOthersWereReadAndItDoesNotSayOtherwise(self):
        # Arrange — the control: the sentence has to be about all of them being held, not about any
        # of them being held.
        said = self.said(self.held(1))

        # Act / Assert
        self.assertEqual((NOTHING_WAS_READ in said, "PR #1" in said), (False, True))


class WatcherDeferralTests(unittest.TestCase):
    """A refusal that holds every editing tool in every session, and the way past it.

    Its own instruction can refuse: a watcher wedged mid-poll holds the lock while its heartbeat goes
    stale, and starting a replacement is exactly what the lock declines. So the escape cannot go
    through the thing that is stuck, which is what the deferral below is for.
    """

    GUARD = REPO_ROOT / ".claude/hooks/refuse/edit_while_a_ready_pr_sits.py"
    PAYLOAD = json.dumps({"tool_name": "Edit", "cwd": str(REPO_ROOT),
                          "tool_input": {"file_path": "CHANGELOG.md",
                                         "old_string": "a", "new_string": "b"}})

    def verdict(self, deferral):
        """The guard's exit code with no watcher state at all, under a HOME of its own."""
        home = Path(tempfile.mkdtemp(prefix="velvet-watcher-home-"))
        try:
            if deferral is not None:
                (home / ".velvet-pr-deferrals").write_text(deferral, encoding="utf-8")
            code, _, _, _ = check.run_guard(self.GUARD, self.PAYLOAD, "gh-empty", REPO_ROOT, home)
            return code
        finally:
            shutil.rmtree(home, ignore_errors=True)

    def test_Given_NothingWatchingAndNoDeferral_When_AWriteIsAttempted_Then_ItIsRefused(self):
        # Act / Assert — the control: an escape that is always open is not an escape.
        self.assertEqual(self.verdict(None), 2)

    def test_Given_NothingWatchingAndTheWatcherHeld_When_AWriteIsAttempted_Then_ItGoesThrough(self):
        # Arrange — the deferral the guard's own message describes, armed with a live stamp.
        deferral = f"watcher the network is down until the VPN is back {int(time.time())}\n"

        # Act / Assert
        self.assertEqual(self.verdict(deferral), 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

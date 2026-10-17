#!/usr/bin/env python3
"""Unit tests for mutation_nightly.py: which window a night measures, and what its report says where.

Run: python3 scripts/ci/test_mutation_nightly.py
"""

import contextlib
import importlib.util
import io
import json
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest
import unittest.mock
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("mutation_nightly",
                                                  Path(__file__).with_name("mutation_nightly.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


nightly = load_module()

HEAD = "e" * 40
OLDER = "d" * 40


def listing(*names, expired=(), total=None):
    return {"total_count": len(names) if total is None else total,
            "artifacts": [{"name": name, "expired": name in expired} for name in names]}


def marker(sha):
    return nightly.REPORTED_ARTIFACT.format(sha)


class ReportedHeadTests(unittest.TestCase):
    def test_Given_OneLiveMarker_When_Read_Then_ItsHeadIsTaken(self):
        # Act
        found = nightly.reported_head(listing("Mutation campaign ledger", marker(HEAD)))

        # Assert
        self.assertEqual(found, HEAD)

    def test_Given_AnExpiredMarker_When_Read_Then_NoneIsTaken(self):
        # Act
        found = nightly.reported_head(listing(marker(HEAD), expired=(marker(HEAD),)))

        # Assert
        self.assertIsNone(found)

    def test_Given_MarkersOfTwoHeads_When_Read_Then_NoneIsTaken(self):
        # Act
        found = nightly.reported_head(listing(marker(HEAD), marker(OLDER)))

        # Assert
        self.assertIsNone(found)

    def test_Given_APartialListing_When_Read_Then_NoneIsTaken(self):
        # Act
        found = nightly.reported_head(listing(marker(HEAD), total=101))

        # Assert
        self.assertIsNone(found)


class MarkerUploadTests(unittest.TestCase):
    def test_Given_TheWorkflowsMarkerUpload_When_ItsNameIsRead_Then_ItIsTheNameTheWindowLooksFor(self):
        # Arrange
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/mutation.yml").read_text()

        # Act
        uploaded = "          name: {}\n".format(
            nightly.REPORTED_ARTIFACT.format("${{ steps.report.outputs.reached }}")) in workflow

        # Assert
        self.assertTrue(uploaded)


class NightlyBudgetTests(unittest.TestCase):
    def test_Given_ANightsShards_When_TheirWavesAreTimed_Then_TheyEndInsideTheSchedulesDay(self):
        # Arrange — the shard job's own limit and how many of them run at once, read off the workflow.
        workflow = (Path(__file__).resolve().parents[2] / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-shard:")[2].partition("\n  mutation-verdict:")[0]
        limit = int(re.search(r"^    timeout-minutes: (\d+)$", job, re.MULTILINE).group(1))
        parallel = int(re.search(r"^      max-parallel: (\d+)$", job, re.MULTILINE).group(1))

        # Act
        minutes = -(-nightly.NIGHTLY_MAX_SHARDS // parallel) * limit

        # Assert
        self.assertLessEqual(minutes, 24 * 60)


class PreviousHeadTests(unittest.TestCase):
    RUNS = {"workflow_runs": [{"id": 3, "run_number": 3}, {"id": 2, "run_number": 2},
                              {"id": 1, "run_number": 1}]}

    def read(self, markers):
        """The API, with the runs above and `markers` mapping a run id to the head it reported."""
        def answer(path):
            if "/workflows/mutation.yml/runs?event=schedule&branch=main&status=completed" in path:
                return self.RUNS
            run = int(path.split("/runs/")[1].split("/")[0])
            return listing(*([marker(markers[run])] if run in markers else []))
        return answer

    def test_Given_TheNewestRunsWithoutAMarker_When_Read_Then_TheNewestThatReportedIsTaken(self):
        # Act
        found = nightly.previous_head("owner/name", "main", "99", self.read({1: OLDER, 2: HEAD}),
                                      lambda sha: True)

        # Assert
        self.assertEqual(found, (HEAD, 2))

    def test_Given_TheCurrentRunCarryingAMarker_When_Read_Then_ItIsPassedOver(self):
        # Act
        found = nightly.previous_head("owner/name", "main", "3", self.read({3: HEAD, 1: OLDER}),
                                      lambda sha: True)

        # Assert
        self.assertEqual(found, (OLDER, 1))

    def test_Given_AReportedHeadTheCheckoutDoesNotHold_When_Read_Then_AnOlderOneIsTaken(self):
        # Act
        found = nightly.previous_head("owner/name", "main", "99", self.read({2: HEAD, 1: OLDER}),
                                      lambda sha: sha != HEAD)

        # Assert
        self.assertEqual(found, (OLDER, 1))


def mutant(path, line):
    return {"path": path, "line": line, "column": 1, "operator": "boundary", "before": "<=",
            "after": "<"}


class ChooseTests(unittest.TestCase):
    """Commits a, b, c and d after `start`, of 1, 1, 1 and 5 mutants, against a ceiling of 2."""

    SIZES = {"a": 1, "b": 1, "c": 1, "d": 5}
    ORDER = ["start", "a", "b", "c", "d"]

    def ask(self, base, commit):
        """`plan` over `base`..`commit`: the mutants of the commits after `base` up to `commit`."""
        span = self.ORDER[self.ORDER.index(base) + 1:self.ORDER.index(commit) + 1]
        mutants = [mutant(name + ".cs", line) for name in span for line in range(self.SIZES[name])]
        return len(mutants) <= 2, mutants

    def test_Given_ABacklogOverTheCeiling_When_Chosen_Then_TheOldestCommitsThatFitAreMeasured(self):
        # Act
        base, reached, _, _ = nightly.choose("start", ["a", "b", "c", "d"], self.ask)

        # Assert — c and d are left for the nights that start where this one reached.
        self.assertEqual((base, reached), ("start", "b"))

    def test_Given_ACommitOverTheCeilingOnItsOwn_When_Chosen_Then_ItIsSkippedAndTheSliceStartsAfterIt(self):
        # Act
        base, reached, measured, skipped = nightly.choose("c", ["d"], self.ask)

        # Assert — the night reaches the skipped commit, so the next one does not stop on it again.
        self.assertEqual((base, reached, measured, [commit for commit, _ in skipped]),
                         ("d", "d", [], ["d"]))

    def test_Given_ASkippedCommitBeforeOnesThatFit_When_Chosen_Then_TheyAreMeasuredFromIt(self):
        # Act
        base, reached, _, _ = nightly.choose("start", ["d", "a"], lambda base, commit: (
            (False, [mutant("D.cs", 1)] * 5) if commit == "d" else (base == "d", [mutant("A.cs", 1)])))

        # Assert
        self.assertEqual((base, reached), ("d", "a"))

    def test_Given_ACommitThatWouldFitOnlyAfterOneThatDoesNot_When_Chosen_Then_TheSliceStopsBefore(self):
        # Arrange — c takes back what b added, so the slice through c fits where the one through b
        # does not; what a night leaves behind has to be every commit after the one it reached.
        fitting = {"a": True, "b": False, "c": True}

        # Act
        _, reached, _, _ = nightly.choose("start", ["a", "b", "c"],
                                          lambda base, commit: (fitting[commit], []))

        # Assert
        self.assertEqual(reached, "a")

    def test_Given_NoCommitSinceTheLastNight_When_Chosen_Then_NothingIsMeasured(self):
        # Act
        chosen = nightly.choose("start", [], self.ask)

        # Assert
        self.assertEqual(chosen, ("start", "start", [], []))


class WindowRepository:
    """A repository of three first-parent commits, the last two each adding a file of 60 mutants.

    Together they are over a ceiling of ten shards of `SHARD_CEILING` mutants, and either alone is
    under it.
    """

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-nightly-"))
        self.git("init", "-q", "-b", "main")
        self.git("config", "user.email", "probe@example.com")
        self.git("config", "user.name", "Probe")
        self.start = self.commit("README.md", "probe\n")
        self.first = self.commit("Packages/com.velvet.core/Runtime/First.cs", self.source("First"))
        self.second = self.commit("Packages/com.velvet.core/Runtime/Second.cs", self.source("Second"))

    @staticmethod
    def source(name):
        members = "".join("        internal static bool M{0}(int a, int b) => a <= b;\n".format(index)
                          for index in range(60))
        return "namespace Velvet\n{\n    internal static class " + name + "\n    {\n" + members + "    }\n}\n"

    def git(self, *arguments):
        return subprocess.run(["git", "-C", str(self.root), *arguments], check=True,
                              capture_output=True, text=True).stdout.strip()

    def commit(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        self.git("add", name)
        self.git("commit", "-qm", "add " + name)
        return self.git("rev-parse", "HEAD")

    def window(self):
        """(what `window` writes, what it prints) where the last reported night reached `start`."""
        output = self.root.parent / (self.root.name + "-window.json")
        environ = {"GITHUB_REPOSITORY": "owner/name", "GITHUB_REF_NAME": "main", "GITHUB_RUN_ID": "9"}
        runs = {"workflow_runs": [{"id": 1, "run_number": 1}]}

        def read(path):
            return runs if "/runs?event=schedule" in path else listing(marker(self.start))
        printed = io.StringIO()
        try:
            with contextlib.redirect_stdout(printed), contextlib.redirect_stderr(io.StringIO()), \
                    unittest.mock.patch.object(nightly, "NIGHTLY_MAX_SHARDS", 10):
                nightly.window_main(nightly.argparse.Namespace(project=str(self.root), output=str(output),
                                                               reuse_from=None), environ, read)
            return json.loads(output.read_text()), printed.getvalue()
        finally:
            output.unlink(missing_ok=True)

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


class WindowTests(unittest.TestCase):
    def setUp(self):
        self.repository = WindowRepository()
        self.addCleanup(self.repository.remove)

    def test_Given_TwoCommitsOverTheCeilingTogether_When_TheWindowIsRead_Then_TheOlderIsReached(self):
        # Act
        _, printed = self.repository.window()

        # Assert
        self.assertIn("reached={}\n".format(self.repository.first), printed)

    def test_Given_TwoCommitsOverTheCeilingTogether_When_TheWindowIsRead_Then_TheNewerIsLeftBehind(self):
        # Act
        window, _ = self.repository.window()

        # Assert
        self.assertEqual([commit["sha"] for commit in window["behind"]], [self.repository.second])

    def test_Given_TheOlderCommitReached_When_TheWindowIsRead_Then_ItsMutantsAloneAreMeasured(self):
        # Act
        window, _ = self.repository.window()

        # Assert
        self.assertEqual(({record["path"] for record in window["measured_mutants"]},
                          len(window["measured_mutants"])),
                         ({"Packages/com.velvet.core/Runtime/First.cs"}, 60))

    def test_Given_AWindowRead_When_ItIsDone_Then_TheCheckoutAndItsWorktreesAreAsTheyWere(self):
        # Act
        self.repository.window()

        # Assert
        self.assertEqual((self.repository.git("status", "--porcelain"),
                          len(self.repository.git("worktree", "list").splitlines())), ("", 1))


class TreeTests(unittest.TestCase):
    def setUp(self):
        self.repository = WindowRepository()
        self.addCleanup(self.repository.remove)

    def test_Given_TheReachedCommit_When_ItsTreeIsTaken_Then_TheFilesAfterItAreGone(self):
        # Act
        nightly.main(["--project", str(self.repository.root), "tree", "--source", self.repository.first])

        # Assert
        self.assertEqual(sorted(path.name for path in
                                (self.repository.root / "Packages/com.velvet.core/Runtime").iterdir()),
                         ["First.cs"])

    def test_Given_AFileTheHeadDeleted_When_TheTreeOfACommitHoldingItIsTaken_Then_ItIsTracked(self):
        # Arrange — an untracked source is one a campaign takes whole, whatever its diff.
        self.repository.git("rm", "-q", "Packages/com.velvet.core/Runtime/First.cs")
        self.repository.git("commit", "-qm", "delete First.cs")

        # Act
        nightly.main(["--project", str(self.repository.root), "tree", "--source", self.repository.first])

        # Assert
        self.assertEqual(self.repository.git("ls-files", "--others", "--exclude-standard"), "")

    def test_Given_TheReachedCommit_When_ItsTreeIsTaken_Then_FilesOutsideTheRootsStayTheHeads(self):
        # Arrange
        (self.repository.root / "README.md").write_text("the head's\n")
        self.repository.git("commit", "-qam", "readme")

        # Act
        nightly.main(["--project", str(self.repository.root), "tree", "--source", self.repository.first])

        # Assert
        self.assertEqual((self.repository.root / "README.md").read_text(), "the head's\n")


VERDICT = textwrap.dedent("""\
    --- mutants no test killed ---
    Runtime/A.cs:5 <= -> < (boundary)  [survived] -

    --- mutants nothing was asked of the suite about ---
    Runtime/B.cs:9 == -> != (equality)  not measured (timed out)

    killed: 3, survived: 1

    UNANSWERED  Runtime/A.cs:5 <= -> < (boundary)
                nothing above this line answers for it

    STALE       Runtime/C.cs:11 declares a survivor, and line 12 has none
    """)

WINDOW = {"head": HEAD, "start": OLDER, "source": "the night reported by run 1", "base": OLDER,
          "reached": HEAD, "skipped": [], "behind": [], "measured_mutants": [mutant("A.cs", 5)]}

PASSED = [("mutation-plan", "success"), ("mutation-verdict", "success")]


class SectionTests(unittest.TestCase):
    def test_Given_AVerdictsOutput_When_Read_Then_EachSurvivorStaleDeclarationAndUnmeasuredMutantIsFound(self):
        # Act
        found = nightly.sections(VERDICT)

        # Assert
        self.assertEqual(found, (
            ["Runtime/A.cs:5 <= -> < (boundary) — nothing above this line answers for it"],
            ["Runtime/C.cs:11 declares a survivor, and line 12 has none"],
            ["mutants nothing was asked of the suite about",
             "Runtime/B.cs:9 == -> != (equality)  not measured (timed out)"]))


class ComposeTests(unittest.TestCase):
    def test_Given_ANightThatLeftNothingToAnswer_When_Composed_Then_NoCommentIsOwed(self):
        # Act
        _, owed = nightly.compose(WINDOW, "killed: 1\n", PASSED, "", "https://run")

        # Assert
        self.assertFalse(owed)

    def test_Given_AnUnansweredSurvivor_When_Composed_Then_TheCommentNamesItsLine(self):
        # Act
        text, _ = nightly.compose(WINDOW, VERDICT, PASSED, "", "https://run")

        # Assert
        self.assertIn("### Unanswered survivors\n- `Runtime/A.cs:5 <= -> < (boundary) — nothing above",
                      text)

    def test_Given_NoVerdict_When_Composed_Then_TheMeasuredSlicesMutantsAreNamedUnmeasured(self):
        # Arrange — the plan chose a slice and every job after it failed.
        results = [("mutation-plan", "success"), ("mutation-shard", "failure")]

        # Act
        text, _ = nightly.compose(WINDOW, None, results, "", "https://run")

        # Assert
        self.assertIn("- `A.cs:5 <= -> < (boundary)`", text.partition("### Not measured")[2])

    def test_Given_AJobThatFailed_When_Composed_Then_TheCommentNamesIt(self):
        # Act
        text, _ = nightly.compose(WINDOW, "killed: 1\n", [("mutation-plan", "failure")], "", "https://run")

        # Assert
        self.assertIn("### Jobs that did not pass\n- `mutation-plan concluded failure`", text)

    def test_Given_ACommitSkippedOverTheCeiling_When_Composed_Then_ItsMutantsAreNamedAndOwed(self):
        # Arrange
        window = dict(WINDOW, skipped=[{"sha": OLDER, "subject": "a large change",
                                         "mutants": [mutant("Old.cs", 3)]}])

        # Act
        text, owed = nightly.compose(window, "killed: 1\n", PASSED, "", "https://run")

        # Assert
        self.assertEqual(("### Their mutants, unmeasured\n- `Old.cs:3 <= -> < (boundary)`" in text, owed),
                         (True, True))

    def test_Given_CommitsLeftForLaterNights_When_Composed_Then_TheyAreListedAndOweNothing(self):
        # Arrange
        window = dict(WINDOW, behind=[{"sha": HEAD, "subject": "a newer change"}])

        # Act
        text, owed = nightly.compose(window, "killed: 1\n", PASSED, "", "https://run")

        # Assert
        self.assertEqual(("### Left for later nights\n- `eeeeeeeeeeee a newer change`" in text, owed),
                         (True, False))

    def test_Given_NoWindow_When_Composed_Then_TheCommentSaysNothingWasMeasured(self):
        # Act
        text, _ = nightly.compose(None, None, [("mutation-plan", "failure")], "", "https://run")

        # Assert
        self.assertIn("nothing was measured", text)


class Issues:
    """The issues API: `existing` is what the label listing answers, and every write is recorded."""

    def __init__(self, existing):
        self.existing, self.writes = existing, []

    def read(self, path):
        return self.existing

    def write(self, path, method="GET", fields=None):
        self.writes.append((method, path))
        return {"number": 41} if path.endswith("/issues") else {}


class PostTests(unittest.TestCase):
    def test_Given_NoTrackingIssue_When_Posted_Then_OneIsOpenedAndCommentedOn(self):
        # Arrange
        issues = Issues([])

        # Act
        nightly.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("POST", "repos/owner/name/labels"),
                                         ("POST", "repos/owner/name/issues"),
                                         ("POST", "repos/owner/name/issues/41/comments")])

    def test_Given_AClosedTrackingIssue_When_Posted_Then_ItIsReopenedAndCommentedOn(self):
        # Arrange
        issues = Issues([{"number": 7, "state": "closed"}])

        # Act
        nightly.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("PATCH", "repos/owner/name/issues/7"),
                                         ("POST", "repos/owner/name/issues/7/comments")])

    def test_Given_APullRequestCarryingTheLabel_When_Posted_Then_ItIsNotTakenForTheIssue(self):
        # Arrange
        issues = Issues([{"number": 8, "state": "open", "pull_request": {}},
                         {"number": 7, "state": "open"}])

        # Act
        nightly.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("POST", "repos/owner/name/issues/7/comments")])


class ReportTests(unittest.TestCase):
    def report(self, window):
        directory = Path(tempfile.mkdtemp(prefix="velvet-nightly-report-"))
        self.addCleanup(shutil.rmtree, directory, True)
        path = directory / "window.json"
        if window is not None:
            path.write_text(json.dumps(window))
        (directory / "verdict.txt").write_text("killed: 1\n")
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            nightly.report_main(nightly.argparse.Namespace(window=str(path),
                                                           verdict=str(directory / "verdict.txt")),
                                {"RESULTS": "mutation-plan=success"}, Issues([]).read, Issues([]).write)
        return printed.getvalue()

    def test_Given_ANightWhoseWindowWasRead_When_Reported_Then_TheCommitItReachedIsPrintedForTheMarker(self):
        # Arrange — a slice that reached an older commit than the head.
        window = dict(WINDOW, reached=OLDER)

        # Act
        printed = self.report(window)

        # Assert
        self.assertIn("\nreached={}\n".format(OLDER), "\n" + printed)

    def test_Given_ANightWhoseWindowWasNotRead_When_Reported_Then_NoCommitIsPrinted(self):
        # Act
        printed = self.report(None)

        # Assert — so no marker is uploaded, and the next night measures these commits again.
        self.assertNotIn("reached=", printed)


if __name__ == "__main__":
    unittest.main(verbosity=2)

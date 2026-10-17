#!/usr/bin/env python3
"""Unit tests for mutation_nightly.py: which window a night measures, and what its report says where.

Run: python3 scripts/ci/test_mutation_nightly.py
"""

import contextlib
import importlib.util
import io
import json
import shutil
import subprocess
import tempfile
import textwrap
import unittest
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
            nightly.REPORTED_ARTIFACT.format("${{ steps.report.outputs.head }}")) in workflow

        # Assert
        self.assertTrue(uploaded)


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
    """The slice over `start`, then commits a, b and c, each adding one mutant."""

    WINDOW = [mutant("A.cs", 1), mutant("B.cs", 1), mutant("C.cs", 1)]

    def ask(self, fitting):
        """`plan` over the slice from each candidate, fitting only the candidates in `fitting`."""
        after = {"start": self.WINDOW, "a": self.WINDOW[1:], "b": self.WINDOW[2:]}
        return lambda candidate: (candidate in fitting, after[candidate])

    def test_Given_AWindowOverTheCeiling_When_Chosen_Then_TheOldestBaseThatFitsIsTaken(self):
        # Act
        base, unmeasured, _, _ = nightly.choose("start", ["a", "b", "c"], self.ask({"a", "b"}))

        # Assert — the commits up to that base are what the night leaves unmeasured.
        self.assertEqual((base, unmeasured), ("a", ["a"]))

    def test_Given_AWindowOverTheCeiling_When_Chosen_Then_TheWindowsMutantsAreTheFirstPlansList(self):
        # Act
        _, _, window, measured = nightly.choose("start", ["a", "b", "c"], self.ask({"b"}))

        # Assert
        self.assertEqual((len(window), measured), (3, self.WINDOW[2:]))

    def test_Given_NoSliceThatFits_When_Chosen_Then_TheHeadIsTakenAndEveryCommitIsUnmeasured(self):
        # Act
        base, unmeasured, _, measured = nightly.choose("start", ["a", "b", "c"], self.ask(set()))

        # Assert
        self.assertEqual((base, unmeasured, measured), ("c", ["a", "b", "c"], []))


class WindowRepository:
    """A repository of three first-parent commits, the last two each adding a file of 60 mutants.

    Together they are over the plan's ceiling of `MAX_SHARDS` shards of `SHARD_CEILING` mutants, and
    either alone is under it.
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
        """What `window` writes when the last reported night ended at `start`."""
        output = self.root.parent / (self.root.name + "-window.json")
        environ = {"GITHUB_REPOSITORY": "owner/name", "GITHUB_REF_NAME": "main", "GITHUB_RUN_ID": "9"}
        runs = {"workflow_runs": [{"id": 1, "run_number": 1}]}

        def read(path):
            return runs if "/runs?event=schedule" in path else listing(marker(self.start))
        try:
            with contextlib.redirect_stdout(io.StringIO()):
                nightly.window_main(nightly.argparse.Namespace(project=str(self.root), output=str(output),
                                                               reuse_from=None), environ, read)
            return json.loads(output.read_text())
        finally:
            output.unlink(missing_ok=True)

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


class WindowTests(unittest.TestCase):
    def setUp(self):
        self.repository = WindowRepository()
        self.addCleanup(self.repository.remove)

    def test_Given_TwoCommitsOverTheCeilingTogether_When_TheWindowIsRead_Then_TheNewestIsMeasured(self):
        # Act
        window = self.repository.window()

        # Assert
        self.assertEqual(window["base"], self.repository.first)

    def test_Given_TwoCommitsOverTheCeilingTogether_When_TheWindowIsRead_Then_TheOlderOnesMutantsAreUnmeasured(self):
        # Act
        window = self.repository.window()

        # Assert
        self.assertEqual(({record["path"] for record in window["unmeasured_mutants"]},
                          len(window["unmeasured_mutants"])),
                         ({"Packages/com.velvet.core/Runtime/First.cs"}, 60))


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
          "unmeasured_commits": [], "unmeasured_mutants": [], "measured_mutants": [mutant("A.cs", 5)]}

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
        text = nightly.compose(WINDOW, "killed: 1\n", PASSED, "", "https://run")

        # Assert
        self.assertIsNone(text)

    def test_Given_AnUnansweredSurvivor_When_Composed_Then_TheCommentNamesItsLine(self):
        # Act
        text = nightly.compose(WINDOW, VERDICT, PASSED, "", "https://run")

        # Assert
        self.assertIn("### Unanswered survivors\n- `Runtime/A.cs:5 <= -> < (boundary) — nothing above",
                      text)

    def test_Given_NoVerdict_When_Composed_Then_TheMeasuredSlicesMutantsAreNamedUnmeasured(self):
        # Arrange — the plan chose a slice and every job after it failed.
        results = [("mutation-plan", "success"), ("mutation-shard", "failure")]

        # Act
        text = nightly.compose(WINDOW, None, results, "", "https://run")

        # Assert
        self.assertIn("- `A.cs:5 <= -> < (boundary)`", text.partition("### Not measured")[2])

    def test_Given_AJobThatFailed_When_Composed_Then_TheCommentNamesIt(self):
        # Act
        text = nightly.compose(WINDOW, "killed: 1\n", [("mutation-plan", "failure")], "", "https://run")

        # Assert
        self.assertIn("### Jobs that did not pass\n- `mutation-plan concluded failure`", text)

    def test_Given_CommitsLeftOutsideTheSlice_When_Composed_Then_TheirMutantsAreNamedUnmeasured(self):
        # Arrange
        window = dict(WINDOW, unmeasured_commits=[{"sha": OLDER, "subject": "an older change"}],
                      unmeasured_mutants=[mutant("Old.cs", 3)])

        # Act
        text = nightly.compose(window, "killed: 1\n", PASSED, "", "https://run")

        # Assert
        self.assertIn("### Their mutants, unmeasured\n- `Old.cs:3 <= -> < (boundary)`", text)

    def test_Given_NoWindow_When_Composed_Then_TheCommentSaysNothingWasMeasured(self):
        # Act
        text = nightly.compose(None, None, [("mutation-plan", "failure")], "", "https://run")

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

    def test_Given_ANightWhoseWindowWasRead_When_Reported_Then_ItsHeadIsPrintedForTheMarker(self):
        # Act
        printed = self.report(WINDOW)

        # Assert
        self.assertIn("\nhead={}\n".format(HEAD), "\n" + printed)

    def test_Given_ANightWhoseWindowWasNotRead_When_Reported_Then_NoHeadIsPrinted(self):
        # Act
        printed = self.report(None)

        # Assert — so no marker is uploaded, and the next night measures these commits again.
        self.assertNotIn("head=", printed)


if __name__ == "__main__":
    unittest.main(verbosity=2)

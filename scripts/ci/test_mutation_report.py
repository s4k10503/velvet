#!/usr/bin/env python3
"""Unit tests for mutation_report.py: what a record run's report says, and where it goes.

Run: python3 scripts/ci/test_mutation_report.py
"""

import importlib.util
import textwrap
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("mutation_report",
                                                  Path(__file__).with_name("mutation_report.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


report = load_module()

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

PASSED = [("mutation-plan", "success"), ("mutation-verdict", "success")]


class SectionTests(unittest.TestCase):
    def test_Given_AVerdictsOutput_When_Read_Then_EachSurvivorStaleDeclarationAndUnmeasuredMutantIsFound(self):
        # Act
        found = report.sections(VERDICT)

        # Assert
        self.assertEqual(found, (
            ["Runtime/A.cs:5 <= -> < (boundary) — nothing above this line answers for it"],
            ["Runtime/C.cs:11 declares a survivor, and line 12 has none"],
            ["mutants nothing was asked of the suite about",
             "Runtime/B.cs:9 == -> != (equality)  not measured (timed out)"]))


class ComposeTests(unittest.TestCase):
    def test_Given_ARunThatLeftNothingToAnswer_When_Composed_Then_NoCommentIsOwed(self):
        # Act
        text = report.compose("killed: 1\n", PASSED, 0, "https://run")

        # Assert
        self.assertIsNone(text)

    def test_Given_AnUnansweredSurvivor_When_Composed_Then_TheCommentNamesItsLine(self):
        # Act
        text = report.compose(VERDICT, PASSED, 0, "https://run")

        # Assert
        self.assertIn("### Unanswered survivors\n- `Runtime/A.cs:5 <= -> < (boundary) — nothing above",
                      text)

    def test_Given_AJobThatFailed_When_Composed_Then_TheCommentNamesIt(self):
        # Act
        text = report.compose("killed: 1\n", [("mutation-plan", "failure")], 0, "https://run")

        # Assert
        self.assertIn("### Jobs that did not pass\n- `mutation-plan concluded failure`", text)

    def test_Given_MutantsPastTheCapAlone_When_Composed_Then_NoCommentIsOwed(self):
        # Act
        text = report.compose("killed: 1\n", PASSED, 30, "https://run")

        # Assert
        self.assertIsNone(text)

    def test_Given_NoVerdictAndAJobThatFailed_When_Composed_Then_TheJobIsNamed(self):
        # Act
        text = report.compose(None, [("mutation-shard", "failure")], 0, "https://run")

        # Assert
        self.assertIn("mutation-shard concluded failure", text)


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
        report.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("POST", "repos/owner/name/labels"),
                                         ("POST", "repos/owner/name/issues"),
                                         ("POST", "repos/owner/name/issues/41/comments")])

    def test_Given_AClosedTrackingIssue_When_Posted_Then_ItIsReopenedAndCommentedOn(self):
        # Arrange
        issues = Issues([{"number": 7, "state": "closed"}])

        # Act
        report.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("PATCH", "repos/owner/name/issues/7"),
                                         ("POST", "repos/owner/name/issues/7/comments")])

    def test_Given_APullRequestCarryingTheLabel_When_Posted_Then_ItIsNotTakenForTheIssue(self):
        # Arrange
        issues = Issues([{"number": 8, "state": "open", "pull_request": {}},
                         {"number": 7, "state": "open"}])

        # Act
        report.post("owner/name", "a report", issues.read, issues.write)

        # Assert
        self.assertEqual(issues.writes, [("POST", "repos/owner/name/issues/7/comments")])


if __name__ == "__main__":
    unittest.main(verbosity=2)

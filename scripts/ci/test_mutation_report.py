#!/usr/bin/env python3
"""Unit tests for mutation_report.py: what a record run's SARIF file and job summary say.

Run: python3 scripts/ci/test_mutation_report.py
"""

import importlib.util
import json
import re
import tempfile
import textwrap
import unittest
import unittest.mock
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


report = load_module(HERE / "mutation_report.py", "mutation_report")


def described(path, line, before, after, operator):
    """A mutant as `Mutant.describe` prints it, padding spaces included."""
    return "{}:{} {} -> {} ({})".format(path, line, before, after, operator)


def verdict(survivors=(), stale=(), extra=""):
    """A verdict job's output over `survivors` of (path, line, before, after, operator)."""
    text = "killed: 3, survived: {}\n".format(len(survivors)) + extra
    for survivor in survivors:
        text += "\nUNANSWERED  {}\n            nothing above this line answers for it\n".format(
            described(*survivor))
    for path, line, subject in stale:
        text += "\nSTALE       {}:{} declares a survivor, and line {} has none\n".format(path, line, subject)
    return text


BOUNDARY = ("Runtime/A.cs", 5, " <= ", " < ", "boundary")
EQUALITY = ("Runtime/B.cs", 9, " == ", " != ", "equality")
PASSED = [("mutation-plan", "success"), ("mutation-verdict", "success")]


def sarif_of(text):
    unanswered, stale = report.sections(text)[:2]
    return report.build_sarif(unanswered, stale)


def results_of(text):
    return sarif_of(text)["runs"][0]["results"]


def fingerprints(text):
    return [result["partialFingerprints"][report.FINGERPRINT] for result in results_of(text)]


class SectionTests(unittest.TestCase):
    def test_Given_AVerdictsOutput_When_Read_Then_EachSurvivorIsReadIntoItsFields(self):
        # Arrange
        text = verdict([BOUNDARY, ("Runtime/C.cs", 2, "Foo();", ";", "line removed")])

        # Act
        found = report.sections(text)[0]

        # Assert
        self.assertEqual(found, [
            {"path": "Runtime/A.cs", "line": 5, "before": "<=", "after": "<", "operator": "boundary",
             "reason": "nothing above this line answers for it"},
            {"path": "Runtime/C.cs", "line": 2, "before": "Foo();", "after": ";", "operator": "line removed",
             "reason": "nothing above this line answers for it"}])

    def test_Given_AClauseRemoval_When_Read_Then_ItsEmptyAfterTextIsKept(self):
        # Arrange
        text = verdict([("Runtime/A.cs", 3, "x > 1 &&", "", "clause removed")])

        # Act
        found = report.sections(text)[0]

        # Assert
        self.assertEqual(found, [{"path": "Runtime/A.cs", "line": 3, "before": "x > 1 &&", "after": "",
                                  "operator": "clause removed",
                                  "reason": "nothing above this line answers for it"}])

    def test_Given_AStaleDeclaration_When_Read_Then_ItsLineAndTheLineItWasForAreFound(self):
        # Act
        found = report.sections(verdict(stale=[("Runtime/C.cs", 11, 12)]))[1]

        # Assert
        self.assertEqual(found, [{"path": "Runtime/C.cs", "line": 11, "subject": 12}])

    def test_Given_AnUnmeasuredMutant_When_Read_Then_ItIsNotAFinding(self):
        # Arrange
        text = verdict(extra="\n--- mutants nothing was asked of the suite about ---\n"
                             "Runtime/B.cs:9 == -> != (equality)  not measured (timed out)\n")

        # Act
        found = report.sections(text)

        # Assert
        self.assertEqual(found, ([], [], [
            "mutants nothing was asked of the suite about",
            "Runtime/B.cs:9 == -> != (equality)  not measured (timed out)"], []))

    def test_Given_AnUnansweredLineThatDoesNotParse_When_Read_Then_ItIsKeptAsUnread(self):
        # Act
        found = report.sections("UNANSWERED  something odd\n            why\n")

        # Assert
        self.assertEqual(found, ([], [], [], ["UNANSWERED something odd — why"]))


class SarifShapeTests(unittest.TestCase):
    def test_Given_ASurvivor_When_Built_Then_TheFileDeclaresSarif210(self):
        # Act
        sarif = sarif_of(verdict([BOUNDARY]))

        # Assert
        self.assertEqual({key: sarif[key] for key in ("version", "$schema")},
                         {"version": "2.1.0", "$schema": "https://json.schemastore.org/sarif-2.1.0.json"})

    def test_Given_ASurvivor_When_Built_Then_TheFileHoldsOneRun(self):
        # Act
        runs = sarif_of(verdict([BOUNDARY]))["runs"]

        # Assert
        self.assertEqual(len(runs), 1)

    def test_Given_ASurvivor_When_Built_Then_TheResultIsUnderItsOperatorsRule(self):
        # Act
        rule = results_of(verdict([BOUNDARY]))[0]["ruleId"]

        # Assert
        self.assertEqual(rule, "boundary")

    def test_Given_ASurvivor_When_Built_Then_TheResultCarriesItsRuleAndRepoRelativeLocation(self):
        # Act
        result = results_of(verdict([BOUNDARY]))[0]

        # Assert
        self.assertEqual(result["locations"], [{"physicalLocation": {
            "artifactLocation": {"uri": "Runtime/A.cs", "uriBaseId": "%SRCROOT%"},
            "region": {"startLine": 5}}}])

    def test_Given_ASurvivor_When_Built_Then_TheMessageNamesTheMutationAndWhyItIsUnanswered(self):
        # Act
        message = results_of(verdict([BOUNDARY]))[0]["message"]["text"]

        # Assert
        self.assertEqual(message, "Mutation `<=` -> `<` (boundary) survives and nothing answers for it: "
                                  "nothing above this line answers for it")

    def test_Given_AStaleDeclaration_When_Built_Then_ItIsAResultUnderItsOwnRuleAtTheDeclaration(self):
        # Act
        result = results_of(verdict(stale=[("Runtime/C.cs", 11, 12)]))[0]

        # Assert
        self.assertEqual("{}:{}".format(result["ruleId"],
                                        result["locations"][0]["physicalLocation"]["region"]["startLine"]),
                         "stale-declaration:11")

    def test_Given_AnyRun_When_Built_Then_EveryResultNamesADeclaredRule(self):
        # Arrange
        sarif = sarif_of(verdict([BOUNDARY, EQUALITY], stale=[("Runtime/C.cs", 11, 12)]))
        run = sarif["runs"][0]

        # Act
        unknown = [r["ruleId"] for r in run["results"]
                   if r["ruleId"] not in {rule["id"] for rule in run["tool"]["driver"]["rules"]}]

        # Assert
        self.assertEqual(unknown, [])

    def test_Given_AnyRun_When_Built_Then_TheFileIsJsonSerialisable(self):
        # Act
        text = json.dumps(sarif_of(verdict([BOUNDARY], stale=[("Runtime/C.cs", 11, 12)])))

        # Assert
        self.assertEqual(json.loads(text)["version"], "2.1.0")


class OperatorRuleTests(unittest.TestCase):
    def test_Given_TheCampaignsOperators_When_Compared_Then_EachHasExactlyOneRule(self):
        # Arrange
        campaign = load_module(ROOT / "scripts" / "test_quality" / "mutation_check.py", "mutation_check")

        # Act
        declared = {rule["id"] for rule in sarif_of("")["runs"][0]["tool"]["driver"]["rules"]}

        # Assert
        self.assertEqual(declared - {report.STALE_RULE},
                         {operator.replace(" ", "-") for operator in campaign.LINE_PRIORITY})


class FingerprintTests(unittest.TestCase):
    def test_Given_TheSameSurvivorAfterALineShift_When_Built_Then_TheFingerprintIsUnchanged(self):
        # Arrange
        before = verdict([BOUNDARY])
        shifted = verdict([("Runtime/A.cs", 40, " <= ", " < ", "boundary")])

        # Act
        same = fingerprints(before) == fingerprints(shifted)

        # Assert
        self.assertTrue(same)

    def test_Given_TheSameLineWithAnotherMutation_When_Built_Then_TheFingerprintDiffers(self):
        # Arrange
        flipped = verdict([("Runtime/A.cs", 5, " >= ", " > ", "boundary")])

        # Act
        same = fingerprints(verdict([BOUNDARY])) == fingerprints(flipped)

        # Assert
        self.assertFalse(same)

    def test_Given_TheSameMutationInAnotherFile_When_Built_Then_TheFingerprintDiffers(self):
        # Arrange
        elsewhere = verdict([("Runtime/Z.cs", 5, " <= ", " < ", "boundary")])

        # Act
        same = fingerprints(verdict([BOUNDARY])) == fingerprints(elsewhere)

        # Assert
        self.assertFalse(same)

    def test_Given_TwoIdenticalMutationsInOneFile_When_Built_Then_EachKeepsItsOwnFingerprint(self):
        # Arrange
        twice = verdict([BOUNDARY, ("Runtime/A.cs", 30, " <= ", " < ", "boundary")])

        # Act
        found = fingerprints(twice)

        # Assert
        self.assertEqual(len(set(found)), 2)

    def test_Given_TwoIdenticalMutationsAndTheLaterOnesLineShifting_When_Built_Then_BothFingerprintsHold(self):
        # Arrange
        first = verdict([BOUNDARY, ("Runtime/A.cs", 30, " <= ", " < ", "boundary")])
        moved = verdict([BOUNDARY, ("Runtime/A.cs", 31, " <= ", " < ", "boundary")])

        # Act
        same = fingerprints(first) == fingerprints(moved)

        # Assert
        self.assertTrue(same)

    def test_Given_AStaleDeclarationThatMovesLine_When_Built_Then_TheFingerprintIsUnchanged(self):
        # Act
        same = (fingerprints(verdict(stale=[("Runtime/C.cs", 11, 12)]))
                == fingerprints(verdict(stale=[("Runtime/C.cs", 25, 26)])))

        # Assert
        self.assertTrue(same)


class EmptyRunTests(unittest.TestCase):
    def test_Given_ARunThatLeftNothingToAnswer_When_Built_Then_ResultsIsAnEmptyList(self):
        # Act
        results = results_of("killed: 1\n")

        # Assert
        self.assertEqual(results, [])

    def test_Given_ARunThatLeftNothingToAnswer_When_Built_Then_EveryRuleIsStillDeclared(self):
        # Act
        rules = sarif_of("killed: 1\n")["runs"][0]["tool"]["driver"]["rules"]

        # Assert
        self.assertEqual(len(rules), len(report.OPERATOR_RULES) + 1)


class SummaryTests(unittest.TestCase):
    def test_Given_AJobThatFailed_When_Summarised_Then_TheSummaryNamesIt(self):
        # Act
        text = report.summary("killed: 1\n", [("mutation-plan", "failure")], 0, "https://run", (0, 0))

        # Assert
        self.assertIn("### Jobs that did not pass\n- `mutation-plan concluded failure`", text)

    def test_Given_MutantsPastTheCap_When_Summarised_Then_TheSummaryCountsThem(self):
        # Act
        text = report.summary("killed: 1\n", PASSED, 30, "https://run", (0, 0))

        # Assert
        self.assertIn("30 mutant(s) past this run's cap are left to the next run.", text)

    def test_Given_AnUnmeasuredMutant_When_Summarised_Then_TheSummaryListsIt(self):
        # Arrange
        text = verdict(extra="\n--- mutants nothing was asked of the suite about ---\n"
                             "Runtime/B.cs:9 == -> != (equality)  not measured (timed out)\n")

        # Act
        summary = report.summary(text, PASSED, 0, "https://run", (0, 0))

        # Assert
        self.assertIn("### Not measured\n- `mutants nothing was asked of the suite about`\n"
                      "- `Runtime/B.cs:9 == -> != (equality)  not measured (timed out)`", summary)

    def test_Given_AMutantKilledByATimeout_When_Summarised_Then_TheSummaryListsItApartFromWhatIsOwed(self):
        # Arrange
        text = verdict(extra="\n--- mutants killed by timing out the suite ---\n"
                             "Runtime/B.cs:9 == -> != (equality)  the editor was killed at 755s\n")

        # Act
        summary = report.summary(text, PASSED, 0, "https://run", (0, 0))

        # Assert
        self.assertIn("### Killed by timeout (counted as detected, no case named)\n"
                      "- `Runtime/B.cs:9 == -> != (equality)  the editor was killed at 755s`", summary)

    def test_Given_Survivors_When_Summarised_Then_TheSummaryCountsWhatWasUploadedUnderTheCategory(self):
        # Act
        text = report.summary(verdict([BOUNDARY]), PASSED, 0, "https://run", (1, 0))

        # Assert
        self.assertIn("1 unanswered survivor(s) and 0 stale declaration(s) are in the SARIF upload "
                      "(category `mutation-campaign`).", text)

    def test_Given_NoVerdict_When_Summarised_Then_TheSummarySaysNothingWasUploaded(self):
        # Act
        text = report.summary(None, [("mutation-shard", "failure")], 0, "https://run", None)

        # Assert
        self.assertIn("No verdict was produced, so nothing was uploaded to code scanning", text)

    def test_Given_MoreUnmeasuredLinesThanTheLimit_When_Summarised_Then_TheRestAreCounted(self):
        # Arrange
        many = verdict(extra="\n--- not measured: 1 of 1 shard(s) recorded nothing ---\n"
                             + "".join("line {}\n".format(n) for n in range(report.LIST_LIMIT + 5)))

        # Act
        text = report.summary(many, PASSED, 0, "https://run", (0, 0))

        # Assert
        self.assertIn("- and 6 more", text)


class MainTests(unittest.TestCase):
    def run_main(self, verdict_text):
        directory = Path(self.enterContext(tempfile.TemporaryDirectory()))
        sarif = directory / "mutation.sarif"
        arguments = ["--sarif", str(sarif)]
        if verdict_text is not None:
            (directory / "verdict.txt").write_text(verdict_text)
            arguments += ["--verdict", str(directory / "verdict.txt")]
        status = None
        with unittest.mock.patch("builtins.print"):
            try:
                status = report.main(arguments, {"RESULTS": "mutation-plan=success"})
            except (SystemExit, Exception):
                pass  # A tree without `--sarif` ends the run here; the cases then fail on what was written.
        return status, sarif

    @staticmethod
    def written(sarif):
        return json.loads(sarif.read_text())["runs"][0]["results"] if sarif.exists() else None

    def test_Given_NoVerdictFile_When_Run_Then_NoSarifIsWritten(self):
        # Act
        status, sarif = self.run_main(None)

        # Assert
        self.assertEqual(status if not sarif.exists() else "a file was written", 0)

    def test_Given_AVerdictWithNothingToAnswer_When_Run_Then_AnEmptyResultsListIsWritten(self):
        # Act
        _, sarif = self.run_main("killed: 1\n")

        # Assert
        self.assertEqual(self.written(sarif), [])

    def test_Given_AVerdictWithASurvivor_When_Run_Then_TheRunStillExitsZero(self):
        # Act
        status, _ = self.run_main(verdict([BOUNDARY]))

        # Assert
        self.assertEqual(status, 0)

    def test_Given_AVerdictWithASurvivor_When_Run_Then_OneResultIsWritten(self):
        # Act
        _, sarif = self.run_main(verdict([BOUNDARY]))

        # Assert
        self.assertEqual(len(self.written(sarif) or []), 1)


class WorkflowTests(unittest.TestCase):
    TEXT = (ROOT / ".github" / "workflows" / "mutation.yml").read_text()
    REPORT_JOB = TEXT[TEXT.index("\n  report:\n"):]

    def test_Given_TheWorkflow_When_Read_Then_SecurityEventsWriteIsGrantedToTheReportJobAlone(self):
        # Act
        granted = [m.start() for m in re.finditer(r"security-events: write", self.TEXT)]

        # Assert
        self.assertEqual(granted, [self.TEXT.find("security-events: write", self.TEXT.find("\n  report:\n"))])

    def test_Given_TheWorkflow_When_Read_Then_NoJobCanWriteIssuesAnyMore(self):
        # Act
        found = "issues: write" in self.TEXT

        # Assert
        self.assertFalse(found)

    def test_Given_TheReportJob_When_Read_Then_TheUploadIsPinnedByFullShaUnderTheScriptsCategory(self):
        # Act
        found = re.search(r"uses: github/codeql-action/upload-sarif@[0-9a-f]{40} # v\d+\.\d+\.\d+\n"
                          r"\s+with:\n\s+sarif_file: mutation\.sarif\n\s+category: (\S+)", self.REPORT_JOB)

        # Assert
        self.assertEqual(found and found.group(1), report.CATEGORY)

    def test_Given_TheReportJob_When_Read_Then_TheUploadWaitsForAFileTheScriptWrote(self):
        # Act
        found = "if: ${{ hashFiles('mutation.sarif') != '' }}\n        uses: github/codeql-action" in self.REPORT_JOB

        # Assert
        self.assertTrue(found)


if __name__ == "__main__":
    unittest.main(verbosity=2)

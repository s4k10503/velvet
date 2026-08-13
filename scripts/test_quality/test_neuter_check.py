#!/usr/bin/env python3
"""Unit tests for the decisions neuter_check.py makes before and after its Unity runs.

The runs need a licence; the decisions do not, and it is the decisions that make a sweep's output mean
anything. Several cases below are ones where the harness would otherwise report full coverage: a filter
that ran nothing reports no holes, a fixture already red reports every case as killed by the cut, and a
reader that came back empty agrees with every record it is handed.

Run: python3 scripts/test_quality/test_neuter_check.py
"""

import argparse
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports neuter_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "neuter_check", Path(__file__).with_name("neuter_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


neuter_check = load_module()


def scaffold(project):
    """A project the audit's readers can run over: the globbed directories and both records, all empty."""
    (project / "scripts/test_quality").mkdir(parents=True)
    for folder, _ in neuter_check.MECHANISM_GLOBS:
        (project / neuter_check.PACKAGE_ROOT / folder).mkdir(parents=True)
    (project / neuter_check.UNCOVERED_FILE).write_text("")
    (project / neuter_check.HOLES_FILE).write_text("")
    return project


class BaselineProblem(unittest.TestCase):
    def test_Given_AFilterThatRanNoCases_When_TheBaselineIsRead_Then_ItIsRefused(self):
        # Act
        problem, red = neuter_check.baseline_problem("Velvet.Tests.NotAClass", {})

        # Assert
        self.assertEqual((bool(problem), red), (True, []))

    def test_Given_ARefusedFilter_When_TheMessageIsRead_Then_ItNamesTheFilter(self):
        # Act
        problem, _ = neuter_check.baseline_problem("Velvet.Tests.NotAClass", {})

        # Assert
        self.assertIn("Velvet.Tests.NotAClass", problem)

    def test_Given_AGreenBaseline_When_ItIsRead_Then_ItIsAccepted(self):
        # Act / Assert
        self.assertEqual(
            neuter_check.baseline_problem("F", {"a": "Passed", "b": "Passed"}), (None, []))

    def test_Given_ARedBaseline_When_ItIsRead_Then_OnlyTheRedCasesAreNamed(self):
        # Act
        problem, red = neuter_check.baseline_problem(
            "F", {"a": "Failed", "b": "Passed", "c": "Inconclusive"})

        # Assert
        self.assertEqual((bool(problem), red), (True, ["a", "c"]))


class CutMap(unittest.TestCase):
    """The map is data the sweep cannot check for itself without spending an editor run on it."""

    def setUp(self):
        self.map = json.loads((REPO_ROOT / neuter_check.CUTS_FILE).read_text())

    def test_Given_EveryFixture_When_ItsCutsAreResolved_Then_EachIsDeclared(self):
        # Arrange
        declared = {cut["name"] for cut in self.map["cuts"]}

        # Act
        dangling = sorted(
            f"{entry['fixture']} names '{name}'"
            for entry in self.map["fixtures"] for name in entry["cuts"] if name not in declared)

        # Assert
        self.assertEqual(dangling, [])

    def test_Given_EveryDeclaredCut_When_TheFixturesAreRead_Then_SomeFixtureAsksIt(self):
        # A cut nothing asks about is disabled by no sweep, so it protects nothing while reading as
        # coverage in the map.
        # Arrange
        asked = {name for entry in self.map["fixtures"] for name in entry["cuts"]}

        # Act
        unasked = sorted(cut["name"] for cut in self.map["cuts"] if cut["name"] not in asked)

        # Assert
        self.assertEqual(unasked, [])

    def test_Given_EveryFixture_When_ItsPlatformIsRead_Then_ItMatchesWhereTheSourceLives(self):
        # A PlayMode fixture asked under EditMode selects no case and reports no hole, which reads as
        # every cut covered. Derived from the source tree so the declaration cannot drift from it.
        # Act
        wrong = []
        for entry in self.map["fixtures"]:
            found = neuter_check.declaring_sources(REPO_ROOT, entry["fixture"].rsplit(".", 1)[-1])
            expected = "PlayMode" if any("PlayMode" in str(path) for path in found) else "EditMode"
            if not found or entry.get("platform") != expected:
                wrong.append(f"{entry['fixture']}: declares {entry.get('platform')!r}, source says {expected}")

        # Assert
        self.assertEqual(wrong, [])

    def test_Given_EveryCutEdit_When_ItsFileIsSought_Then_ItExists(self):
        # Act
        missing = sorted(
            edit["file"] for cut in self.map["cuts"] for edit in cut["edits"]
            if not (REPO_ROOT / edit["file"]).is_file())

        # Assert
        self.assertEqual(missing, [])


class AuditReadsNothing(unittest.TestCase):
    """What the audit does on each reader that came back with nothing.

    A glob that matched no file, a map that parsed to no cut and no fixture, and a record naming no
    fixture each disagree with nothing, so an audit that exits 0 having read an empty tree is
    indistinguishable from one that checked the repository. A case per floor rather than one over all
    of them: with the cut and fixture floors OR'd into a single branch, either could be set to zero
    and this suite stayed green.
    """

    def audit_empty_tree(self, cuts=None):
        with tempfile.TemporaryDirectory() as tree:
            project = scaffold(Path(tree))
            return neuter_check.audit(project, cuts or {"cuts": {}, "fixtures": {}})

    def test_Given_ATreeWithNoMechanismInIt_When_TheAuditReadsIt_Then_TheGlobFloorRefuses(self):
        # Act
        problems = self.audit_empty_tree()

        # Assert
        self.assertTrue(any("mechanism glob found 0" in problem for problem in problems), problems)

    def test_Given_ACutMapThatParsedToNoCut_When_TheAuditReadsIt_Then_TheCutFloorRefuses(self):
        # Arrange — enough fixtures that only the cut term can report.
        fixtures = {f"Velvet.Tests.F{n}": {"cuts": []} for n in range(neuter_check.FIXTURE_FLOOR)}

        # Act
        problems = self.audit_empty_tree({"cuts": {}, "fixtures": fixtures})

        # Assert
        self.assertTrue(any("parsed to 0 cuts" in problem for problem in problems), problems)

    def test_Given_ACutMapThatParsedToNoFixture_When_TheAuditReadsIt_Then_TheFixtureFloorRefuses(self):
        # Arrange — enough cuts that only the fixture term can report.
        cuts = {f"c{n}": {"edits": []} for n in range(neuter_check.CUT_FLOOR)}

        # Act
        problems = self.audit_empty_tree({"cuts": cuts, "fixtures": {}})

        # Assert
        self.assertTrue(any("parsed to 0 fixtures" in problem for problem in problems), problems)

    def test_Given_AHoleRecordNamingNoFixture_When_TheAuditReadsIt_Then_TheRecordFloorRefuses(self):
        # Act
        problems = self.audit_empty_tree()

        # Assert
        self.assertTrue(any("fixtures (0)" in problem for problem in problems), problems)


class ReportOverTheRecord(unittest.TestCase):
    """--report writes the fixtures a run swept and nothing else, so a narrowed sweep aimed at the
    checked-in record replaces the rest of it with them — which the audit downstream reads as a record
    every line still in it agrees with."""

    def problems(self, fixtures, report):
        return neuter_check.baseline_arg_problems(argparse.Namespace(
            project=".", fixtures=fixtures, report=report, baseline=None))

    def test_Given_ANarrowedSweep_When_ItReportsOverTheRecord_Then_ItIsRefused(self):
        # Act
        problems = self.problems(["Velvet.Tests.HasVariantTests"], neuter_check.HOLES_FILE)

        # Assert
        self.assertIn("--report would write only those over", "\n".join(problems))

    def test_Given_AWholeSweep_When_ItReportsOverTheRecord_Then_NothingIsReported(self):
        # Act
        problems = self.problems(None, neuter_check.HOLES_FILE)

        # Assert
        self.assertEqual(problems, [])

    def test_Given_ANarrowedSweep_When_ItReportsElsewhere_Then_NothingIsReported(self):
        # Act
        problems = self.problems(["Velvet.Tests.HasVariantTests"], "Logs/sweep.txt")

        # Assert
        self.assertEqual(problems, [])


class CoverageDrift(unittest.TestCase):
    """Both directions of the uncovered record, which is the only thing that answers for a mechanism
    nobody wrote a cut for."""

    def test_Given_AMechanismWithNoCutAndNoEntry_When_TheRecordIsCompared_Then_ItIsReported(self):
        # Act
        drift = neuter_check.coverage_drift(["Runtime/Styling/StyleNewClass.cs"], set(), set())

        # Assert
        self.assertIn("has no cut and is not recorded", "\n".join(drift))

    def test_Given_AMechanismACutDisables_When_ItIsStillRecorded_Then_ItIsReported(self):
        # Arrange
        path = "Runtime/Styling/StyleNewClass.cs"

        # Act
        drift = neuter_check.coverage_drift([path], {path}, {path})

        # Assert
        self.assertIn("a cut disables it", "\n".join(drift))

    def test_Given_AMechanismACutDisables_When_TheRecordDoesNotNameIt_Then_NothingIsReported(self):
        # Arrange
        path = "Runtime/Styling/StyleNewClass.cs"

        # Act
        drift = neuter_check.coverage_drift([path], {path}, set())

        # Assert
        self.assertEqual(drift, [])

    def test_Given_ARecordedEntryARenameRemoved_When_TheCoverageIsRead_Then_ItIsReported(self):
        # Arrange — an entry describing nothing leaves the record's count looking the same.
        with tempfile.TemporaryDirectory() as tree:
            project = scaffold(Path(tree))
            (project / neuter_check.UNCOVERED_FILE).write_text("Runtime/Styling/StyleGone.cs\n")

            # Act
            problems = neuter_check.coverage_problems(project, {"cuts": {}, "fixtures": {}})

        # Assert
        self.assertIn("names no file", "\n".join(problems))


class RenamedCases(unittest.TestCase):
    """A renamed case is declared by no method, and the given name is the one the results carry.

    Read from method declarations alone, the hole baseline names cases this repository appears not to
    declare, and the audit reports every one as rot in the record rather than as a reader stopping
    short of a spelling.
    """

    def cases_of(self, body):
        with tempfile.TemporaryDirectory() as tree:
            project = Path(tree)
            source = project / "Packages/com.velvet.core/Runtime/Area/Tests/Editor/FooTests.cs"
            source.parent.mkdir(parents=True)
            source.write_text("internal sealed class FooTests\n{\n" + body + "\n}\n")
            return neuter_check.declared_cases(project, "Velvet.Tests.FooTests")

    def test_Given_ACaseRenamedByATestNameArgument_When_ItsSourceIsRead_Then_ThatNameIsDeclared(self):
        # Act
        cases = self.cases_of('    [TestCase(true, TestName = "Given_A_When_B_Then_C")]\n'
                              '    public void Other(bool flag) { }')

        # Assert
        self.assertEqual(cases, {"Given_A_When_B_Then_C"})

    def test_Given_ACaseRenamedBySetName_When_ItsSourceIsRead_Then_ThatNameIsDeclared(self):
        # Act
        cases = self.cases_of('        yield return new TestCaseData(1)\n'
                              '            .SetName("Given_A_When_B_Then_C");')

        # Assert
        self.assertEqual(cases, {"Given_A_When_B_Then_C"})


class HoleBaseline(unittest.TestCase):
    """The baseline is compared as a set, so an entry naming something gone matches no sweep and reads
    as a hole that closed — a guard retired by a rename rather than by a decision."""

    FIXTURE = "Velvet.Tests.GapParityTests"
    CUTS = {"fixtures": {FIXTURE: {"cuts": ["gap-parser"]}}}
    CASES = {FIXTURE: {"Given_A_When_B_Then_C"}}

    def problems(self, line):
        return neuter_check.hole_problems([(1, line)], self.CUTS, self.CASES)

    def test_Given_AWellFormedEntry_When_ItIsRead_Then_NothingIsReported(self):
        # Act
        problems = self.problems(f"{self.FIXTURE}\tgap-parser\tGiven_A_When_B_Then_C\tPassed")

        # Assert
        self.assertEqual(problems, [])

    def test_Given_AnEntryWithAMissingField_When_ItIsRead_Then_ItIsReported(self):
        # Act
        problems = self.problems(f"{self.FIXTURE}\tgap-parser\tGiven_A_When_B_Then_C")

        # Assert
        self.assertIn("3 tab-separated fields", "\n".join(problems))

    def test_Given_AnEntryNamingAFixtureTheMapLost_When_ItIsRead_Then_ItIsReported(self):
        # Act
        problems = self.problems("Velvet.Tests.Gone\tgap-parser\tGiven_A_When_B_Then_C\tPassed")

        # Assert
        self.assertIn("no fixture 'Velvet.Tests.Gone'", "\n".join(problems))

    def test_Given_AnEntryNamingACutItsFixtureIsNotAsked_When_ItIsRead_Then_ItIsReported(self):
        # Act
        problems = self.problems(f"{self.FIXTURE}\tring-parser\tGiven_A_When_B_Then_C\tPassed")

        # Assert
        self.assertIn("not registered against cut 'ring-parser'", "\n".join(problems))

    def test_Given_AnEntryNamingACaseARenameRemoved_When_ItIsRead_Then_ItIsReported(self):
        # Act
        problems = self.problems(f"{self.FIXTURE}\tgap-parser\tGiven_A_When_B_Then_Renamed\tPassed")

        # Assert
        self.assertIn("declares no case", "\n".join(problems))

    def test_Given_AnEntryClaimingTheCutKilledIt_When_ItIsRead_Then_ItIsReported(self):
        # Act
        problems = self.problems(f"{self.FIXTURE}\tgap-parser\tGiven_A_When_B_Then_C\tFailed")

        # Assert
        self.assertIn("'Failed' is not a result a hole can carry", "\n".join(problems))


if __name__ == "__main__":
    unittest.main()

#!/usr/bin/env python3
"""Unit tests for the decisions neuter_check.py makes before and after its Unity runs.

The runs need a licence; the decisions do not, and it is the decisions that make a sweep's output mean
anything. Several cases below are ones where the harness would otherwise report full coverage: a filter
that ran nothing reports no holes, a fixture already red reports every case as killed by the cut, and a
reader that came back empty agrees with every record it is handed.

Run: python3 scripts/test_quality/test_neuter_check.py
"""

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
    """What the audit does on a tree it can find neither a mechanism nor a cut in.

    An empty glob and an empty cut map disagree with no record, so the reading that must never pass is
    the one where there was no reading: an audit that exits 0 having looked at an empty tree is
    indistinguishable from one that checked the repository. The empty cut map is what makes these two
    cases sharp — with no anchor to locate, nothing but the floors can report.
    """

    def audit_empty_tree(self):
        with tempfile.TemporaryDirectory() as tree:
            project = scaffold(Path(tree))
            return neuter_check.audit(project, {"cuts": {}, "fixtures": {}})

    def test_Given_ATreeWithNoMechanismInIt_When_TheAuditReadsIt_Then_TheGlobFloorRefuses(self):
        # Act
        problems = self.audit_empty_tree()

        # Assert
        self.assertTrue(any("mechanism glob found 0" in problem for problem in problems), problems)

    def test_Given_ACutMapThatParsedToNothing_When_TheAuditReadsIt_Then_TheMapFloorRefuses(self):
        # Act
        problems = self.audit_empty_tree()

        # Assert
        self.assertTrue(any("cut map parsed to 0" in problem for problem in problems), problems)


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

    def test_Given_AMechanismACutDisables_When_TheRecordDoesNotName_It_Then_NothingIsReported(self):
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

    Read from method declarations alone, eighteen of StyleFontTests' recorded holes name a case this
    repository does not appear to declare — which the hole baseline would report as rot in the record
    rather than as a reader that stops at one of the three spellings.
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

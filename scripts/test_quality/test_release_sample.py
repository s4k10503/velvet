#!/usr/bin/env python3
"""Unit tests for the release gate's sample: which mutants `mutation_check.py` draws, the order a
capped plan deals them in, and what `--gate` refuses over a sample.

The campaign stand-ins are test_mutation_check.py's, driven against the module it loaded, so a patch
here reaches the code those stand-ins run.

Run: python3 scripts/test_quality/test_release_sample.py
"""

import importlib.util
import json
import shutil
import tempfile
import unittest
from pathlib import Path
from unittest import mock


def load_suite():
    spec = importlib.util.spec_from_file_location(
        "test_mutation_check", Path(__file__).with_name("test_mutation_check.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


suite = load_suite()
mutation_check = suite.mutation_check


def mutants_of(source):
    text = source.read_text()
    return mutation_check.one_per_line(
        mutation_check.mutations_for(source, text, set(range(1, len(text.splitlines()) + 1))))


def salt_ranking(campaign, order):
    """A salt under which `ranked` puts the campaign's two mutants in `order`."""
    project = campaign.project.resolve()
    mutants = mutants_of(project / campaign.SOURCE)
    return next(salt for salt in ("salt-{}".format(number) for number in range(1000))
                if mutation_check.ranked(mutants, project, salt) == order)


def drop_entry(campaign, line):
    """Takes the record's entry for the mutant on `line` out of it."""
    for path in campaign.record.glob("*.json"):
        if json.loads(path.read_text())["mutant"]["line"] == line:
            path.unlink()


class SampleDrawTests(unittest.TestCase):
    """`sampled`: which mutants of a scope the release gate requires a standing verdict for."""

    def mutants(self, body):
        """(the project, the mutants `body` generates as its Probe.cs)."""
        project = Path(tempfile.mkdtemp(prefix="mutation-sample-")).resolve()
        self.addCleanup(shutil.rmtree, project, True)
        source = project / suite.StubbedCampaign.SOURCE
        source.parent.mkdir(parents=True)
        source.write_text(body)
        return project, mutants_of(source)

    @staticmethod
    def drawn_lines(project, mutants, salt, size=10):
        """The text of each drawn mutant's line, so that draws over files laid out differently compare."""
        return sorted(mutants[number - 1].path.read_text().splitlines()[mutants[number - 1].line - 1].strip()
                      for number in mutation_check.sampled(mutants, project, salt, size))

    def test_Given_TheSameMutantsListedInAnotherOrder_When_Sampled_Then_TheSameOnesAreDrawn(self):
        # Arrange
        project, mutants = self.mutants(suite.probe_body(40))
        first = self.drawn_lines(project, mutants, "salt")

        # Act
        reversed_draw = self.drawn_lines(project, list(reversed(mutants)), "salt")

        # Assert
        self.assertEqual(reversed_draw, first)

    def test_Given_TwoSalts_When_TheSameScopeIsSampled_Then_TheDrawsDiffer(self):
        # Arrange
        project, mutants = self.mutants(suite.probe_body(40))
        first = self.drawn_lines(project, mutants, "v3.0.0-main")

        # Act
        second = self.drawn_lines(project, mutants, "v3.1.0-main")

        # Assert
        self.assertNotEqual(second, first)

    def test_Given_ALineInsertedAboveEveryMutant_When_Sampled_Then_TheSameOnesAreDrawn(self):
        # Arrange — the insertion moves every mutant's line and the file's bytes, and no mutation.
        body = suite.probe_body(40)
        project, mutants = self.mutants(body)
        first = self.drawn_lines(project, mutants, "salt")
        moved_project, moved = self.mutants(body.replace("    {\n", "    {\n        // moved\n", 1))

        # Act
        second = self.drawn_lines(moved_project, moved, "salt")

        # Assert
        self.assertEqual(second, first)

    def test_Given_AScopeSmallerThanTheSampleSize_When_Sampled_Then_EveryMutantIsDrawn(self):
        # Arrange
        project, mutants = self.mutants(suite.probe_body(3))

        # Act
        drawn = mutation_check.sampled(mutants, project, "salt")

        # Assert
        self.assertEqual(sorted(drawn), [1, 2, 3])


class RankedDealTests(unittest.TestCase):
    """`--cap` deals the unanswered mutants in rank order."""

    def test_Given_TwoUnansweredMutantsAndACapOfOne_When_Planned_Then_TheLowerRankedIsDealtAndTheOtherPending(self):
        # Arrange — a salt ranking the second mutant first, so rank order is not the list's.
        campaign = suite.RecordCampaign()
        salt = salt_ranking(campaign, [2, 1])

        # Act
        with mock.patch.object(mutation_check, "sample_salt", lambda _project, _base: salt):
            campaign.plan("--cap", "1")
        decision = campaign.project / "reuse.json"
        pending = json.loads(decision.read_text())["pending"] if decision.is_file() else None

        # Assert
        self.assertEqual(pending, [1])

    def test_Given_ALapsedSurvivorRankedLast_When_PlannedWithACapOfOne_Then_ItIsDealtAheadOfTheRest(self):
        # Arrange — an empty sample, the first mutant never measured, and the second's survivor entry
        # lapsed by an edit to a test source; the salt ranks the first ahead of it.
        campaign = suite.RecordCampaign()
        campaign.after_a_campaign()
        drop_entry(campaign, 5)
        lapse_tests(campaign)
        salt = salt_ranking(campaign, [1, 2])

        # Act
        with mock.patch.object(mutation_check, "sample_salt", lambda _project, _base: salt), \
                mock.patch.object(mutation_check, "SAMPLE_SIZE", 0):
            campaign.plan("--cap", "1")
        decision = campaign.project / "reuse.json"
        pending = json.loads(decision.read_text())["pending"] if decision.is_file() else None

        # Assert
        self.assertEqual(pending, [1])

    def test_Given_ASampledMutantAndALapsedSurvivor_When_PlannedWithACapOfOne_Then_TheSampledOneIsDealt(self):
        # Arrange — the first mutant, never measured, is the sample; the second's survivor entry lapsed.
        campaign = suite.RecordCampaign()
        campaign.after_a_campaign()
        drop_entry(campaign, 5)
        lapse_tests(campaign)
        salt = salt_ranking(campaign, [1, 2])

        # Act
        with mock.patch.object(mutation_check, "sample_salt", lambda _project, _base: salt), \
                mock.patch.object(mutation_check, "SAMPLE_SIZE", 1):
            campaign.plan("--cap", "1")
        decision = campaign.project / "reuse.json"
        pending = json.loads(decision.read_text())["pending"] if decision.is_file() else None

        # Assert
        self.assertEqual(pending, [2])


# `RecordCampaign`'s body with its second mutant, on line 7, declared a survivor.
DECLARED_OTHER = suite.RecordCampaign.BODY.replace(
    "        internal static bool Other",
    "        // MUTANT_SURVIVES(equivalent): every caller clamps the operand already.\n"
    "        internal static bool Other")


def lapse_tests(campaign):
    """Changes the code of the fixture no kill was traced to, which lapses every survivor's entry."""
    campaign.write_fixture("Packages/com.velvet.core/Runtime/Tests/Editor/OtherTests.cs", "OtherTests",
                           "Given_A_When_B_Then_Changed")


def gate_over_sample_of_first(campaign):
    """(the exit, what it printed) of the gate whose sample is the first of the two mutants alone."""
    salt = salt_ranking(campaign, [1, 2])
    with mock.patch.object(mutation_check, "sample_salt", lambda _project, _base: salt), \
            mock.patch.object(mutation_check, "SAMPLE_SIZE", 1):
        code = campaign.run_over_diff("--gate", "--reuse-from", str(campaign.record))
    return code, campaign.printed


class SampledGateTests(unittest.TestCase):
    """`--gate` over a sample of one: the first of `RecordCampaign`'s two mutants."""

    @staticmethod
    def gate_with_one_entry(drop, body=None):
        """(the exit, what it printed) with both mutants killed and the entry on line `drop` taken out."""
        campaign = suite.RecordCampaign(body)
        campaign.measure()
        campaign.decide()
        drop_entry(campaign, drop)
        return gate_over_sample_of_first(campaign)

    def test_Given_AnUnansweredSurvivorOutsideTheSample_When_Gated_Then_ItIsRefusedAsUnanswered(self):
        # Arrange — the second mutant, outside the sample, survived and its entry stands.
        campaign = suite.RecordCampaign()
        campaign.after_a_campaign()

        # Act
        code, printed = gate_over_sample_of_first(campaign)
        refusal = printed if code == 1 else ""

        # Assert
        self.assertIn("\nUNANSWERED  Packages/com.velvet.core/Runtime/Probe.cs:6", refusal)

    def test_Given_AnUnansweredSurvivorOutsideTheSampleWhoseEntryLapsed_When_Gated_Then_ItPasses(self):
        # Arrange — a test source changed since the second mutant was recorded as a survivor, which
        # nothing answers; record runs measure it again, and the gate does not wait for them.
        campaign = suite.RecordCampaign()
        campaign.after_a_campaign()
        lapse_tests(campaign)

        # Act
        code, _ = gate_over_sample_of_first(campaign)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ADeclarationOverAnUnmeasuredMutantOutsideTheSample_When_Gated_Then_ItPasses(self):
        # Act — the declaration moves the second mutant to line 7, whose entry is taken out.
        code, _ = self.gate_with_one_entry(7, DECLARED_OTHER)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_OnlyTheSampledMutantInTheRecord_When_Gated_Then_ItPasses(self):
        # Act — the second mutant, on line 6, is outside the sample and unmeasured.
        code, _ = self.gate_with_one_entry(6)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_TheSampledMutantMissingFromTheRecord_When_Gated_Then_ItAloneIsUnresolved(self):
        # Act — the first mutant, on line 5, is the sample, and only the second is in the record.
        code, printed = self.gate_with_one_entry(5)
        unresolved = printed.count("\nUNRESOLVED  ") if code == 1 else None

        # Assert
        self.assertEqual(unresolved, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)

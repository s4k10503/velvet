#!/usr/bin/env python3
"""Unit tests for kill_set_diff.py.

What the comparison reads is the records `mutation_check.py --output` leaves, so the cases build two
directories of them and hand both to `main`, the way the workflow's diff job does. One case writes a
record with `mutation_check.write_verdict` itself, because a reader of a format its writer has since
changed reports nothing lost over directories it can no longer read.

Run: python3 scripts/test_quality/test_kill_set_diff.py
"""

import contextlib
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path


def load_module(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


kill_set_diff = load_module("kill_set_diff")
mutation_check = kill_set_diff.mutation_check

KILLED = mutation_check.KILLED
SURVIVED = mutation_check.SURVIVED

FIRST = "Packages/com.velvet.core/Runtime/A.cs:3 <= -> < (boundary)"
SECOND = "Packages/com.velvet.core/Runtime/A.cs:9 && -> || (logic)"


def record(mutant, verdict, column=4, killers=(), detail=""):
    return {"mutant": mutant, "column": column, "verdict": verdict, "killers": list(killers),
            "detail": detail}


class KillSetDiffTests(unittest.TestCase):

    def compare(self, base, head):
        """(exit status, printed lines) of the comparison over two lists of records, each written under
        the index its position gives it, which is how a campaign numbers them."""
        printed = io.StringIO()
        with tempfile.TemporaryDirectory() as root, contextlib.redirect_stdout(printed):
            directories = []
            for name, records in (("base", base), ("head", head)):
                directory = Path(root) / name
                directory.mkdir()
                for index, held in enumerate(records, start=1):
                    (directory / "mutant-{:03d}.json".format(index)).write_text(json.dumps(held))
                directories.append(str(directory))
            status = kill_set_diff.main(directories)
        return status, printed.getvalue().splitlines()

    def test_Given_AMutantKilledOnTheBaseThatSurvivesOnTheHead_When_Compared_Then_ItIsAReportedLostKill(self):
        # Arrange
        base = [record(FIRST, KILLED, killers=["N.A.Given_X"]), record(SECOND, KILLED)]
        head = [record(FIRST, SURVIVED), record(SECOND, KILLED)]

        # Act
        status, lines = self.compare(base, head)

        # Assert
        self.assertEqual((status, [line for line in lines if line.startswith("LOST KILL")]),
                         (kill_set_diff.LOST, ["LOST KILL  {} (column 4)".format(FIRST)]))

    def test_Given_AMutantTheBaseDidNotKillThatTheHeadDoes_When_Compared_Then_ItPasses(self):
        # Arrange
        base = [record(FIRST, KILLED), record(SECOND, SURVIVED)]
        head = [record(FIRST, KILLED), record(SECOND, KILLED, killers=["N.A.Given_Y"])]

        # Act
        status, lines = self.compare(base, head)

        # Assert
        self.assertEqual((status, len([line for line in lines if line.startswith("extra kill " + SECOND)])),
                         (0, 1))

    def test_Given_AMutantKilledOnTheBaseThatTheHeadNeverMeasured_When_Compared_Then_ItIsALostKill(self):
        # Arrange — a timeout on the head leaves a verdict that is no survivor and no kill.
        base = [record(FIRST, KILLED)]
        head = [record(FIRST, mutation_check.TIMED_OUT)]

        # Act
        status, _ = self.compare(base, head)

        # Assert
        self.assertEqual(status, kill_set_diff.LOST)

    def test_Given_AMutantOnlyOneTreeGenerates_When_Compared_Then_NothingIsCompared(self):
        # Arrange — the head's extra mutant is a kill, which a comparison over the rest would let through.
        base = [record(FIRST, KILLED)]
        head = [record(FIRST, KILLED), record(SECOND, KILLED)]

        # Act
        status, lines = self.compare(base, head)

        # Assert
        self.assertEqual((status, [line for line in lines if "only in" in line]),
                         (kill_set_diff.UNCOMPARABLE, ["  only in head: {} (column 4)".format(SECOND)]))

    def test_Given_TheSameMutantsUnderOtherIndices_When_Compared_Then_TheyAreMatchedByWhatTheyAre(self):
        # Arrange — the head numbers them the other way round, and kills the one the base survived on.
        base = [record(FIRST, KILLED), record(SECOND, SURVIVED)]
        head = [record(SECOND, SURVIVED), record(FIRST, KILLED)]

        # Act
        status, _ = self.compare(base, head)

        # Assert
        self.assertEqual(status, 0)

    def test_Given_TwoMutantsDescribedAlike_When_Compared_Then_TheColumnTellsThemApart(self):
        # Arrange — one line's two mutants, of which the base killed the first and the head the second.
        base = [record(FIRST, KILLED, column=4), record(FIRST, SURVIVED, column=9)]
        head = [record(FIRST, SURVIVED, column=4), record(FIRST, KILLED, column=9)]

        # Act
        status, lines = self.compare(base, head)

        # Assert
        self.assertEqual((status, [line for line in lines if line.startswith("LOST KILL")]),
                         (kill_set_diff.LOST, ["LOST KILL  {} (column 4)".format(FIRST)]))

    def test_Given_AMutantTheSuiteHungOnBase_When_TheHeadCrashedOnIt_Then_ItPasses(self):
        # Arrange
        base = [record(FIRST, mutation_check.HUNG)]
        head = [record(FIRST, mutation_check.CRASHED)]

        # Act
        status, _ = self.compare(base, head)

        # Assert
        self.assertEqual(status, 0)

    def test_Given_ABaseThatKilledNothing_When_Compared_Then_ItProvesNothingAndFails(self):
        # Arrange
        base = [record(FIRST, SURVIVED)]
        head = [record(FIRST, SURVIVED)]

        # Act
        status, _ = self.compare(base, head)

        # Assert
        self.assertEqual(status, kill_set_diff.UNCOMPARABLE)

    def test_Given_TwoRecordsOfOneMutant_When_Compared_Then_NothingIsCompared(self):
        # Arrange — the first reads as the kill the base had, so a comparison taking either would pass.
        base = [record(FIRST, KILLED)]
        head = [record(FIRST, KILLED), record(FIRST, SURVIVED)]

        # Act
        status, _ = self.compare(base, head)

        # Assert
        self.assertEqual(status, kill_set_diff.UNCOMPARABLE)

    def test_Given_ARecordTheCampaignWrote_When_Read_Then_ItIsKeyedByItsDescriptionAndColumn(self):
        # Arrange
        with tempfile.TemporaryDirectory() as root:
            project = Path(root)
            source = project / "Packages/com.velvet.core/Runtime/A.cs"
            mutant = mutation_check.Mutant(source, 3, 12, "<=", "<", "boundary")
            mutant.verdict = KILLED
            mutation_check.write_verdict(project, 1, "digest", mutant, project, ["N.A.Given_X"])

            # Act
            records, problems = kill_set_diff.read_records(project)

        # Assert
        self.assertEqual((sorted(records), problems),
                         ([(mutant.describe(project), 12)], []))


if __name__ == "__main__":
    unittest.main(verbosity=2)

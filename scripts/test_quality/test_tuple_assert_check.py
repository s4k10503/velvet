#!/usr/bin/env python3
"""Unit tests for tuple_assert_check.py's reading, plus a guard over this repository's own record.

Run: python3 scripts/test_quality/test_tuple_assert_check.py
"""

import importlib.util
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT = Path(__file__).resolve().with_name("tuple_assert_check.py")


def load_module():
    """Imports tuple_assert_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location("tuple_assert_check", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


tuple_assert_check = load_module()

RELATIVE = "scripts/probe/test_probe.py"

FIXTURE = """import unittest


class Probe(unittest.TestCase):
    def test_Given_A_When_B_Then_C(self):
        code, said = 2, "x"
        {line}
"""


def entries(line):
    """The entries one assertion line is read as, inside a case of a whole file."""
    return tuple_assert_check.entries_of(FIXTURE.format(line=line), RELATIVE)


def run_check(project, record):
    """The exit code of the check over `project` against a record holding `record`."""
    (project / "scripts/test_quality").mkdir(parents=True, exist_ok=True)
    (project / tuple_assert_check.DEFAULT_BASELINE).write_text(record, encoding="utf-8")
    return subprocess.run([sys.executable, str(SCRIPT), "--project", str(project)],
                          capture_output=True, text=True).returncode


class ReadingTests(unittest.TestCase):

    def test_Given_TwoTupleDisplaysCompared_When_Read_Then_TheCaseAndCallAreRecorded(self):
        # Arrange
        line = 'self.assertEqual((code, "x" in said), (2, True))'

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [RELATIVE + "\tProbe.test_Given_A_When_B_Then_C\t" + line])

    def test_Given_ATupleOfLocalsAgainstAName_When_Read_Then_ItIsPacking(self):
        # Arrange / Act
        found = entries("self.assertEqual((code, said), expected)")

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ALiteralSequenceWrittenFirst_When_Read_Then_ItIsOneExpectedValue(self):
        # Arrange / Act
        found = entries('self.assertEqual(["2.1", "2.0"], said)')

        # Assert
        self.assertEqual(found, [])

    def test_Given_ANegativeLiteralSequenceWrittenFirst_When_Read_Then_ItIsOneExpectedValue(self):
        # Arrange / Act
        found = entries("self.assertEqual([-1, 2], said)")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AConstantTupleAgainstATupleOfLocals_When_Read_Then_ItIsPacking(self):
        # Arrange / Act — the expected value written first, as some suites here do.
        found = entries("self.assertEqual((2, True), (code, bool(said)))")

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AComputedValueAgainstATupleDisplay_When_Read_Then_ItIsOneValue(self):
        # Arrange / Act
        found = entries("self.assertEqual(divmod(code, 2), (code, 0))")

        # Assert
        self.assertEqual(found, [])

    def test_Given_AOneElementTuple_When_Read_Then_NothingIsRecorded(self):
        # Arrange / Act
        found = entries("self.assertEqual((code,), (2,))")

        # Assert
        self.assertEqual(found, [])

    def test_Given_ATupleOutsideAnAssertion_When_Read_Then_NothingIsRecorded(self):
        # Arrange / Act
        found = entries("said.format((code, said), (2, True))")

        # Assert
        self.assertEqual(found, [])


class RecordTests(unittest.TestCase):

    def setUp(self):
        self.project = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.project)
        (self.project / "scripts/probe").mkdir(parents=True)
        self.line = 'self.assertEqual((code, "x" in said), (2, True))'
        (self.project / RELATIVE).write_text(FIXTURE.format(line=self.line), encoding="utf-8")
        self.entry = RELATIVE + "\tProbe.test_Given_A_When_B_Then_C\t" + self.line + "\n"

    def test_Given_TheRecordHoldsWhatIsHere_When_Checked_Then_ItPasses(self):
        # Arrange / Act
        code = run_check(self.project, self.entry)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_APackedAssertionTheRecordLacks_When_Checked_Then_ItFails(self):
        # Arrange / Act
        code = run_check(self.project, "")

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AnEntryRepeatedInTheRecord_When_Checked_Then_TheSpareOneFails(self):
        # Arrange / Act — the record is a multiset, so a rewrite of one of two identical calls is seen.
        code = run_check(self.project, self.entry * 2)

        # Assert
        self.assertEqual(code, 1)

    def test_Given_ThisRepository_When_Checked_Then_TheRecordMatchesTheTree(self):
        # Arrange / Act
        done = subprocess.run([sys.executable, str(SCRIPT), "--project", str(REPO_ROOT)],
                              capture_output=True, text=True)

        # Assert
        self.assertEqual(done.stderr, "")


if __name__ == "__main__":
    unittest.main()

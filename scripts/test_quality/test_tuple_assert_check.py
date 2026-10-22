#!/usr/bin/env python3
"""Unit tests for tuple_assert_check.py's reading, plus a guard over this repository's own record.

Run: python3 scripts/test_quality/test_tuple_assert_check.py
"""

import importlib.util
import os
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


def entries_of_body(body):
    """The entries a block of statements is read as, inside one case."""
    indented = "".join("        " + line + "\n" for line in body.splitlines())
    source = FIXTURE.format(line="").rstrip() + "\n" + indented
    return tuple_assert_check.entries_of(source, RELATIVE)


def run_check(project, record, *interpreter_flags):
    """The completed check over `project` against a record holding `record`."""
    (project / "scripts/test_quality").mkdir(parents=True, exist_ok=True)
    (project / tuple_assert_check.DEFAULT_BASELINE).write_text(record, encoding="utf-8")
    environment = dict(os.environ, GIT_CEILING_DIRECTORIES=str(project.parent))
    return subprocess.run([sys.executable, *interpreter_flags, str(SCRIPT), "--project", str(project)],
                          capture_output=True, text=True, env=environment)


def exit_when_reported(done, text):
    """The exit code where stderr holds `text`, and -1 where it does not, as a crash leaves it."""
    return done.returncode if text in done.stderr else -1


class ReadingTests(unittest.TestCase):

    def test_Given_TwoTupleDisplaysCompared_When_Read_Then_TheCaseAndCallAreRecorded(self):
        # Arrange
        line = 'self.assertEqual((code, "x" in said), (2, True))'

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [RELATIVE + "\tProbe.test_Given_A_When_B_Then_C\t" + line])

    def test_Given_ATupleOfLocalsAgainstAName_When_Read_Then_ItIsPacking(self):
        # Arrange
        line = "self.assertEqual((code, said), expected)"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ALiteralSequenceWrittenFirst_When_Read_Then_ItIsOneExpectedValue(self):
        # Arrange
        line = 'self.assertEqual(["2.1", "2.0"], said)'

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ANegativeLiteralSequenceWrittenFirst_When_Read_Then_ItIsOneExpectedValue(self):
        # Arrange
        line = "self.assertEqual([-1, 2], said)"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AConstantTupleAgainstATupleOfLocals_When_Read_Then_ItIsPacking(self):
        # Arrange
        line = "self.assertEqual((2, True), (code, bool(said)))"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_AComputedValueAgainstATupleDisplay_When_Read_Then_ItIsOneValue(self):
        # Arrange
        line = "self.assertEqual(divmod(code, 2), (code, 0))"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AOneElementTuple_When_Read_Then_NothingIsRecorded(self):
        # Arrange
        line = "self.assertEqual((code,), (2,))"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ATupleOutsideAnAssertion_When_Read_Then_NothingIsRecorded(self):
        # Arrange
        line = "said.format((code, said), (2, True))"

        # Act
        found = entries(line)

        # Assert
        self.assertEqual(found, [])


class BoundDisplayTests(unittest.TestCase):

    def test_Given_ADisplayBoundToALocalThenAsserted_When_Read_Then_ItIsPacking(self):
        # Arrange
        body = 'got = (code, "x" in said)\nself.assertEqual(got, (2, True))'

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ABoundListAssertedAsTheOnlyArgument_When_Read_Then_ItIsPacking(self):
        # Arrange
        body = "got = [code, bool(said)]\nself.assertTrue(got)"

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(len(found), 1)

    def test_Given_ABoundDisplayOfLiterals_When_Read_Then_ItIsOneExpectedValue(self):
        # Arrange
        body = "want = (1, 2)\nself.assertEqual(want, said)"

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(found, [])

    def test_Given_TheNameRebindToAComputedValueBeforeTheCall_When_Read_Then_NothingIsRecorded(self):
        # Arrange
        body = "got = (code, said)\ngot = code\nself.assertEqual(got, 2)"

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ABoundDisplayWrittenSecondAgainstAComputedValue_When_Read_Then_ItIsOneValue(self):
        # Arrange
        body = "want = [code, said]\nself.assertEqual(divmod(code, 2), want)"

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADisplayBoundAfterTheAssertion_When_Read_Then_NothingIsRecorded(self):
        # Arrange
        body = "self.assertEqual(got, 2)\ngot = (code, said)"

        # Act
        found = entries_of_body(body)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADisplayBoundInAnotherCase_When_Read_Then_NothingIsRecorded(self):
        # Arrange
        source = FIXTURE.format(line="got = (code, said)") + (
            "\n    def test_Given_Other(self):\n        self.assertEqual(got, 2)\n")

        # Act
        found = tuple_assert_check.entries_of(source, RELATIVE)

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
        # Arrange
        record = self.entry

        # Act
        done = run_check(self.project, record)

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_APackedAssertionTheRecordLacks_When_Checked_Then_ItFails(self):
        # Arrange
        record = ""

        # Act
        done = run_check(self.project, record)

        # Assert
        self.assertEqual(exit_when_reported(done, "packed\n  " + RELATIVE), 1)

    def test_Given_AnEntryRepeatedInTheRecord_When_Checked_Then_TheSpareOneFails(self):
        # Arrange
        record = self.entry * 2

        # Act
        done = run_check(self.project, record)

        # Assert
        self.assertEqual(exit_when_reported(done, "no longer here, so remove it from the record"), 1)

    def test_Given_AnUntrackedSuiteInAGitWorkTree_When_Checked_Then_ItIsNotRead(self):
        # Arrange
        subprocess.run(["git", "init", "-q", str(self.project)], check=True)
        subprocess.run(["git", "-C", str(self.project), "add", RELATIVE], check=True)
        (self.project / "scripts/probe/test_scratch.py").write_text(
            FIXTURE.format(line=self.line), encoding="utf-8")

        # Act
        done = run_check(self.project, self.entry)

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_ASuiteThatWarnsWhenParsed_When_CheckedWithWarningsAsErrors_Then_ItPasses(self):
        # Arrange
        (self.project / RELATIVE).write_text(FIXTURE.format(line=r'same = "\d"'), encoding="utf-8")

        # Act
        done = run_check(self.project, "", "-W", "error")

        # Assert
        self.assertEqual(done.returncode, 0)

    def test_Given_ThisRepository_When_Checked_Then_TheRecordMatchesTheTree(self):
        # Arrange
        command = [sys.executable, str(SCRIPT), "--project", str(REPO_ROOT)]

        # Act
        done = subprocess.run(command, capture_output=True, text=True)

        # Assert
        self.assertEqual(done.returncode, 0)


if __name__ == "__main__":
    unittest.main()

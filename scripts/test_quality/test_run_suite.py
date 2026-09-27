#!/usr/bin/env python3
"""Unit tests for scripts/test_quality/run_suite.py.

A module that dies during its own imports exits 0 having run nothing, and prints what the check it
imported prints — which reads like a pass. The count is what separates them, and it is read from
outside because no guard inside the module gets to run.

Run: python3 scripts/test_quality/test_run_suite.py
"""

import importlib.util
import subprocess
import shutil
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUNNER = REPO_ROOT / "scripts/test_quality/run_suite.py"

_spec = importlib.util.spec_from_file_location("run_suite", RUNNER)
run_suite = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(run_suite)

PASSING = '''import unittest


class T(unittest.TestCase):
    def test_it(self):
        self.assertTrue(True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
'''

FAILING = PASSING.replace("self.assertTrue(True)", "self.assertTrue(False)")

DIES_ON_IMPORT = '''import sys

print("the check ran")
sys.exit(0)
'''


class CountReadingTests(unittest.TestCase):
    def test_Given_unittestsOwnSummary_When_Read_Then_TheCountIsTaken(self):
        # Act / Assert
        self.assertEqual(run_suite.counted("Ran 12 tests in 0.5s\n\nOK\n"), 12)

    def test_Given_ASingularSummary_When_Read_Then_TheCountIsTaken(self):
        # Arrange — unittest writes "1 test", not "1 tests".
        # Act / Assert
        self.assertEqual(run_suite.counted("Ran 1 test in 0.0s\n\nOK\n"), 1)

    def test_Given_NoSummaryAtAll_When_Read_Then_ThereIsNoCount(self):
        # Arrange — what a module that died during its imports leaves: the check's own stdout and
        # nothing of unittest's.
        # Act / Assert
        self.assertIsNone(run_suite.counted("the check ran\n"))


class VerdictTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="run-suite-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def module(self, name, body):
        path = self.root / name
        path.write_text(body)
        return path

    def verdict(self, path):
        return subprocess.run([sys.executable, str(RUNNER), str(path)],
                              capture_output=True, text=True, timeout=60).returncode

    def test_Given_ASuiteThatRuns_When_ItPasses_Then_TheRunnerPasses(self):
        # Act / Assert
        self.assertEqual(self.verdict(self.module("test_ok.py", PASSING)), 0)

    def test_Given_ASuiteThatRuns_When_ItFails_Then_TheRunnerFails(self):
        # Arrange — the control: a runner that refused everything would satisfy the case below.
        # Act / Assert
        self.assertEqual(self.verdict(self.module("test_bad.py", FAILING)), 1)

    def test_Given_AModuleThatDiesDuringItsImports_When_Run_Then_ItIsRefused(self):
        # Arrange — the shape this exists for. Directly, this exits 0 and prints only what the check
        # it imported printed.
        self.module("probe.py", DIES_ON_IMPORT)
        dies = self.module("test_dies.py", '''import importlib.util, unittest
from pathlib import Path
spec = importlib.util.spec_from_file_location("probe", Path(__file__).with_name("probe.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class T(unittest.TestCase):
    def test_it(self):
        self.assertTrue(False)


if __name__ == "__main__":
    unittest.main(verbosity=2)
''')
        direct = subprocess.run([sys.executable, str(dies)], capture_output=True, text=True,
                                timeout=60).returncode

        # Act / Assert — the direct exit rides along, because a runner that refused a module the
        # plain invocation already refused would be answering a question nobody had.
        self.assertEqual((direct, self.verdict(dies)), (0, 1))


THREE_CLASSES = """import unittest


class A(unittest.TestCase):
    def test_a(self):
        self.assertTrue(True)


class B(unittest.TestCase):
    def test_b(self):
        self.assertTrue({verdict})


class C(unittest.TestCase):
    def test_c(self):
        self.assertTrue(True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
"""

# The first process to import this defines one class more than the others: what a module whose test
# set depends on the process looks like to the runner.
UNEVEN = """import os, unittest
from pathlib import Path

marker = Path(__file__).with_suffix(".marker")
try:
    os.close(os.open(str(marker), os.O_CREAT | os.O_EXCL))
    first = True
except FileExistsError:
    first = False


class A(unittest.TestCase):
    def test_a(self):
        self.assertTrue(True)


if first:
    class B(unittest.TestCase):
        def test_b(self):
            self.assertTrue(True)


if __name__ == "__main__":
    unittest.main(verbosity=2)
"""


class ParallelVerdictTests(VerdictTests):
    def verdict(self, path):
        return subprocess.run([sys.executable, str(RUNNER), "--jobs", "2", str(path)],
                              capture_output=True, text=True, timeout=120).returncode

    def test_Given_ClassesSpreadOverTheProcesses_When_OneFails_Then_TheRunnerFailsWhereOnlyThen(self):
        # Arrange — the passing twin rides along, since a runner failing every parallel run fails
        # this one too.
        passing = self.module("test_passing.py", THREE_CLASSES.format(verdict="True"))
        failing = self.module("test_failing.py", THREE_CLASSES.format(verdict="False"))

        # Act / Assert
        self.assertEqual((self.verdict(passing), self.verdict(failing)), (0, 1))

    def test_Given_AModuleWithNoTests_When_SpreadOverTheProcesses_Then_TheRunIsRefused(self):
        # Arrange — the module with a class rides along, since a runner refusing every parallel run
        # refuses the empty one too.
        empty = self.module("test_empty.py", "import unittest\n\nif __name__ == '__main__':\n"
                                              "    unittest.main(verbosity=2)\n")
        populated = self.module("test_populated.py", PASSING)

        # Act / Assert
        self.assertEqual((self.verdict(populated), self.verdict(empty)), (0, 1))

    def test_Given_ProcessesThatLoadedDifferentTests_When_Counted_Then_TheRunIsRefused(self):
        # Arrange — the even module rides along, since a runner refusing every parallel run satisfies
        # the uneven one too.
        even = self.module("test_even.py", UNEVEN.replace("if first:", "if True:"))
        uneven = self.module("test_uneven.py", UNEVEN)

        # Act / Assert
        self.assertEqual((self.verdict(even), self.verdict(uneven)), (0, 1))


class Fixtureless(unittest.TestCase):
    """Two cases named so the loader does not take them for this module's own."""

    def probe_one(self):
        pass

    def probe_two(self):
        pass


class WithClassFixture(Fixtureless):
    @classmethod
    def setUpClass(cls):
        super().setUpClass()


class UnitTests(unittest.TestCase):
    def test_Given_AClassWithAClassFixture_When_Split_Then_ItsTestsShareAProcess(self):
        # Arrange
        tests = [Fixtureless("probe_one"), Fixtureless("probe_two"),
                 WithClassFixture("probe_one"), WithClassFixture("probe_two")]

        # Act
        sizes = [len(unit) for unit in run_suite.units(tests)]

        # Assert
        self.assertEqual(sizes, [1, 1, 2])


if __name__ == "__main__":
    unittest.main(verbosity=2)

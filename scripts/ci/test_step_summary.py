#!/usr/bin/env python3
"""Unit tests for step_summary.py: the job summaries stay under the size GitHub accepts.

Run: python3 scripts/ci/test_step_summary.py
"""

import importlib.util
import re
import sys
import unittest
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
sys.path.insert(0, str(HERE))


def load_module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


summary = load_module(HERE / "step_summary.py", "step_summary")


def verdict(files, per_file, tail=""):
    """A verdict job's output with `per_file` unanswered survivors in each of `files` files."""
    text = "\n--- mutants no test killed ---\n"
    for f in range(files):
        for n in range(per_file):
            text += "Runtime/F{}.cs:{} a <= b -> a < b (boundary)  [survived] detail\n".format(f, n + 1)
    for f in range(files):
        for n in range(per_file):
            text += "\nUNANSWERED  Runtime/F{}.cs:{} a <= b -> a < b (boundary)\n            nothing answers\n".format(
                f, n + 1)
    text += "\ngate: {} mutant(s); 0 resolved, 0 unresolved, {} unanswered, 0 stale\n".format(
        files * per_file, files * per_file)
    return text + tail


def size(text):
    return len(text.encode())


class BoundTests(unittest.TestCase):
    def test_Given_AVerdictLargerThanTheUploadLimit_When_Summarised_Then_TheBytesStayUnderTheBound(self):
        # Arrange
        text = verdict(200, 60)

        # Act
        written = size(summary.verdict_summary(text))

        # Assert
        self.assertLessEqual(written, summary.LIMIT_BYTES)

    def test_Given_ManyFilesWithinTheCap_When_SummarisedUnderASmallBound_Then_TheBytesStayUnderIt(self):
        # Arrange
        text = verdict(3000, 1)

        # Act
        written = summary.verdict_summary(text, limit=4000)

        # Assert
        self.assertLessEqual(size(written), 4000)

    def test_Given_ManyFilesWithinTheCap_When_SummarisedUnderASmallBound_Then_ThePointerIsStillLast(self):
        # Arrange
        text = verdict(3000, 1)

        # Act
        written = summary.verdict_summary(text, limit=4000)

        # Assert
        self.assertIn("artifact", written.splitlines()[-1])

    def test_Given_ABlockOfOneVeryLongLine_When_Written_Then_TheBytesStayUnderTheBound(self):
        # Arrange
        text = "x" * 3_000_000

        # Act
        written = summary.block("Title", text, limit=10_000)

        # Assert
        self.assertLessEqual(size(written), 10_000)

    def test_Given_AMultiByteTextPastTheBound_When_Written_Then_TheBytesStayUnderTheBound(self):
        # Arrange
        text = "あ" * 100_000

        # Act
        written = summary.block("Title", text, limit=10_000)

        # Assert
        self.assertLessEqual(size(written), 10_000)

    def test_Given_ATextWithinTheBound_When_Clamped_Then_ItIsUnchanged(self):
        # Act
        kept = summary.clamp("a\nb\n", 100)

        # Assert
        self.assertEqual(kept, "a\nb\n")

    def test_Given_ATextWithABacktickFence_When_Blocked_Then_TheFenceIsClosedExactlyOnce(self):
        # Arrange
        text = "before\n```\nafter\n"

        # Act
        written = summary.block("Title", text)

        # Assert
        self.assertEqual(written.count("```"), 2)


class ContentTests(unittest.TestCase):
    def test_Given_ManySurvivorsInOneFile_When_Summarised_Then_OnlyThePerFileCapIsListed(self):
        # Arrange
        text = verdict(1, 25)

        # Act
        written = summary.verdict_summary(text, per_file=10)

        # Assert
        self.assertEqual(written.count("- line "), 10)

    def test_Given_ManySurvivorsInOneFile_When_Summarised_Then_TheRestAreCounted(self):
        # Arrange
        text = verdict(1, 25)

        # Act
        written = summary.verdict_summary(text, per_file=10)

        # Assert
        self.assertIn("and 15 more in this file", written)

    def test_Given_Survivors_When_Summarised_Then_TheCountsByVerdictLead(self):
        # Arrange
        text = verdict(2, 3)

        # Act
        written = summary.verdict_summary(text)

        # Assert
        self.assertLess(written.index("- survived: 6"), written.index("**Runtime/F0.cs**"))

    def test_Given_AVerdict_When_Summarised_Then_ThePointerToTheFullListIsTheLastLine(self):
        # Act
        written = summary.verdict_summary(verdict(1, 1))

        # Assert
        self.assertIn("Mutation campaign verdict", written.splitlines()[-1])


class MainTests(unittest.TestCase):
    def test_Given_APlainFileOverTheBound_When_Run_Then_TheOutputIsUnderTheBound(self):
        # Arrange
        import io, tempfile, contextlib
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "report.md"
            path.write_text("line of the report\n" * 100_000)
            out = io.StringIO()

            # Act
            with contextlib.redirect_stdout(out):
                summary.main(["plain", str(path)])

        # Assert
        self.assertLessEqual(size(out.getvalue()), summary.LIMIT_BYTES + 1)


class WorkflowTests(unittest.TestCase):
    def test_Given_TheWorkflows_When_Read_Then_NoStepAppendsAFileToTheSummaryWithoutTheBound(self):
        # Arrange
        unbounded = []
        for name in ("mutation.yml", "kill-set.yml"):
            for line in (ROOT / ".github" / "workflows" / name).read_text().splitlines():
                if "GITHUB_STEP_SUMMARY" in line and "step_summary.py" not in line:
                    unbounded.append((name, line.strip()))

        # Assert
        self.assertEqual(unbounded, [])


if __name__ == "__main__":
    unittest.main(verbosity=2)

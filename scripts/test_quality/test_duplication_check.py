#!/usr/bin/env python3
"""Unit tests for duplication_check.py's block set, plus a guard over this repository's baseline.

The swap case is the whole point of the file: a change that removes one repeated block and introduces
another leaves the total untouched, so a count-shaped baseline reports nothing while duplication in a
newly written area — the case the check exists for — walks past it.

Run: python3 scripts/test_quality/test_duplication_check.py
"""

import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports duplication_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "duplication_check", Path(__file__).with_name("duplication_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


duplication_check = load_module()


def block(seed):
    """Six lines distinct enough to clear MIN_DISTINCT_LINES, keyed by seed."""
    return "\n".join(f"    var {seed}{index} = Compute{seed}({index});" for index in range(6))


def package(**files):
    """A throwaway package tree, returned as its root."""
    root = Path(tempfile.mkdtemp()) / "Packages/com.velvet.core"
    for name, body in files.items():
        path = root / (name + ".cs")
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body + "\n")
    return root


class BlockSetTests(unittest.TestCase):
    def test_Given_ABlockInTwoFiles_When_TheSetIsRead_Then_ItNamesBothFiles(self):
        # Arrange
        root = package(Alpha=block("a"), Beta=block("a"))

        # Act
        entries = duplication_check.repeated_blocks(root)

        # Assert
        self.assertEqual([entry.split("\t")[1] for entry in entries], ["Alpha.cs,Beta.cs"])

    def test_Given_ABlockInOneFileOnly_When_TheSetIsRead_Then_ItIsAbsent(self):
        # Arrange
        root = package(Alpha=block("a"), Beta=block("b"))

        # Act
        entries = duplication_check.repeated_blocks(root)

        # Assert
        self.assertEqual(entries, set())

    def test_Given_ADuplicateRemovedAndAnotherAdded_When_TheSetsAreCompared_Then_BothShow(self):
        # Arrange — the same number of blocks repeat before and after, so a count sees nothing.
        before = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("a"), Gamma=block("g")))
        after = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("b"), Gamma=block("b")))

        # Act
        added, removed = after - before, before - after

        # Assert — the sizes ride along because two empty sets differ by nothing in either direction.
        self.assertEqual((len(before), len(after), len(added), len(removed)), (1, 1, 1, 1))

    def test_Given_ABlockMovedBetweenFiles_When_TheSetsAreCompared_Then_ItIsADepartureAndAnArrival(self):
        # Arrange — same block, same count, one of its two homes renamed.
        before = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("a")))
        after = duplication_check.repeated_blocks(package(Alpha=block("a"), Delta=block("a")))

        # Act
        changed = (after - before) | (before - after)

        # Assert
        self.assertEqual(len(changed), 2)


class BlockIdReadingTests(unittest.TestCase):
    """What separates a block that started repeating from one that gained or lost a file.

    Both leave an entry on each side of the comparison, and reading the arrival alone says a block
    started repeating — measured, as a design question raised on a pull request over a block that had
    repeated in ten files for as long as the baseline existed.
    """

    def test_Given_AnEntry_When_ItsBlockIsRead_Then_ItIsTheHashAndNotTheFiles(self):
        # Act / Assert
        self.assertEqual(duplication_check.block_id("035c4cad19e4\ta.cs,b.cs"), "035c4cad19e4")

    def test_Given_ABlockMovedBetweenFiles_When_TheSidesAreRead_Then_NeitherBlockStarted(self):
        # Arrange — the same block on both sides, so its arrival is not a block that started.
        before = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("a")))
        after = duplication_check.repeated_blocks(package(Alpha=block("a"), Delta=block("a")))
        arrived, departed = after - before, before - after

        # Act
        started = [e for e in arrived
                   if duplication_check.block_id(e) not in {duplication_check.block_id(d)
                                                            for d in departed}]

        # Assert
        self.assertEqual(started, [])

    def test_Given_ABlockThatDidNotRepeatBefore_When_TheSidesAreRead_Then_ItStarted(self):
        # Arrange — a second home for a block that had none, which is what the sentence is for.
        before = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("b")))
        after = duplication_check.repeated_blocks(package(Alpha=block("a"), Beta=block("a")))
        arrived, departed = after - before, before - after

        # Act
        started = [e for e in arrived
                   if duplication_check.block_id(e) not in {duplication_check.block_id(d)
                                                            for d in departed}]

        # Assert
        self.assertEqual(len(started), 1)


class ExitCodeTests(unittest.TestCase):
    """What the two arrivals cost, which is the half the block-id reading does not decide.

    Separating them is only worth anything if both still stop the change; a reading that renamed one
    of them and let it pass would report duplication more precisely and guard nothing.
    """

    def anchored(self, **files):
        """A package whose baseline is never empty, which `read_baseline` refuses to read."""
        return package(Anchor=block("z"), Mirror=block("z"), **files)

    def run_against(self, before, after):
        """`main` over `after`, baselined on `before`, returning (exit code, stderr)."""
        baseline = Path(tempfile.mkdtemp()) / "baseline.txt"
        baseline.write_text("\n".join(sorted(duplication_check.repeated_blocks(before))) + "\n")
        argv = ["duplication_check.py", "--project", str(after.parents[1]),
                "--baseline", str(baseline)]
        err = io.StringIO()
        with mock.patch.object(sys, "argv", argv), contextlib.redirect_stderr(err), \
                contextlib.redirect_stdout(io.StringIO()):
            return duplication_check.main(), err.getvalue()

    # GREEN_ON_BASE(characterization): the base stops it because every arrival stopped it. This is
    # the half the split must not drop, and dropping it is silent — the check would still run, still
    # report, and let new duplication through.
    def test_Given_ABlockThatStartedRepeating_When_Checked_Then_ItStopsTheChange(self):
        # Arrange
        before = self.anchored(Alpha=block("a"), Beta=block("b"))
        after = self.anchored(Alpha=block("a"), Beta=block("a"))

        # Act
        code, said = self.run_against(before, after)

        # Assert
        self.assertEqual((code, "now repeat that did not before" in said), (1, True))

    def test_Given_ABlockThatMovedFiles_When_Checked_Then_ItIsTheOnlyThingSaid(self):
        # Arrange — a move leaves an entry on each side, and each side read alone names a different
        # thing that did not happen: a block that started repeating, and one that stopped.
        before = self.anchored(Alpha=block("a"), Beta=block("a"))
        after = self.anchored(Alpha=block("a"), Delta=block("a"))

        # Act
        code, said = self.run_against(before, after)

        # Assert
        self.assertEqual((code,
                          "repeat in different files" in said,
                          "now repeat that did not before" in said,
                          "no longer repeat" in said),
                         (1, True, False, False))

    # GREEN_ON_BASE(characterization): a block that stopped repeating ratcheted the baseline before
    # the arrivals were separated, and it is the reading the split had to leave where it was.
    def test_Given_ABlockThatStoppedRepeating_When_Checked_Then_TheBaselineIsRatcheted(self):
        # Arrange
        before = self.anchored(Alpha=block("a"), Beta=block("a"))
        after = self.anchored(Alpha=block("a"), Beta=block("b"))

        # Act
        code, said = self.run_against(before, after)

        # Assert
        self.assertEqual((code, "no longer repeat" in said),
                         (duplication_check.BASELINE_DRIFT_EXIT, True))


class BaselineTests(unittest.TestCase):
    def test_Given_ThisRepositorysPackage_When_ComparedToItsBaseline_Then_TheSetsAgree(self):
        # Arrange
        package_root = REPO_ROOT / duplication_check.PACKAGE_REL
        baseline_path = REPO_ROOT / duplication_check.DEFAULT_BASELINE

        # Act
        current = duplication_check.repeated_blocks(package_root)
        baseline = duplication_check.read_baseline(baseline_path)

        # Assert — the size rides along because two empty sets agree about nothing.
        self.assertEqual((len(baseline) > 0, sorted(current - baseline), sorted(baseline - current)),
                         (True, [], []))

    def test_Given_TheBaselineFile_When_ItsLinesAreRead_Then_EachNamesMoreThanOneFile(self):
        # Arrange — an entry naming one file would mean a block that does not repeat was recorded.
        baseline = duplication_check.read_baseline(REPO_ROOT / duplication_check.DEFAULT_BASELINE)

        # Act
        singles = [entry for entry in baseline if len(entry.split("\t")[1].split(",")) < 2]

        # Assert
        self.assertEqual((len(baseline) > 0, singles), (True, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)

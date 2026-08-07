#!/usr/bin/env python3
"""Unit tests for duplication_check.py's block set, plus a guard over this repository's baseline.

The swap case is the whole point of the file: a change that removes one repeated block and introduces
another leaves the total untouched, so a count-shaped baseline reports nothing while duplication in a
newly written area — the case the check exists for — walks past it.

Run: python3 scripts/test_quality/test_duplication_check.py
"""

import importlib.util
import tempfile
import unittest
from pathlib import Path

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

#!/usr/bin/env python3
"""Unit tests for mutation_record.py: the record branch read and written against a local origin.

Run: python3 scripts/ci/test_mutation_record.py
"""

import importlib.util
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("mutation_record",
                                                  Path(__file__).with_name("mutation_record.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


record = load_module()


class Origin:
    """A bare origin, and clones of it standing for the jobs that read and write the record."""

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-record-"))
        self.bare = self.root / "origin.git"
        subprocess.run(["git", "init", "-q", "--bare", "-b", "main", str(self.bare)], check=True)
        seed = self.clone("seed")
        (seed / "README.md").write_text("probe\n")
        self.git(seed, "add", "README.md")
        self.git(seed, "commit", "-qm", "seed")
        self.git(seed, "push", "-q", "origin", "main")

    @staticmethod
    def git(cwd, *arguments):
        return subprocess.run(["git", "-c", "user.email=p@e", "-c", "user.name=P", *arguments], cwd=cwd,
                              check=True, capture_output=True, text=True).stdout

    def clone(self, name):
        path = self.root / name
        subprocess.run(["git", "clone", "-q", str(self.bare), str(path)], check=True, capture_output=True)
        return path

    def entries(self, name, **contents):
        directory = self.root / name
        directory.mkdir()
        for key, text in contents.items():
            (directory / "{}.json".format(key)).write_text(text)
        return directory

    def remove(self):
        shutil.rmtree(self.root, ignore_errors=True)


class RecordTests(unittest.TestCase):
    def setUp(self):
        self.origin = Origin()
        self.addCleanup(self.origin.remove)

    def fetched(self, name):
        directory = self.origin.root / ("fetched-" + name)
        record.fetch(self.origin.clone(name), directory)
        return {path.name: path.read_text() for path in sorted(directory.glob("*.json"))}

    def test_Given_NoRecordBranch_When_Fetched_Then_TheDirectoryIsEmpty(self):
        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {})

    def test_Given_PublishedEntries_When_Fetched_Then_EachComesBackUnderItsKey(self):
        # Arrange
        record.publish(self.origin.clone("writer"), self.origin.entries("one", a='{"v": 1}'), "first")

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {"a.json": '{"v": 1}'})

    def test_Given_ASecondPublishFromAStaleClone_When_Fetched_Then_BothRunsEntriesAreHeld(self):
        # Arrange — the second writer cloned before the first pushed, so its clone holds no record.
        early, late = self.origin.clone("early"), self.origin.clone("late")
        record.publish(early, self.origin.entries("one", a='{"v": 1}'), "first")
        record.publish(late, self.origin.entries("two", b='{"v": 2}'), "second")

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(sorted(found), ["a.json", "b.json"])

    def test_Given_AnEntryPublishedAgain_When_Fetched_Then_TheNewerVerdictReplacesIt(self):
        # Arrange
        record.publish(self.origin.clone("writer"), self.origin.entries("one", a='{"v": 1}'), "first")
        record.publish(self.origin.clone("again"), self.origin.entries("two", a='{"v": 2}'), "second")

        # Act
        found = self.fetched("reader")

        # Assert
        self.assertEqual(found, {"a.json": '{"v": 2}'})

    def test_Given_APublish_When_ItIsDone_Then_TheWritersCheckoutIsAsItWas(self):
        # Arrange
        writer = self.origin.clone("writer")

        # Act
        record.publish(writer, self.origin.entries("one", a='{"v": 1}'), "first")

        # Assert
        self.assertEqual((self.origin.git(writer, "status", "--porcelain"),
                          self.origin.git(writer, "rev-parse", "--abbrev-ref", "HEAD").strip()), ("", "main"))

    def test_Given_TheRecordBranch_When_Read_Then_ItSharesNoHistoryWithMain(self):
        # Arrange — the record is never merged anywhere, so it carries none of the code's history.
        record.publish(self.origin.clone("writer"), self.origin.entries("one", a='{"v": 1}'), "first")

        # Act
        shared = subprocess.run(["git", "merge-base", "main", record.RECORD_BRANCH], cwd=self.origin.bare,
                                capture_output=True, text=True).stdout.strip()

        # Assert
        self.assertEqual(shared, "")


if __name__ == "__main__":
    unittest.main(verbosity=2)

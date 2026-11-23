#!/usr/bin/env python3
"""Unit tests for per_case_manifest.py.

Run: python3 scripts/ci/test_per_case_manifest.py
"""

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location(
        "per_case_manifest", Path(__file__).with_name("per_case_manifest.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


per_case_manifest = load_module()


def entry(shard, shards=2, roster=10, status=0, problems=(), platform="EditMode"):
    return {"platform": platform, "shard": shard, "shards": shards, "roster": roster, "cases": 5,
            "problems": list(problems), "status": status}


class FaultsTests(unittest.TestCase):

    def faults(self, *entries, expected=None):
        holder = tempfile.TemporaryDirectory()
        self.addCleanup(holder.cleanup)
        for index, item in enumerate(entries):
            directory = Path(holder.name) / "records-{}".format(index)
            directory.mkdir()
            (directory / per_case_manifest.MANIFEST).write_text(json.dumps(item))
        return per_case_manifest.faults(holder.name, expected)

    def test_Given_ShardsThatTileASuite_When_Read_Then_NoFaultIsReported(self):
        # Arrange
        entries = (entry(1), entry(2))

        # Act
        lines = self.faults(*entries)

        # Assert
        self.assertEqual(lines, [])

    def test_Given_AShardWhoseJobWroteNoManifest_When_Read_Then_ItIsNamedMissing(self):
        # Arrange
        entries = (entry(1),)

        # Act
        lines = self.faults(*entries)

        # Assert
        self.assertEqual(lines, ["EditMode: shards [2] of 2 are missing or doubled in what was read"])

    def test_Given_ShardsCutFromRostersOfDifferentLengths_When_Read_Then_TheyAreReported(self):
        # Arrange
        entries = (entry(1, roster=10), entry(2, roster=11))

        # Act
        lines = self.faults(*entries)

        # Assert
        self.assertEqual(lines, ["EditMode: shards were cut from rosters of different lengths: [10, 11]"])

    def test_Given_AShardWithAFailedLaunch_When_Read_Then_ItIsNamedWithTheLaunch(self):
        # Arrange
        entries = (entry(1), entry(2, status=2, problems=["launch 0004: status 2"]))

        # Act
        lines = self.faults(*entries)

        # Assert
        self.assertEqual(lines, ["EditMode 2/2 failed with status 2: launch 0004: status 2"])

    def test_Given_ASuiteExpectedAndNoManifestOfIt_When_Read_Then_ItIsNamedMissing(self):
        # Arrange — six healthy EditMode manifests say nothing of a PlayMode suite that wrote none.
        entries = [entry(shard, shards=6) for shard in range(1, 7)]

        # Act
        lines = self.faults(*entries, expected={"EditMode": 6, "PlayMode": 1})

        # Assert
        self.assertEqual(lines, ["PlayMode: no manifest, so none of its 1 shard(s) wrote records"])

    def test_Given_ASuiteWhoseManifestsCountFewerShardsThanExpected_When_Read_Then_TheCountIsReported(self):
        # Arrange
        entries = (entry(1, shards=2), entry(2, shards=2))

        # Act
        lines = self.faults(*entries, expected={"EditMode": 6})

        # Assert
        self.assertEqual(lines, ["EditMode: expected 6 shard(s), manifests say [2]"])

    def test_Given_ADirectoryWithNoManifest_When_Read_Then_ARunWithNoRecordsIsReported(self):
        # Arrange
        lines = self.faults()

        # Act
        reported = len(lines)

        # Assert
        self.assertEqual(reported, 1)

    def test_Given_ASuiteWithShardsOfDifferentCounts_When_Read_Then_TheCountsAreReported(self):
        # Arrange
        entries = (entry(1, shards=2), entry(2, shards=3))

        # Act
        lines = self.faults(*entries)

        # Assert
        self.assertEqual(lines, ["EditMode: shards disagree on their count: [2, 3]"])


if __name__ == "__main__":
    unittest.main(verbosity=2)

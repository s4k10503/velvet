#!/usr/bin/env python3
"""Unit tests for previous_campaign.py's choice of the earlier campaign a head may carry kills from.

Run: python3 scripts/ci/test_previous_campaign.py
"""

import importlib.util
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location(
        "previous_campaign", Path(__file__).with_name("previous_campaign.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


previous_campaign = load_module()
PATH = previous_campaign.campaign.PATH
HEAD = "c" * 40


def run(number, head, conclusion="failure", path=PATH, status="completed"):
    return {"id": 1000 + number, "run_number": number, "head_sha": head, "conclusion": conclusion,
            "path": path, "status": status}


def chosen(runs, descends=lambda sha: True, records=lambda found: True, current=None):
    found = previous_campaign.previous(runs, HEAD, current, descends, records)
    return found and found["run_number"]


class PreviousCampaignTests(unittest.TestCase):
    def test_Given_TwoEarlierCampaigns_When_Chosen_Then_TheNewerIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40)])

        # Assert
        self.assertEqual(found, 2)

    def test_Given_TheNewerCampaignCancelled_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40, conclusion="cancelled")])

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerCampaignOnThisSameHead_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, HEAD)])

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerCampaignOnAHeadThisOneDoesNotDescendFrom_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40)], descends=lambda sha: sha != "b" * 40)

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerCampaignLeftNoRecords_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40)], records=lambda found: found["run_number"] != 2)

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerCampaignIsThisRun_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40)], current=1002)

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerRunIsAnotherWorkflows_When_Chosen_Then_TheOlderCampaignIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40, path=".github/workflows/test.yml")])

        # Assert
        self.assertEqual(found, 1)

    def test_Given_TheNewerCampaignStillRunning_When_Chosen_Then_TheOlderIsTaken(self):
        # Act
        found = chosen([run(1, "a" * 40), run(2, "b" * 40, conclusion=None, status="in_progress")])

        # Assert
        self.assertEqual(found, 1)


class HoldsRecordsTests(unittest.TestCase):
    def test_Given_EachArtifactAListingCanHold_When_Read_Then_OnlyAnUnexpiredRecordsArtifactCounts(self):
        # Arrange
        listings = [[{"name": "Mutation EditMode shard 3", "expired": False}],
                    [{"name": "Mutation PlayMode shard 0", "expired": False}],
                    [{"name": "Mutation carried", "expired": False}],
                    [{"name": "Mutation EditMode shard 3", "expired": True}],
                    [{"name": "EditMode-results", "expired": False}]]

        # Act
        found = [previous_campaign.holds_records(listing) for listing in listings]

        # Assert
        self.assertEqual(found, [True, True, True, False, False])


if __name__ == "__main__":
    unittest.main(verbosity=2)

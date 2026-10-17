#!/usr/bin/env python3
"""Unit tests for campaign.py: which runs and checks are the campaign's.

Run: python3 scripts/pr/test_campaign.py
"""

import importlib.util
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location("campaign", Path(__file__).with_name("campaign.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


campaign = load_module()


def run(number, conclusion="success", status="completed", path=campaign.PATH):
    return {"id": 9000 + number, "run_number": number, "path": path, "status": status,
            "conclusion": conclusion}


class CampaignRunTests(unittest.TestCase):
    """Which of a head's runs and checks are the campaign's."""

    def test_Given_TheHeadsRuns_When_TheCampaignsSuitesAreRead_Then_EveryCampaignsIsTakenAndNoOther(self):
        # Arrange -- an older campaign, a newer one and a run of another workflow.
        runs = [dict(run(1), check_suite_id=11), dict(run(2), check_suite_id=12),
                dict(run(3, path=".github/workflows/test.yml"), check_suite_id=13)]

        # Act
        found = campaign.suites(runs)

        # Assert
        self.assertEqual(found, {11, 12})

    def test_Given_TheWorkflow_When_ItsDeclaredNameIsRead_Then_ItIsTheNameChecksAreLeftOutBy(self):
        # Arrange
        workflow = (Path(__file__).resolve().parents[2] / campaign.PATH).read_text()

        # Act
        declared = workflow.partition("\n")[0]

        # Assert
        self.assertEqual(declared, "name: " + campaign.NAME)


if __name__ == "__main__":
    unittest.main()

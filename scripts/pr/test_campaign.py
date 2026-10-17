#!/usr/bin/env python3
"""Unit tests for campaign.py: which runs are a campaign's, and the key and ledger a campaign leaves.

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

KEY = "a1" * 32
OTHER = "b2" * 32


def run(number, conclusion="success", status="completed", path=campaign.PATH):
    return {"id": 9000 + number, "run_number": number, "path": path, "status": status,
            "conclusion": conclusion}


def listing(*names, expired=()):
    return {"total_count": len(names),
            "artifacts": [{"name": name, "expired": name in expired} for name in names]}


def measured(key=KEY):
    return listing("Mutation EditMode shard 0", campaign.key_artifact_name(key))


class KeyArtifactTests(unittest.TestCase):
    def test_Given_AKeyAndNoCitedRun_When_ItIsNamedAndRead_Then_ItIsTheKeyMeasuredByNoOther(self):
        # Arrange
        name = campaign.key_artifact_name(KEY)

        # Act
        found = campaign.keyed(listing(name))

        # Assert
        self.assertEqual((name, found), ("Mutation campaign key " + KEY, (KEY, None)))

    def test_Given_AKeyReusedFromARun_When_ItIsNamedAndRead_Then_TheRunIsCited(self):
        # Arrange
        name = campaign.key_artifact_name(KEY, 9001)

        # Act
        found = campaign.keyed(listing(name))

        # Assert
        self.assertEqual((name, found), ("Mutation campaign key {} reused from run 9001".format(KEY),
                                        (KEY, 9001)))

    def test_Given_TheShardArtifactsAlone_When_ReadForAKey_Then_NoneIsFound(self):
        # Arrange
        payload = listing("Mutation EditMode shard 0", "Mutation PlayMode shard 1", "Mutation mutant list")

        # Act
        found = campaign.keyed(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_AnExpiredKeyArtifact_When_Read_Then_NoneIsFound(self):
        # Arrange
        name = campaign.key_artifact_name(KEY)

        # Act
        found = campaign.keyed(listing(name, expired=(name,)))

        # Assert
        self.assertIsNone(found)

    def test_Given_TwoKeysOnOneRun_When_Read_Then_NoneIsFound(self):
        # Arrange
        payload = listing(campaign.key_artifact_name(KEY), campaign.key_artifact_name(OTHER))

        # Act
        found = campaign.keyed(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_APartialListing_When_Read_Then_NoneIsFound(self):
        # Arrange -- the page carries one of two artifacts, so the other may be a second key.
        payload = dict(listing(campaign.key_artifact_name(KEY)), total_count=2)

        # Act
        found = campaign.keyed(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_ANameThatOnlyContainsAKey_When_Read_Then_NoneIsFound(self):
        # Arrange
        payload = listing("not a Mutation campaign key " + KEY)

        # Act
        found = campaign.keyed(payload)

        # Assert
        self.assertIsNone(found)


class ReusableTests(unittest.TestCase):
    def test_Given_APassedCampaignThatMeasuredTheKey_When_Looked_For_Then_ItIsTheOneReused(self):
        # Arrange
        candidates = [(run(3), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertEqual(found, 9003)

    def test_Given_ACampaignOfAnotherKey_When_Looked_For_Then_NothingIsReused(self):
        # Arrange
        candidates = [(run(3), measured(OTHER))]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_AFailedCampaignOfTheKey_When_Looked_For_Then_NothingIsReused(self):
        # Arrange -- a key is only uploaded after a pass, but the run can have failed since, by a
        # re-run of a shard.
        candidates = [(run(3, "failure"), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_ARunOfAnotherWorkflowCarryingTheKey_When_Looked_For_Then_NothingIsReused(self):
        # Arrange
        candidates = [(run(3, path=".github/workflows/test.yml"), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_ACampaignStillRunning_When_Looked_For_Then_NothingIsReused(self):
        # Arrange
        candidates = [(run(3, None, status="in_progress"), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_OnlyACampaignThatReusedTheKey_When_Looked_For_Then_NothingIsReused(self):
        # Arrange -- the run that measured it is not among the candidates, as when its artifacts lapsed.
        candidates = [(run(4), listing(campaign.key_artifact_name(KEY, 9001)))]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_AReuseNewerThanTheMeasuringRun_When_Looked_For_Then_TheMeasuringRunIsTaken(self):
        # Arrange
        candidates = [(run(4), listing(campaign.key_artifact_name(KEY, 9001))), (run(1), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertEqual(found, 9001)

    def test_Given_ACampaignWhoseKeyArtifactExpired_When_Looked_For_Then_NothingIsReused(self):
        # Arrange
        expired = campaign.key_artifact_name(KEY)
        candidates = [(run(3), listing(expired, expired=(expired,)))]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertIsNone(found)

    def test_Given_TwoCampaignsOfTheKey_When_Looked_For_Then_TheFirstListedIsTaken(self):
        # Arrange -- the caller lists newest first.
        candidates = [(run(5), measured()), (run(2), measured())]

        # Act
        found = campaign.reusable(KEY, candidates)

        # Assert
        self.assertEqual(found, 9005)


class OwnKeyArtifactTests(unittest.TestCase):
    """A run carrying both names of one key, as a re-run that measured what it had reused leaves."""

    def test_Given_ARunHoldingMeasuredAndReusedNamesOfOneKey_When_Looked_For_Then_ItIsASource(self):
        # Arrange
        both = listing(campaign.key_artifact_name(KEY, 9001), campaign.key_artifact_name(KEY))

        # Act
        found = campaign.reusable(KEY, [(run(4), both)])

        # Assert
        self.assertEqual(found, 9004)


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
        declared = workflow.startswith("name: " + campaign.NAME + "\n")

        # Assert
        self.assertEqual((declared, campaign.NAME), (True, "Mutation campaign"))


class LedgerArtifactTests(unittest.TestCase):
    def ledgers(self, *entries, total=None):
        return {"total_count": len(entries) if total is None else total,
                "artifacts": [{"id": 70 + position, "name": name, "expired": expired}
                              for position, (name, expired) in enumerate(entries)]}

    def test_Given_OneLiveLedger_When_Read_Then_ItsIdIsTaken(self):
        # Arrange
        payload = self.ledgers(("Mutation EditMode shard 0", False), (campaign.LEDGER_ARTIFACT, False))

        # Act
        found = campaign.ledger_artifact(payload)

        # Assert
        self.assertEqual(found, 71)

    def test_Given_ALedgerThatExpired_When_Read_Then_NoneIsTaken(self):
        # Arrange
        payload = self.ledgers((campaign.LEDGER_ARTIFACT, True))

        # Act
        found = campaign.ledger_artifact(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_TwoLiveLedgers_When_Read_Then_NoneIsTaken(self):
        # Arrange
        payload = self.ledgers((campaign.LEDGER_ARTIFACT, False), (campaign.LEDGER_ARTIFACT, False))

        # Act
        found = campaign.ledger_artifact(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_APartialListing_When_Read_Then_NoneIsTaken(self):
        # Arrange -- more artifacts exist than the page lists, so the ledger could be among the rest twice.
        payload = self.ledgers((campaign.LEDGER_ARTIFACT, False), total=101)

        # Act
        found = campaign.ledger_artifact(payload)

        # Assert
        self.assertIsNone(found)

    def test_Given_TheWorkflowsLedgerUpload_When_ItsNameIsRead_Then_ItIsTheNameTheReaderLooksFor(self):
        # Arrange
        workflow = (Path(__file__).resolve().parents[2] / campaign.PATH).read_text()

        # Act
        uploaded = "          name: " + campaign.LEDGER_ARTIFACT + "\n" in workflow

        # Assert
        self.assertEqual((uploaded, campaign.LEDGER_ARTIFACT), (True, "Mutation campaign ledger"))

    def test_Given_AnAnswerOfTheWrongShape_When_Read_Then_NoneIsTaken(self):
        # Act
        found = campaign.ledger_artifact([])

        # Assert
        self.assertIsNone(found)


if __name__ == "__main__":
    unittest.main()

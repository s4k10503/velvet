#!/usr/bin/env python3
"""Unit tests for campaign.py's reading of the key a passing campaign leaves and of a reused verdict.

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


class UnbackedTests(unittest.TestCase):
    def test_Given_ARunThatMeasuredItself_When_Checked_Then_NothingIsOwed(self):
        # Arrange
        own = measured()

        # Act
        why = campaign.unbacked(own, None, None)

        # Assert
        self.assertIsNone(why)

    def test_Given_ARunCitingAPassThatMeasuredTheKey_When_Checked_Then_NothingIsOwed(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))

        # Act
        why = campaign.unbacked(own, run(1), measured())

        # Assert
        self.assertIsNone(why)

    def test_Given_ARunCitingAFailedRun_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))

        # Act
        why = campaign.unbacked(own, run(1, "failure"), measured())

        # Assert
        self.assertEqual(why, "cites run 9001, which is not a pass of mutation.yml")

    def test_Given_ARunCitingARunThatCannotBeRead_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))

        # Act
        why = campaign.unbacked(own, None, None)

        # Assert
        self.assertEqual(why, "cites run 9001, which could not be read")

    def test_Given_ARunCitingAnotherRunThanTheOneRead_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))

        # Act
        why = campaign.unbacked(own, run(2), measured())

        # Assert
        self.assertEqual(why, "cites run 9001, which is not a pass of mutation.yml")

    def test_Given_ARunCitingAPassOfAnotherKey_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))

        # Act
        why = campaign.unbacked(own, run(1), measured(OTHER))

        # Assert
        self.assertEqual(why, "cites run 9001, which did not itself measure this key")

    def test_Given_ARunCitingAPassWhoseKeyArtifactExpired_When_Checked_Then_ItIsUnbackedAsUnreadable(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001))
        expired = campaign.key_artifact_name(KEY)

        # Act
        why = campaign.unbacked(own, run(1), listing(expired, expired=(expired,)))

        # Assert
        self.assertEqual(why, "cites run 9001, whose key artifact is missing, expired or ambiguous")

    def test_Given_ARunCitingARunThatReusedInTurn_When_Checked_Then_ItIsUnbacked(self):
        # Arrange -- a chain is never followed: the cited run has to have measured.
        own = listing(campaign.key_artifact_name(KEY, 9001))
        cited = listing(campaign.key_artifact_name(KEY, 9000))

        # Act
        why = campaign.unbacked(own, run(1), cited)

        # Assert
        self.assertEqual(why, "cites run 9001, which did not itself measure this key")


class OwnKeyArtifactTests(unittest.TestCase):
    """A run's own record fails closed unless it is wholly absent, as on a run from before the key."""

    def test_Given_ARunWhoseOnlyKeyArtifactExpired_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        name = campaign.key_artifact_name(KEY)

        # Act
        why = campaign.unbacked(listing(name, expired=(name,)), None, None)

        # Assert
        self.assertEqual(why, "holds a key artifact that has expired")

    def test_Given_ARunHoldingArtifactsOfTwoKeys_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY), campaign.key_artifact_name(OTHER))

        # Act
        why = campaign.unbacked(own, None, None)

        # Assert
        self.assertEqual(why, "holds key artifacts of more than one key")

    def test_Given_ARunHoldingMeasuredAndReusedNamesOfOneKey_When_Checked_Then_ItReadsAsMeasured(self):
        # Arrange -- a re-run that measured what it had reused.
        own = listing(campaign.key_artifact_name(KEY), campaign.key_artifact_name(KEY, 9001))

        # Act
        why, found = campaign.unbacked(own, None, None), campaign.keyed(own)

        # Assert
        self.assertEqual((why, found), (None, (KEY, None)))

    def test_Given_ARunHoldingTwoReusedNamesOfOneKey_When_Checked_Then_ItIsUnbacked(self):
        # Arrange
        own = listing(campaign.key_artifact_name(KEY, 9001), campaign.key_artifact_name(KEY, 9002))

        # Act
        why = campaign.unbacked(own, None, None)

        # Assert
        self.assertEqual(why, "holds more than one key artifact")

    def test_Given_ARunHoldingMeasuredAndReusedNamesOfOneKey_When_Looked_For_Then_ItIsASource(self):
        # Arrange
        both = listing(campaign.key_artifact_name(KEY, 9001), campaign.key_artifact_name(KEY))

        # Act
        found = campaign.reusable(KEY, [(run(4), both)])

        # Assert
        self.assertEqual(found, 9004)

    def test_Given_ARunWithNoKeyArtifactAtAll_When_Checked_Then_ItIsAcceptedAsALegacyRun(self):
        # Arrange
        own = listing("Mutation EditMode shard 0")

        # Act
        why = campaign.unbacked(own, None, None)

        # Assert
        self.assertIsNone(why)

    def test_Given_AnExpiredKeyArtifactBesideALiveOne_When_Checked_Then_TheLiveOneStands(self):
        # Arrange -- the control: only a record with nothing live left fails closed.
        name = campaign.key_artifact_name(KEY)
        old = campaign.key_artifact_name(KEY, 9001)

        # Act
        why = campaign.unbacked(listing(name, old, expired=(old,)), None, None)

        # Assert
        self.assertIsNone(why)


class ReasonTests(unittest.TestCase):
    HEAD = "a" * 40

    def test_Given_APassedCampaignWhoseReusedVerdictIsUnbacked_When_Reasoned_Then_TheHeadIsHeld(self):
        # Arrange
        why = "cites run 9001, which did not itself measure this key"

        # Act
        held = campaign.reason({campaign.LABEL}, campaign.PASSED, self.HEAD, "", unbacked=why)

        # Assert
        self.assertEqual(held, "its mutation.yml run on aaaaaaa does not stand: it " + why)

    def test_Given_APassedCampaignWithNothingUnbacked_When_Reasoned_Then_NothingHoldsTheHead(self):
        # Arrange -- the control: the reuse reading changes nothing for a pass that stands.
        held = campaign.reason({campaign.LABEL}, campaign.PASSED, self.HEAD, "", unbacked=None)

        # Assert
        self.assertIsNone(held)

    def test_Given_AFailedCampaign_When_ReasonedWithAnUnbackedVerdict_Then_ItStillReadsAsFailed(self):
        # Arrange
        held = campaign.reason(set(), campaign.FAILED, self.HEAD, "", unbacked="cites run 1")

        # Assert
        self.assertEqual(held, "its mutation.yml run on aaaaaaa failed")


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

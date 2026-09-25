#!/usr/bin/env python3
"""Unit tests for ci_mutation_shard.py: the licence it activates and returns around a campaign.

The editor is replaced by a recorder, since what the cases ask is which commands run and in what
order, and a real activation needs the repository's secrets.

Run: python3 scripts/test_quality/test_ci_mutation_shard.py
"""

import base64
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock


def load_module():
    """Imports ci_mutation_shard by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "ci_mutation_shard", Path(__file__).with_name("ci_mutation_shard.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


ci_mutation_shard = load_module()

SERIAL = "F4-ABCD-EFGH-IJKL-MNOP-QRST"


def licence_file(serial):
    """A `.ulf` carrying `serial` where the real one does: base64 behind four characters of padding."""
    payload = base64.b64encode(("\x01\x02\x03\x04" + serial).encode("latin-1")).decode()
    return '<root><License><DeveloperData Value="{}"/></License></root>'.format(payload)


class Recorder:
    """Stands in for `subprocess.call`, answering activation with `activation` and the campaign with
    `campaign`, and keeping every command it was asked to run."""

    def __init__(self, activation=0, campaign=0):
        self.activation, self.campaign = activation, campaign
        self.commands = []

    def __call__(self, command, **_options):
        self.commands.append(command)
        if "-serial" in command:
            if isinstance(self.activation, BaseException):
                raise self.activation
            return self.activation
        if any(str(part).endswith("mutation_check.py") for part in command):
            return self.campaign
        return 0

    def kinds(self):
        return ["activate" if "-serial" in command else
                "return" if "-returnlicense" in command else "campaign"
                for command in self.commands]


def run_shard(recorder, environ=None):
    with tempfile.TemporaryDirectory() as holder:
        version = Path(holder) / "ProjectVersion.txt"
        version.write_text("m_EditorVersion: 6000.3.23f1\n")
        with mock.patch.object(ci_mutation_shard, "VERSION_FILE", version):
            return ci_mutation_shard.main(
                ["--", "--base", "abc", "--shard", "0/2"],
                environ=environ or {"UNITY_LICENSE": licence_file(SERIAL), "UNITY_EMAIL": "e",
                                    "UNITY_PASSWORD": "p"},
                run=recorder, sleep=lambda _seconds: None, machine_id=lambda: None,
                display=lambda: ({}, lambda: None))


class LicenceTests(unittest.TestCase):
    def test_Given_ALicenceFile_When_ItsSerialIsRead_Then_ThePaddingIsDropped(self):
        # Act / Assert
        self.assertEqual(ci_mutation_shard.serial_from_license(licence_file(SERIAL)), SERIAL)

    def test_Given_ACampaignThatFails_When_TheShardEnds_Then_TheLicenceIsStillReturned(self):
        # Arrange
        recorder = Recorder(campaign=1)

        # Act
        code = run_shard(recorder)

        # Assert
        self.assertEqual((code, recorder.kinds()), (1, ["activate", "campaign", "return"]))

    def test_Given_AnActivationThatNeverSucceeds_When_TheShardRuns_Then_NoMutantIsMeasured(self):
        # Arrange
        recorder = Recorder(activation=1)

        # Act
        code = run_shard(recorder)

        # Assert
        self.assertEqual((code, "campaign" in recorder.kinds()), (1, False))

    def test_Given_AnActivationThatFails_When_TheShardRuns_Then_ItIsAskedAgainBeforeGivingUp(self):
        # Arrange
        recorder = Recorder(activation=1)

        # Act
        run_shard(recorder)

        # Assert
        self.assertEqual(recorder.kinds().count("activate"), ci_mutation_shard.ACTIVATION_ATTEMPTS)

    def test_Given_AnActivationThatNeverExits_When_TheShardRuns_Then_ItFailsRatherThanWaiting(self):
        # Arrange — an editor killed at the bound, which `subprocess.call` reports by raising.
        recorder = Recorder(activation=subprocess.TimeoutExpired("Unity", 1))

        # Act
        code = run_shard(recorder)

        # Assert
        self.assertEqual((code, "campaign" in recorder.kinds()), (1, False))

    def test_Given_TheCampaignsArguments_When_TheShardRuns_Then_TheyReachItWithTheImagesEditor(self):
        # Arrange
        recorder = Recorder()

        # Act
        run_shard(recorder)

        # Assert
        campaign = next(command for command in recorder.commands
                        if any(str(part).endswith("mutation_check.py") for part in command))
        self.assertEqual(campaign[3:], ["--base", "abc", "--shard", "0/2", "--unity",
                                        ci_mutation_shard.UNITY])


if __name__ == "__main__":
    unittest.main(verbosity=2)

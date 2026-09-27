#!/usr/bin/env python3
"""Unit tests for licensed_editor.py: the licence it activates and returns around the work it runs.

The editor is replaced by a recorder, since what the cases ask is which commands run and in what
order, and a real activation needs the repository's secrets.

Run: python3 scripts/ci/test_licensed_editor.py
"""

import base64
import importlib.util
import subprocess
import tempfile
import unittest
from pathlib import Path


def load_module():
    """Imports licensed_editor by path, since scripts/ci is not a package."""
    spec = importlib.util.spec_from_file_location(
        "licensed_editor", Path(__file__).with_name("licensed_editor.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


licensed_editor = load_module()

SERIAL = "F4-ABCD-EFGH-IJKL-MNOP-QRST"


def licence_file(serial):
    """A `.ulf` carrying `serial` where the real one does: base64 behind four characters of padding."""
    payload = base64.b64encode(("\x01\x02\x03\x04" + serial).encode("latin-1")).decode()
    return '<root><License><DeveloperData Value="{}"/></License></root>'.format(payload)


class Recorder:
    """Stands in for `subprocess.call`, answering activation with `activation` and anything that is
    neither an activation nor a return with `work`, and keeping every command it was asked to run."""

    def __init__(self, activation=0, work=0):
        self.activation, self.work = activation, work
        self.commands = []

    def __call__(self, command, **_options):
        self.commands.append(command)
        if "-serial" in command:
            if isinstance(self.activation, BaseException):
                raise self.activation
            return self.activation
        if "-returnlicense" in command:
            return 0
        return self.work

    def kinds(self):
        return ["activate" if "-serial" in command else
                "return" if "-returnlicense" in command else "work"
                for command in self.commands]


def run_editor(recorder, arguments=("--", "-projectPath", "/p", "-executeMethod", "A.B")):
    with tempfile.TemporaryDirectory() as holder:
        version = Path(holder) / "ProjectVersion.txt"
        version.write_text("m_EditorVersion: 6000.6.3f1\n")
        return licensed_editor.main(
            list(arguments), version_file=version,
            environ={"UNITY_LICENSE": licence_file(SERIAL), "UNITY_EMAIL": "e", "UNITY_PASSWORD": "p"},
            run=recorder, sleep=lambda _seconds: None, machine_id=lambda: None,
            display=lambda: ({}, lambda: None))


class LicenceTests(unittest.TestCase):
    def test_Given_ALicenceFile_When_ItsSerialIsRead_Then_ThePaddingIsDropped(self):
        # Act / Assert
        self.assertEqual(licensed_editor.serial_from_license(licence_file(SERIAL)), SERIAL)

    def test_Given_WorkThatFails_When_ItEnds_Then_ItsStatusIsReturnedAndSoIsTheLicence(self):
        # Arrange — a status no path of this module returns of its own, so only the work's can be it.
        recorder = Recorder(work=3)

        # Act
        code = run_editor(recorder)

        # Assert
        self.assertEqual((code, recorder.kinds()), (3, ["activate", "work", "return"]))

    def test_Given_AnActivationThatNeverSucceeds_When_Run_Then_TheWorkNeverRuns(self):
        # Arrange
        recorder = Recorder(activation=1)

        # Act
        code = run_editor(recorder)

        # Assert
        self.assertEqual((code, "work" in recorder.kinds()), (1, False))

    def test_Given_AnActivationThatFails_When_Run_Then_ItIsAskedAgainBeforeGivingUp(self):
        # Arrange
        recorder = Recorder(activation=1)

        # Act
        run_editor(recorder)

        # Assert
        self.assertEqual(recorder.kinds().count("activate"), licensed_editor.ACTIVATION_ATTEMPTS)

    def test_Given_AnActivationThatNeverExits_When_Run_Then_ItFailsRatherThanWaiting(self):
        # Arrange — an editor killed at the bound, which `subprocess.call` reports by raising.
        recorder = Recorder(activation=subprocess.TimeoutExpired("Unity", 1))

        # Act
        code = run_editor(recorder)

        # Assert
        self.assertEqual((code, "work" in recorder.kinds()), (1, False))


class EditorCommandTests(unittest.TestCase):
    def test_Given_ArgumentsAfterTheSeparator_When_Run_Then_TheImagesEditorTakesThemInBatchmode(self):
        # Arrange
        recorder = Recorder()

        # Act
        run_editor(recorder, ["--", "-projectPath", "/p", "-executeMethod", "A.B"])

        # Assert — -quit, so a method that does not exit the editor itself still ends it, and a log on
        # stdout, so a compile error reaches the job's log.
        work = [command for command, kind in zip(recorder.commands, recorder.kinds()) if kind == "work"]
        self.assertEqual(work, [[licensed_editor.UNITY, "-batchmode", "-quit", "-logFile", "/dev/stdout",
                                 "-projectPath", "/p", "-executeMethod", "A.B"]])


if __name__ == "__main__":
    unittest.main()

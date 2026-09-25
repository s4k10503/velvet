#!/usr/bin/env python3
"""Run `mutation_check.py` inside the CI editor image, holding a Unity licence for exactly as long.

`game-ci/unity-test-runner` launches one editor per step, and a campaign launches one per mutant from
inside its own loop, with a source edit between each. So the campaign cannot run through the action; it runs in the same image the action pulls, and this does the two things the
action would otherwise have done around it: activate the licence the repository's secrets describe
before the first editor, and return it after the last, whatever the campaign exited with.

    python3 scripts/test_quality/ci_mutation_shard.py -- --base <sha> --shard 0/4 --max 12

Everything after `--` is handed to mutation_check.py, with `--unity` set to the image's editor.
Run it under `xvfb-run`, which is how the image's own `unity-editor` wrapper gives the editor a display.
"""

import base64
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

UNITY = "/opt/unity/Editor/Unity"
CAMPAIGN = Path(__file__).resolve().with_name("mutation_check.py")
VERSION_FILE = Path("ProjectSettings/ProjectVersion.txt")

ACTIVATION_ATTEMPTS = 5
FIRST_BACKOFF = 15


def serial_from_license(license_text):
    """The serial a `.ulf` carries, which is what an activation is asked with.

    The same reading `game-ci/unity-test-runner` takes of the `UNITY_LICENSE` secret: the base64 in
    `DeveloperData`, less the four characters in front of the serial.
    """
    found = re.search(r'<DeveloperData Value="([^"]*)"', license_text)
    if not found:
        raise SystemExit("UNITY_LICENSE holds no DeveloperData, so no serial can be read from it")
    return base64.b64decode(found.group(1)).decode("latin-1")[4:]


def blank_project(root, version_file):
    """A project with nothing to import, for the editor launches that only activate or return."""
    settings = Path(root) / "ProjectSettings"
    settings.mkdir(parents=True)
    (settings / "ProjectVersion.txt").write_text(Path(version_file).read_text())
    (Path(root) / "Packages").mkdir()
    (Path(root) / "Packages" / "manifest.json").write_text('{"dependencies": {}}\n')
    (Path(root) / "Assets").mkdir()
    return Path(root)


def licence_command(unity, blank, *arguments):
    return [unity, "-batchmode", "-quit", "-logFile", "/dev/stdout", *arguments,
            "-projectPath", str(blank)]


def activate(unity, blank, serial, email, password, run=subprocess.call, sleep=time.sleep):
    """Whether the editor took the licence, retrying with a doubling wait as the action does."""
    delay = FIRST_BACKOFF
    for attempt in range(1, ACTIVATION_ATTEMPTS + 1):
        if run(licence_command(unity, blank, "-serial", serial, "-username", email,
                               "-password", password)) == 0:
            return True
        if attempt < ACTIVATION_ATTEMPTS:
            print("::warning::licence activation failed, attempt {} of {}; retrying in {}s".format(
                attempt, ACTIVATION_ATTEMPTS, delay), flush=True)
            sleep(delay)
            delay *= 2
    return False


def randomize_machine_id():
    """What the action's entrypoint does before activating a serial beginning `F`."""
    identifier = subprocess.run(["dbus-uuidgen"], capture_output=True, text=True, check=True).stdout
    Path("/etc/machine-id").write_text(identifier)
    Path("/var/lib/dbus").mkdir(parents=True, exist_ok=True)
    link = Path("/var/lib/dbus/machine-id")
    if link.is_symlink() or link.exists():
        link.unlink()
    link.symlink_to("/etc/machine-id")


def main(argv, environ=os.environ, run=subprocess.call, sleep=time.sleep,
         machine_id=randomize_machine_id):
    passthrough = argv[argv.index("--") + 1:] if "--" in argv else argv
    serial = environ.get("UNITY_SERIAL") or serial_from_license(environ.get("UNITY_LICENSE", ""))
    email, password = environ.get("UNITY_EMAIL", ""), environ.get("UNITY_PASSWORD", "")
    # Masked before anything can print it: the runner masks the secrets it was given, and the serial
    # is derived from one rather than being one.
    print("::add-mask::{}".format(serial))
    print("::add-mask::{}XXXX".format(serial[:-4]), flush=True)
    if serial.startswith("F"):
        machine_id()

    with tempfile.TemporaryDirectory(prefix="blank-") as holder:
        blank = blank_project(Path(holder) / "project", VERSION_FILE)
        if not activate(UNITY, blank, serial, email, password, run, sleep):
            print("::error::the Unity licence could not be activated, so no mutant was measured")
            return 1
        try:
            return run([sys.executable, str(CAMPAIGN), *passthrough, "--unity", UNITY])
        finally:
            run(licence_command(UNITY, blank, "-returnlicense", "-username", email,
                                "-password", password))


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

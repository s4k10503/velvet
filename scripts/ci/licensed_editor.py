#!/usr/bin/env python3
"""Run a command inside the CI editor image while holding a Unity licence, and return the licence after.

The workflow's own `docker run` of a `unityci/editor` image is what calls this, where a game-ci action
cannot be used: a mutation campaign launches one editor per mutant from inside its own loop, and the
newest-editor compile needs `--shm-size`, which unity-builder v5 has no input for, while v6 fails any
build method whose log lacks the success text game-ci's own build script prints. This does what the
action would otherwise have done around the work: activate
the licence the repository's secrets describe before the first editor, and return it after the last,
whatever the work exited with. The work's exit status is what this exits with.

    python3 scripts/ci/licensed_editor.py -- -projectPath /github/workspace -executeMethod <Type.Method>

Run that way, everything after `--` is handed to the image's editor in batchmode, logging to stdout.
`ci_mutation_shard.py` calls `run_licensed` with a command of its own. Every launch gets a display from
an Xvfb this starts, which is what the image's own `unity-editor` wrapper gives each launch through
`xvfb-run`.
"""

import base64
import functools
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

UNITY = "/opt/unity/Editor/Unity"
VERSION_FILE = Path("ProjectSettings/ProjectVersion.txt")

ACTIVATION_ATTEMPTS = 5
FIRST_BACKOFF = 15
# Seconds before an editor launch that only activates or returns is killed. Without a bound, one that
# never exits holds the job to its own timeout with nothing measured.
LICENCE_TIMEOUT = 600


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


def editor_command(arguments, unity=UNITY):
    """The editor launch `main` runs: batchmode, quitting when done, its log on stdout."""
    return [unity, "-batchmode", "-quit", "-logFile", "/dev/stdout", *arguments]


def bounded(run, command, what):
    """The launch's exit status, or None where it outran `LICENCE_TIMEOUT` and was killed."""
    try:
        return run(command, timeout=LICENCE_TIMEOUT)
    except subprocess.TimeoutExpired:
        print("::warning::the {} ran past {}s and was killed".format(what, LICENCE_TIMEOUT), flush=True)
        return None


def activate(unity, blank, serial, email, password, run=subprocess.call, sleep=time.sleep):
    """Whether the editor took the licence, retrying with a doubling wait as the action does."""
    delay = FIRST_BACKOFF
    for attempt in range(1, ACTIVATION_ATTEMPTS + 1):
        if bounded(run, licence_command(unity, blank, "-serial", serial, "-username", email,
                                        "-password", password), "licence activation") == 0:
            return True
        if attempt < ACTIVATION_ATTEMPTS:
            print("::warning::licence activation failed, attempt {} of {}; retrying in {}s".format(
                attempt, ACTIVATION_ATTEMPTS, delay), flush=True)
            sleep(delay)
            delay *= 2
    return False


def say(what):
    """A phase line, timestamped, so the step's log shows where a job spent its time."""
    print("[{}] {}".format(time.strftime("%H:%M:%S", time.gmtime()), what), flush=True)


def start_display(number=99, wait=30):
    """(the environment carrying DISPLAY, a function that stops the server)."""
    server = subprocess.Popen(["Xvfb", ":{}".format(number), "-screen", "0", "1280x1024x24",
                               "-nolisten", "tcp"])
    socket = Path("/tmp/.X11-unix/X{}".format(number))
    deadline = time.time() + wait
    while not socket.exists() and server.poll() is None and time.time() < deadline:
        time.sleep(0.5)
    if not socket.exists():
        server.kill()
        raise SystemExit("Xvfb did not come up on :{} within {}s".format(number, wait))

    def stop():
        server.terminate()
        server.wait()

    return dict(os.environ, DISPLAY=":{}".format(number)), stop


def randomize_machine_id():
    """What the action's entrypoint does before activating a serial beginning `F`."""
    identifier = subprocess.run(["dbus-uuidgen"], capture_output=True, text=True, check=True).stdout
    Path("/etc/machine-id").write_text(identifier)
    Path("/var/lib/dbus").mkdir(parents=True, exist_ok=True)
    link = Path("/var/lib/dbus/machine-id")
    if link.is_symlink() or link.exists():
        link.unlink()
    link.symlink_to("/etc/machine-id")


def run_licensed(command, what, version_file=VERSION_FILE, environ=os.environ, run=subprocess.call,
                 sleep=time.sleep, machine_id=randomize_machine_id, display=start_display):
    """`command`'s exit status, run under a display between an activation and a return; 1 where the
    licence could not be activated, in which case `command` never runs."""
    serial = environ.get("UNITY_SERIAL") or serial_from_license(environ.get("UNITY_LICENSE", ""))
    email, password = environ.get("UNITY_EMAIL", ""), environ.get("UNITY_PASSWORD", "")
    # Masked before anything can print it: the runner masks the secrets it was given, and the serial
    # is derived from one rather than being one.
    print("::add-mask::{}".format(serial))
    print("::add-mask::{}XXXX".format(serial[:-4]), flush=True)
    if serial.startswith("F"):
        machine_id()

    shown, stop = display()
    launch = functools.partial(run, env=shown)
    try:
        with tempfile.TemporaryDirectory(prefix="blank-") as holder:
            blank = blank_project(Path(holder) / "project", version_file)
            say("activating the licence")
            if not activate(UNITY, blank, serial, email, password, launch, sleep):
                print("::error::the Unity licence could not be activated, so {} did not run".format(what))
                return 1
            try:
                say("running {}".format(what))
                return launch(command)
            finally:
                say("returning the licence")
                bounded(launch, licence_command(UNITY, blank, "-returnlicense", "-username", email,
                                                "-password", password), "licence return")
    finally:
        stop()


def main(argv, **licensed):
    arguments = argv[argv.index("--") + 1:] if "--" in argv else argv
    return run_licensed(editor_command(arguments), "the editor", **licensed)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))

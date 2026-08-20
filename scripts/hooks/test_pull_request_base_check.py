#!/usr/bin/env python3
"""Unit tests for pull_request_base_check.py.

Each synthetic guard below is one way a merge guard can relate to a base. One compares every head
against `main` whatever the pull request says, which is the defect the check exists for; two read
the base off the pull request, one for each thing a base is asked; one refuses nothing, which is how
a directory satisfies the first world by doing nothing at all; and one reaches for a reading no
world here arranges, whose verdict is therefore about an unreadable state rather than about a base.

The check over this repository's own guards is a workflow step rather than a case here — running it
twice per job costs two more worlds and answers the same question.

Run: python3 scripts/hooks/test_pull_request_base_check.py
"""

import importlib.util
import shutil
import tempfile
import unittest
from pathlib import Path


def load_module():
    """Imports pull_request_base_check by path, since scripts/hooks is not a package."""
    spec = importlib.util.spec_from_file_location(
        "pull_request_base_check", Path(__file__).with_name("pull_request_base_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


check = load_module()

PREAMBLE = '''#!/usr/bin/env python3
"""A synthetic guard."""
import json
import subprocess
import sys

HOOK_TOOLS = {"Bash"}


def where():
    payload = json.load(sys.stdin)
    if payload.get("tool_name") not in HOOK_TOOLS:
        sys.exit(0)
    return payload["cwd"]


def pull_request(cwd):
    finished = subprocess.run(["gh", "api", "repos/{owner}/{repo}/pulls/1"],
                              cwd=cwd, capture_output=True, text=True)
    return json.loads(finished.stdout) if finished.returncode == 0 else None


def contains(cwd, base, head):
    subprocess.run(["git", "-C", cwd, "fetch", "-q", "origin", base, head], capture_output=True)
    return subprocess.run(["git", "-C", cwd, "merge-base", "--is-ancestor",
                           "origin/" + base, "origin/" + head], capture_output=True).returncode == 0


def refuse(reason):
    sys.stderr.write(reason + "\\n")
    sys.exit(2)


'''

COMPARES_AGAINST_MAIN = PREAMBLE + '''cwd = where()
if not contains(cwd, "main", pull_request(cwd)["head"]["ref"]):
    refuse("it does not contain origin/main")
sys.exit(0)
'''

REFUSES_A_HEAD_BEHIND_ITS_BASE = PREAMBLE + '''cwd = where()
target = pull_request(cwd)
if not contains(cwd, target["base"]["ref"], target["head"]["ref"]):
    refuse("it does not contain the base it names")
sys.exit(0)
'''

REFUSES_AN_UNPUBLISHED_BASE = PREAMBLE + '''cwd = where()
base = pull_request(cwd)["base"]["ref"]
subprocess.run(["git", "-C", cwd, "fetch", "-q", "origin", base], capture_output=True)
declared = subprocess.run(
    ["git", "-C", cwd, "show", "origin/" + base + ":Packages/com.velvet.core/package.json"],
    capture_output=True, text=True)
version = json.loads(declared.stdout)["version"]
tags = subprocess.run(["git", "-C", cwd, "ls-remote", "--tags", "origin"],
                      capture_output=True, text=True).stdout
if "refs/tags/v" + version not in tags:
    refuse("its base holds an unpublished release")
sys.exit(0)
'''

REFUSES_NOTHING = PREAMBLE + '''where()
sys.exit(0)
'''

READS_WHAT_NO_WORLD_ARRANGES = PREAMBLE + '''cwd = where()
subprocess.run(["gh", "api", "repos/{owner}/{repo}/issues"], cwd=cwd, capture_output=True)
sys.exit(0)
'''


def directory(**guards):
    """A guard directory holding one file per named source."""
    made = Path(tempfile.mkdtemp(prefix="velvet-base-guards-"))
    for name, source in guards.items():
        path = made / (name + ".py")
        path.write_text(source, encoding="utf-8")
        path.chmod(0o755)
    return made


class GuardTests(unittest.TestCase):
    def setUp(self):
        self.directories = []

    def tearDown(self):
        for made in self.directories:
            shutil.rmtree(made, ignore_errors=True)

    def faults(self, floor=1, **guards):
        made = directory(**guards)
        self.directories.append(made)
        return check.faults(made, floor=floor)

    def test_Given_AGuardComparingEveryHeadAgainstMain_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(hard_coded=COMPARES_AGAINST_MAIN,
                            stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual([fault.split(":")[0] for fault in found], ["hard_coded.py"])

    def test_Given_GuardsThatReadTheBaseOffThePullRequest_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange / Act
        found = self.faults(stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual(found, [])

    def test_Given_NothingRefusingAHeadBehindItsBase_When_TheCheckRuns_Then_TheFloorIsReported(self):
        # Arrange / Act
        found = self.faults(unpublished=REFUSES_AN_UNPUBLISHED_BASE, quiet=REFUSES_NOTHING)

        # Assert
        self.assertEqual(found, ["no guard refuses a head that does not contain the base it names, "
                                 "so the world above is satisfied by guards that refuse nothing"])

    def test_Given_NothingRefusingAnUnpublishedBase_When_TheCheckRuns_Then_TheFloorIsReported(self):
        # Arrange / Act
        found = self.faults(stale=REFUSES_A_HEAD_BEHIND_ITS_BASE, quiet=REFUSES_NOTHING)

        # Assert
        self.assertEqual(found, ["no guard refuses a merge onto a base holding a version the "
                                 "CHANGELOG closed and nobody published, so the world above is "
                                 "satisfied by guards that refuse nothing"])

    def test_Given_AGuardReadingWhatNoWorldArranges_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE,
                            elsewhere=READS_WHAT_NO_WORLD_ARRANGES)

        # Assert
        self.assertEqual([fault.splitlines()[-1].strip() for fault in found],
                         ["gh api repos/{owner}/{repo}/issues"])

    def test_Given_ADirectoryHoldingFewerGuardsThanTheFloor_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(floor=3, stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual([fault.split(" holds ")[-1] for fault in found], ["2 guards, fewer than 3"])


if __name__ == "__main__":
    unittest.main(verbosity=2)

#!/usr/bin/env python3
"""Unit tests for pull_request_base_check.py.

Each synthetic guard below is one way a merge guard can relate to a base. One compares every head
against `main` whatever the pull request says, which is the defect the check exists for; two read
the base off the pull request, one for each thing a base is asked, and both again reading the first
merge in a command and no other; one refuses nothing, which is how a directory satisfies the first
world by doing nothing at all; one refuses by printing a deny decision rather than by its exit code;
and one reaches for a reading no world here arranges, whose verdict is therefore about an unreadable
state rather than about a base.

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


def posed():
    """The checkout, and every pull request the command would merge."""
    payload = json.load(sys.stdin)
    if payload.get("tool_name") not in HOOK_TOOLS:
        sys.exit(0)
    command = payload["tool_input"]["command"]
    return payload["cwd"], [token for token in command.split() if token.isdigit()]


def pull_request(cwd, number):
    finished = subprocess.run(["gh", "api", "repos/{owner}/{repo}/pulls/" + number],
                              cwd=cwd, capture_output=True, text=True)
    return json.loads(finished.stdout) if finished.returncode == 0 else None


def contains(cwd, base, head):
    subprocess.run(["git", "-C", cwd, "fetch", "-q", "origin", base, head], capture_output=True)
    return subprocess.run(["git", "-C", cwd, "merge-base", "--is-ancestor",
                           "origin/" + base, "origin/" + head], capture_output=True).returncode == 0


def refuse(reason):
    sys.stderr.write(reason + "\\n")
    sys.exit(2)


cwd, numbers = posed()
'''

COMPARES_AGAINST_MAIN = PREAMBLE + '''for number in numbers:
    if not contains(cwd, "main", pull_request(cwd, number)["head"]["ref"]):
        refuse("it does not contain origin/main")
sys.exit(0)
'''

REFUSES_A_HEAD_BEHIND_ITS_BASE = PREAMBLE + '''for number in numbers:
    target = pull_request(cwd, number)
    if not contains(cwd, target["base"]["ref"], target["head"]["ref"]):
        refuse("it does not contain the base it names")
sys.exit(0)
'''

REFUSES_AN_UNPUBLISHED_BASE = PREAMBLE + '''for number in numbers:
    base = pull_request(cwd, number)["base"]["ref"]
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

# The same two, reading the first merge in the command and no other — the shape both base-reading
# guards had until a compound command was posed to them.
FIRST_MERGE_ONLY = "for number in numbers[:1]:"
STALE_FIRST_ONLY = REFUSES_A_HEAD_BEHIND_ITS_BASE.replace("for number in numbers:", FIRST_MERGE_ONLY)
UNPUBLISHED_FIRST_ONLY = REFUSES_AN_UNPUBLISHED_BASE.replace("for number in numbers:",
                                                             FIRST_MERGE_ONLY)

REFUSES_NOTHING = PREAMBLE + '''sys.exit(0)
'''

READS_WHAT_NO_WORLD_ARRANGES = PREAMBLE + '''subprocess.run(
    ["gh", "api", "repos/{owner}/{repo}/issues"], cwd=cwd, capture_output=True)
sys.exit(0)
'''

# A refusal that is not an exit code: blind_git_add.py refuses this way, and reading 0 as a pass
# would score it as a guard that allowed.
DENIES_BY_DECISION = PREAMBLE + '''sys.stdout.write(json.dumps({"hookSpecificOutput": {
    "permissionDecision": "deny", "permissionDecisionReason": "no"}}))
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

    def test_Given_GuardsReadingOnlyTheFirstMergeInACommand_When_TheCheckRuns_Then_EachIsNamed(self):
        # Arrange / Act
        found = self.faults(stale=STALE_FIRST_ONLY, unpublished=UNPUBLISHED_FIRST_ONLY)

        # Assert — the whole text, not the names in front of it: a cut that reports every guard from
        # the first world names these two as well, and a comparison over names alone passes on it.
        compound = ("refuses that merge on its own and allows a command carrying it second, so it "
                    "reads an operand rather than the command")
        self.assertEqual(found, [f"stale.py: {compound}", f"unpublished.py: {compound}"])

    def test_Given_AGuardRefusingByDecisionRatherThanExitCode_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE,
                            denier=DENIES_BY_DECISION)

        # Assert
        self.assertEqual([fault.split(":")[0] for fault in found], ["denier.py"])

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

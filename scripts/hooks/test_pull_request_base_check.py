#!/usr/bin/env python3
"""Unit tests for pull_request_base_check.py.

Each synthetic guard below is one way a merge guard can relate to a base. One reads `main`'s push
runs whatever the pull request says, which is the defect the check exists for; two read the base
off the pull request, one for each thing a base is asked, and both again reading the first merge in
a command and no other; one refuses a head that does not contain its base, which a head whose own
checks passed is not refused for; one refuses nothing, which is how a directory satisfies the first
worlds by doing nothing at all; one refuses by printing a deny decision rather than by its exit code;
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


def failed_past(cwd, base, head):
    """Whether a required workflow's last push run on `base` failed at a commit `head` lacks."""
    for workflow in ("test.yml", "generators.yml"):
        listed = subprocess.run(["gh", "api", "repos/{owner}/{repo}/actions/workflows/" + workflow
                                 + "/runs?branch=" + base + "&event=push&per_page=20"],
                                cwd=cwd, capture_output=True, text=True)
        newest = json.loads(listed.stdout)["workflow_runs"][0]
        subprocess.run(["git", "-C", cwd, "fetch", "-q", "origin", head], capture_output=True)
        held = subprocess.run(["git", "-C", cwd, "merge-base", "--is-ancestor", newest["head_sha"],
                               "origin/" + head], capture_output=True).returncode == 0
        if newest["conclusion"] == "failure" and not held:
            return True
    return False


def refuse(reason):
    sys.stderr.write(reason + "\\n")
    sys.exit(2)


cwd, numbers = posed()
'''

READS_MAINS_RUNS = PREAMBLE + '''for number in numbers:
    if failed_past(cwd, "main", pull_request(cwd, number)["head"]["ref"]):
        refuse("origin/main last failed on push")
sys.exit(0)
'''

REFUSES_A_RED_BASE = PREAMBLE + '''for number in numbers:
    target = pull_request(cwd, number)
    if failed_past(cwd, target["base"]["ref"], target["head"]["ref"]):
        refuse("the base it names last failed on push")
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

# The red-base and publication guards, reading the first merge in the command and no other — the
# shape the first two base-reading guards had until a compound command was posed to them.
FIRST_MERGE_ONLY = "for number in numbers[:1]:"
RED_FIRST_ONLY = REFUSES_A_RED_BASE.replace("for number in numbers:", FIRST_MERGE_ONLY)
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

    def test_Given_AGuardReadingMainsRunsWhateverTheBase_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(hard_coded=READS_MAINS_RUNS,
                            red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert — once per world where the head lacks main's tip and the named base is green.
        self.assertEqual([fault.split(":")[0] for fault in found], ["hard_coded.py", "hard_coded.py"])

    def test_Given_GuardsThatReadTheBaseOffThePullRequest_When_TheCheckRuns_Then_NothingIsReported(self):
        # Arrange / Act
        found = self.faults(red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AGuardRefusingAHeadBehindAGreenBase_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(stale=REFUSES_A_HEAD_BEHIND_ITS_BASE,
                            red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual([fault.split(":")[0] for fault in found], ["stale.py"])

    def test_Given_NothingRefusingARedBase_When_TheCheckRuns_Then_TheFloorIsReported(self):
        # Arrange / Act
        found = self.faults(unpublished=REFUSES_AN_UNPUBLISHED_BASE, quiet=REFUSES_NOTHING)

        # Assert
        self.assertEqual(found, ["no guard refuses a merge onto a base whose push runs last failed "
                                 "at a commit the head lacks, so the worlds above are satisfied by "
                                 "guards that refuse nothing"])

    def test_Given_NothingRefusingAnUnpublishedBase_When_TheCheckRuns_Then_TheFloorIsReported(self):
        # Arrange / Act
        found = self.faults(red=REFUSES_A_RED_BASE, quiet=REFUSES_NOTHING)

        # Assert
        self.assertEqual(found, ["no guard refuses a merge onto a base holding a version the "
                                 "CHANGELOG closed and nobody published, so the worlds above are "
                                 "satisfied by guards that refuse nothing"])

    def test_Given_GuardsReadingOnlyTheFirstMergeInACommand_When_TheCheckRuns_Then_EachIsNamed(self):
        # Arrange / Act
        found = self.faults(red=RED_FIRST_ONLY, unpublished=UNPUBLISHED_FIRST_ONLY)

        # Assert — the whole text, not the names in front of it: a cut that reports every guard from
        # the first world names these two as well, and a comparison over names alone passes on it.
        compound = ("refuses that merge on its own and allows a command carrying it second, so it "
                    "reads an operand rather than the command")
        self.assertEqual(found, [f"red.py: {compound}", f"unpublished.py: {compound}"])

    def test_Given_AGuardRefusingByDecisionRatherThanExitCode_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE,
                            denier=DENIES_BY_DECISION)

        # Assert
        self.assertEqual([fault.split(":")[0] for fault in found], ["denier.py", "denier.py"])

    def test_Given_AGuardReadingWhatNoWorldArranges_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE,
                            elsewhere=READS_WHAT_NO_WORLD_ARRANGES)

        # Assert
        self.assertEqual([fault.splitlines()[-1].strip() for fault in found],
                         ["gh api repos/{owner}/{repo}/issues"])

    def test_Given_ADirectoryHoldingFewerGuardsThanTheFloor_When_TheCheckRuns_Then_ItIsReported(self):
        # Arrange / Act
        found = self.faults(floor=3, red=REFUSES_A_RED_BASE,
                            unpublished=REFUSES_AN_UNPUBLISHED_BASE)

        # Assert
        self.assertEqual([fault.split(" holds ")[-1] for fault in found], ["2 guards, fewer than 3"])


if __name__ == "__main__":
    unittest.main(verbosity=2)

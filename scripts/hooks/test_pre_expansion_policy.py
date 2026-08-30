#!/usr/bin/env python3
"""Every refusing guard is held to what it says it does with an operand the shell has not expanded.

A PreToolUse hook is handed the command as typed, so `$BRANCH`, a backtick and `$(…)` arrive as
themselves. A guard that resolves such an operand asks about the literal, the resolution fails, and
for most of them that is the pass -- the check does not happen, and a guard that did not run reports
exactly what one that ran and found nothing reports.

Which way to err is not uniform and cannot be decided once: a guard over merges must refuse, because
a merge is what cannot be taken back, while one whose verdict is its command's own text resolves
nothing and has nothing to miss. So each guard states its own answer and the command that
demonstrates it, and one added without either fails here rather than joining the set silently.

Here rather than in an EditMode fixture, which is where this used to live. `unity-tests` is skipped
wherever `UNITY_LICENSE` is unset -- a fork, or this repository configured without the secret -- and
the policy was then enforced by nothing, while every other check over this hook family runs on the
licence-free lane. Nothing about the reading needs an editor: it reads Python sources and runs the
guards as subprocesses.

Run: python3 scripts/hooks/test_pre_expansion_policy.py
"""

import json
import re
import subprocess
import sys
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
REFUSE = REPO_ROOT / ".claude" / "hooks" / "refuse"

POLICY = re.compile(r'^UNEXPANDED_POLICY = "(?P<policy>refuse|allow|n/a)"$', re.M)
PROBE = re.compile(r"^UNEXPANDED_PROBE = (?P<quote>['\"])(?P<probe>.*)(?P=quote)$", re.M)

# A floor under both cases: an empty directory states nothing and disagrees with nothing, and each
# assertion below carries the count so that reading cannot pass for a clean one.
FLOOR = 5


def guards():
    return sorted(REFUSE.glob("*.py"))


def stated(path):
    source = path.read_text(encoding="utf-8")
    policy = POLICY.search(source)
    probe = PROBE.search(source)
    return (policy.group("policy") if policy else None,
            probe.group("probe") if probe else None)


def answer(path, probe):
    """What the guard does with the probe: "refuse", "allow", or how it failed."""
    event = {"tool_name": "Bash", "cwd": str(REPO_ROOT), "tool_input": {"command": probe}}
    try:
        done = subprocess.run([sys.executable, "-B", str(path)], input=json.dumps(event),
                              capture_output=True, text=True, timeout=60)
    except subprocess.TimeoutExpired:
        return "timed out"
    return {0: "allow", 2: "refuse"}.get(done.returncode, "exit {}".format(done.returncode))


class PreExpansionPolicyTests(unittest.TestCase):
    # GREEN_ON_BASE(characterization): the guards are the same on both trees and so is what they
    # declare. What this change moves is which lane reads them, and a lane is not something a case
    # can be red about.
    def test_Given_EveryRefusingGuard_When_ItsSourceIsRead_Then_ItStatesAPreExpansionPolicy(self):
        # Arrange -- "n/a" is a guard that reads no shell operand at all, so it owes no probe.
        found = guards()
        silent = [path.name for path in found
                  for policy, probe in [stated(path)]
                  if policy is None or (policy != "n/a" and probe is None)]

        # Act / Assert -- the count rides along, since an empty directory states nothing either.
        self.assertEqual((len(found) > FLOOR, silent), (True, []))

    # GREEN_ON_BASE(characterization): as above -- the base answers its own probes the same way,
    # since neither the guards nor their probes are what moved.
    def test_Given_EveryStatedPolicy_When_ItsOwnProbeIsPosed_Then_TheGuardAnswersWhatItStates(self):
        # Arrange
        posed = [(path, policy, probe) for path in guards()
                 for policy, probe in [stated(path)]
                 if policy in ("refuse", "allow")]
        disagreements = ["{} states {}, answers {}".format(path.name, policy, observed)
                         for path, policy, probe in posed
                         for observed in [answer(path, probe)]
                         if observed != policy]

        # Act / Assert -- the count rides along, since a parse that matched nothing agrees with
        # everything, which is the same silence this is about.
        self.assertEqual((len(posed) > FLOOR, disagreements), (True, []))


if __name__ == "__main__":
    unittest.main(verbosity=2)

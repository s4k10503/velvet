#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/untracked_scratch.py.

What the report names is a decision the reader is asked to make, and a file that was in the tree
before the agent started is not one the agent can make — it was handed the same one again on every
stop. The cases below hold the narrowing that ends that: which of the payload's two transcripts the
bound comes from, that a leftover written during the run survives it, that a reading which fails
widens the report rather than silencing it, and that the gitignored-source half is not narrowed at
all.

Run: python3 scripts/hooks/test_untracked_scratch.py
"""

import datetime
import json
import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
HOOK = REPO_ROOT / ".claude/hooks/report/untracked_scratch.py"

AGENT_START = 1788616800.0
SESSION_START = AGENT_START - 3600
BEFORE_BOTH = SESSION_START - 3600
BETWEEN = AGENT_START - 1800
AFTER_AGENT = AGENT_START + 1800

STALE = "stale-scratch.txt"
PROBE = "Packages/com.velvet.core/Runtime/Component/Tests/Editor/ProbeFixture.cs"
EXCLUDED = "Runtime/Excluded.cs"
AWKWARD = 'a"b.txt'
AS_LISTED = '"a\\"b.txt"'


def stamp(moment):
    """A moment spelled the way a transcript record spells it: UTC, milliseconds, and a Z."""
    at = datetime.datetime.fromtimestamp(moment, datetime.timezone.utc)
    return at.strftime("%Y-%m-%dT%H:%M:%S.") + "{:03d}Z".format(at.microsecond // 1000)


class UntrackedScratchTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-untracked-scratch-"))
        self.project = self.root / "project"
        self.project.mkdir()
        subprocess.run(["git", "init", "-q", "-b", "main", str(self.project)], check=True,
                       timeout=60)
        (self.project / ".gitignore").write_text("Excluded.cs\n", encoding="utf-8")
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "add", ".gitignore"], check=True, timeout=60)
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "commit", "-q", "-m", "exclude the source file"],
                       check=True, timeout=60)
        self.session = self.transcript("session.jsonl", SESSION_START)
        self.agent = self.transcript("agent.jsonl", AGENT_START)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def transcript(self, name, moment):
        path = self.root / name
        path.write_text(json.dumps({"type": "user", "timestamp": stamp(moment)}) + "\n",
                        encoding="utf-8")
        return path

    def place(self, relative, moment):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("scratch\n", encoding="utf-8")
        os.utime(path, (moment, moment))
        return path

    def run_hook(self, agent=None, zone=None):
        record = {"hook_event_name": "SubagentStop", "cwd": str(self.project),
                  "transcript_path": str(self.session)}
        if agent is not None:
            record["agent_transcript_path"] = str(agent)
        environment = dict(os.environ)
        if zone is not None:
            environment["TZ"] = zone
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(record),
                              capture_output=True, text=True, cwd=str(self.project),
                              env=environment, timeout=120)

    def report(self, agent=None, zone=None):
        return self.run_hook(agent, zone).stdout

    def test_Given_AFileOlderThanTheSubagent_When_TheReportIsTaken_Then_OnlyWhatCameAfterIsNamed(self):
        # Arrange — the pair the narrowing has to separate: a file nobody in this run put there, and
        # the probe fixture this hook exists to catch. Asked as one comparison, since a report that
        # named neither would satisfy the half about the stale file on its own.
        self.place(STALE, BEFORE_BOTH)
        self.place(PROBE, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((STALE in printed, PROBE in printed), (False, True))

    def test_Given_EveryUntrackedFileOlderThanTheSubagent_When_TheReportIsTaken_Then_NothingIsPrinted(self):
        # Arrange — the exit status is compared beside the output because a hook that died on its
        # own imports also prints nothing.
        self.place(STALE, BEFORE_BOTH)

        # Act
        done = self.run_hook(self.agent)

        # Assert
        self.assertEqual((done.stdout, done.returncode), ("", 0))

    def test_Given_AFileWrittenAfterTheSessionButBeforeTheSubagent_When_TheReportIsTaken_Then_TheSubagentsOwnStartWithholdsIt(self):
        # Arrange — the pair above with the withheld file moved between the two bounds the payload
        # carries, which is where the two readings disagree: the case above is withheld under
        # either, and this one only under the subagent's own start.
        self.place(STALE, BETWEEN)
        self.place(PROBE, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((STALE in printed, PROBE in printed), (False, True))

    def test_Given_APartlyStaleTree_When_TheReportIsTaken_Then_TheHeadlineCountsOnlyWhatItNames(self):
        # Arrange — two withheld against one named, so a count taken before the narrowing reads 3.
        self.place(STALE, BEFORE_BOTH)
        self.place("also-stale.txt", BEFORE_BOTH)
        self.place(PROBE, AFTER_AGENT)

        # Act
        headline = json.loads(self.report(self.agent))["systemMessage"]

        # Assert
        self.assertIn("1 untracked", headline)

    # GREEN_ON_BASE(characterization): the two times this change compares, posed from a zone behind
    # UTC. A stamp read without its offset lands eight hours late there and withholds a file the run
    # wrote, and the same misreading is invisible on a runner already keeping UTC.
    def test_Given_AZoneBehindUTC_When_TheReportIsTaken_Then_AFileWrittenDuringTheRunIsNamed(self):
        # Arrange — POSIX form, whose sign is inverted, so no zone database has to be installed.
        self.place(PROBE, AFTER_AGENT)

        # Act
        printed = self.report(self.agent, zone="UTC+08")

        # Assert
        self.assertIn(PROBE, printed)

    # GREEN_ON_BASE(characterization): a file written at the bound itself, which the narrowing keeps
    # for the reason it keeps a path it cannot age.
    def test_Given_AFileWrittenAtTheSubagentsFirstRecord_When_TheReportIsTaken_Then_ItIsNamed(self):
        # Arrange
        self.place(STALE, AGENT_START)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): a payload this change reads a new key out of, posed without
    # that key. The narrowing is what has a side to fail to, and this says which side that is.
    def test_Given_NoAgentTranscriptInThePayload_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange — placed where the session's start would have withheld it, so nothing else in the
        # payload can stand in for the reading that is missing.
        self.place(STALE, BETWEEN)

        # Act
        printed = self.report()

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a path that does not open.
    def test_Given_AnAgentTranscriptThatIsNotThere_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BETWEEN)

        # Act
        printed = self.report(self.root / "no-such-transcript.jsonl")

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a transcript being
    # appended to as it is read.
    def test_Given_AnAgentTranscriptOpeningOnAPartialLine_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BETWEEN)
        partial = self.root / "partial.jsonl"
        partial.write_text('{"type": "user", "timesta', encoding="utf-8")

        # Act
        printed = self.report(partial)

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a first record that is
    # whole JSON and carries no key to ask for.
    def test_Given_AnAgentTranscriptOpeningOnANonObject_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BETWEEN)
        scalar = self.root / "scalar.jsonl"
        scalar.write_text("42\n", encoding="utf-8")

        # Act
        printed = self.report(scalar)

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): a path the narrowing cannot age. Dropping what it cannot
    # measure would be the narrowing deciding a question it never answered.
    def test_Given_AnUntrackedPathTheListingQuotes_When_TheReportIsTaken_Then_ItIsNamedThoughItsAgeCannotBeRead(self):
        # Arrange — placed old, so a reading that reached the file would withhold it. The listing
        # spells this name back C-quoted, and no file answers to that spelling.
        self.place(AWKWARD, BEFORE_BOTH)

        # Act
        context = json.loads(self.report(self.agent))["hookSpecificOutput"]["additionalContext"]

        # Assert
        self.assertIn(AS_LISTED, context)

    # GREEN_ON_BASE(characterization): the half this change leaves alone. An excluded source file
    # matters because it STAYS excluded, so an age bound would name it while it was new and never
    # again.
    def test_Given_AnExcludedSourceFileOlderThanTheSubagent_When_TheReportIsTaken_Then_ItIsNamed(self):
        # Arrange
        self.place(EXCLUDED, BEFORE_BOTH)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertIn(EXCLUDED, printed)


if __name__ == "__main__":
    unittest.main()

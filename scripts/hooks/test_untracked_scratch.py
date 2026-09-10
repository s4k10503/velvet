#!/usr/bin/env python3
"""Unit tests for .claude/hooks/report/untracked_scratch.py.

What the report names is a decision the reader is asked to make, and a file that was in the tree
before the agent started is not one the agent can make — it was handed the same one again on every
stop. The cases below hold the narrowing that ends that: which of the payload's two transcripts the
bound comes from and which record of it, that a leftover written during the run survives it, and
that a reading which fails widens the report rather than silencing it or answering with the other
transcript the payload carries. Three hold names a line-based listing would have spelled back
quoted, that spelling not being the path's own: an old one the bound reaches, a fresh one named as
the file is, and an excluded source whose spelling had been hiding it from the suffix match. Two
hold what the report says its count is of, in the arm where a bound was taken and in the arm where
none was. One holds the listing the bound is kept away from, the gitignored sources; one holds that
a source under the project's own trees is not a second such listing, so exempting a kind from the
bound goes red there.
Two more hold the scope the docstring claims — one tree, named in the headline — so widening the
scan without rewriting that claim goes red.

One holds the root the reading answers with, where the directory's own name ends in a space.

Eleven more hold what the report reads and what it writes. Two hold that an entry carrying a line
break of its own still occupies one line of the block, one for each half of the listing. One holds
that a name git leaves as itself is not divided into two, and one that a name holding a return is
aged by the file's. Four hold that a record's marker is read off a record and never off an origin
field, each posing an origin path that opens with the marker: one per pairing letter in the worktree
column, and two in the index column, the second of those holding a byte the encoding does not map
and written straight into the index, this filesystem not carrying it. Two hold a root whose own
name holds a line break, one per channel. One holds the side a path whose age will not read falls
on, asked of the reading directly rather than through a report.

Run: python3 scripts/hooks/test_untracked_scratch.py
"""

import datetime
import importlib.util
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


def load_hook():
    """Imports the hook by path, since .claude holds no packages."""
    spec = importlib.util.spec_from_file_location("untracked_scratch", HOOK)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


hook = load_hook()

AGENT_START = 1788616800.0
SESSION_START = AGENT_START - 3600
BEFORE_BOTH = SESSION_START - 3600
BETWEEN = AGENT_START - 1800
AFTER_AGENT = AGENT_START + 1800
AGENT_END = AGENT_START + 3600

STALE = "stale-scratch.txt"
WRITTEN = "written-during-the-run.txt"
PROBE = "Packages/com.velvet.core/Runtime/Component/Tests/Editor/ProbeFixture.cs"
EXCLUDED = "Runtime/Excluded.cs"
NESTED = "sub/deeper/nested-scratch.txt"
LINK = "link-to-elsewhere.txt"
SIBLING_PROBE = "SiblingProbeFixture.cs"

# Three names a line-based listing would have had to quote: one whose quoting would add no escape,
# one carrying both a quote and a byte over 0x80, and one for the ignored half.
SPACED = "with space.txt"
AWKWARD = 'a"b ß.txt'
EXCLUDED_SPACED = "Runtime/Excluded source.cs"

# A name whose own line break would divide it across the block, and what the block spells it as
# instead. `ORDINARY` sorts ahead of it, so the two entries are these two in this order.
ORDINARY = "ordinary.cs"
BROKEN = "probe\nfixture.cs"
BROKEN_AS_SHOWN = '"probe\\nfixture.cs"'

# A name holding a carriage return, for the half of the reading that is about bytes rather than
# about records.
RETURNING = "with-return\r.txt"

# Three of the eleven boundaries `splitlines()` documents, chosen because git leaves them as
# themselves once `core.quotePath` is off. All three in one name, so that a reading which handled
# two of them would still fail.
RAW_SEPARATORS = "mid\u0085a\u2028b\u2029c.txt"

# A gitignored source whose own name holds a line break, and what the block spells it as instead.
EXCLUDED_BROKEN = "Runtime/broken\nsource.cs"
EXCLUDED_BROKEN_AS_SHOWN = '"Runtime/broken\\nsource.cs"'
EXCLUDING = "Excluded.cs\nExcluded source.cs\nbroken*.cs\n"

# A checkout whose own directory name holds a line break, which both channels have to spell
# rather than write as it is.
BROKEN_ROOT = "pro\nject"

# A committed name opening with the untracked marker, and the substring that says the report took a
# rename's old path for an entry of its own.
MARKED = "?? phantom.cs"
PHANTOM = "phantom.cs"
RENAMED = "renamed.cs"

# The same name with a byte UTF-8 does not map added to it, and the spelling a printed report
# carries it as: the escape leaves a surrogate in the path and `json.dumps` writes that surrogate
# out as text, so a search for the path itself finds nothing in the report and pins nothing. The
# name is written into the index rather than onto disk — a file carrying the byte was the first
# arrangement tried and this filesystem refused to make one, with `Illegal byte sequence`.
RAW_BYTE_MARKED = b"?? lat\xedn1.cs"
RAW_BYTE_PHANTOM = "lat\\udcedn1.cs"

UNTRACKED_MARKER = "?? "

# A pairing whose origin path opens with that marker, so that a reading which does not step over
# the origin names it as an entry of its own. What the origin is called is what makes that visible:
# measured, for an origin opening with neither the marker nor a pairing letter, a reading that
# misses the worktree column answers with the same listing as this one — which is why both cases
# below compare the marker as a term of their own. Beside it an untracked file, so the report holds
# a listing rather than nothing, and content enough for git to pair on.
PAIRED_SOURCE = "?? original.cs"
PAIRED_RENAME = "moved.cs"
PAIRED_COPY = "copy.cs"
PAIRED_BODY = "".join("line {}\n".format(number) for number in range(40))
BESIDE_THE_PAIRING = "Runtime/scratch.txt"


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
        (self.project / ".gitignore").write_text("Excluded.cs\nExcluded source.cs\n",
                                                 encoding="utf-8")
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "add", ".gitignore"], check=True, timeout=60)
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "commit", "-q", "-m", "exclude the source file"],
                       check=True, timeout=60)
        self.session = self.transcript("session.jsonl", SESSION_START)
        self.agent = self.transcript("agent.jsonl", AGENT_START)

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def transcript(self, name, *moments):
        path = self.root / name
        path.write_text("".join(json.dumps({"type": "user", "timestamp": stamp(at)}) + "\n"
                                for at in moments), encoding="utf-8")
        return path

    def commit(self, relative, body):
        """A tracked file with `body` in it, so that git has something to pair a later path with."""
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body, encoding="utf-8")
        for command in (["add", relative], ["commit", "-q", "-m", "commit " + relative]):
            subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                            "-c", "user.name=t", *command], check=True, timeout=60)
        return path

    def listed(self):
        """The bytes git's own untracked listing holds.

        A name git never listed and a name the report withheld are the same absence, so a case
        posing a name the filesystem might mangle and asserting the report does not carry it
        compares this beside that absence. Without it, a `place` whose file the listing never
        reached passes having measured no withholding at all.
        """
        return subprocess.run(["git", "-C", str(self.project), "status", "--porcelain", "-z",
                               "--untracked-files=all"], capture_output=True, check=True,
                              timeout=60).stdout

    def index(self, records):
        """Index records written straight in, for a path this filesystem will not hold."""
        subprocess.run(["git", "-C", str(self.project), "update-index", "-z", "--index-info"],
                       input=records, check=True, timeout=60)

    def place(self, relative, moment):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("scratch\n", encoding="utf-8")
        os.utime(path, (moment, moment))
        return path

    def run_hook(self, agent=None, zone=None, cwd=None):
        """Run the hook with the payload the only thing naming the tree.

        The two ambient readings — the launch directory, and the CLAUDE_PROJECT_DIR the settings
        entry's own command spells — are aimed at a directory holding no repository, so a hook
        taking its tree from either finds none and reports nothing. Pointed there rather than
        unset, because with the variable absent the payload wins either way.
        """
        working = cwd or self.project
        record = {"hook_event_name": "SubagentStop", "cwd": str(working),
                  "transcript_path": str(self.session)}
        if agent is not None:
            record["agent_transcript_path"] = agent if isinstance(agent, bool) else str(agent)
        environment = dict(os.environ, CLAUDE_PROJECT_DIR=str(self.root))
        if zone is not None:
            environment["TZ"] = zone
        return subprocess.run([sys.executable, "-B", str(HOOK)], input=json.dumps(record),
                              capture_output=True, text=True, cwd=str(self.root),
                              env=environment, timeout=120)

    def report(self, agent=None, zone=None, cwd=None):
        return self.run_hook(agent, zone, cwd).stdout

    def block(self, printed):
        """Everything under the report's first paragraph, which is one line however long it reads."""
        context = json.loads(printed)["hookSpecificOutput"]["additionalContext"]
        return context.split("\n", 1)[1]

    def test_Given_AFileOlderThanTheSubagent_When_TheReportIsTaken_Then_OnlyWhatCameAfterIsNamed(self):
        # Arrange — the pair the narrowing has to separate: a file nobody in this run put there, and
        # one the run wrote. Asked as one comparison, since a report that named neither would
        # satisfy the half about the stale file on its own.
        self.place(STALE, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((STALE in printed, WRITTEN in printed), (False, True))

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
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((STALE in printed, WRITTEN in printed), (False, True))

    def test_Given_APartlyStaleTree_When_TheReportIsTaken_Then_TheHeadlineCountsOnlyWhatItNames(self):
        # Arrange — two withheld against one named, so a count taken before the narrowing reads 3.
        self.place(STALE, BEFORE_BOTH)
        self.place("also-stale.txt", BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        headline = json.loads(self.report(self.agent))["systemMessage"]

        # Assert
        self.assertIn("1 untracked", headline)

    def test_Given_ABoundWasTaken_When_TheReportIsRead_Then_BothChannelsSayTheCountIsBounded(self):
        # Arrange — asked of both channels at once, since a count of what the report names, read as
        # a count of what the tree holds, misleads whichever reader it reaches.
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        report = json.loads(self.report(self.agent))

        # Assert
        self.assertEqual(("1 untracked since this subagent started" in report["systemMessage"],
                          "the tree may hold more"
                          in report["hookSpecificOutput"]["additionalContext"]), (True, True))

    # GREEN_ON_BASE(characterization): the arm where no bound was taken.
    # Making the sentence above unconditional is what reddens this, and unconditional it would be
    # a claim the listing does not carry here.
    def test_Given_NoBoundWasTaken_When_TheReportIsRead_Then_NeitherChannelClaimsOne(self):
        # Arrange — no agent transcript in the payload, which is one of the readings that leaves
        # the listing unnarrowed.
        self.place(STALE, BEFORE_BOTH)

        # Act
        report = json.loads(self.report())

        # Assert
        self.assertEqual(("this subagent started" in report["systemMessage"],
                          "this subagent started"
                          in report["hookSpecificOutput"]["additionalContext"]), (False, False))

    # GREEN_ON_BASE(characterization): the two times this change compares, posed from a zone behind
    # UTC. A stamp read without its offset lands eight hours late there and withholds a file the run
    # wrote, and the same misreading is invisible on a runner already keeping UTC.
    def test_Given_AZoneBehindUTC_When_TheReportIsTaken_Then_AFileWrittenDuringTheRunIsNamed(self):
        # Arrange — POSIX form, whose sign is inverted, so no zone database has to be installed.
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent, zone="UTC+08")

        # Assert
        self.assertIn(WRITTEN, printed)

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
        # Arrange — placed before the session's start as well, so a hook answering a failed reading
        # with the other transcript the payload carries would withhold it rather than widen.
        self.place(STALE, BEFORE_BOTH)

        # Act
        printed = self.report()

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a path that does not open.
    def test_Given_AnAgentTranscriptThatIsNotThere_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BEFORE_BOTH)

        # Act
        printed = self.report(self.root / "no-such-transcript.jsonl")

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a transcript being
    # appended to as it is read.
    def test_Given_AnAgentTranscriptOpeningOnAPartialLine_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BEFORE_BOTH)
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
        self.place(STALE, BEFORE_BOTH)
        scalar = self.root / "scalar.jsonl"
        scalar.write_text("42\n", encoding="utf-8")

        # Act
        printed = self.report(scalar)

        # Assert
        self.assertIn(STALE, printed)

    # GREEN_ON_BASE(characterization): the same widening, reached through a value that is not a
    # path at all. The spelling is a bool rather than an object because the two fail differently
    # here, and this is the one a guard written against the object's failure would let through.
    def test_Given_AnAgentTranscriptPathThatIsNotAPath_When_TheReportIsTaken_Then_TheFileIsStillNamed(self):
        # Arrange
        self.place(STALE, BEFORE_BOTH)

        # Act
        printed = self.report(True)

        # Assert
        self.assertIn(STALE, printed)

    def test_Given_APathWhoseAgeWillNotRead_When_TheBoundIsAsked_Then_ItCountsAsWritten(self):
        # Arrange — a path the listing could name and `lstat` will not answer for. Asked of the
        # reading itself, since a tree posed here would have to lose the file between the two.
        missing = self.project / "no-such-file.txt"

        # Act
        kept = hook.written_since(missing, AGENT_START)

        # Assert
        self.assertIs(kept, True)

    # GREEN_ON_BASE(characterization): the bound reaching a name a line-based listing would spell
    # back quoted, which the base gets by decoding that spelling and this branch gets from a listing
    # that has no spelling of its own. Dropping `-z` from the two status reads is what reddens it.
    def test_Given_AnOldPathHoldingASpace_When_TheReportIsTaken_Then_TheBoundWithholdsIt(self):
        # Arrange — the shape four of this repository's own tracked paths carry. The written one is
        # beside it because a report that named neither would satisfy the half about the spaced path
        # on its own.
        self.place(SPACED, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)
        listed = self.listed()

        # Act
        context = json.loads(self.report(self.agent))["hookSpecificOutput"]["additionalContext"]

        # Assert
        self.assertEqual((SPACED in context, WRITTEN in context,
                          SPACED.encode("utf-8") in listed), (False, True, True))

    # GREEN_ON_BASE(characterization): a name holding a quote and a byte over 0x80 named as the file
    # is, which the base gets by decoding a quoted spelling and this branch gets from a listing that
    # has none. Dropping `-z` from the two status reads is what reddens it.
    def test_Given_AFreshPathHoldingAQuoteAndAHighByte_When_TheReportIsTaken_Then_ItIsNamedAsTheFileIs(self):
        # Arrange
        self.place(AWKWARD, AFTER_AGENT)

        # Act
        context = json.loads(self.report(self.agent))["hookSpecificOutput"]["additionalContext"]

        # Assert
        self.assertIn(AWKWARD, context)

    # GREEN_ON_BASE(characterization): one line per entry in the untracked half, which the base
    # already gives. Replacing `shown(untracked[:LISTED], len(untracked))` with a join over the
    # paths themselves is what reddens it.
    def test_Given_ANameHoldingALineBreak_When_TheListingIsRead_Then_EachEntryIsOneLine(self):
        # Arrange
        self.place(ORDINARY, AFTER_AGENT)
        self.place(BROKEN, AFTER_AGENT)

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual(listing.splitlines(), [ORDINARY, BROKEN_AS_SHOWN])

    # GREEN_ON_BASE(characterization): the same one line per entry in the gitignored half, which
    # the base already gives and no case reached, so that half was free to join its paths as they
    # are. Replacing `shown(ignored[:LISTED], len(ignored))` with such a join is what reddens it.
    def test_Given_AGitignoredSourceHoldingALineBreak_When_TheListingIsRead_Then_ItsEntryIsOneLine(self):
        # Arrange — the pattern is written here rather than into the shared setUp, since no other
        # case wants a name shaped like this excluded. Nothing untracked is placed beside it, so
        # the block holds the gitignored listing alone.
        (self.project / ".gitignore").write_text(EXCLUDING, encoding="utf-8")
        self.place(EXCLUDED_BROKEN, BEFORE_BOTH)

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual(listing.splitlines(), [EXCLUDED_BROKEN_AS_SHOWN])

    # GREEN_ON_BASE(characterization): the bound reaching a name holding a carriage return, which
    # the base gets by decoding git's quoted spelling and this branch has to get from git's own
    # bytes. Reading the listing through the text pipe `git` uses is what reddens it.
    def test_Given_ANameHoldingACarriageReturn_When_TheReportIsTaken_Then_TheBoundReachesTheFile(self):
        # Arrange — asked of the bound rather than of the block's spelling, so that how the block
        # spells such a name is left to the case that poses it. The written file keeps the report
        # from being empty, so what is compared is a listing rather than the absence of one.
        self.place(RETURNING, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)
        listed = self.listed()

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual((listing.splitlines(), RETURNING.encode("utf-8") in listed),
                         ([WRITTEN], True))

    def test_Given_ANameGitLeavesRaw_When_TheReportIsTaken_Then_TheBoundReachesTheFile(self):
        # Arrange — `core.quotePath` off makes no difference to this reading and decides a
        # line-based one, which is the arrangement this is written against. The written file keeps
        # the report from being empty, so what is compared is a listing rather than the absence of
        # one.
        subprocess.run(["git", "-C", str(self.project), "config", "core.quotePath", "false"],
                       check=True, timeout=60)
        self.place(RAW_SEPARATORS, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)
        listed = self.listed()

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual((listing.splitlines(), RAW_SEPARATORS.encode("utf-8") in listed),
                         ([WRITTEN], True))

    # GREEN_ON_BASE(characterization): no phantom, which the base gets from reading the porcelain
    # by lines and this branch has to get from stepping over a rename's origin field, that field
    # carrying no marker of its own. Stepping over no origin field at all is what reddens it.
    def test_Given_ARenameWhoseOldNameOpensWithTheMarker_When_TheReportIsTaken_Then_NoPhantomIsNamed(self):
        # Arrange — the rename is staged, so the old name reaches the listing only as the far half
        # of one record. That git paired the two rather than reporting a delete and an add is what
        # puts the name there at all, so it is compared beside the report.
        (self.project / MARKED).write_text("phantom\n", encoding="utf-8")
        for command in (["add", MARKED], ["commit", "-q", "-m", "commit the marked name"]):
            subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                            "-c", "user.name=t", *command], check=True, timeout=60)
        os.rename(self.project / MARKED, self.project / RENAMED)
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "add", "-A"], check=True, timeout=60)
        paired = subprocess.run(["git", "-C", str(self.project), "status", "--porcelain"],
                                capture_output=True, text=True, check=True, timeout=60).stdout
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((PHANTOM in printed, WRITTEN in printed, paired.startswith("R ")),
                         (False, True, True))

    # GREEN_ON_BASE(characterization): no phantom where the origin holds a byte the encoding does
    # not map, which the base gets from the same escape this branch reads the records through.
    # Decoding the listing as strict UTF-8 rather than through `DECODING` is what reddens it, and
    # no other case here poses a byte outside the encoding.
    def test_Given_ARenameWhoseOldNameHoldsAByteOutsideTheEncoding_When_TheReportIsTaken_Then_NoPhantomIsNamed(self):
        # Arrange — the sibling above renames a file to put its old name in a pairing; here the
        # index is written directly, since no file on this filesystem can carry the byte. The
        # written file keeps the report from being empty, so what is compared is a listing rather
        # than the absence of one, and that git paired the two is compared beside it.
        blob = subprocess.run(["git", "-C", str(self.project), "hash-object", "-w", "--stdin"],
                              input=b"tracked\n", capture_output=True, check=True,
                              timeout=60).stdout.strip()
        self.index(b"100644 " + blob + b"\t" + RAW_BYTE_MARKED + b"\x00")
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "commit", "-q", "-m", "commit the raw name"],
                       check=True, timeout=60)
        self.index(b"0 " + b"0" * 40 + b"\t" + RAW_BYTE_MARKED + b"\x00"
                   + b"100644 " + blob + b"\t" + RENAMED.encode("utf-8") + b"\x00")
        (self.project / RENAMED).write_bytes(b"tracked\n")
        paired = subprocess.run(["git", "-C", str(self.project), "status", "--porcelain"],
                                capture_output=True, text=True, check=True, timeout=60).stdout
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((RAW_BYTE_PHANTOM in printed, WRITTEN in printed,
                          paired.startswith("R ")), (False, True, True))

    # GREEN_ON_BASE(characterization): a worktree rename's origin is no entry, which the base gets
    # from reading the porcelain by lines — where an origin sits inside a line the status pair
    # starts — and this branch has to get from stepping over a field. Narrowing `record[1] in "RC"`
    # to `record[1] in "C"` is what reddens it, and nothing else here poses a worktree rename.
    def test_Given_AWorktreeRenameWhoseOldNameOpensWithTheMarker_When_TheListingIsRead_Then_ItHoldsTheUntrackedFileAlone(self):
        # Arrange — intent-to-add is what puts the new path where git can pair it with the old one
        # without the rename being staged. Two controls sit in the comparison: that git paired the
        # two in the worktree column, since an unpaired tree poses nothing, and that the source is
        # still spelled with the marker, since a source renamed past that poses nothing either.
        self.commit(PAIRED_SOURCE, PAIRED_BODY)
        os.rename(self.project / PAIRED_SOURCE, self.project / PAIRED_RENAME)
        subprocess.run(["git", "-C", str(self.project), "add", "-N", PAIRED_RENAME],
                       check=True, timeout=60)
        self.place(BESIDE_THE_PAIRING, AFTER_AGENT)
        paired = subprocess.run(["git", "-C", str(self.project), "status", "--porcelain"],
                                capture_output=True, text=True, check=True, timeout=60).stdout

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual((listing, " R " in paired,
                          PAIRED_SOURCE.startswith(UNTRACKED_MARKER)),
                         (BESIDE_THE_PAIRING, True, True))

    # GREEN_ON_BASE(characterization): the same origin behind the other letter, which the base gets
    # the same way. Narrowing `record[1] in "RC"` to `record[1] in "R"` is what reddens it, and
    # nothing else here poses a copy.
    def test_Given_AWorktreeCopyWhoseSourceNameOpensWithTheMarker_When_TheListingIsRead_Then_ItHoldsTheUntrackedFileAlone(self):
        # Arrange — copy detection is off unless `status.renames` asks for it, and it pairs a new
        # path with a source the same change modifies, so here the source stays in the tree and
        # reports a modification of its own beside the pairing. The two controls are the ones the
        # rename case states.
        subprocess.run(["git", "-C", str(self.project), "config", "status.renames", "copies"],
                       check=True, timeout=60)
        self.commit(PAIRED_SOURCE, PAIRED_BODY)
        (self.project / PAIRED_SOURCE).write_text(PAIRED_BODY + "one more\n", encoding="utf-8")
        (self.project / PAIRED_COPY).write_text(PAIRED_BODY, encoding="utf-8")
        subprocess.run(["git", "-C", str(self.project), "add", "-N", PAIRED_COPY],
                       check=True, timeout=60)
        self.place(BESIDE_THE_PAIRING, AFTER_AGENT)
        paired = subprocess.run(["git", "-C", str(self.project), "status", "--porcelain"],
                                capture_output=True, text=True, check=True, timeout=60).stdout

        # Act
        listing = self.block(self.report(self.agent))

        # Assert
        self.assertEqual((listing, " C " in paired,
                          PAIRED_SOURCE.startswith(UNTRACKED_MARKER)),
                         (BESIDE_THE_PAIRING, True, True))

    # GREEN_ON_BASE(characterization): the suffix match reaching a name a line-based listing would
    # spell back quoted, which the base gets by decoding that spelling and this branch gets from a
    # listing that has none. Dropping `-z` from the two status reads is what reddens it.
    def test_Given_AnExcludedSourceHoldingASpace_When_TheReportIsTaken_Then_ItIsNamed(self):
        # Arrange — the other half of the same listing, where a spelling that is not the file's own
        # costs a source file its suffix rather than its age.
        self.place(EXCLUDED_SPACED, BEFORE_BOTH)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertIn(EXCLUDED_SPACED, printed)

    def test_Given_AnUntrackedSourceUnderTheProjectsTrees_When_TheReportIsTaken_Then_TheBoundWithholdsItAsWell(self):
        # Arrange — the kind and the age at which a kind exemption from the bound would name a
        # file. The written one is beside it because a report that named neither would satisfy the
        # half about the source on its own.
        self.place(PROBE, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((PROBE in printed, WRITTEN in printed), (False, True))

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

    def test_Given_ACwdBelowTheRepositoryRoot_When_TheReportIsTaken_Then_TheBoundStillApplies(self):
        # Arrange — the listing spells both of these from the root whatever directory git ran in, so
        # a cwd one level down is where a bound joined to the cwd stops reaching any of them.
        self.place(NESTED, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent, cwd=self.project / "sub")

        # Assert
        self.assertEqual((NESTED in printed, WRITTEN in printed), (False, True))

    def test_Given_ASubagentTranscriptWhoseLastRecordIsLater_When_TheReportIsTaken_Then_TheFirstIsTheBound(self):
        # Arrange — a transcript whose two records straddle the named file, so only a bound taken
        # from the first keeps it. The withheld one is beside it because keeping neither would
        # satisfy that half alone.
        finished = self.transcript("finished.jsonl", AGENT_START, AGENT_END)
        self.place(STALE, BEFORE_BOTH)
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(finished)

        # Assert
        self.assertEqual((STALE in printed, WRITTEN in printed), (False, True))

    def test_Given_ASymlinkMadeDuringTheRunToAnOlderTarget_When_TheReportIsTaken_Then_TheLinkIsAgedNotItsTarget(self):
        # Arrange — the target sits outside the tree, so the listing names the link and not what it
        # points at. A stale file sits beside it because naming neither would satisfy that half
        # alone.
        target = self.root / "older-target.txt"
        target.write_text("target\n", encoding="utf-8")
        os.utime(target, (BEFORE_BOTH, BEFORE_BOTH))
        link = self.project / LINK
        link.symlink_to(target)
        os.utime(link, (AFTER_AGENT, AFTER_AGENT), follow_symlinks=False)
        self.place(STALE, BEFORE_BOTH)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((LINK in printed, STALE in printed), (True, False))

    # GREEN_ON_BASE(characterization): the scope the docstring claims. Both trees are this
    # repository and both leftovers are the stopping agent's own age, so only the reading decides
    # which is named — and widening it to every tree `git worktree list` gives reddens this.
    def test_Given_ALeftoverInAnotherWorktreeOfTheSameRepository_When_TheReportIsTaken_Then_OnlyTheSessionsCheckoutIsRead(self):
        # Arrange
        elsewhere = self.root / "worktree"
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@velvet",
                        "-c", "user.name=t", "worktree", "add", "-q", "-b", "sibling",
                        str(elsewhere)], check=True, timeout=60)
        (elsewhere / SIBLING_PROBE).write_text("sibling\n", encoding="utf-8")
        os.utime(elsewhere / SIBLING_PROBE, (AFTER_AGENT, AFTER_AGENT))
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        printed = self.report(self.agent)

        # Assert
        self.assertEqual((SIBLING_PROBE in printed, WRITTEN in printed), (False, True))

    def test_Given_AReportWithSomethingToName_When_TheHeadlineIsRead_Then_ItNamesTheTreeItRead(self):
        # Arrange — posed from below the root, and asked of both spellings at once because one
        # path can contain the other, which would let a headline printing the cwd it was handed
        # pass the half about the root.
        rooted = self.project.resolve()
        below = self.project / "sub"
        below.mkdir()
        self.place(WRITTEN, AFTER_AGENT)

        # Act
        headline = json.loads(self.report(self.agent, cwd=below))["systemMessage"]

        # Assert
        self.assertEqual((str(rooted) in headline, str(below) in headline), (True, False))


class WhitespaceRootTests(unittest.TestCase):
    """Its own class because the shared setUp above names the project directory, and the
    directory's name is what this poses."""

    def test_Given_ARootWhoseNameEndsInASpace_When_TheReportIsTaken_Then_TheLeftoverIsNamed(self):
        # Arrange — trimming the terminator's whitespace takes the space with it, and the
        # directory named by what is left is not there. Both status reads taken from it answer
        # nothing, so the guard prints nothing — which is what it prints for a clean tree too.
        root = Path(tempfile.mkdtemp(prefix="velvet-untracked-scratch-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        project = root / "project "
        project.mkdir()
        subprocess.run(["git", "init", "-q", "-b", "main", str(project)], check=True, timeout=60)
        (project / STALE).write_text("scratch\n", encoding="utf-8")

        # Act
        done = subprocess.run(
            [sys.executable, "-B", str(HOOK)],
            input=json.dumps({"hook_event_name": "SubagentStop", "cwd": str(project)}),
            capture_output=True, text=True, cwd=str(root),
            env=dict(os.environ, CLAUDE_PROJECT_DIR=str(root)), timeout=120)

        # Assert
        self.assertIn(STALE, done.stdout)


class LineBrokenRootTests(unittest.TestCase):
    """Its own class for the reason `WhitespaceRootTests` gives, posing a root whose name divides
    the line it is written into rather than one whose name ends in whitespace."""

    def rooted(self):
        """A checkout whose own directory name holds a line break, with one leftover in it."""
        root = Path(tempfile.mkdtemp(prefix="velvet-untracked-scratch-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        project = root / BROKEN_ROOT
        project.mkdir()
        subprocess.run(["git", "init", "-q", "-b", "main", str(project)], check=True, timeout=60)
        (project / STALE).write_text("scratch\n", encoding="utf-8")
        return project

    def report(self, project):
        done = subprocess.run(
            [sys.executable, "-B", str(HOOK)],
            input=json.dumps({"hook_event_name": "SubagentStop", "cwd": str(project)}),
            capture_output=True, text=True, cwd=str(project.parent),
            env=dict(os.environ, CLAUDE_PROJECT_DIR=str(project.parent)), timeout=120)
        return json.loads(done.stdout)

    def test_Given_ARootHoldingALineBreak_When_TheHeadlineIsRead_Then_ItIsOneLineNamingTheTree(self):
        # Arrange — asked of the line count and of the name at once, since a headline that stopped
        # naming the tree at all would occupy one line as well.
        project = self.rooted()

        # Act
        headline = self.report(project)["systemMessage"]

        # Assert
        self.assertEqual((headline.splitlines(), json.dumps(str(project.resolve())) in headline),
                         ([headline], True))

    def test_Given_ARootHoldingALineBreak_When_TheBlockIsRead_Then_ItsOpeningSentenceIsOneLine(self):
        # Arrange — asked of that sentence and of what hangs under it at once, since the entries a
        # reader takes off the block are whatever the first line does not cover.
        project = self.rooted()

        # Act
        context = self.report(project)["hookSpecificOutput"]["additionalContext"]

        # Assert
        self.assertEqual((json.dumps(str(project.resolve())) in context.split("\n", 1)[0],
                          context.split("\n", 1)[1].splitlines()), (True, [STALE]))


if __name__ == "__main__":
    unittest.main()

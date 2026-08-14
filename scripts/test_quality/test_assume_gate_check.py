#!/usr/bin/env python3
"""Unit tests for assume_gate_check.py's reading, plus a guard over this repository's own record.

A case this reader does not reach and a gate it does not recognise come back the same way -- as a
repository with nothing to report -- so neither announces itself. A section marker it misreads is
worse than silent: it announces a case as missing the marker the case carries, and hides every gate
the section it failed to locate would have found. Each reading is therefore measured against a case
written in the spelling this repository uses, and the record is held to the source it was taken from.

Run: python3 scripts/test_quality/test_assume_gate_check.py
"""

import importlib.util
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module():
    """Imports assume_gate_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "assume_gate_check", Path(__file__).resolve().with_name("assume_gate_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


assume_gate_check = load_module()

RELATIVE = "Packages/com.velvet.core/Runtime/P/Tests/Editor/ProbeTests.cs"

FIXTURE = """using NUnit.Framework;

namespace Velvet.Tests
{{
    internal sealed class ProbeTests
    {{
        [Test]
        public void Given_A_When_B_Then_C()
        {{
{body}
        }}
    }}
}}
"""


def readings(body):
    """Every (reading, detail) one case body is read as, through the whole file reader."""
    text = FIXTURE.format(body=body)
    code = assume_gate_check.code_lines(text)
    raw = text.splitlines()
    case = assume_gate_check.csharp_cases(text, RELATIVE)[0]
    span = slice(case.first_line - 1, case.last_line)
    return assume_gate_check.readings_of(code[span], raw[span])


class ActValueTests(unittest.TestCase):
    """A gate over a value the Act introduced, in the spellings this repository writes it in."""

    def test_Given_AGateOverAnOutVarTheActDeclared_When_TheCaseIsRead_Then_ItGatesTheBehaviour(self):
        # Arrange -- the parse-then-assume spelling, with the gate inside the Act section.
        body = ("            // Act\n"
                "            var ok = Resolver.TryResolve(\"m-[10px]\", out var spec);\n"
                "            Assume.That(ok, Is.True);\n\n"
                "            // Assert\n"
                "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_ACT_VALUE, "ok")])

    def test_Given_TheSameGateWrittenBelowTheAssertMarker_When_TheCaseIsRead_Then_ItStillGatesIt(self):
        # Arrange -- the other place this repository writes the same gate. The value is what makes it
        # one, so moving the line must not change the reading.
        body = ("            // Act\n"
                "            var ok = Resolver.TryResolve(\"m-[10px]\", out var spec);\n\n"
                "            // Assert\n"
                "            Assume.That(ok, Is.True);\n"
                "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_ACT_VALUE, "ok")])

    def test_Given_AGateOverAnAssertDeclaredDeconstruction_When_TheCaseIsRead_Then_ItGatesIt(self):
        # Arrange -- `var (a, b) = ...` names its locals inside brackets, which the single-name
        # spelling does not reach.
        body = ("            // Act\n"
                "            var (found, spec) = Resolver.Resolve(\"m-[10px]\");\n"
                "            Assume.That(found, Is.True);\n\n"
                "            // Assert\n"
                "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_ACT_VALUE, "found")])

    def test_Given_AGateOverStateTheArrangeBuilt_When_TheCaseIsRead_Then_ItIsNotOne(self):
        # Arrange -- a precondition over what the Arrange set up is the legitimate use, and reading it
        # as a gate would put half the suite in the record and teach nobody anything.
        body = ("            // Arrange\n"
                "            var element = Mount();\n"
                "            Assume.That(element.childCount, Is.EqualTo(1));\n\n"
                "            // Act\n"
                "            var removed = Patch(element);\n\n"
                "            // Assert\n"
                "            Assert.That(removed, Is.True);")

        # Act / Assert
        self.assertEqual(readings(body), [])

    def test_Given_AGateOverATypedLocalTheActDeclared_When_TheCaseIsRead_Then_ItGatesTheBehaviour(self):
        # Arrange -- the spelling a case reaches for when the local is assigned inside a lambda, so
        # `var` will not do. It is the same gate over the same behaviour, and the repository has one.
        body = ("            // Act\n"
                "            VisualElement spacer = null;\n"
                "            Root.Query<VisualElement>().ForEach(e => spacer = e);\n\n"
                "            // Assert\n"
                "            Assume.That(spacer, Is.Not.Null);\n"
                "            Assert.That(spacer.resolvedStyle.width, Is.EqualTo(4f));")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_ACT_VALUE, "spacer")])

    def test_Given_AnAssignmentToSomethingAlreadyDeclared_When_TheCaseIsRead_Then_ItDeclaresNothing(self):
        # Arrange -- `element.style.width = 4` is two identifiers and an `=` like a declaration is,
        # and reading it as one would put every case touching an Act-side property under a gate.
        body = ("            // Arrange\n"
                "            var element = Mount();\n"
                "            Assume.That(width, Is.EqualTo(4f));\n\n"
                "            // Act\n"
                "            element.style.width = 4;\n\n"
                "            // Assert\n"
                "            Assert.That(element.resolvedStyle.width, Is.EqualTo(4f));")

        # Act / Assert
        self.assertEqual(readings(body), [])

    def test_Given_AGateOverAMemberNamedLikeAnActLocal_When_TheCaseIsRead_Then_ItIsNotOne(self):
        # Arrange -- the subject reads `element` and reaches a member that happens to be spelled
        # like the Act's local. Reading every identifier in it instead puts the case under a gate it
        # does not have, which it did for two cases in this repository.
        body = ("            // Arrange\n"
                "            var element = Mount();\n"
                "            Assume.That(element.resolvedStyle.opacity, Is.GreaterThan(0f));\n\n"
                "            // Act\n"
                "            var opacity = Drive(element);\n\n"
                "            // Assert\n"
                "            Assert.That(opacity, Is.EqualTo(1f));")

        # Act / Assert
        self.assertEqual(readings(body), [])

    def test_Given_AGateOverACallRatherThanAnActLocal_When_TheCaseIsRead_Then_ItIsNotOne(self):
        # Arrange -- the environment gate this repository writes as the Act itself, e.g. whether a
        # panel granted focus. It names no local, so nothing here claims to judge it.
        body = ("            // Arrange\n"
                "            var leaf = MountChain();\n\n"
                "            // Act\n"
                "            Assume.That(DriveFocus(leaf), Is.True);\n\n"
                "            // Assert\n"
                "            Assert.IsTrue(leaf.ClassListContains(\"bg-outer\"));")

        # Act / Assert
        self.assertEqual(readings(body), [])


class ChainedMarkerTests(unittest.TestCase):
    """A comment line naming two or three sections, which this repository writes 471 times.

    Reading only the first name off one leaves the sections it also names unlocated, and an unlocated
    section is not a refusal -- the act-value reading simply finds nothing, and the case is recorded
    as missing a marker it carries.
    """

    def test_Given_EachSeparatorThePackageWrites_When_TheCaseIsRead_Then_TheActIsLocatedThrough(self):
        # Arrange -- the same case three times over `/`, `+` and `&`, which are the separators the
        # package uses. One spelling per case would leave the other two deletable from the pattern.
        bodies = ["            // Arrange {} Act — the resolve is the behaviour and its setup at once.\n"
                  "            var ok = Resolver.TryResolve(\"m-[10px]\", out var spec);\n"
                  "            Assume.That(ok, Is.True);\n\n"
                  "            // Assert\n"
                  "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));".format(separator)
                  for separator in ("/", "+", "&")]

        # Act
        found = [readings(body) for body in bodies]

        # Assert
        self.assertEqual(found, [[(assume_gate_check.GATES_ACT_VALUE, "ok")]] * 3)

    def test_Given_AllThreeSectionsOnOneLine_When_TheCaseIsRead_Then_TheAssertMarkerIsLocatedToo(self):
        # Arrange -- the last name of a chain is the one a pattern reading a single separator drops,
        # and dropping the Assert one turns a gate below it into no reading at all.
        body = ("            // Arrange / Act / Assert — the call is the setup, the behaviour and the reading.\n"
                "            var resolved = Resolver.Resolve(\"m-[10px]\");\n"
                "            Assume.That(resolved, Is.Not.Null);\n"
                "            Assert.That(resolved.Property, Is.EqualTo(\"margin\"));")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_IN_ASSERT, "resolved")])


class AssertSectionTests(unittest.TestCase):
    def test_Given_AGateInTheAssertSection_When_TheCaseIsRead_Then_ItGatesTheBehaviour(self):
        # Arrange -- everything below the marker is a reading of the behaviour, which is what the
        # section means, so a gate there gates it whatever the gate is over.
        body = ("            // Arrange\n"
                "            var element = Mount();\n\n"
                "            // Act\n"
                "            Patch(element);\n\n"
                "            // Assert\n"
                "            Assume.That(element.panel, Is.Not.Null);\n"
                "            Assert.That(element.childCount, Is.Zero);")

        # Act / Assert
        self.assertEqual(readings(body), [(assume_gate_check.GATES_IN_ASSERT, "element.panel")])

    def test_Given_TheSameGateAboveTheAssertMarker_When_TheCaseIsRead_Then_ItIsNotOne(self):
        # Arrange
        body = ("            // Arrange\n"
                "            var element = Mount();\n"
                "            Assume.That(element.panel, Is.Not.Null);\n\n"
                "            // Act\n"
                "            Patch(element);\n\n"
                "            // Assert\n"
                "            Assert.That(element.childCount, Is.Zero);")

        # Act / Assert
        self.assertEqual(readings(body), [])


class UnreadableTests(unittest.TestCase):
    def test_Given_ACaseWithNoSectionMarkers_When_ItIsRead_Then_ItSaysBothReadingsAreUnavailable(self):
        # Arrange -- neither reading is available without the sections, and a case that quietly
        # passed because nothing could look at it is what this exists to refuse. One entry per
        # reading rather than per case, so restoring one marker takes one entry off the record.
        body = ("            Mount();\n"
                "            Assume.That(HasBinding, Is.True);\n"
                "            Step(1);\n"
                "            Assert.That(HasBinding, Is.False);")

        # Act
        found = readings(body)

        # Assert
        self.assertEqual(found, [(assume_gate_check.UNREADABLE,
                                  "no // Act marker, so a gate over what the Act made is not read"),
                                 (assume_gate_check.UNREADABLE,
                                  "no // Assert marker, so a gate below it is not read")])

    def test_Given_ACaseWithOnlyTheActMarker_When_ItIsRead_Then_TheMissingReadingIsAnnounced(self):
        # Arrange -- half the question is still answerable, and taking that half while saying nothing
        # about the other is what reported these cases as clean.
        body = ("            // Act\n"
                "            var ok = Resolve();\n"
                "            Assume.That(ok, Is.True);\n"
                "            Assert.That(ok, Is.True);")

        # Act
        found = readings(body)

        # Assert
        self.assertEqual(found, [(assume_gate_check.UNREADABLE,
                                  "no // Assert marker, so a gate below it is not read"),
                                 (assume_gate_check.GATES_ACT_VALUE, "ok")])

    def test_Given_ACaseWithNoAssumeAtAll_When_ItIsRead_Then_NothingIsReported(self):
        # Arrange -- the sections are missing here too, so the absence of the Assume has to be what
        # keeps it out rather than the presence of the markers.
        body = ("            Mount();\n"
                "            Assert.That(HasBinding, Is.False);")

        # Act / Assert
        self.assertEqual(readings(body), [])

    def test_Given_AGateInACommentedOutLine_When_TheCaseIsRead_Then_ItIsNotOne(self):
        # Arrange -- the markers this reads are comments and the gates are not, so the two halves are
        # read off different views of the same line.
        body = ("            // Act\n"
                "            var ok = Resolve();\n\n"
                "            // Assert\n"
                "            // Assume.That(ok, Is.True);\n"
                "            Assert.That(ok, Is.True);")

        # Act / Assert
        self.assertEqual(readings(body), [])


class EntryTests(unittest.TestCase):
    """What one case contributes to the record, over the whole walk rather than one body."""

    def entries_for(self, body):
        holder = tempfile.mkdtemp(prefix="assume-gate-entries-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        path = Path(holder) / RELATIVE
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(FIXTURE.format(body=body))
        entries, _ = assume_gate_check.scan(Path(holder))
        return entries

    def test_Given_TwoGatesDifferingOnlyInsideAStringLiteral_When_ItIsScanned_Then_EachIsItsOwnEntry(self):
        # Arrange -- the reading is taken off the masked body, where both of these are the same text.
        # Writing that into the record collapses them, which is the net-zero the key change was made
        # to prevent: fix one of the two and add another, and the record does not move.
        body = ("            // Arrange\n"
                "            var element = Mount();\n\n"
                "            // Act\n"
                "            Patch(element);\n\n"
                "            // Assert\n"
                "            Assume.That(element.ClassListContains(\"absolute\"), Is.True);\n"
                "            Assume.That(element.ClassListContains(\"child\"), Is.True);\n"
                "            Assert.That(element.childCount, Is.Zero);")

        # Act
        entries = self.entries_for(body)

        # Assert
        self.assertEqual(sorted(entry.split("\t")[-1] for entry in entries),
                         ['element.ClassListContains("absolute")',
                          'element.ClassListContains("child")'])

    def test_Given_TwoGatesOfOneKindInOneCase_When_ItIsScanned_Then_EachIsItsOwnEntry(self):
        # Arrange -- keyed on the case alone these collapse, and what that hides is one of them
        # being fixed while the other arrives: the record does not move and the check exits zero.
        body = ("            // Act\n"
                "            var ok = Resolver.TryResolve(\"m-[10px]\", out var spec);\n"
                "            Assume.That(ok, Is.True);\n"
                "            Assume.That(spec.Value, Is.Not.Null);\n\n"
                "            // Assert\n"
                "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));")

        # Act
        entries = self.entries_for(body)

        # Assert
        self.assertEqual(sorted(entry.split("\t")[-1] for entry in entries), ["ok", "spec.Value"])


class RefusalTests(unittest.TestCase):
    """Both directions the record can disagree with the tree, taken through the exit status.

    `RecordTests` runs the script whole as well, over a record that agrees and over records nothing
    in the tree answers to, so the direction this exists for -- an entry that arrived -- is reached
    by nothing else. A guard that stopped refusing one keeps every reading in this module correct and
    every other case green.
    """

    GATED = ("            // Act\n"
             "            var ok = Resolver.TryResolve(\"m-[10px]\", out var spec);\n"
             "            Assume.That(ok, Is.True);\n"
             "            Assume.That(spec.Value, Is.Not.Null);\n\n"
             "            // Assert\n"
             "            Assert.That(spec.Property, Is.EqualTo(\"margin\"));")

    def exit_status(self, record):
        holder = tempfile.mkdtemp(prefix="assume-gate-refusal-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        source = root / RELATIVE
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_text(FIXTURE.format(body=self.GATED))
        kept = root / assume_gate_check.DEFAULT_BASELINE
        kept.parent.mkdir(parents=True, exist_ok=True)
        entries, _ = assume_gate_check.scan(root)
        kept.write_text("".join(entry + "\n" for entry in sorted(record(entries))))
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/assume_gate_check.py"),
             "--project", str(root)], capture_output=True, text=True).returncode

    def test_Given_ARecordMissingOneGate_When_TheCheckRuns_Then_ItRefusesForThatOneAlone(self):
        # Arrange -- the record holds every other entry of the same tree, so nothing was removed and
        # the refusal can only be the arrived gate. The complete record rides alongside because a
        # check that refused everything would satisfy the first half on its own.
        arrived = self.exit_status(
            lambda entries: {entry for entry in entries if not entry.endswith("\tok")})

        # Act
        complete = self.exit_status(lambda entries: entries)

        # Assert
        self.assertEqual((arrived, complete), (1, 0))


class RecordTests(unittest.TestCase):
    """The record this repository carries, against the source it was taken from."""

    def test_Given_TheRecordedSet_When_TheRepositoryIsScannedAgain_Then_TheyAgree(self):
        # Arrange -- an entry the scan no longer finds is one whose case was fixed or renamed, and an
        # entry it finds and the record has not is a new gate. Both fail, and a scan that read
        # nothing loses every entry rather than passing.
        result = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/assume_gate_check.py"),
             "--project", str(REPO_ROOT)], capture_output=True, text=True)

        # Act / Assert
        self.assertEqual((result.returncode, result.stderr), (0, ""))

    @staticmethod
    def tracked_test_files():
        listed = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files"],
                                capture_output=True, text=True, check=True).stdout.splitlines()
        return [path for path in listed if assume_gate_check.kind_of(path) == "csharp"]

    def test_Given_EveryTrackedCSharpTestFile_When_TheScanRootIsApplied_Then_NoneSitsOutsideIt(self):
        # Arrange -- the scan walks one directory, so a test assembly added beside it would be read by
        # nothing and report nothing, which is the same answer as a clean one.
        # Act
        outside = [path for path in self.tracked_test_files()
                   if not path.startswith(assume_gate_check.PACKAGE_REL + "/")]

        # Assert
        self.assertEqual(outside, [])

    def test_Given_TheCasesGitTracks_When_TheScanWalksTheTree_Then_ItReadsEveryOneOfThem(self):
        # Arrange -- against git's list rather than a floor, because a floor a growing repository
        # clears is one a directory the walk stopped reaching clears too. Areas here contribute no
        # entry at all, so a walk that stopped reaching one would change no verdict and no count but
        # this.
        declared = sum(len(assume_gate_check.csharp_cases(
            (REPO_ROOT / path).read_text(encoding="utf-8", errors="replace"), path))
            for path in self.tracked_test_files())

        # Act
        _, read = assume_gate_check.scan(REPO_ROOT)

        # Assert
        self.assertEqual(read, declared)

    def test_Given_AnEmptyRecordAndNoCaseToRead_When_TheCheckRuns_Then_ItIsStillNotSatisfied(self):
        # Arrange -- `--write-baseline` over a tree the walk reads nothing in leaves an empty file,
        # and an empty record agrees with an empty scan. That is the same vacuous pass as the case
        # below, reached through the record rather than through the tree, and the two are separate
        # readings: the record there is this repository's own and carries every entry.
        holder = tempfile.mkdtemp(prefix="assume-gate-blank-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        blank = Path(holder)
        (blank / assume_gate_check.DEFAULT_BASELINE).parent.mkdir(parents=True)
        (blank / assume_gate_check.DEFAULT_BASELINE).write_text("")

        # Act
        result = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/assume_gate_check.py"),
             "--project", str(blank)], capture_output=True, text=True)

        # Assert
        self.assertNotEqual(result.returncode, 0)

    def test_Given_ARepositoryHoldingNoTestAtAll_When_ItIsScanned_Then_TheRecordIsNotSatisfied(self):
        # Arrange -- the vacuous pass this shape is prone to: nothing read, nothing to report, green.
        holder = tempfile.mkdtemp(prefix="assume-gate-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        empty = Path(holder)
        (empty / "scripts/test_quality").mkdir(parents=True)
        (empty / "scripts/test_quality/assume_gate_baseline.txt").write_text(
            (REPO_ROOT / "scripts/test_quality/assume_gate_baseline.txt").read_text())

        # Act
        result = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/assume_gate_check.py"),
             "--project", str(empty)], capture_output=True, text=True)

        # Assert
        self.assertEqual(result.returncode, 1)


if __name__ == "__main__":
    unittest.main(verbosity=2)

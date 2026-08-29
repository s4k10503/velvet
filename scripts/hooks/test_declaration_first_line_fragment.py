#!/usr/bin/env python3
"""Unit tests for declaration_first_line_fragment.py.

The refusing cases are the shapes this tree's own declarations actually break off in, and the
allowing ones are what a guard over prose is at risk of taking with them: a preposition stranded at
a clause end, a clause closed by a semicolon, a possessive apostrophe, a marker that is fixture text
rather than a declaration.

`TreeTests` is the false-positive floor. A guard nobody can write a declaration past gets turned
off, so the declarations already here are posed to it and the ones that read as a claim have to
come back allowed.

Run: python3 scripts/hooks/test_declaration_first_line_fragment.py
"""

import bisect
import importlib.util
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
GUARD = REPO_ROOT / ".claude/hooks/refuse/declaration_first_line_fragment.py"

# The two scripts whose reading of a declaration this guard is written against.
READER_NAMES = ("base_red_check", "mutation_check")

REFUSE = 2
ALLOW = 0

# Assembled rather than spelled: base_red_check.py counts a marker once per line it occurs on,
# reading a fixture string here as a declaration this file wrote over no case at all.
BASE = "GREEN_ON" + "_BASE"
SURVIVES = "MUTANT" + "_SURVIVES"
GREEN = BASE + "(characterization)"
SETTLED = f"    // {GREEN}: the base already separates these two.\n"
CONTINUATION = "    // The rest of the sentence sits under it.\n"
FIXTURE = "namespace Velvet.Tests\n{\n" + SETTLED + "    [Test] void X() { }\n}\n"


def load(name, path):
    """Imports a script by path, since neither .claude/hooks nor scripts/test_quality is a package."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


guard = load("declaration_first_line_fragment", GUARD)
READERS = {name: load(name, REPO_ROOT / "scripts/test_quality" / f"{name}.py")
           for name in READER_NAMES}
mutation_check = READERS["mutation_check"]


class GuardCase(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-declaration-"))
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.file = self.root / "SomeTests.cs"
        self.file.write_text(FIXTURE, encoding="utf-8")

    def pose(self, payload, tool="Edit"):
        event = json.dumps({"tool_name": tool, "cwd": str(self.root), "tool_input": payload})
        finished = subprocess.run([sys.executable, "-B", str(GUARD)], input=event, text=True,
                                  capture_output=True, timeout=120)
        return finished.returncode, finished.stderr

    def writes(self, reason, path=None, marker=None):
        """The verdict on an edit replacing the settled declaration with `reason`."""
        return self.pose({
            "file_path": str(path or self.file),
            "old_string": SETTLED,
            "new_string": f"    // {marker or GREEN}: {reason}\n" + CONTINUATION,
        })[0]


class FragmentTests(GuardCase):
    def test_Given_AFirstLineEndingOnAnArticle_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("this column happened to be right on both sides. Deriving the")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineEndingOnACoordinator_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("the same fresh mount the base already gives this direction, and")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineEndingOnACommaAndARelativiser_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act — the comma is what makes this one: a relative clause is announced and then
        # not written. Verbatim from the tree.
        code = self.writes("the divider a departing child already loses on the base, which")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineEndingOnAComma_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("the base anchors a drained portal child on the reconcile root,")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineLeavingACodeSpanOpen_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act — the wrap fell inside the span, so the line is cut mid-token.
        code = self.writes("the reading `folded_reason takes is what the branch keeps")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineLeavingAParenthesisOpen_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act — the bracket is the only fault: the line ends on a noun, and on nothing
        # the punctuation reading or the word list would take.
        code = self.writes("the base already separates these two (by portal scope")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AMutantSurvivesDeclaration_When_ItsFirstLineBreaksOff_Then_ItIsRefused(self):
        # Arrange / Act — the two markers are read the same way, so neither is guarded alone.
        code = self.writes("both spellings clamp to the", marker=SURVIVES + "(equivalent)")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_ARefusedDeclaration_When_TheRefusalIsRead_Then_ItQuotesTheFirstLine(self):
        # Arrange
        reason = "the same fresh mount the base already gives this direction, and"

        # Act
        _, text = self.pose({
            "file_path": str(self.file), "old_string": SETTLED,
            "new_string": f"    // {GREEN}: {reason}\n" + CONTINUATION,
        })

        # Assert
        self.assertIn(reason, text)


class StandingClaimTests(GuardCase):
    def test_Given_AFirstLineEndingOnAFullStop_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.writes("the base already separates these two by portal scope.")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnAStrandedPreposition_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — this repository writes "the case the canary exists for", and a clause
        # ends there.
        code = self.writes("the base already answers what the canary exists for")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnASemicolon_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — a semicolon closes the clause and joins it to the next.
        code = self.writes("no switch in these regions carries a catch-all on either side;")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnAnEmDash_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — verbatim from SuspenseBoundaryTests, and a whole claim: the dash follows
        # a complete clause the way a semicolon does, and refusing it refused a good declaration.
        code = self.writes("the base reaches this outcome by never deferring at all —")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnABareRelativiser_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — with no comma announcing a relative clause, a clause ends here.
        code = self.writes("both orderings reach the same bound and it does not matter which")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnASubordinator_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — `because`, `if`, `while`, `since`, `when` and `than` are one class with
        # `although` and `unless`, and taking half of it made the table unpredictable.
        code = self.writes("the base already separates these two, and it reads the same because")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnADemonstrative_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.writes("the keyed-reorder order this refactor must not change that")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineEndingOnSo_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — the reader's own branch writes a fixture ending here, and a clause does
        # end on it.
        code = self.writes("every caller clamps the operand and this reads the same, so")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineCarryingAPossessiveApostrophe_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — an apostrophe is not a delimiter to balance in this repository's prose.
        code = self.writes("the mark an enclosing suspend wrote on the boundary's own children")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineCarryingABalancedCodeSpan_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act
        code = self.writes("the reading `folded_reason` takes is what the branch keeps")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ACodeSpanHoldingAnUnclosedBracket_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — the bracket is the code's, not the sentence's. This repository writes
        # `switch (` and `EndsWith(")", …)` in comment prose already, and counted whole the span
        # made a first line that stands read as one that wraps.
        code = self.writes("the reading `fragment(` takes is what the branch keeps")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AFirstLineCarryingBareListLabels_When_TheEditIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — a closer with no opener cannot be a wrap of the first line, since there
        # is no line above it for the opener to sit on.
        code = self.writes("the base answers a) and b) the same way as the branch")

        # Assert
        self.assertEqual(code, ALLOW)


class TableTests(GuardCase):
    """Every member of the decision table posed a case, and the near misses posed one too.

    Deleting a member is what a refuse hook cannot afford to take in silence, and until these ran
    members could be removed with the suite still green. The probes are spelled here rather than
    read from the guard: a list taken from the table itself passes whatever the table holds, so it
    can never notice a member leaving it.

    Spelling them leaves the other direction open — a member the table *gains* is asked about by
    nothing, and a word wrongly in `DANGLING` refuses good declarations, which is the direction that
    gets a guard turned off. `Then_NoneIsUnposed` below closes it by comparing the two.
    """

    # A frame that ends on whatever it is given and trips nothing else on the way.
    FRAME = "the base already separates these two on the reading given {}"

    DANGLING = ("a", "an", "the", "and", "or", "nor",
                "its", "their", "our", "your", "my", "every")
    RELATIVISERS = ("which", "who", "whom", "whose", "where", "when")
    OPENERS = ("(", "[", "`", '"')
    # Posed by `FragmentTests` rather than here; spelled so the comparison below covers its table too.
    UNCLOSED = (",",)

    def test_Given_EveryWordTheTableRefuses_When_AFirstLineEndsOnOne_Then_EachIsRefused(self):
        # Arrange / Act
        allowed = sorted(word for word in self.DANGLING
                         if self.writes(self.FRAME.format(word)) != REFUSE)

        # Assert — the count rides along, since an empty set of words leaves nothing allowed either.
        self.assertEqual((len(self.DANGLING), allowed), (12, []))

    def test_Given_EveryWordTheTableLeavesOut_When_AFirstLineEndsOnOne_Then_EachIsAllowed(self):
        # Arrange — one class away from the members above, and every one of them ends a clause in
        # this repository's prose. Half a class in the table is what made it unpredictable, so the
        # half that is out is pinned as well as the half that is in.
        standing = ("for", "with", "that", "this", "so", "is",
                    "because", "if", "while", "since", "when", "than", "which", "whose")

        # Act
        refused = sorted(word for word in standing
                         if self.writes(self.FRAME.format(word)) != ALLOW)

        # Assert
        self.assertEqual((len(standing), refused), (14, []))

    def test_Given_EveryRelativiserBehindAComma_When_AFirstLineEndsOnOne_Then_EachIsRefused(self):
        # Arrange / Act
        allowed = sorted(word for word in self.RELATIVISERS
                         if self.writes(f"the base already separates these two, {word}") != REFUSE)

        # Assert
        self.assertEqual((len(self.RELATIVISERS), allowed), (6, []))

    def test_Given_EveryDelimiterTheTableBalances_When_OneIsLeftOpen_Then_EachIsRefused(self):
        # Arrange / Act — the bracket and the quotation mark had no case of their own, so the rules
        # for them could be dropped with nothing going red.
        allowed = sorted(opener for opener in self.OPENERS
                         if self.writes(self.FRAME.format(f"{opener}portal scope")) != REFUSE)

        # Assert
        self.assertEqual((len(self.OPENERS), allowed), (4, []))

    def test_Given_EveryMemberTheTableHolds_When_TheProbesAreCompared_Then_NoneIsUnposed(self):
        # Arrange — spelled probes cannot notice a member arriving, only one leaving. Compared
        # against the table rather than read from it, so both directions are closed at once.
        posed = ({("word", word) for word in self.DANGLING}
                 | {("relativiser", word) for word in self.RELATIVISERS}
                 | {("opener", opener) for opener in self.OPENERS}
                 | {("mark", mark) for mark in self.UNCLOSED})
        held = ({("word", word) for word in guard.DANGLING}
                | {("relativiser", word) for word in guard.RELATIVISERS}
                | {("opener", opener) for opener, _ in guard.PAIRS}
                | {("opener", mark) for mark, _ in guard.BALANCED}
                | {("mark", mark) for mark in guard.UNCLOSED})

        # Act
        unposed = sorted(held - posed)

        # Assert — the member count rides along, since an empty table leaves nothing unposed either.
        self.assertEqual((len(held), unposed), (23, []))

    def test_Given_EveryMarkThatFollowsAWholeClause_When_AFirstLineEndsOnOne_Then_EachIsAllowed(self):
        # Arrange — the three that were refused on the reading that only a comma leaves a sentence
        # open, plus the two nothing ever questioned.
        marks = (".", ";", ":", "—", "–")

        # Act
        refused = sorted(mark for mark in marks
                         if self.writes(f"the base already separates these two{mark}") != ALLOW)

        # Assert
        self.assertEqual((len(marks), refused), (5, []))


class ScopeTests(GuardCase):
    def test_Given_ADeclarationTheFileAlreadyCarries_When_AnUnrelatedEditIsPosed_Then_ItIsAllowed(self):
        # Arrange — the tree holds declarations that break off, and a guard reading the whole file
        # would make each of their files unwritable.
        self.file.write_text(
            "namespace Velvet.Tests\n{\n"
            f"    // {GREEN}: the base already separates these two and\n"
            + CONTINUATION + "    [Test] void X() { }\n}\n", encoding="utf-8")

        # Act
        code, _ = self.pose({"file_path": str(self.file),
                             "old_string": "[Test] void X() { }",
                             "new_string": "[Test] void Y() { }"})

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ANewFileCarryingABrokenDeclaration_When_AWriteIsPosed_Then_ItIsRefused(self):
        # Arrange
        content = ("namespace Velvet.Tests\n{\n"
                   f"    // {GREEN}: the base already separates these and\n"
                   + CONTINUATION + "}\n")

        # Act
        code, _ = self.pose({"file_path": str(self.root / "New.cs"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AMarkerInsideACSharpVerbatimString_When_AWriteIsPosed_Then_ItIsAllowed(self):
        # Arrange — the C# half of the shape below: a snippet in a verbatim string, where the marker
        # sits behind the comment opener it carries and a reading over the line's prefix takes it
        # for a declaration. Refused, this made a file with nothing wrong in it unwritable, and
        # there is no in-band route for a new one.
        content = ("namespace Velvet.Tests\n{\n"
                   "    internal sealed class T\n    {\n"
                   "        private const string Source = @\"\n"
                   f"            // {GREEN}: the base already separates these two and\n"
                   "            // the branch does not change that.\n"
                   "\";\n    }\n}\n")

        # Act
        code, _ = self.pose({"file_path": str(self.root / "Probe.cs"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ABrokenDeclarationInACSharpComment_When_AWriteIsPosed_Then_ItIsRefused(self):
        # Arrange — the same file kind at the position a declaration is read from, so the case
        # above cannot pass by the C# lane having stopped reading anything at all.
        content = ("namespace Velvet.Tests\n{\n"
                   f"    // {GREEN}: the base already separates these two and\n"
                   + CONTINUATION + "}\n")

        # Act
        code, _ = self.pose({"file_path": str(self.root / "Probe.cs"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_ABrokenDeclarationTheFileAlreadyCarries_When_ASecondCopyIsAdded_Then_ItIsRefused(self):
        # Arrange — copying a sibling's declaration is how this repository writes them, and the
        # tree holds verbatim-duplicate pairs already. Read as a set of reasons, the copy is text
        # the file carries and passes; reworded by one word it would not.
        carried = f"    // {GREEN}: the base already separates these two and\n" + CONTINUATION
        self.file.write_text("namespace Velvet.Tests\n{\n" + carried
                             + "    [Test] void X() { }\n}\n", encoding="utf-8")

        # Act
        code, _ = self.pose({"file_path": str(self.file),
                             "old_string": "    [Test] void X() { }\n",
                             "new_string": carried + "    [Test] void Y() { }\n"})

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AMarkerInsideAPythonStringLiteral_When_AWriteIsPosed_Then_ItIsAllowed(self):
        # Arrange — the shape the readers' own fixtures hold: a C# snippet inside a triple-quoted
        # string, where the marker sits at the head of its line behind the comment opener it
        # carries. A reading over the line's prefix takes that for a declaration, so a snippet
        # spelled any other way pins the scoping in name only.
        content = ('SOURCE = """\n'
                   f"                // {SURVIVES}(equivalent): both spellings clamp to the\n"
                   "                // same bound, so nothing can differ.\n"
                   "                if (a <= b) { }\n"
                   '"""\n')

        # Act
        code, _ = self.pose({"file_path": str(self.root / "test_probe.py"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ABrokenDeclarationInAPythonComment_When_AWriteIsPosed_Then_ItIsRefused(self):
        # Arrange — the same file kind, in the position the Python lane reads a declaration from.
        content = (f"# {GREEN}: the base already cut this correctly and the\n"
                   "# colon stop must not widen it.\n")

        # Act
        code, _ = self.pose({"file_path": str(self.root / "test_probe.py"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AMarkdownFileCarryingABrokenDeclaration_When_AWriteIsPosed_Then_ItIsAllowed(self):
        # Arrange — a marker in a guide is prose about the convention; nothing reads a declaration
        # out of it, so a document explaining a malformed one stays writable.
        content = (f"// {GREEN}: the base already separates these two and\n"
                   + CONTINUATION)

        # Act
        code, _ = self.pose({"file_path": str(self.root / "guide.md"), "content": content},
                            tool="Write")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_AnEditWhoseOldStringIsNotInTheFile_When_ItIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — the tool will fail on its own, and there is no proposed text to read.
        code, _ = self.pose({"file_path": str(self.file), "old_string": "nothing like this",
                             "new_string": f"    // {GREEN}: ending on the\n"})

        # Assert
        self.assertEqual(code, ALLOW)


class RawStringTests(GuardCase):
    """The C# shape the mask behind the guard does not read.

    `mutation_check.mask_spans` reads no raw string literal, so the body of one comes back as a
    string that ended on the first lone quotation mark in it and a run of line comments after that.
    A file with nothing wrong in it was refused that way, and a fresh `Write` has no in-band route
    past a refusal.
    """

    # A raw string whose body holds a lone quotation mark and then a marker line, and a genuine
    # comment carrying a second marker below it. Assembled the way the module header is, so the
    # readers do not count these as declarations of this file's own.
    SOURCE = ("internal sealed class Probe\n{\n"
              '    private const string Source = """\n'
              '        a body that mentions a " quotation mark\n'
              f"        // {GREEN}: the base already separates these two and\n"
              "        // the branch does not change that.\n"
              '        """;\n\n'
              f"    // {GREEN}: the base already anchors the drained child and\n"
              "    // the branch does not change where it lands.\n"
              "    [Test] void X() { }\n}\n")

    STRING_BODY_MARKER = 5

    def test_Given_AMarkerInsideACSharpRawString_When_AWriteIsPosed_Then_ItIsAllowed(self):
        # Arrange / Act — the marker inside the string body is not a declaration, and refusing it
        # made a well-formed file unwritable. The genuine one below the string is missed either
        # way: the mask swallowed the comment carrying it before the stand-down could reach it.
        code, _ = self.pose({"file_path": str(self.root / "Probe.cs"), "content": self.SOURCE},
                            tool="Write")

        # Assert
        self.assertEqual(code, ALLOW)

    def test_Given_ARawStringTheMaskMisreads_When_ItsDefectsAreRead_Then_TheMisreadLineIsOutsideThem(self):
        # Arrange — why the stand-down is the whole file. Skipping only the lines `mask_defects`
        # flags would leave the line the refusal was written about still being read.
        flagged = {number
                   for first, last, _ in mutation_check.mask_defects(self.SOURCE)
                   for number in range(first, last + 1)}
        starts = [start for start, _ in mutation_check.line_spans(self.SOURCE)]

        # Act
        read_as_comment = sorted(
            bisect.bisect_right(starts, start)
            for start, _, kind in mutation_check.mask_spans(self.SOURCE)
            if kind == mutation_check.LINE_COMMENT)

        # Assert — the flagged set rides along, since a mask reporting no defect at all would leave
        # every line outside it too.
        self.assertEqual((len(flagged) > 0, self.STRING_BODY_MARKER in read_as_comment,
                          self.STRING_BODY_MARKER in flagged),
                         (True, True, False))


class ReaderFloorTests(unittest.TestCase):
    """The readers' side of what this guard leaves to them.

    The guard adds no word count, on the stated ground that a first line under four words is
    refused by the readers already and that the first line is what they measure. Both are facts
    about `base_red_check.py` and `mutation_check.py`, so they fail here rather than in a sentence
    beside the early return that rests on them.
    """

    SHORT = "the base already"

    # Spelled rather than read from either reader, which would agree with itself whatever the floor
    # became. Four is the number the guard's own docstring declines to reimplement.
    UNDER_THE_FLOOR = "the reason's first line is under 4 words"

    def test_Given_AReasonUnderFourWords_When_EachReaderReadsIt_Then_EachRefusesItByTheFloor(self):
        # Arrange — each reader's own first category, so what the complaint answers is the word
        # count and not a category one of them does not hold.
        declared = [reader.Declaration(reader.CATEGORIES[0], self.SHORT, line=1)
                    for reader in READERS.values()]

        # Act
        complaints = sorted(declaration.complaint or "allowed" for declaration in declared)

        # Assert
        self.assertEqual(complaints, [self.UNDER_THE_FLOOR] * len(READERS))

    # GREEN_ON_BASE(characterization): every pattern stopped at the wrap before and stops at it now.
    # What moved is how this reads the reason out — by the last group rather than by number — so a
    # pattern gaining a group does not silently start comparing something else.
    def test_Given_AReasonWrappedOntoASecondLine_When_EachPatternReadsIt_Then_EachStopsAtTheWrap(self):
        # Arrange — the guard judges the first line because that is what the readers measure their
        # four-word floor on, and these are the patterns that decide it. A reader that folds the
        # continuation in for a different question does not move the floor.
        first = "the base already separates these two and"
        blocks = {"base_red_check": f"// {BASE}(characterization): {first}\n// the rest of it.\n",
                  "mutation_check": f"// {SURVIVES}(equivalent): {first}\n// the rest of it.\n"}

        # Act — the reason is each pattern's last group, which is what it is whether or not the
        # pattern carries an operator group between the category and it.
        read = {name: reader.DECLARATION.search(blocks[name]).groups()[-1]
                for name, reader in READERS.items()}
        read.update((f"guard/{name}", guard.DECLARATION.search(block).groups()[-1])
                    for name, block in blocks.items())

        # Assert
        self.assertEqual(sorted(set(read.values())), [first])


class TreeTests(unittest.TestCase):
    """The declarations this repository already carries, posed to the guard's own reading."""

    @staticmethod
    def declarations():
        tracked = subprocess.run(["git", "ls-files"], cwd=REPO_ROOT, capture_output=True,
                                 text=True, timeout=60).stdout.split()
        found = []
        for name in tracked:
            path = REPO_ROOT / name
            if path.suffix not in guard.READ_IN or not path.exists():
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            if BASE not in text and SURVIVES not in text:
                continue
            lines = text.splitlines()
            for number, marker, reason in guard.declarations(text, path.suffix):
                wraps = (number < len(lines)
                         and lines[number].strip().startswith(("//", "#"))
                         and BASE not in lines[number]
                         and SURVIVES not in lines[number])
                found.append((f"{name}:{number}", reason, wraps))
        return found

    def test_Given_EveryDeclarationInTheTree_When_TheGuardReadsIt_Then_NoSingleLineOneIsRefused(self):
        # Arrange — these carry their whole reason on the marker line, so there is no wrap for the
        # guard to be about, and the only way past a refusal here is rewriting a declaration whose
        # author was not asked. That is the cost that gets a guard turned off.
        settled = [entry for entry in self.declarations() if not entry[2]]

        # Act
        refused = [f"{where}: {guard.fragment(reason)}" for where, reason, _ in settled
                   if guard.fragment(reason)]

        # Assert — the count rides along, since an empty tree refuses nothing either.
        self.assertEqual((len(settled) > 0, refused), (True, []))

    def test_Given_EveryDeclarationInTheTree_When_TheGuardReadsIt_Then_SomeWrappedOneIsRefused(self):
        # Arrange — a rule nothing in this tree trips is one nothing measured, and the tree is
        # where the defect was found.
        wrapped = [entry for entry in self.declarations() if entry[2]]

        # Act
        refused = [where for where, reason, _ in wrapped if guard.fragment(reason)]

        # Assert
        self.assertEqual((len(wrapped) > 0, len(refused) > 0), (True, True))


if __name__ == "__main__":
    unittest.main(verbosity=2)

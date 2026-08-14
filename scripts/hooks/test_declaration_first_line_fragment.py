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


def load_guard():
    """Imports the guard by path, since .claude/hooks holds no packages."""
    spec = importlib.util.spec_from_file_location("declaration_first_line_fragment", GUARD)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


guard = load_guard()


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


class TableTests(GuardCase):
    """Every member of the decision table posed a case, and the near misses posed one too.

    Deleting a member is what a refuse hook cannot afford to take in silence, and until these ran,
    seventeen of the twenty-one rules could be removed with the suite still green. The probes are
    spelled here rather than read from the guard: a list taken from the table itself passes whatever
    the table holds, so it can never notice a member leaving it.
    """

    # A frame that ends on whatever it is given and trips nothing else on the way.
    FRAME = "the base already separates these two on the reading given {}"

    def test_Given_EveryWordTheTableRefuses_When_AFirstLineEndsOnOne_Then_EachIsRefused(self):
        # Arrange
        dangling = ("a", "an", "the", "and", "or", "nor",
                    "its", "their", "our", "your", "my", "every")

        # Act
        allowed = sorted(word for word in dangling
                         if self.writes(self.FRAME.format(word)) != REFUSE)

        # Assert — the count rides along, since an empty set of words leaves nothing allowed either.
        self.assertEqual((len(dangling), allowed), (12, []))

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
        # Arrange
        relativisers = ("which", "who", "whom", "whose", "where", "when")

        # Act
        allowed = sorted(word for word in relativisers
                         if self.writes(f"the base already separates these two, {word}") != REFUSE)

        # Assert
        self.assertEqual((len(relativisers), allowed), (6, []))

    def test_Given_EveryDelimiterTheTableBalances_When_OneIsLeftOpen_Then_EachIsRefused(self):
        # Arrange — the bracket and the quotation mark had no case of their own, so the rules for
        # them could be dropped with nothing going red.
        openers = ("(", "[", "`", '"')

        # Act
        allowed = sorted(opener for opener in openers
                         if self.writes(self.FRAME.format(f"{opener}portal scope")) != REFUSE)

        # Assert
        self.assertEqual((len(openers), allowed), (4, []))

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

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

    def test_Given_AFirstLineEndingOnARelativiser_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("the divider a departing child already loses on the base, which")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineEndingOnAComma_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("the base anchors a drained portal child on the reconcile root,")

        # Assert
        self.assertEqual(code, REFUSE)

    def test_Given_AFirstLineEndingOnAnEmDash_When_TheEditIsPosed_Then_ItIsRefused(self):
        # Arrange / Act
        code = self.writes("the base reaches this outcome by never deferring at all —")

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

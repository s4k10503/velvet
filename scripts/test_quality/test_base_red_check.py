#!/usr/bin/env python3
"""Unit tests for base_red_check.py's reading, plus guards over this repository's own test files.

Reading a branch is separable from the base run that answers it, so it is tested here and needs no
licence. Everything the run reports rests on this half: a case the reader misses is a case nothing
asks about, and a case whose span it gets wrong is attributed to a line somebody else changed. Both
failures are silent -- the run comes back green having measured the wrong thing.

Run: python3 scripts/test_quality/test_base_red_check.py
"""

import contextlib
import importlib.util
import io
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_module(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        name, Path(__file__).resolve().with_name(name + ".py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


base_red_check = load_module("base_red_check")

MARKER = "GREEN_ON_BASE"

FIXTURE_TEMPLATE = """using NUnit.Framework;

namespace Velvet.Tests
{
    internal sealed class ProbeTests
    {
        private int _count;

        [SetUp]
        public void SetUp()
        {
            _count = 0;
        }

        // {marker}(characterization): the ordering the base already has and this keeps.
        [Test]
        public void Given_A_When_B_Then_C()
        {
            var brace = "}";
            Assert.That(brace, Is.EqualTo("}"));
        }

        [UnityTest]
        public void Given_D_When_E_Then_F() => Assert.That(_count, Is.Zero);

        private void Helper()
        {
            _count++;
        }
    }
}
"""

FIXTURE = FIXTURE_TEMPLATE.replace("{marker}", MARKER)


def case_named(cases, suffix):
    return next(case for case in cases if case.name.endswith(suffix))


def tracked(lane):
    """Every tracked file of one lane, read as text.

    Tracked rather than walked: Library/PackageCache holds Unity's own fixtures, which are not this
    repository's to hold to its conventions and which this reader is not written for.
    """
    names = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files"],
                           capture_output=True, text=True, check=True).stdout.splitlines()
    found = []
    for relative in names:
        if base_red_check.kind_of(relative) != lane:
            continue
        path = REPO_ROOT / relative
        if path.exists():
            found.append((relative, path.read_text(encoding="utf-8", errors="replace")))
    return found


def csharp_test_files():
    """Every tracked C# file that declares at least one test case."""
    return [(relative, text) for relative, text in tracked("csharp")
            if base_red_check.CSHARP_CASE_ATTRIBUTE.search(text)]


def imported_csharp_fixtures():
    """Every fixture file Unity compiles, found without asking `kind_of` which lane it is in.

    `tracked` selects by lane, and a file the lane reader misses is exactly what nothing built on it
    can report. The generator solution under `Generators~` builds and runs outside Unity, so its
    fixtures are not ones a test platform reports and not ones to carry onto a base tree.

    The attribute is looked for in the code rather than the raw text, since `[Test]` occurs in this
    repository's prose about tests as well as in its declarations of them.
    """
    names = subprocess.run(["git", "-C", str(REPO_ROOT), "ls-files"],
                           capture_output=True, text=True, check=True).stdout.splitlines()
    found = []
    for relative in names:
        path = REPO_ROOT / relative
        if not relative.endswith(".cs") or "~/" in relative or not path.exists():
            continue
        code = "\n".join(base_red_check.code_lines(
            path.read_text(encoding="utf-8", errors="replace")))
        if base_red_check.CSHARP_CASE_ATTRIBUTE.search(code):
            found.append(relative)
    return found


def python_test_files():
    return tracked("python")


DECLARED_SCOPE = re.compile(
    r"\b(?:namespace|interface|enum|class|struct|record(?:\s+(?:class|struct))?)"
    r"\s+([A-Za-z_][A-Za-z0-9_.]*)")


def body_span(blanked, after):
    """(first offset inside the body, offset of its closing brace) for a declaration, or None.

    None is a declaration that never opens a body, which this repository writes as a positional
    record. Nothing can be named under one.
    """
    depth = 0
    for offset in range(after, len(blanked)):
        character = blanked[offset]
        if character == ";" and depth == 0:
            return None
        if character == "{":
            depth += 1
            if depth == 1:
                opened = offset
        elif character == "}":
            depth -= 1
            if depth == 0:
                return opened, offset
    return None


def declared_scopes(text):
    """Qualified name -> whether it is abstract, for every namespace and type one file declares.

    Each declaration is qualified by the ones whose braces enclose it, rather than by a running stack
    of the ones seen so far. The distinction is the point: a stack that fails to unwind emits owner
    chains no declaration spells, and a set of legitimate chains built from a stack the same way
    would agree with every one of them and hold nothing.
    """
    mask = base_red_check.code_mask(text)
    blanked = "".join(text[offset] if mask[offset] else " " for offset in range(len(text)))
    declared = []
    for match in DECLARED_SCOPE.finditer(blanked):
        body = body_span(blanked, match.end())
        if body is None:
            continue
        line = blanked[blanked.rfind("\n", 0, match.start()) + 1:match.end()]
        declared.append((match.start(), body, match.group(1),
                         bool(base_red_check.CSHARP_ABSTRACT.search(line))))
    scopes = {}
    for start, _, name, abstract in declared:
        enclosing = sorted((outer, outer_name) for outer, (inner, outer_end), outer_name, _ in declared
                           if inner <= start <= outer_end)
        qualified = ".".join([outer_name for _, outer_name in enclosing] + [name])
        scopes[qualified] = abstract
    return scopes


def answerable_names(corpus):
    """Every qualified type in the corpus a runner could report a case under: declared, and concrete."""
    answerable = set()
    for text in corpus.values():
        answerable |= {name for name, abstract in declared_scopes(text).items() if not abstract}
    return answerable


def unanswered_names(corpus, heirs):
    """Every case in `corpus` whose runner name no concrete class in `corpus` can answer to.

    Both ways that happens: a case the naming drops because the abstract fixture it is written in has
    no concrete heir, and a case it keeps under an owner chain nothing declares. The whole chain is
    read, not its last segment -- every segment is part of the name `-testFilter` is given, so a
    chain of names each of which is declared somewhere still matches nothing when nothing nests them
    that way.
    """
    answerable = answerable_names(corpus)
    unanswered = []
    for relative, text in corpus.items():
        for case in base_red_check.csharp_cases(text, relative):
            named = base_red_check.as_the_runner_names_them([case], heirs)
            if not named:
                unanswered.append("{}: {} runs under no concrete class".format(relative, case.name))
            unanswered += ["{}: {} names {}, which nothing declares".format(
                relative, case.name, produced.name.rsplit(".", 1)[0])
                for produced in named if produced.name.rsplit(".", 1)[0] not in answerable]
    return unanswered


class MaskedLineTests(unittest.TestCase):
    """That a masked line is as long as the raw one, which two readers index against each other by.

    A construct whose mask reaches the line terminator is where it stops being true, and the cost is
    silent: `assume_gate_check.py` takes the expression an `Assume` gates off the masked body and
    writes the raw text at those offsets into its record, so a line one character longer than its
    raw twin misquotes every gate below it in the same case.
    """

    def test_Given_AMaskCrossingALineTerminator_When_TheLinesAreMasked_Then_EachKeepsItsRawLength(self):
        # Arrange -- the three spellings that reach a terminator: a line comment before a CRLF, and a
        # verbatim string and a block comment that carry on to the next line.
        text = ("int a = 1; // note\r\nvar s = @\"one\ntwo\";\n/* three\nfour */\nint b = 2;\n")

        # Act
        masked = base_red_check.code_lines(text)

        # Assert
        self.assertEqual([len(line) for line in masked],
                         [len(line) for line in text.splitlines()])


class CSharpReadingTests(unittest.TestCase):
    def test_Given_AFixture_When_ItIsRead_Then_OnlyTheTestMethodsAreCases(self):
        # Act
        names = [case.name for case in base_red_check.csharp_cases(FIXTURE)]

        # Assert
        self.assertEqual(names, ["Velvet.Tests.ProbeTests.Given_A_When_B_Then_C",
                                 "Velvet.Tests.ProbeTests.Given_D_When_E_Then_F"])

    def test_Given_ACaseWithABraceInAStringLiteral_When_ItIsRead_Then_ItsSpanReachesItsClosingBrace(self):
        # Arrange
        case = case_named(base_red_check.csharp_cases(FIXTURE), "Given_A_When_B_Then_C")

        # Act / Assert -- the declaration above it is the first line, the closing brace the last.
        self.assertEqual((case.first_line, case.last_line), (15, 21))

    def test_Given_AnExpressionBodiedCase_When_ItIsRead_Then_ItEndsAtItsSemicolon(self):
        # Arrange
        case = case_named(base_red_check.csharp_cases(FIXTURE), "Given_D_When_E_Then_F")

        # Act / Assert
        self.assertEqual((case.first_line, case.last_line), (23, 24))

    def test_Given_ANestedFixture_When_ItIsRead_Then_BothClassesQualifyTheCase(self):
        # Arrange
        text = ("namespace N\n{\n    class Outer\n    {\n        class Inner\n        {\n"
                "            [Test]\n            public void Given_X_When_Y_Then_Z() => Assert.Pass();\n"
                "        }\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.Outer.Inner.Given_X_When_Y_Then_Z"])

    def test_Given_AStringLiteralHoldingTheWordClass_When_ItIsRead_Then_ItOwnsNoCaseBelowIt(self):
        # Arrange -- this repository writes "the enter-from class is applied" into assertion messages,
        # and a scan of the raw line reads a type named `is` out of it. Every case below is then
        # emitted under a name no runner reports, which is a reading nobody takes and a passing one.
        text = ("namespace N\n{\n    class C\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C()\n        {\n"
                "            Assert.That(x, Is.True, \"The class is applied on mount\");\n        }\n\n"
                "        [Test]\n        public void Given_D_When_E_Then_F() => Assert.Pass();\n"
                "    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C", "N.C.Given_D_When_E_Then_F"])

    def test_Given_ANestedTypeThatCloses_When_TheCasesBelowAreRead_Then_ItDoesNotQualifyThem(self):
        # Arrange -- a helper type declared among a fixture's members, an args type or a fake, with no
        # further declaration after it to displace it from the stack.
        text = ("namespace N\n{\n    class C\n    {\n        abstract class Args\n        {\n"
                "        }\n\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C"])

    def test_Given_ANestedAbstractTypeThatCloses_When_TheCaseBelowIsRead_Then_ItIsNotWrittenInIt(self):
        # Arrange -- the same shape read for the other half: a case the naming believes is inherited
        # is rewritten into one name per concrete heir, and an args type has none, so it is dropped.
        text = ("namespace N\n{\n    class C\n    {\n        abstract class Args\n        {\n"
                "        }\n\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n")

        # Act
        case = base_red_check.csharp_cases(text)[0]

        # Assert
        self.assertIsNone(case.abstract_owner)

    def test_Given_APositionalRecordAboveAFixture_When_TheCasesAreRead_Then_ItDoesNotQualifyThem(self):
        # Arrange -- this repository declares its store state as a positional record beside the
        # fixture. It opens no body, so nothing closes it and nothing pops it off the type stack.
        text = ("namespace N\n{\n    internal readonly record struct ToggleState(bool Show);\n\n"
                "    class C\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C"])

    def test_Given_PositionalRecordsAmongAFixturesMembers_When_TheCasesBelowAreRead_Then_TheyNestNothing(self):
        # Arrange -- one bodiless declaration also blocks the ones under it from leaving the stack,
        # so the chain grows by a segment per record rather than by one.
        text = ("namespace N\n{\n    class C\n    {\n        private sealed record A(int X);\n"
                "        private sealed record B(int Y);\n\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C"])

    def test_Given_ARecordStructWithABody_When_ItsCaseIsRead_Then_ItIsNamedForTheTypeNotTheKeyword(self):
        # Arrange -- `record` takes an optional `class` or `struct`, which sits where the other two
        # spellings put the name. The repository-wide guard cannot hold this: it reads declarations
        # through the same expression, so it would name the owner `struct` as well and agree.
        text = ("namespace N\n{\n    record struct C\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C"])

    def test_Given_APreprocessorDirectiveHoldingAnApostrophe_When_TheFileIsRead_Then_TheCasesBelowAreToo(self):
        # Arrange -- `#region Boundary's own mount` opens a character literal to anything reading the
        # directive as code, and it then runs to the next apostrophe anywhere in the file, blanking
        # every brace, attribute and type between the two.
        text = ("namespace N\n{\n    class C\n    {\n        #region Boundary's own mount\n\n"
                "        [Test]\n        public void Given_A_When_B_Then_C() => Assert.Pass();\n\n"
                "        #endregion\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.C.Given_A_When_B_Then_C"])

    def test_Given_ASecondFixtureAfterTheFirstCloses_When_ItIsRead_Then_TheFirstDoesNotQualifyIt(self):
        # Arrange
        text = ("namespace N\n{\n    class First\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n\n"
                "    class Second\n    {\n        [Test]\n"
                "        public void Given_D_When_E_Then_F() => Assert.Pass();\n    }\n}\n")

        # Act
        names = [case.name for case in base_red_check.csharp_cases(text)]

        # Assert
        self.assertEqual(names, ["N.First.Given_A_When_B_Then_C", "N.Second.Given_D_When_E_Then_F"])


class DeclarationTests(unittest.TestCase):
    def test_Given_ADeclarationAboveTheAttributeBlock_When_TheCaseIsRead_Then_ItCarriesIt(self):
        # Arrange
        case = case_named(base_red_check.csharp_cases(FIXTURE), "Given_A_When_B_Then_C")

        # Act / Assert
        self.assertEqual(case.declaration.category, "characterization")

    def test_Given_ABlankLineBetweenTheDeclarationAndTheCase_When_ItIsRead_Then_ItDoesNotCarry(self):
        # Arrange
        text = ("class C\n{\n    // " + MARKER + "(characterization): a reason with enough words.\n"
                "\n    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n")

        # Act
        case = base_red_check.csharp_cases(text)[0]

        # Assert
        self.assertIsNone(case.declaration)

    def test_Given_ADeclarationOverTheCaseAbove_When_TheNextCaseIsRead_Then_ItDoesNotCarry(self):
        # Arrange
        text = ("class C\n{\n    // " + MARKER + "(characterization): a reason with enough words.\n"
                "    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n\n"
                "    [Test]\n    public void Given_D_When_E_Then_F() => Assert.Pass();\n}\n")

        # Act
        cases = base_red_check.csharp_cases(text)

        # Assert
        self.assertIsNone(case_named(cases, "Given_D_When_E_Then_F").declaration)

    def test_Given_AReasonWrappedOntoASecondCommentLine_When_ItIsRead_Then_TheWrappedHalfIsPartOfIt(self):
        # Arrange
        text = ("class C\n{\n    // " + MARKER + "(characterization): the reordering\n"
                "    // this rename keeps is the base's own.\n"
                "    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n")

        # Act
        case = base_red_check.csharp_cases(text)[0]

        # Assert
        self.assertEqual(case.declaration.reason,
                         "the reordering this rename keeps is the base's own.")

    def test_Given_APythonReasonWrappedOntoASecondLine_When_ItIsRead_Then_TheWrappedHalfIsPartOfIt(self):
        # Arrange -- the other marker a comment block is written with. A fold that reaches only `//`
        # leaves this lane reading first lines, and nothing else here would say so.
        text = ("import unittest\n"
                "class C(unittest.TestCase):\n"
                "    # " + MARKER + "(refactor): the names\n"
                "    # this rename preserves.\n"
                "    def test_a(self):\n        pass\n")

        # Act
        case = base_red_check.python_cases(text)[0]

        # Assert
        self.assertEqual(case.declaration.reason, "the names this rename preserves.")

    def test_Given_AShortClaimAboveAnUnrelatedRemark_When_ItIsRead_Then_TheFloorStillRefusesIt(self):
        # Arrange
        text = ("class C\n{\n    // " + MARKER + "(refactor): rename\n"
                "    // This fixture needs a real panel and is guarded by TestGraphics.\n"
                "    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n")

        # Act
        declaration = base_red_check.csharp_cases(text)[0].declaration

        # Assert -- one comparison over both: the reason is what a reader that never folds gets
        # wrong, and the complaint is what one that measures the floor over the fold gets wrong.
        self.assertEqual(
            (declaration.reason, declaration.complaint is not None),
            ("rename This fixture needs a real panel and is guarded by TestGraphics.", True))

    def test_Given_ADeclarationCarriedByThePlan_When_TheSecondInvocationDecides_Then_ItJudgesTheSame(self):
        # Arrange -- the C# lane reads the tree in one invocation and decides in another, so whatever
        # the plan does not carry is a reading the deciding half takes differently from the reading
        # half. Nothing else here goes through it.
        text = ("class C\n{\n    // " + MARKER + "(refactor): rename\n"
                "    // This fixture needs a real panel.\n"
                "    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n")
        case = base_red_check.csharp_cases(text)[0]
        case.declaration.written_here = False

        # Act
        restored = base_red_check.from_plan(
            base_red_check.as_plan("sha", [case], [], {}, {})["cases"])[0].declaration

        # Assert -- the three the plan is a transport for, in one comparison: the reason, the floor
        # verdict the claim decides, and whose declaration it is.
        self.assertEqual(
            (restored.reason, restored.complaint is not None, restored.written_here),
            ("rename This fixture needs a real panel.", True, False))

    def test_Given_ACaseCarriedByThePlan_When_TheSecondInvocationDecides_Then_ItKeysAndMeasuresItAlike(self):
        # Arrange -- the declaration is not the only thing the plan transports. `path` decides the
        # lane, the key and the platform the soundness reading is taken on, and a plan carrying a
        # wrong one turns a failing verdict into a plausible "could not compile there". That is the
        # silent direction: a dropped field raises where a corrupt one reads.
        case = base_red_check.Case("N.C.Given_A_When_B_Then_C",
                                   "Packages/p/Runtime/A/Tests/Editor/CTests.cs", 1, 2)

        # Act
        restored = base_red_check.from_plan(
            base_red_check.as_plan("sha", [case], [], {}, {})["cases"])[0]

        # Assert
        self.assertEqual((restored.key, base_red_check.measured_by(restored.path)),
                         (case.key, base_red_check.measured_by(case.path)))

    # A C# fixture as the modules here hold them, so the line below opens with `//` while being no
    # part of any comment. A reader keyed on that opener counts it, and no case here carries it.
    CSHARP_IN_A_PYTHON_STRING = '''
// GREEN_ON_BASE(characterization): the keyed-reorder order this refactor must not change.
'''

    def test_Given_APythonStringHoldingAMarker_When_TheFileIsCounted_Then_OnlyTheCommentCounts(self):
        # Arrange -- both halves in one module, since a reading that reaches neither is the other way
        # of getting this wrong.
        module = ('# GREEN_ON_BASE(refactor): the settle-path names this rename preserves.\n'
                  'SNIPPET = """' + self.CSHARP_IN_A_PYTHON_STRING + '"""\n')

        # Act
        written, _ = base_red_check.orphaned_declarations(
            "scripts/test_quality/test_probe.py", module)

        # Assert
        self.assertEqual(written, 1)

    def test_Given_ACSharpStringHoldingAMarker_When_TheFileIsCounted_Then_OnlyTheCommentCounts(self):
        # Arrange -- the other lane's spelling of the same thing: a fixture asserting over the
        # declaration syntax carries it as data.
        text = ('class C\n{\n'
                '    // GREEN_ON_BASE(refactor): the reordering this rename preserves.\n'
                '    const string Sample = @"// GREEN_ON_BASE(characterization): a sample of one.";\n'
                '}\n')

        # Act
        written, _ = base_red_check.orphaned_declarations(
            "Packages/p/Runtime/A/Tests/Editor/CTests.cs", text)

        # Assert
        self.assertEqual(written, 1)

    # GREEN_ON_BASE(characterization): a marker anywhere inside a block comment counts as written.
    # Moving the reading onto lines rather than onto whole comment texts had to keep that.
    def test_Given_AMarkerOnTheSecondLineOfABlockComment_When_TheFileIsCounted_Then_ItIsWritten(self):
        # Arrange -- the block spans two lines and only the first of them opens it, so a reading that
        # covers the opener alone stops seeing the marker while still calling the file balanced.
        text = ('class C\n{\n'
                '    /* a note standing over the case below,\n'
                '       GREEN_ON_BASE(refactor): the reordering this rename preserves. */\n'
                '    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n')

        # Act
        written, _ = base_red_check.orphaned_declarations(
            "Packages/p/Runtime/A/Tests/Editor/CTests.cs", text)

        # Assert
        self.assertEqual(written, 1)

    def test_Given_ACSharpStringMarkerAboveACase_When_TheFileIsRead_Then_NoCaseCarriesIt(self):
        # Arrange -- the marker is a fixture's own material and the case sits directly under it, so a
        # reading taken off the raw line carries it. That silences the case as declared instead of
        # failing it, and the written side counts nothing to say so.
        text = ('class C\n{\n'
                '    const string Sample = @"\n'
                '// GREEN_ON_BASE(characterization): the keyed-reorder order this preserves.";\n'
                '    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n')

        # Act
        carried = [case.declaration for case
                   in base_red_check.cases_in("Packages/p/Runtime/A/Tests/Editor/CTests.cs", text)]

        # Assert
        self.assertEqual(carried, [None])

    def test_Given_ACSharpStringMarkerOnALineThatAlsoComments_When_TheFileIsRead_Then_NoCaseCarriesIt(self):
        # Arrange — the literal closes and a comment opens on the one line. Asking whether the line
        # is commented accepts a marker that sits in neither, and the written count accepts it too,
        # so the file balances and the case is silenced with nothing having written a declaration.
        text = ('class C\n{\n'
                '    const string Sample = @"\n'
                '// GREEN_ON_BASE(characterization): the keyed-reorder order this preserves."; // note\n'
                '    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n')

        # Act
        carried = [case.declaration for case
                   in base_red_check.cases_in("Packages/p/Runtime/A/Tests/Editor/CTests.cs", text)]

        # Assert
        self.assertEqual(carried, [None])

    def test_Given_APythonStringMarkerOnALineThatAlsoComments_When_TheFileIsRead_Then_NoCaseCarriesIt(self):
        # Arrange — the other lane's spelling of the same line, in the shape these modules hold: the
        # triple quote closes the fixture text and a comment follows it.
        module = ('import unittest\n\n\n'
                  'class Fixture(unittest.TestCase):\n'
                  '    SNIPPET = """\n'
                  '# GREEN_ON_BASE(refactor): the settle-path names this rename preserves.""" # note\n'
                  '    def test_something(self):\n        self.assertTrue(True)\n')

        # Act
        carried = [case.declaration for case
                   in base_red_check.cases_in("scripts/test_quality/test_probe.py", module)]

        # Assert
        self.assertEqual(carried, [None])

    def test_Given_APythonStringMarkerAboveACase_When_TheFileIsRead_Then_NoCaseCarriesIt(self):
        # Arrange -- the other lane's spelling of it, in the shape these modules already hold: a
        # marker inside the text a fixture asserts over, with a case directly beneath.
        module = ('import unittest\n\n\n'
                  'class Fixture(unittest.TestCase):\n'
                  '    SNIPPET = """\n'
                  '# GREEN_ON_BASE(refactor): the settle-path names this rename preserves."""\n'
                  '    def test_something(self):\n        self.assertTrue(True)\n')

        # Act
        carried = [case.declaration for case
                   in base_red_check.cases_in("scripts/test_quality/test_probe.py", module)]

        # Assert
        self.assertEqual(carried, [None])

    def test_Given_ADeclarationWrittenOverAHelper_When_TheFileIsRead_Then_ItIsReportedAsOrphaned(self):
        # Arrange -- it silences nothing and looks like it does, and the case it was meant for then
        # fails as green on the base under advice to write what is already there.
        text = ('class C\n{\n'
                '    // GREEN_ON_BASE(refactor): the reordering this rename preserves.\n'
                '    private void Helper() { }\n\n'
                '    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n')

        # Act
        written, carried = base_red_check.orphaned_declarations(
            "Packages/p/Runtime/A/Tests/Editor/CTests.cs", text)

        # Assert
        self.assertEqual((written, carried), (1, 0))

    def test_Given_ACategoryNothingDefines_When_TheDeclarationIsChecked_Then_ItIsComplainedAbout(self):
        # Act
        complaint = base_red_check.Declaration("whatever", "a reason with enough words in it").complaint

        # Assert
        self.assertIn("whatever", complaint)

    def test_Given_AReasonOfThreeWords_When_TheDeclarationIsChecked_Then_ItIsComplainedAbout(self):
        # Act
        complaint = base_red_check.Declaration("refactor", "see the above").complaint

        # Assert
        self.assertIn("under", complaint)

    def test_Given_AWellFormedDeclaration_When_ItIsChecked_Then_ThereIsNoComplaint(self):
        # Act / Assert
        self.assertIsNone(
            base_red_check.Declaration("refactor", "a pure rename of the applier").complaint)


class SelectionTests(unittest.TestCase):
    def test_Given_ALineInsideOneCase_When_TheFileIsSelected_Then_OnlyThatCaseIsTaken(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE)

        # Act
        taken = base_red_check.touched(cases, {20})

        # Assert
        self.assertEqual([case.name for case in taken],
                         ["Velvet.Tests.ProbeTests.Given_A_When_B_Then_C"])

    def test_Given_ALineInSetUp_When_TheFileIsSelected_Then_NoCaseIsTaken(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE)

        # Act
        taken = base_red_check.touched(cases, {12})

        # Assert -- an untouched case is worth more as a control than as an accusation.
        self.assertEqual(taken, [])

    def test_Given_ALineInSetUp_When_TheUnjudgedLinesAreCounted_Then_ItIsAmongThem(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE)

        # Act / Assert
        self.assertEqual(base_red_check.outside(cases, {12}), {12})

    def test_Given_ALineInsideACase_When_TheUnjudgedLinesAreCounted_Then_ItIsNotAmongThem(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE)

        # Act / Assert
        self.assertEqual(base_red_check.outside(cases, {20}), set())

    def test_Given_AStringLineOpeningLikeAComment_When_TheUnjudgedLinesAreCounted_Then_ItIsAmongThem(self):
        # Arrange -- a field holding a C# fixture as data, whose last line opens with `//` and sits
        # directly above a case. Walking the block up over it absorbs the field into that case, and
        # what a case covers is what `outside` subtracts: the line stops being reported as shared,
        # so a run stops saying the file's other cases are no longer the base's text.
        text = ('class C\n{\n'
                '    const string Sample = @"[Test]\n'
                '// a line of the fixture this asserts over";\n'
                '    [Test]\n    public void Given_A_When_B_Then_C() => Assert.Pass();\n}\n')
        cases = base_red_check.csharp_cases(text)

        # Act / Assert
        self.assertEqual(base_red_check.outside(cases, {4}), {4})

    def test_Given_AFileTheBranchAddedWhole_When_ItIsSelected_Then_EveryCaseIsTaken(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE)

        # Act
        taken = base_red_check.touched(cases, None)

        # Assert
        self.assertEqual(len(taken), len(cases))


class PathTests(unittest.TestCase):
    def test_Given_AnEditorTestFile_When_ItsPlatformIsRead_Then_ItIsTheEditModeRunner(self):
        # Act / Assert
        self.assertEqual(base_red_check.platform_of("Packages/x/Runtime/A/Tests/Editor/BTests.cs"),
                         "EditMode")

    def test_Given_APlayModeTestFile_When_ItsPlatformIsRead_Then_ItIsThePlayModeRunner(self):
        # Act / Assert
        self.assertEqual(base_red_check.platform_of("Packages/x/Runtime/A/Tests/PlayMode/BTests.cs"),
                         "PlayMode")

    def test_Given_AProductionSource_When_ItsLaneIsRead_Then_ItIsInNone(self):
        # Act / Assert
        self.assertIsNone(base_red_check.kind_of("Packages/x/Runtime/A/Reconciler.cs"))

    def test_Given_APythonTestModule_When_ItsLaneIsRead_Then_ItIsThePythonOne(self):
        # Act / Assert
        self.assertEqual(base_red_check.kind_of("scripts/pr/test_settle.py"), "python")

    def test_Given_ASharedTestHelper_When_ItIsClassified_Then_ItIsCarriedOntoTheBase(self):
        # Act / Assert -- it holds no case, and a case that calls it compiles without it nowhere.
        self.assertTrue(base_red_check.is_test_side("Packages/x/TestUtilities/PanelTestBase.cs"))

    def test_Given_AProductionSource_When_ItIsClassified_Then_ItIsNotCarriedOntoTheBase(self):
        # Act / Assert -- carrying it would put the branch's own fix under the case it is measuring.
        self.assertFalse(base_red_check.is_test_side("Packages/x/Runtime/A/Reconciler.cs"))


class CompileErrorTests(unittest.TestCase):
    def test_Given_AUnityCompileError_When_TheLogIsRead_Then_TheSourceIsRepositoryRelative(self):
        # Arrange
        log = ("/tmp/base/Packages/com.velvet.core/Runtime/A/Tests/Editor/BTests.cs(12,34): "
               "error CS0117: 'V' does not contain a definition for 'Portal'\n")

        # Act / Assert
        self.assertEqual(base_red_check.compile_error_files(log),
                         ["Packages/com.velvet.core/Runtime/A/Tests/Editor/BTests.cs"])

    def test_Given_AWarningRatherThanAnError_When_TheLogIsRead_Then_NoSourceIsBlamed(self):
        # Arrange
        log = "Packages/x/Runtime/A.cs(1,1): warning CS0168: unused variable\n"

        # Act / Assert
        self.assertEqual(base_red_check.compile_error_files(log), [])


class VerdictTests(unittest.TestCase):
    def probe(self, declaration=None):
        return base_red_check.Case("N.C.Given_A_When_B_Then_C", "a/Tests/Editor/CTests.cs", 1, 2,
                                   declaration)

    def test_Given_AnUndeclaredCaseGreenOnTheBase_When_ItIsDecided_Then_ItFailsTheRun(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Passed", fixture_ran=True)

        # Assert
        self.assertIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_AnUndeclaredCaseRedOnTheBase_When_ItIsDecided_Then_ItDoesNot(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Failed", fixture_ran=True)

        # Assert
        self.assertNotIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_ADeclaredCaseGreenOnTheBase_When_ItIsDecided_Then_ItDoesNotFailTheRun(self):
        # Arrange
        declared = self.probe(base_red_check.Declaration("characterization", "the order it already has"))

        # Act
        verdict, _ = base_red_check.decide(declared, "Passed", fixture_ran=True)

        # Assert
        self.assertNotIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_ADeclaredCaseRedOnTheBase_When_ItIsDecided_Then_TheDeclarationIsReportedStale(self):
        # Arrange
        declared = self.probe(base_red_check.Declaration("characterization", "the order it already has"))

        # Act
        verdict, _ = base_red_check.decide(declared, "Failed", fixture_ran=True)

        # Assert -- a declaration nothing withdraws is one that silences a later, real green.
        self.assertEqual(verdict, base_red_check.DECLARED_STALE)

    def test_Given_ADeclarationTheBranchDidNotWrite_When_TheCaseIsGreen_Then_ItStillFailsTheRun(self):
        # Arrange -- it answers for the change that wrote it, which is already on the base. Reading it
        # as an exemption for whatever the branch does to the case next lets one declaration silence
        # every later green-on-base in that case for good.
        merged = base_red_check.Declaration("refactor", "the keyed order this rename keeps",
                                            written_here=False)

        # Act
        verdict, _ = base_red_check.decide(self.probe(merged), "Passed", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.PASSED_ON_BASE)

    def test_Given_ADeclarationTheBranchDidNotWrite_When_TheCaseIsRed_Then_ItIsReadAsUndeclared(self):
        # Arrange -- red on the base is the outcome the check is asking for, and a declaration that
        # answers for somebody else's change must not turn it into a complaint about this one.
        merged = base_red_check.Declaration("refactor", "the keyed order this rename keeps",
                                            written_here=False)

        # Act
        verdict, _ = base_red_check.decide(self.probe(merged), "Failed", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    def test_Given_ACaseThatRaisedOnTheBase_When_ItIsDecided_Then_ItIsNotReadAsRed(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Error", fixture_ran=True)

        # Assert -- it stopped before it disagreed with anything, so it pins nothing either way.
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ACaseInconclusiveOnTheBase_When_ItIsDecided_Then_ItIsNotReadAsRed(self):
        # Arrange -- an Assume of its own was false there, which is the C# spelling of the same thing.
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Inconclusive", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ACaseSkippedOnTheBase_When_ItIsDecided_Then_ItIsNotReadAsRed(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Skipped", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ACaseTheBaseCouldNotAnswer_When_ItIsReported_Then_ItFailsTheGate(self):
        # Arrange
        case = self.probe()

        # Act
        offenders = base_red_check.report(
            [case], [], {case.key: "Skipped", "N.CanaryTests.Given_X_When_Y_Then_Z": "Passed"},
            {"EditMode": ["N.CanaryTests"]}, wrote=True)

        # Assert
        self.assertEqual([offender.verdict for offender in offenders],
                         [base_red_check.COULD_NOT_ANSWER])

    # GREEN_ON_BASE(characterization): the unknown-category refusal the new verdicts must leave alone.
    def test_Given_AMalformedDeclarationOnACaseGreenOnTheBase_When_ItIsDecided_Then_ItFailsTheRun(self):
        # Arrange -- a category the script does not know reads to everyone else as an approved
        # exemption, and the case it sits on is one that passes there, which is the reading this
        # whole check exists to refuse.
        # Act
        verdict, _ = base_red_check.decide(
            self.probe(base_red_check.Declaration("invented", "the names this rename preserves")),
            "Passed", fixture_ran=True)

        # Assert
        self.assertIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_ACaseWhoseRunSaidSomethingUnreadable_When_ItIsDecided_Then_ItFailsTheRun(self):
        # Arrange -- unlike the three above, nothing here says what the base did with the case, so
        # the two directions it could be filed under are both a guess and one of them exits zero.
        # Act
        verdict, _ = base_red_check.decide(self.probe(), "Unreadable", fixture_ran=True)

        # Assert
        self.assertIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_ADeclaredCaseTheBaseCouldNotAnswerFor_When_ItIsDecided_Then_TheDeclarationStands(self):
        # Arrange -- an environment that skipped the case says nothing about whether it belongs on
        # the base, and the branch cannot act on being told to delete a declaration that is right.
        # Act
        verdict, _ = base_red_check.decide(
            self.probe(base_red_check.Declaration("refactor", "the names this rename preserves")),
            "Skipped", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseWhoseRunSaidSomethingUnreadable_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange -- the third way the base can fail to reach a verdict, and the one that reads as a
        # verdict to anything comparing against the two named readings rather than against the set.
        # Act
        verdict, _ = base_red_check.decide(
            self.probe(base_red_check.Declaration("refactor", "the names this rename preserves")),
            "Unreadable", fixture_ran=True)

        # Assert
        self.assertEqual(verdict, base_red_check.NOT_REPORTED)

    def test_Given_ADeclaredCaseWhoseFixtureTheBaseNeverBuilt_When_ItIsDecided_Then_ItIsStillEvidence(self):
        # Arrange -- the same reading in the C# lane's spelling, and the older of the two.
        # Act
        verdict, _ = base_red_check.decide(
            self.probe(base_red_check.Declaration("refactor", "the names this rename preserves")),
            None, fixture_ran=False)

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_COMPILE)

    def test_Given_ACaseAbsentFromAFixtureThatRan_When_ItIsDecided_Then_ItFailsTheRun(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), None, fixture_ran=True)

        # Assert -- a name the runner does not answer to is a reading nobody took.
        self.assertIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_ACaseWhoseFixtureTheBaseNeverBuilt_When_ItIsDecided_Then_ItIsEvidenceNotAFailure(self):
        # Act
        verdict, _ = base_red_check.decide(self.probe(), None, fixture_ran=False)

        # Assert -- it names a symbol the base has not got, which is what a pin is supposed to do.
        self.assertNotIn(verdict, base_red_check.FAILING_VERDICTS)

    def test_Given_EveryArgumentListOfAParameterisedCasePassing_When_ItIsLookedUp_Then_ItReadsPassed(self):
        # Arrange
        reported = {"N.C.Given_A_When_B_Then_C(1)": "Passed", "N.C.Given_A_When_B_Then_C(2)": "Passed"}

        # Act / Assert
        self.assertEqual(base_red_check.outcome_for("N.C.Given_A_When_B_Then_C", reported), "Passed")

    def test_Given_OneArgumentListFailing_When_TheCaseIsLookedUp_Then_ItDoesNotReadPassed(self):
        # Arrange
        reported = {"N.C.Given_A_When_B_Then_C(1)": "Passed", "N.C.Given_A_When_B_Then_C(2)": "Failed"}

        # Act / Assert
        self.assertEqual(base_red_check.outcome_for("N.C.Given_A_When_B_Then_C", reported), "Failed")

    def test_Given_ASiblingWhoseNameExtendsThisOne_When_TheCaseIsLookedUp_Then_ItIsNotCountedIn(self):
        # Arrange -- a prefix match on the bare name would swallow it; only an argument list may follow.
        reported = {"N.C.Given_A_When_B_Then_CD": "Failed"}

        # Act / Assert
        self.assertIsNone(base_red_check.outcome_for("N.C.Given_A_When_B_Then_C", reported))


class FixtureRollupTests(unittest.TestCase):
    def test_Given_AParameterisedEntry_When_TheFixturesThatRanAreRead_Then_TheArgumentsAreNotPartOfIt(self):
        # Arrange -- an argument list can hold a dot, which a bare rsplit would take as the separator.
        reported = {'N.C.Given_A_When_B_Then_C("a.b")': "Passed"}

        # Act / Assert
        self.assertEqual(base_red_check.fixtures_that_ran(reported), {"N.C"})


class CanaryTests(unittest.TestCase):
    """The fixtures a base tree is read by where the branch left no control case of its own."""

    def tree(self, *tracked):
        holder = tempfile.mkdtemp(prefix="base-red-canary-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        subprocess.run(["git", "-C", holder, "init", "-q"], check=True)
        for relative in tracked + ("Library/PackageCache/x@1/Tests/Editor/ForeignTests.cs",):
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("namespace N\n{\n    class {}\n    {{\n        [Test]\n"
                            "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }}\n}}\n"
                            .replace("{}", Path(relative).stem, 1))
        subprocess.run(["git", "-C", holder, "add", *tracked], check=True)
        return root

    def test_Given_ATestFileGitDoesNotTrack_When_TheCanariesAreChosen_Then_ItIsNotAmongThem(self):
        # Arrange -- Library/PackageCache holds Unity's own fixtures, which compile into assemblies
        # this project cannot pose a filter for, so a canary taken from there never reports at all.
        root = self.tree("Packages/p/Runtime/A/Tests/Editor/MineTests.cs")

        # Act
        canaries = base_red_check.canary_fixtures(root, "EditMode", carry=[])

        # Assert
        self.assertEqual(canaries, ["N.MineTests"])

    def test_Given_TheOnlyTrackedFixtureIsOneTheBranchCarries_When_TheCanariesAreChosen_Then_NoneIs(self):
        # Arrange -- a file the branch replaced says nothing about the tree it was copied onto.
        carried = "Packages/p/Runtime/A/Tests/Editor/MineTests.cs"
        root = self.tree(carried)

        # Act / Assert
        self.assertEqual(base_red_check.canary_fixtures(root, "EditMode", carry=[carried]), [])


class PythonOutcomeTests(unittest.TestCase):
    """What one unittest invocation is read to have said, over the four things it can say."""

    TRAILER = "Ran 1 test in 0.001s\n\n{}\n"

    def test_Given_ACaseThatDisagreed_When_ItsRunIsRead_Then_ItIsTheBaseAnswering(self):
        # Act
        outcome = base_red_check.python_outcome(self.TRAILER.format("FAILED (failures=1)"))

        # Assert
        self.assertEqual(outcome, "Failed")

    def test_Given_ACaseThatRaised_When_ItsRunIsRead_Then_ItIsNotTheBaseAnswering(self):
        # Arrange -- a name the base has not got dies here, and exits with the same status as a case
        # that ran and disagreed.
        # Act
        outcome = base_red_check.python_outcome(self.TRAILER.format("FAILED (errors=1)"))

        # Assert
        self.assertEqual(outcome, "Error")

    def test_Given_ACaseThatPassed_When_ItsRunIsRead_Then_ItIsAPass(self):
        # Act
        outcome = base_red_check.python_outcome(self.TRAILER.format("OK"))

        # Assert
        self.assertEqual(outcome, "Passed")

    def test_Given_ACaseThatSkipped_When_ItsRunIsRead_Then_ItIsNotAPass(self):
        # Arrange -- unittest exits zero for a skip, so the status alone reads it as agreement.
        # Act
        outcome = base_red_check.python_outcome(self.TRAILER.format("OK (skipped=1)"))

        # Assert
        self.assertEqual(outcome, "Skipped")

    def test_Given_ATrailerNamingNeitherCount_When_ItIsRead_Then_NothingIsReadOffIt(self):
        # Arrange -- unittest prints this for a case marked expectedFailure that passed. Reading it
        # as an exception exits zero, and picking that side of the line is the fail-open this
        # separation exists to close.
        # Act
        outcome = base_red_check.python_outcome(
            self.TRAILER.format("FAILED (unexpected successes=1)"))

        # Assert
        self.assertEqual(outcome, "Unreadable")

    def test_Given_ARunThatPrintedNoTrailer_When_ItIsRead_Then_ItIsNotTheBaseAnswering(self):
        # Arrange -- a module whose top level reaches for a name the branch adds raises out of the
        # loader, and the process prints a traceback where the trailer would be.
        # Act
        outcome = base_red_check.python_outcome("Traceback (most recent call last):\n")

        # Assert
        self.assertEqual(outcome, "Error")


class SilenceTests(unittest.TestCase):
    """Which of the two ways a case can be missing the run reports as having measured nothing."""

    def summary_over(self, reported):
        case = base_red_check.Case("N.C.Given_A_When_B_Then_C", "a/Tests/Editor/CTests.cs", 1, 2)
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            base_red_check.report([case], [], reported)
        return printed.getvalue()

    def test_Given_TheTwoWaysACaseGoesMissing_When_TheRunIsSummarised_Then_OnlyTheRaisedOneCounts(self):
        # Arrange -- one comparison over both, because either alone holds on a run that summarises
        # nothing at all. A case the base could not build is the strongest pin this takes: it names
        # a symbol the branch adds, and counting it among the readings nobody took tells the author
        # of a correct test that the run measured nothing.
        raised = self.summary_over({"N.C.Given_A_When_B_Then_C": "Error"})

        # Act
        uncompilable = self.summary_over({})

        # Assert
        self.assertEqual(("could not answer for" in raised, "could not answer for" in uncompilable),
                         (True, False))

    def test_Given_ACaseTheBaseBuiltNoneOfTheFixtureOf_When_TheRunIsSummarised_Then_ItIsCountedToo(self):
        # Arrange -- the verdict is the fixture's, so a case in one that reported nothing carries it
        # whether or not it is the case naming what the base has not got. Counting them is how a
        # reviewer sees how much of the run that is without counting the per-case listing by hand.
        uncompilable = self.summary_over({})

        # Act
        answered = self.summary_over({"N.C.Given_A_When_B_Then_C": "Failed"})

        # Assert -- the answered run rides along because a line printed unconditionally satisfies
        # the first half on its own.
        self.assertEqual(("1 of 1 case(s) sit in a fixture" in uncompilable,
                          "sit in a fixture" in answered), (True, False))


class UnittestTrailerTests(unittest.TestCase):
    """Which deaths at a module's own top level print a trailer, since `python_outcome` reads one.

    The engine's behaviour rather than this repository's, so it is measured here rather than stated
    beside the reader. The reader's no-trailer branch exists for the half that prints nothing, and
    without this nothing says which half that is -- both halves reach the same verdict through it,
    so its own end-to-end case cannot tell them apart.
    """

    MODULE = ("import unittest\n\n{top}\n\n\nclass T(unittest.TestCase):\n"
              "    def test_Given_A_When_B_Then_C(self):\n        pass\n")

    def printed(self, top):
        holder = tempfile.mkdtemp(prefix="unittest-trailer-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        (Path(holder) / "test_probe.py").write_text(self.MODULE.format(top=top))
        result = subprocess.run(
            [sys.executable, "-m", "unittest", "-v", "test_probe.T.test_Given_A_When_B_Then_C"],
            cwd=holder, capture_output=True, text=True)
        return result.stdout + result.stderr

    def test_Given_ATopLevelImportError_When_ACaseOfThatModuleIsRun_Then_ATrailerIsPrinted(self):
        # Arrange -- the loader turns this one into a case that fails, so the run closes normally.
        # Act
        printed = self.printed("import no_module_of_this_name_at_all")

        # Assert
        self.assertIsNotNone(base_red_check.UNITTEST_SUMMARY.search(printed))

    def test_Given_ATopLevelRaiseOfAnythingElse_When_ACaseOfThatModuleIsRun_Then_NoTrailerIsPrinted(self):
        # Arrange -- an attribute the branch added and the base has not got raises exactly here.
        # Act
        printed = self.printed("raise AttributeError('the base has not got this')")

        # Assert
        self.assertIsNone(base_red_check.UNITTEST_SUMMARY.search(printed))


class PythonCanaryTests(unittest.TestCase):
    """The cases a base tree's Python lane is read by, chosen the way the C# lane chooses fixtures."""

    MODULE = ("import unittest\n\n\nclass T(unittest.TestCase):\n"
              "    def test_Given_A_When_B_Then_C(self):\n        pass\n")

    def tree(self, *tracked):
        holder = tempfile.mkdtemp(prefix="base-red-pycanary-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        subprocess.run(["git", "-C", holder, "init", "-q"], check=True)
        for relative in tracked:
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(self.MODULE)
        subprocess.run(["git", "-C", holder, "add", *tracked], check=True)
        return root

    def test_Given_AModuleTheBranchDidNotCarry_When_TheCanariesAreChosen_Then_ACaseOfItIsTaken(self):
        # Arrange
        root = self.tree("scripts/x/test_theirs.py")

        # Act
        chosen = base_red_check.python_canaries(root, carry=[])

        # Assert
        self.assertEqual([case.key for case in chosen], ["scripts/x/test_theirs.py::T.test_Given_A_When_B_Then_C"])

    def test_Given_TheOnlyModuleIsOneTheBranchCarries_When_TheCanariesAreChosen_Then_NoneIs(self):
        # Arrange -- a module the branch replaced says nothing about the tree it was copied onto.
        carried = "scripts/x/test_mine.py"
        root = self.tree(carried)

        # Act / Assert
        self.assertEqual(base_red_check.python_canaries(root, carry=[carried]), [])


class PythonSurfaceEvidenceTests(unittest.TestCase):
    def test_Given_UnprovenMissingSurfaces_When_TheyAreCompared_Then_NeitherIsEvidence(self):
        # Arrange -- one name is absent on both trees, and one path belongs to the environment rather
        # than the repository. Neither absence demonstrates a dependency on what the branch changed.
        holder = tempfile.mkdtemp(prefix="base-red-pysurface-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        base = root / "base"
        branch = root / "branch"
        for tree in (base, branch):
            module = tree / "scripts/helper.py"
            module.parent.mkdir(parents=True)
            module.write_text("def kept():\n    return 1\n")
        case = base_red_check.Case("T.test_a", "scripts/test_mine.py", 1, 2)

        # Act
        misspelled = base_red_check.added_python_surface(
            "AttributeError: module 'helper' has no attribute 'misspelled'", base, branch, case)
        environment = base_red_check.added_python_surface(
            "FileNotFoundError: [Errno 2] No such file or directory: '/tmp/not-provided'",
            base, branch, case)

        # Assert
        self.assertEqual((misspelled, environment), (False, False))


class PythonLaneRunTests(unittest.TestCase):
    """The Python lane end to end, over trees arranged to answer in the ways it must separate.

    A reading taken from `decide` alone cannot say whether the lane reaches it, and the lane reaching
    it is half of what the fail-open was: the run exited zero having asked a tree that answered
    nothing.
    """

    HELPER = "def kept():\n    return 1\n"

    CASE = ("import unittest\n\nimport helper\n\n\nclass T(unittest.TestCase):\n"
            "    def test_Given_A_When_B_Then_{name}(self):\n        {body}\n")

    def run_lane(self, base, branch):
        """(exit status, what it printed) for a lane over a tree holding `base`, changed to `branch`."""
        holder = tempfile.mkdtemp(prefix="base-red-pylane-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        subprocess.run(["git", "-C", holder, "init", "-q"], check=True)
        for name, files in (("base", base), ("branch", branch)):
            for relative, text in files.items():
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text)
            subprocess.run(["git", "-C", holder, "add", "-A"], check=True)
            subprocess.run(["git", "-C", holder, "-c", "user.email=t@t", "-c", "user.name=t",
                            "commit", "-q", "-m", name], check=True)
        since = subprocess.run(["git", "-C", holder, "rev-parse", "HEAD^"],
                               capture_output=True, text=True, check=True).stdout.strip()
        result = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", holder, "--base", since, "--lane", "python",
             "--output", str(root / "logs")],
            capture_output=True, text=True)
        self.addCleanup(subprocess.run, ["git", "-C", holder, "worktree", "prune"],
                        capture_output=True)
        return result.returncode, result.stdout + result.stderr

    def test_Given_ACaseReachingForASymbolTheBranchAdds_When_TheLaneRuns_Then_ItIsLoadEvidence(self):
        # Arrange -- the shape a branch produces for free: a helper it added, and a case that dies
        # reaching for it on a base which has not got it.
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass")}
        branch = {"scripts/helper.py": self.HELPER + "\n\ndef added():\n    return 2\n",
                  "scripts/test_mine.py": self.CASE.format(
                      name="C", body="self.assertEqual(helper.added(), 2)")}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert -- the base tree demonstrably lacks a symbol the branch defines, which is evidence
        # only while it is separated from an arbitrary exception under the other verdict.
        self.assertEqual((status, "could not load there" in printed), (0, True))

    # GREEN_ON_BASE(characterization): the half of the separation the new verdict must leave alone.
    def test_Given_ACaseTheBaseRanAndDisagreedWith_When_TheLaneRuns_Then_ItIsCountedRed(self):
        # Arrange -- the same branch, asking for a value the base's own helper answers differently.
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass")}
        branch = {"scripts/helper.py": "def kept():\n    return 2\n",
                  "scripts/test_mine.py": self.CASE.format(
                      name="C", body="self.assertEqual(helper.kept(), 2)")}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, base_red_check.RED_ON_BASE in printed), (0, True))

    def test_Given_AModuleWhoseTopLevelReachesForTheAddedSymbol_When_TheLaneRuns_Then_ItIsLoadEvidence(self):
        # Arrange -- the case the trailer alone does not cover. A module raising anything but
        # ImportError at its own top level takes the loader down with it, so the process prints a
        # traceback where the summary would be and the exit status is the same one a disagreement has.
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass")}
        branch = {"scripts/helper.py": self.HELPER + "\n\nadded = 2\n",
                  "scripts/test_mine.py": self.CASE.format(
                      name="C", body="self.assertEqual(WANTED, 2)") + "\n\nWANTED = helper.added\n"}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, "could not load there" in printed), (0, True))

    def test_Given_ACaseImportingAFileTheBranchAdds_When_TheLaneRuns_Then_ItIsLoadEvidence(self):
        # Arrange -- the self-hosting shape: a changed test module imports a sibling implementation
        # the same branch adds, so the carried test reaches a path the base tree demonstrably lacks.
        base = {"scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass"),
                "scripts/helper.py": self.HELPER}
        branch = {"scripts/added.py": "VALUE = 2\n",
                  "scripts/test_mine.py": self.CASE.replace("import helper", "import added").format(
                      name="C", body="self.assertEqual(added.VALUE, 2)")}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, "could not load there" in printed), (0, True))

    def test_Given_AModuleOpeningAFileTheBranchAdds_When_TheLaneRuns_Then_ItIsLoadEvidence(self):
        # Arrange -- importlib-backed sibling loaders report a missing repository file rather than a
        # missing module. Resolving the base worktree path must still reach the branch counterpart.
        module = ("import unittest\nfrom pathlib import Path\n\n"
                  "exec(Path(__file__).with_name('added.py').read_text())\n\n\n"
                  "class T(unittest.TestCase):\n"
                  "    def test_Given_A_When_B_Then_C(self):\n"
                  "        self.assertEqual(VALUE, 2)\n")
        base = {"scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass"),
                "scripts/helper.py": self.HELPER}
        branch = {"scripts/added.py": "VALUE = 2\n", "scripts/test_mine.py": module}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, "could not load there" in printed), (0, True))

    def test_Given_ACaseRaisingForAnotherReason_When_TheLaneRuns_Then_ItStillFailsClosed(self):
        # Arrange -- the branch passes this case, but the base takes an ordinary exception path. That
        # says neither that the assertion disagreed nor that a branch-added surface was unavailable.
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass")}
        body = ("self.assertEqual(helper.kept(), 2) if helper.kept() != 1 "
                "else (_ for _ in ()).throw(RuntimeError('not a surface gap'))")
        branch = {"scripts/helper.py": "def kept():\n    return 2\n",
                  "scripts/test_mine.py": self.CASE.format(name="C", body=body)}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, "could not answer there" in printed), (1, True))

    # GREEN_ON_BASE(characterization): assertion messages do not replace unittest's failure result.
    def test_Given_AFailedAssertionNamingAnAddedSurface_When_TheLaneRuns_Then_ItStaysRed(self):
        # Arrange -- exception-shaped text is only the assertion's message. The unittest result says
        # the case compared and disagreed, so the message must not replace that stronger reading.
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": self.CASE.format(name="D", body="pass")}
        message = "AttributeError: module 'helper' has no attribute 'added'"
        body = "self.assertEqual(helper.kept(), 2, {!r})".format(message)
        branch = {"scripts/helper.py": self.HELPER + "\n\ndef added():\n    return 2\n",
                  "scripts/test_mine.py": self.CASE.format(name="C", body=body)}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert
        self.assertEqual((status, base_red_check.RED_ON_BASE in printed), (0, True))

    def test_Given_ABaseTreeWhoseOwnCasesAllDie_When_TheLaneRuns_Then_ItRefusesToPass(self):
        # Arrange -- nothing in the lane answers there, which is the reading a canary exists to take.
        # Without one each case reads as one the base could not answer, and nothing says they all did.
        broken = "import no_such_module_at_all\n\n\n" + self.CASE.format(name="D", body="pass")
        base = {"scripts/helper.py": self.HELPER,
                "scripts/test_mine.py": self.CASE.format(name="C", body="pass"),
                "scripts/test_theirs.py": broken}
        branch = {"scripts/test_mine.py": self.CASE.format(
            name="C", body="self.assertEqual(helper.added(), 2)")}

        # Act
        status, printed = self.run_lane(base, branch)

        # Assert -- the verdict rides with the status because the script exits 1 for an uncaught
        # exception too, so the status alone does not say the canary is what refused the run.
        self.assertEqual((status, base_red_check.BASE_UNSOUND in printed), (1, True))


class InheritedCaseTests(unittest.TestCase):
    CORPUS = {
        "a/Tests/Editor/BaseTests.cs":
            "namespace N\n{\n    public abstract class BaseTests<T>\n    {\n        [Test]\n"
            "        public void Given_A_When_B_Then_C() => Assert.Pass();\n    }\n}\n",
        "a/Tests/Editor/ButtonTests.cs":
            "namespace N\n{\n    internal sealed class ButtonTests : BaseTests<Button> { }\n}\n",
        "a/Tests/Editor/MiddleTests.cs":
            "namespace N\n{\n    public abstract class MiddleTests : BaseTests<Label> { }\n}\n",
    }

    def test_Given_AConcreteSubclass_When_TheHeirsAreRead_Then_ItIsOneOfThem(self):
        # Act
        heirs = base_red_check.concrete_heirs(self.CORPUS)

        # Assert
        self.assertEqual(heirs.get("BaseTests"), {"N.ButtonTests"})

    def test_Given_ACaseWrittenInAnAbstractFixture_When_ItIsNamed_Then_ItIsNamedForTheHeir(self):
        # Arrange -- the runner reports it under the class that derives, never the one it is in, so a
        # filter built from the file matches nothing and the case reads as one nothing could build.
        path = "a/Tests/Editor/BaseTests.cs"
        cases = base_red_check.cases_in(path, self.CORPUS[path])

        # Act
        resolved = base_red_check.as_the_runner_names_them(
            cases, base_red_check.concrete_heirs(self.CORPUS))

        # Assert
        self.assertEqual([case.name for case in resolved],
                         ["N.ButtonTests.Given_A_When_B_Then_C"])

    def test_Given_ACaseInAConcreteFixture_When_ItIsNamed_Then_ItIsLeftAlone(self):
        # Arrange
        cases = base_red_check.csharp_cases(FIXTURE, "a/Tests/Editor/ProbeTests.cs")

        # Act
        resolved = base_red_check.as_the_runner_names_them(cases, {"ProbeTests": {"N.Other"}})

        # Assert
        self.assertEqual([case.name for case in resolved], [case.name for case in cases])


class ResultsFileTests(unittest.TestCase):
    def test_Given_ADirectoryHoldingNoResults_When_ItIsRead_Then_ItSaysNothingWasWritten(self):
        # Arrange -- an editor that stops on a compile error writes no results at all, which is not
        # the same reading as one that ran and named no case of some fixture.
        holder = tempfile.mkdtemp(prefix="base-red-results-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)

        # Act / Assert
        self.assertEqual(base_red_check.results_from(holder), ({}, False))

    def test_Given_AResultsFileWithOneCase_When_ItIsRead_Then_ItSaysSomethingWasWritten(self):
        # Arrange
        holder = tempfile.mkdtemp(prefix="base-red-results-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        Path(holder, "r.xml").write_text(
            '<test-run><test-case fullname="N.C.Given_A_When_B_Then_C" result="Passed" /></test-run>')

        # Act / Assert
        self.assertEqual(base_red_check.results_from(holder),
                         ({"N.C.Given_A_When_B_Then_C": "Passed"}, True))


class ResultLabelTests(unittest.TestCase):
    """What the base said about a case that stopped on an exception, and which of those disagreed.

    Read from the results file wherever the reading is what was wrong: a case that starts after it
    would agree with the broken script as readily as the fixed one. The two `outcome_for` cases are
    the exception, because how several argument lists collapse into one reading is a question about
    readings rather than about the file they arrived in.
    """

    @staticmethod
    def labelled(label, message, *frames):
        """A reported case carrying `label`, over the frames given innermost first.

        The frame list is the arrangement: which of them names a file of this tree first is the whole
        question, so a shape written without one answers a different question from its name.
        """
        return ('<test-run>'
                '<test-case fullname="N.ProbeTests.Given_A_When_B_Then_C" result="Failed" '
                'label="{}"><failure><message>{}</message>{}</failure>'
                '</test-case></test-run>'.format(
                    label, message,
                    '<stack-trace>{}</stack-trace>'.format("\n".join(frames)) if frames else ""))

    @classmethod
    def threw(cls, *frames):
        """A reported case that stopped on an exception, over the frames given innermost first."""
        return cls.labelled("Error", "System.NullReferenceException : Object reference not set to "
                                     "an instance of an object", *frames)

    # A frame naming no file of this tree, which `THREW_IN_PRODUCTION` puts in front of the deciding
    # one so that arrangement reads past a frame rather than off the top of the trace.
    ENGINE_FRAME = ("  at System.Reflection.MonoField.GetValue (System.Object obj) [0x00080] in "
                    "&lt;c5eeda5e65d44b388e164c6c5cfe0702&gt;:0 ")
    TEST_FRAME = ("  at N.ProbeTests.Given_A_When_B_Then_C () [0x00011] in "
                  "./Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs:41 ")
    STATE_MACHINE_TEST_FRAME = (
        "  at N.ProbeTests+&lt;Given_A_When_B_Then_C&gt;d__4.MoveNext () [0x00011] in "
        "./Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs:41 ")
    SIMILAR_HELPER_FRAME = (
        "  at N.ProbeTests.Given_A_When_B_Then_Cleanup () [0x00011] in "
        "./Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs:70 ")
    PRODUCTION_FRAME = ("  at P.FiberElementPoolReset.Clear (P.Fiber fiber) [0x0000c] in "
                        "./Packages/p/Runtime/A/FiberElementPoolReset.cs:120 ")
    TEARDOWN_FRAME = ("  at N.ProbeTests.TearDown () [0x00001] in "
                      "./Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs:70 ")
    SETUP_FRAME = ("  at N.ProbeTests.SetUp () [0x00001] in "
                   "./Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs:24 ")

    @property
    def THREW(self):
        """The scaffolding shape: a fixture reflecting for state the base has not got, and throwing."""
        return self.threw(self.ENGINE_FRAME, self.TEST_FRAME)

    @property
    def THREW_IN_PRODUCTION(self):
        """The crash-regression shape: the base throwing where the branch's fix stops it."""
        return self.threw(self.ENGINE_FRAME, self.PRODUCTION_FRAME, self.TEST_FRAME)

    @classmethod
    def threw_in_teardown(cls, body, teardown):
        """A case whose teardown threw, over whatever frames its body left in front of the marker.

        One element carries both traces in that order, because a runner records a teardown throw onto
        the case's own result. `ScaffoldingSectionRecordingTests` pins that shape against the runner.
        """
        return cls.threw(*body, "--TearDown", *teardown)

    @classmethod
    def threw_in_setup(cls, setup):
        """A case whose setup threw, which leaves the marker opening the element: no body ran.

        A setup's section stands where the body's would, so a reading that does not cut at it takes
        the setup's reach for the case's own.
        """
        return cls.threw("--SetUp", *setup)

    @classmethod
    def threw_in_action(cls, body, action):
        """A case wrapped by a test action that threw after it. Same shape, one attribute over."""
        return cls.threw(*body, "--AfterTest", *action)

    @classmethod
    def threw_before_the_body(cls, action):
        """A case whose wrapping action threw ahead of it, which leaves the marker opening the trace.

        An action's before half stands where a setup's does. Which sections a runner opens at all is
        `ScaffoldingSectionRecordingTests`'s question rather than this module's.
        """
        return cls.threw("--BeforeTest", *action)

    @staticmethod
    def unlabelled(message, *frames):
        """A reported case carrying no label at all, which is what a failed assertion arrives as.

        The shape a `ResultStateException` out of a scaffold lands in too: its state carries an empty
        label, so nothing is written beside the result, and the trace it replaced the case's own with
        opens with no marker. `ScaffoldingSectionRecordingTests` pins both halves against the runner.
        """
        return ('<test-run>'
                '<test-case fullname="N.ProbeTests.Given_A_When_B_Then_C" result="Failed">'
                '<failure><message>{}</message>{}</failure>'
                '</test-case></test-run>'.format(
                    message,
                    '<stack-trace>{}</stack-trace>'.format("\n".join(frames)) if frames else ""))

    def refused(self, label, *frames):
        """A reported case the runner stopped from outside its body, over `frames` if any."""
        return self.labelled(label, "the runner gave a reason of its own", *frames)

    def read(self, body):
        holder = tempfile.mkdtemp(prefix="base-red-label-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        Path(holder, "r.xml").write_text(body)
        return base_red_check.results_from(holder)[0]

    def probe(self, declaration=None):
        return base_red_check.Case("N.ProbeTests.Given_A_When_B_Then_C",
                                   "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs", 1, 2,
                                   declaration)

    def verdict_for(self, body, declaration=None):
        reported = self.read(body)
        case = self.probe(declaration)
        return base_red_check.decide(case, base_red_check.outcome_for(case.key, reported), True)[0]

    def test_Given_ACaseThatThrewFromItsOwnBody_When_TheResultsAreRead_Then_ItIsNotADisagreement(self):
        # Act
        reported = self.read(self.THREW)

        # Assert
        self.assertEqual(reported["N.ProbeTests.Given_A_When_B_Then_C"], "Error")

    def test_Given_AnUndeclaredCaseThatThrewFromItsOwnBody_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Act
        verdict = self.verdict_for(self.THREW)

        # Assert -- it stopped before it could disagree, so it is evidence of nothing either way.
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseThatThrewFromItsOwnBody_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange -- the harm this whole reading exists to prevent: a correct declaration told to
        # delete itself because the base tree could not answer for the case under it.
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(self.THREW, declared)

        # Assert
        self.assertNotEqual(verdict, base_red_check.DECLARED_STALE)

    # GREEN_ON_BASE(characterization): a crash the branch fixes, counted red before the label was
    # read at all and having to stay red now that it is.
    def test_Given_ACaseThatThrewInsideProductionCode_When_TheResultsAreRead_Then_ItIsADisagreement(self):
        # Act
        reported = self.read(self.THREW_IN_PRODUCTION)

        # Assert
        self.assertEqual(reported["N.ProbeTests.Given_A_When_B_Then_C"], "Failed")

    # GREEN_ON_BASE(characterization): the verdict a crash regression carried before the label was
    # read, which reading it must not take away.
    def test_Given_AnUndeclaredCaseThatThrewInsideProductionCode_When_ItIsDecided_Then_ItIsCountedRed(self):
        # Act
        verdict = self.verdict_for(self.THREW_IN_PRODUCTION)

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    # GREEN_ON_BASE(characterization): the stale-declaration gate over a crash regression, which the
    # base already applied and which reading every throw as a non-answer would have dropped.
    def test_Given_ADeclaredCaseThatThrewInsideProductionCode_When_ItIsDecided_Then_ItIsCalledStale(self):
        # Arrange -- the other direction of the same harm: a declaration is only correct while the
        # base cannot answer, and here the base answered by crashing.
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(self.THREW_IN_PRODUCTION, declared)

        # Assert
        self.assertEqual(verdict, base_red_check.DECLARED_STALE)

    def test_Given_AThrowWhoseTestSideFrameComesFirst_When_ItIsDecided_Then_ItKeepsTheNonAnswer(self):
        # Arrange -- production called back into the fixture and the fixture threw, so both sides
        # are named and the innermost of them is the test side. The first such frame decides, not
        # whichever side occurs anywhere in the trace.
        # Act
        verdict = self.verdict_for(
            self.threw(self.ENGINE_FRAME, self.TEST_FRAME, self.PRODUCTION_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ATeardownThrowAfterABodyThatPassed_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- the body left no trace, so the marker opens the element and every frame under it
        # is the teardown's own reach.
        # Act
        verdict = self.verdict_for(
            self.threw_in_teardown((), (self.PRODUCTION_FRAME, self.TEARDOWN_FRAME)))

        # Assert -- the base agreed with this case; crediting it red hands back the evidence the
        # gate exists to demand.
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseWhoseTeardownThrew_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(
            self.threw_in_teardown((), (self.PRODUCTION_FRAME, self.TEARDOWN_FRAME)), declared)

        # Assert
        self.assertNotEqual(verdict, base_red_check.DECLARED_STALE)

    # GREEN_ON_BASE(characterization): the red a body's own production throw already carries, which
    # cutting the trace at the teardown must not take away.
    def test_Given_ATeardownThrowAfterTheBodyThrewInProduction_When_ItIsDecided_Then_ItIsCountedRed(self):
        # Arrange -- the body's own trace stands in front of the marker and answers for the case. The
        # teardown threw in the teardown method itself, so the section after the marker is test-side
        # first and a reading taken there returns the opposite verdict.
        # Act
        verdict = self.verdict_for(self.threw_in_teardown(
            (self.PRODUCTION_FRAME, self.TEST_FRAME), (self.TEARDOWN_FRAME,)))

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    def test_Given_ASetupThrowThatReachedProduction_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- a fixture that mounts in its setup, on a base whose production code crashes
        # there. The marker opens the element because no body ran, so every frame under it is the
        # setup's reach and the case itself never disagreed with anything.
        # Act
        verdict = self.verdict_for(
            self.threw_in_setup((self.PRODUCTION_FRAME, self.SETUP_FRAME)))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseWhoseSetupThrew_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange -- the harmful half: a correct declaration told to delete itself over a case the
        # base never ran.
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(
            self.threw_in_setup((self.PRODUCTION_FRAME, self.SETUP_FRAME)), declared)

        # Assert
        self.assertNotEqual(verdict, base_red_check.DECLARED_STALE)

    def test_Given_ATestActionThrowAfterABodyThatPassed_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- an action wraps the case the way a teardown follows it, and its section is
        # recorded onto the case just the same.
        # Act
        verdict = self.verdict_for(
            self.threw_in_action((), (self.PRODUCTION_FRAME, self.TEARDOWN_FRAME)))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    # GREEN_ON_BASE(characterization): a cut the script already made and this module measured nothing
    # of, so the reading it pins is the base's as much as this branch's.
    def test_Given_ATestActionThrowAheadOfTheBody_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- the fourth section, and the only one this module measured nothing of: dropping
        # its name from the script reddened no case here. A `[Test, Performance]` case is wrapped by
        # one, `PerformanceAttribute` being an `IOuterUnityTestAction`, so four PlayMode fixtures in
        # this repository carry the section.
        # Act
        verdict = self.verdict_for(
            self.threw_before_the_body((self.PRODUCTION_FRAME, self.SETUP_FRAME)))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    # GREEN_ON_BASE(characterization): a body's own production throw is red on the base already.
    # Naming the scaffolding sections one by one, rather than matching every opener of their shape,
    # must not take that away.
    def test_Given_ABodyThrowWhoseInnerExceptionOpensASection_When_ItIsDecided_Then_ItStaysRed(self):
        # Arrange -- an inner exception's frames are opened by a marker of a section's shape, and the
        # outer throw names no file of this tree, so the only repository frame stands behind that
        # opener. Cutting at any opener rather than at the named ones loses the case's whole reading.
        # Act
        verdict = self.verdict_for(self.threw(
            self.ENGINE_FRAME, "--InvalidTimeZoneException", self.PRODUCTION_FRAME, self.TEST_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    def test_Given_ASetupThrowBeforeATeardownThrow_When_ItIsDecided_Then_TheFirstMarkerCuts(self):
        # Arrange -- both scaffolds threw, so the element carries two sections and no body between
        # them. Cutting at the teardown alone would read the setup's reach as the case's own.
        # Act
        verdict = self.verdict_for(self.threw(
            "--SetUp", self.PRODUCTION_FRAME, self.SETUP_FRAME,
            "--TearDown", self.TEARDOWN_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_AScaffoldThrowCarryingNoLabel_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- Unity's end-of-scope log check raises a `ResultStateException` out of a teardown,
        # which arrives as a plain failure: no label, and a trace the runner replaced whole, so the
        # marker and the body's frames are both gone. The frames left behind name production code, so
        # a reading taken over them credits the base with a disagreement the case never had.
        # Act
        verdict = self.verdict_for(self.unlabelled(
            "TearDown : Unhandled log message: 'a cleanup threw'. Use UnityEngine.TestTools.LogAssert",
            self.PRODUCTION_FRAME, self.TEARDOWN_FRAME))

        # Assert -- the base agreed with this case, and its scaffolding is what failed it.
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseWhoseScaffoldFailedIt_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange -- the harm this reading exists to prevent: a correct declaration told to delete
        # itself over a case whose body the base never disagreed with.
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(self.unlabelled(
            "SetUp : Unhandled log message: 'a mount threw'. Use UnityEngine.TestTools.LogAssert",
            self.SETUP_FRAME),
            declared)

        # Assert
        self.assertNotEqual(verdict, base_red_check.DECLARED_STALE)

    # GREEN_ON_BASE(characterization): the red a body's own disagreement already carries, which
    # reading the message must not widen over.
    def test_Given_AScaffoldThrowAfterTheBodyDisagreed_When_ItIsDecided_Then_ItIsStillCountedRed(self):
        # Arrange -- the other half of the line. A body that disagreed leaves its own message, and
        # the section is appended behind it, so the head of the message is what separates a case its
        # scaffolding decided from one that disagreed and then had a scaffold throw as well.
        # Act
        verdict = self.verdict_for(self.unlabelled(
            "Expected: (1, a)\nTearDown : Unhandled log message: 'and then a cleanup threw'"))

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    def test_Given_BodyAssertionsBeginningWithEachSectionName_When_Decided_Then_TheyStayRed(self):
        # Arrange
        sections = base_red_check.SCAFFOLD_SECTIONS

        # Act
        verdicts = [self.verdict_for(self.unlabelled(
            "{} : the body expected this text".format(section), self.TEST_FRAME))
                    for section in sections]

        # Assert
        self.assertEqual(verdicts, [base_red_check.RED_ON_BASE] * len(sections))

    # GREEN_ON_BASE(characterization): a state-machine frame still identifies the body on the base.
    def test_Given_AStateMachineBodyAssertionBeginningWithASectionName_When_Decided_Then_ItStaysRed(self):
        # Arrange
        message = "TearDown : the coroutine body expected this text"

        # Act
        verdict = self.verdict_for(self.unlabelled(message, self.STATE_MACHINE_TEST_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    def test_Given_AScaffoldTraceWhoseHelperExtendsTheCaseName_When_Decided_Then_ItStaysAScaffold(self):
        # Arrange
        message = "TearDown : the cleanup helper threw"

        # Act
        verdict = self.verdict_for(self.unlabelled(message, self.SIMILAR_HELPER_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_AThrowNamingNoFileOfThisTree_When_ItIsDecided_Then_ItKeepsTheNonAnswer(self):
        # Arrange -- a throw whose trace places it on neither side. The reading falls to the side
        # that fails no run, since a verdict that does is not one to take from silence.
        # Act
        verdict = self.verdict_for(self.threw(self.ENGINE_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ACaseTheRunnerWouldNotRun_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- a non-runnable case never reached its body, so nothing in it disagreed.
        # Act
        verdict = self.verdict_for(self.refused("Invalid"))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ADeclaredCaseTheRunnerWouldNotRun_When_ItIsDecided_Then_ItIsNotCalledStale(self):
        # Arrange
        declared = base_red_check.Declaration(
            "characterization", "the keyed-reorder order this refactor must not change")

        # Act
        verdict = self.verdict_for(self.refused("Invalid"), declared)

        # Assert
        self.assertNotEqual(verdict, base_red_check.DECLARED_STALE)

    def test_Given_ACaseWhoseRunWasCancelled_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- a cancelled run stopped the case from outside it, which says nothing about the
        # behaviour under it either.
        # Act
        verdict = self.verdict_for(self.refused("Cancelled"))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    def test_Given_ANonRunnableCaseWhoseTraceNamesProduction_When_ItIsDecided_Then_ItIsNotCountedRed(self):
        # Arrange -- only a throw is read by where it came from. A case the runner refused never
        # entered its body, so a production frame in whatever trace it carries is not the base
        # disagreeing, and the frames must not be consulted for it at all.
        # Act
        verdict = self.verdict_for(
            self.refused("Invalid", self.ENGINE_FRAME, self.PRODUCTION_FRAME, self.TEST_FRAME))

        # Assert
        self.assertEqual(verdict, base_red_check.COULD_NOT_ANSWER)

    # GREEN_ON_BASE(characterization): the reading a disagreement already had, which the label must
    # not widen over.
    def test_Given_ACaseWhoseAssertionDisagreed_When_ItIsDecided_Then_ItIsStillCountedRed(self):
        # Arrange -- the other half of the line, kept beside it: reading the label must not turn
        # every failure into a non-answer, which would leave nothing able to read as red at all.
        # Act
        verdict = self.verdict_for(
            '<test-run><test-case fullname="N.ProbeTests.Given_A_When_B_Then_C" result="Failed">'
            '<failure><message>Expected: (1, a)</message></failure></test-case></test-run>')

        # Assert
        self.assertEqual(verdict, base_red_check.RED_ON_BASE)

    # GREEN_ON_BASE(characterization): a passing case's reading, which no label may take part in.
    def test_Given_APassingCaseCarryingALabel_When_TheResultsAreRead_Then_TheLabelDoesNotDisplaceIt(self):
        # Arrange -- `passed on the base` is the reading that fails a run, and no label names it, so
        # a label read over a passing case could only ever lose that verdict.
        # Act
        reported = self.read(
            '<test-run><test-case fullname="N.ProbeTests.Given_A_When_B_Then_C" result="Passed" '
            'label="Error" /></test-run>')

        # Assert
        self.assertEqual(reported["N.ProbeTests.Given_A_When_B_Then_C"], "Passed")

    def test_Given_OneArgumentListThatDisagreedAndOneThatThrew_When_ItIsLookedUp_Then_TheDisagreementWins(self):
        # Arrange -- dict order decided this before, and the two sides are not symmetric: over a
        # declared case one of them is a failing verdict and the other is not.
        reported = {"N.C.Given_A_When_B_Then_C(1)": "Error", "N.C.Given_A_When_B_Then_C(2)": "Failed"}

        # Act / Assert
        self.assertEqual(base_red_check.outcome_for("N.C.Given_A_When_B_Then_C", reported), "Failed")

    # GREEN_ON_BASE(characterization): the answer where no list reached a verdict, which the ranking
    # beside it must leave alone.
    def test_Given_EveryArgumentListStoppedBeforeItCould_When_ItIsLookedUp_Then_NoneIsCalledADisagreement(self):
        # Arrange -- the fallback still has to answer when nothing among them ran to a verdict.
        reported = {"N.C.Given_A_When_B_Then_C(1)": "Error",
                    "N.C.Given_A_When_B_Then_C(2)": "Inconclusive"}

        # Act / Assert
        self.assertEqual(base_red_check.outcome_for("N.C.Given_A_When_B_Then_C", reported), "Error")


class PlatformInstrumentTests(unittest.TestCase):
    def test_Given_ACanaryThatPassed_When_ThePlatformIsRead_Then_ItIsNotWithdrawn(self):
        # Arrange -- one passing case is the bar: the question is whether anything built and ran.
        reported = {"N.CanaryTests.Given_A_When_B_Then_C": "Passed",
                    "N.CanaryTests.Given_D_When_E_Then_F": "Failed"}

        # Act / Assert
        self.assertEqual(base_red_check.unsound_platforms({"EditMode": ["N.CanaryTests"]}, reported),
                         {})

    def test_Given_NoCanaryReportedAtAll_When_ThePlatformIsRead_Then_ItIsWithdrawn(self):
        # Arrange -- a tree that built nothing reports every case as uncompilable, and uncompilable
        # is not a failure here, so without this the run comes back green having measured nothing.
        # Act
        withdrawn = base_red_check.unsound_platforms({"EditMode": ["N.CanaryTests"]}, {})

        # Assert
        self.assertIn("EditMode", withdrawn)

    def test_Given_APythonCanaryThatDidNotPass_When_TheLaneIsWithdrawn_Then_ItIsNamedReadably(self):
        # Arrange -- a Python fixture key is a path and a class, and the C# spelling of this message
        # cuts at the last dot, which lands inside the file extension.
        reported = {"scripts/x/test_theirs.py::T.test_Given_A_When_B_Then_C": "Failed"}

        # Act
        withdrawn = base_red_check.unsound_platforms(
            {base_red_check.PYTHON_LANE: ["scripts/x/test_theirs.py::T"]}, reported)

        # Assert
        self.assertEqual(withdrawn[base_red_check.PYTHON_LANE], "none of T passed there")

    def test_Given_APlatformTheBaseTreeOffersNoCanary_When_ItIsRead_Then_ItIsWithdrawn(self):
        # Arrange -- without a base-owned case, no result can distinguish a branch test that could not
        # compile from a run that never built its fixture.
        # Act / Assert
        self.assertEqual(base_red_check.unsound_platforms({"EditMode": []}, {}),
                         {"EditMode": "no base-tree canary was available there"})


class InstrumentTests(unittest.TestCase):
    def control(self, name, result):
        case = base_red_check.Case(name, "a/Tests/Editor/CTests.cs", 1, 2)
        return case, {name: result}

    def test_Given_AControlCaseRedOnTheBase_When_TheInstrumentIsRead_Then_ItsFixtureIsWithdrawn(self):
        # Arrange
        case, reported = self.control("N.C.Given_A_When_B_Then_C", "Failed")

        # Act / Assert
        self.assertEqual(base_red_check.unsound_fixtures([case], reported).get("N.C"),
                         "Given_A_When_B_Then_C is failed there")

    def test_Given_EveryControlCaseGreen_When_TheInstrumentIsRead_Then_NoFixtureIsWithdrawn(self):
        # Arrange
        case, reported = self.control("N.C.Given_A_When_B_Then_C", "Passed")

        # Act / Assert
        self.assertEqual(base_red_check.unsound_fixtures([case], reported), {})


class WithdrawalTests(unittest.TestCase):
    """Which carried file goes back when a round of the base run reported nothing of some fixture."""

    HELPER = "Packages/p/TestUtilities/PanelTestBase.cs"
    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"

    def log(self, blamed):
        return "/tmp/base/{}(12,34): error CS0117: 'V' has no definition for 'Portal'\n".format(blamed)

    def test_Given_TheLogBlamesACarriedHelper_When_TheOffenderIsChosen_Then_ItIsTheHelper(self):
        # Arrange -- the helper holds no case, so a choice made over case-bearing files alone cannot
        # name it, and every fixture in its assembly is silent behind it.
        # Act
        offender = base_red_check.next_to_withdraw(
            self.log(self.HELPER), carry=[self.HELPER, self.FIXTURE], withdrawn=set(),
            silent=[self.FIXTURE])

        # Assert
        self.assertEqual(offender, self.HELPER)

    def test_Given_TheBlamedFileIsAlreadyWithdrawn_When_TheOffenderIsChosen_Then_ItIsNotChosenAgain(self):
        # Arrange -- choosing it twice is a round that changes nothing, and there are only so many.
        # Act
        offender = base_red_check.next_to_withdraw(
            self.log(self.HELPER), carry=[self.HELPER, self.FIXTURE], withdrawn={self.HELPER},
            silent=[self.FIXTURE])

        # Assert
        self.assertEqual(offender, self.FIXTURE)


class UnmeasuredRunTests(unittest.TestCase):
    """A run that produced no results file at all, which is what a failed base run leaves behind."""

    def cases(self):
        return [base_red_check.Case("N.C.Given_A_When_B_Then_C",
                                    "Packages/p/Runtime/A/Tests/Editor/CTests.cs", 1, 2)]

    def plan(self):
        """What `--emit` writes, built by the script rather than transcribed beside it.

        A literal here would be a second copy of the entry shape, free to go on carrying a field
        `as_plan` had stopped writing -- which is this seam's own failure mode, and transcribing it
        is why the round trip went uncovered. Built this way, a field the emitting half drops
        reaches the deciding half's own tests.
        """
        probe = base_red_check.Case("N.ProbeTests.Given_A_When_B_Then_C",
                                    "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs", 1, 2)
        return base_red_check.as_plan("0" * 40, [probe], [], {}, {"EditMode": ["N.CanaryTests"]})

    def verdict_over(self, *results):
        """The exit status of the --verdict lane over a directory holding `results`."""
        holder = tempfile.mkdtemp(prefix="base-red-verdict-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        plan = Path(holder, "plan.json")
        plan.write_text(json.dumps(self.plan()))
        written = Path(holder, "results")
        written.mkdir()
        for index, body in enumerate(results):
            Path(written, "r{}.xml".format(index)).write_text(body)
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--verdict", str(plan), "--results", str(written)],
            capture_output=True, text=True).returncode

    def test_Given_ABaseRunThatLeftNoResultsFile_When_TheVerdictLaneRuns_Then_ItRefuses(self):
        # Arrange -- the ordinary shape of a base run that failed. game-ci's runner is continue-on-
        # error, so a licence failure, an editor crash, an OOM and a timeout all reach this step with
        # an empty artifacts directory, and every case then reads as one the base could not build.
        # Act
        status = self.verdict_over()

        # Assert
        self.assertEqual(status, 1)

    def test_Given_ABaseRunWhoseCanaryPassed_When_TheCaseIsAbsent_Then_ItIsStillEvidenceNotAFailure(self):
        # Arrange -- the counterpart, so the refusal above is not the lane refusing everything: a tree
        # that demonstrably answers, and a case it named nothing of, is the reading this exists to take.
        # Act
        status = self.verdict_over(
            '<test-run><test-case fullname="N.CanaryTests.Given_X_When_Y_Then_Z" result="Passed" />'
            '</test-run>')

        # Assert
        self.assertEqual(status, 0)

    def test_Given_ARunThatWroteNothingAndNoCanary_When_TheVerdictsAreTaken_Then_ItStillFails(self):
        # Arrange -- the canary list is empty when the base tree offers no fixture of its own on this
        # platform, and an empty one must not read as permission to believe an unmeasured run.
        cases = self.cases()

        # Act
        offenders = base_red_check.report(cases, [], {}, {"EditMode": []}, wrote=False)

        # Assert
        self.assertEqual([case.verdict for case in offenders], [base_red_check.BASE_UNSOUND])

    def printed(self, reported, canaries, wrote):
        held = io.StringIO()
        with contextlib.redirect_stdout(held):
            base_red_check.report(self.cases(), [], reported, canaries, wrote)
        return held.getvalue()

    def test_Given_BothKindsOfOffender_When_TheRemedyIsPrinted_Then_OnlyTheAnsweredOneIsOfferedIt(self):
        # Arrange -- a declaration answers for a case the base measured. Over one it never measured
        # it sends the author to sharpen a case that may be perfectly sharp, since a neighbour's own
        # Assume is enough to withdraw the fixture. Both halves in one comparison, because printing
        # it for neither is the other way of getting this wrong.
        marker = "GREEN_ON_BASE"

        # Act
        unmeasured = self.printed({}, {"EditMode": []}, wrote=False)
        answered = self.printed({"N.C.Given_A_When_B_Then_C": "Passed"}, {}, wrote=True)

        # Assert
        self.assertEqual((marker in unmeasured, marker in answered), (False, True))

    def test_Given_ARunThatWroteAnEmptyResultsFile_When_TheVerdictsAreTaken_Then_TheCanaryFailsIt(self):
        # Arrange -- the other half of the same bar, kept beside it: a file that parses to no case is
        # a run that happened and built nothing, and the canary is what reads that.
        cases = self.cases()

        # Act
        offenders = base_red_check.report(cases, [], {}, {"EditMode": ["N.CanaryTests"]}, wrote=True)

        # Assert
        self.assertEqual([case.verdict for case in offenders], [base_red_check.BASE_UNSOUND])

    def test_Given_ARunWithNoAvailableCanary_When_TheVerdictsAreTaken_Then_TheLaneFailsClosed(self):
        # Arrange
        python_cases = [base_red_check.Case(
            "test_probe.ProbeTests.test_Given_A_When_B_Then_C", "scripts/test_probe.py", 1, 2)]
        csharp_cases = self.cases()

        # Act
        python_offenders = base_red_check.report(
            python_cases, [], {}, {base_red_check.PYTHON_LANE: []}, wrote=True)
        csharp_offenders = base_red_check.report(
            csharp_cases, [], {}, {"EditMode": []}, wrote=True)

        # Assert
        self.assertEqual(([case.verdict for case in python_offenders],
                          [case.verdict for case in csharp_offenders]),
                         ([base_red_check.BASE_UNSOUND], [base_red_check.BASE_UNSOUND]))


class BranchReadingTests(unittest.TestCase):
    """`collect` and `deleted_files` over a real two-commit repository rather than an invented diff."""

    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"

    def source(self, declaration="", first="Assert.Pass()"):
        return ("namespace N\n{\n    class ProbeTests\n    {\n        [SetUp]\n"
                "        public void SetUp()\n        {\n            _count = 0;\n        }\n\n"
                + declaration +
                "        [Test]\n        public void Given_A_When_B_Then_C() => " + first + ";\n\n"
                "        [Test]\n        public void Given_D_When_E_Then_F() => Assert.Pass();\n"
                "    }\n}\n")

    def repository(self, before, after, rename_to=None):
        """A repository whose HEAD~1 holds `before` and whose HEAD holds `after`."""
        holder = tempfile.mkdtemp(prefix="base-red-branch-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        run = lambda *arguments: subprocess.run(["git", "-C", holder, *arguments], check=True,
                                                capture_output=True, text=True)
        run("init", "-q")
        run("config", "user.email", "probe@example.com")
        run("config", "user.name", "probe")
        path = root / self.FIXTURE
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(before)
        run("add", self.FIXTURE)
        run("commit", "-qm", "base")
        if rename_to:
            (root / rename_to).parent.mkdir(parents=True, exist_ok=True)
            path.unlink()
            (root / rename_to).write_text(after)
            run("add", self.FIXTURE, rename_to)
        else:
            path.write_text(after)
            run("add", self.FIXTURE)
        run("commit", "-qm", "branch")
        return root

    def test_Given_ABranchThatChangedASetUp_When_ItIsRead_Then_ItsOtherCasesAreNotControls(self):
        # Arrange -- the untouched case is not the base's own text any more: what it shares with the
        # case beside it moved. Reading the tree by it converts the sharpest red-on-base evidence
        # there is -- a tightened assertion in shared material -- into "the base cannot answer".
        root = self.repository(self.source(),
                               self.source(first="Assert.Fail()").replace("_count = 0", "_count = 1"))

        # Act
        _, _, control, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertEqual(control, [])

    def test_Given_ABranchThatChangedOneCaseOnly_When_ItIsRead_Then_TheOtherIsAControl(self):
        # Arrange -- the counterpart, so the case above is not passing for want of any control at all.
        root = self.repository(self.source(), self.source(first="Assert.Fail()"))

        # Act
        _, _, control, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertEqual([case.name for case in control], ["N.ProbeTests.Given_D_When_E_Then_F"])

    def test_Given_ADeclarationTheBaseAlreadyHeld_When_TheBranchEditsTheCase_Then_ItIsNotThisBranchs(self):
        # Arrange -- nothing removes a declaration at merge, so one written for an earlier change sits
        # over the case for good and silences every later branch that edits it.
        declared = "        // " + MARKER + "(refactor): a pure rename of the applier.\n"
        root = self.repository(self.source(declared), self.source(declared, first="Assert.Fail()"))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertFalse(case_named(cases, "Given_A_When_B_Then_C").declaration.written_here)

    def test_Given_ADeclarationTheBranchWrote_When_TheCaseIsRead_Then_ItIsThisBranchs(self):
        # Arrange -- the counterpart, so the case above is not passing for want of reading any
        # declaration at all.
        declared = "        // " + MARKER + "(refactor): a pure rename of the applier.\n"
        root = self.repository(self.source(), self.source(declared))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertTrue(case_named(cases, "Given_A_When_B_Then_C").declaration.written_here)

    def test_Given_ABranchThatRewroteTheWrappedHalfOfAReason_When_ItIsRead_Then_TheDeclarationIsIts(self):
        # Arrange -- the reason spans both comment lines, and this branch rewrote the second. Asking
        # only whether the marker's own line moved answers "the base's own" for a declaration this
        # branch wrote half of, and the remedy it then prints is to restate what was just restated.
        before = ("        // " + MARKER + "(refactor): a pure rename\n"
                  "        // of the applier the base already carries.\n")
        after = ("        // " + MARKER + "(refactor): a pure rename\n"
                 "        // of the applier this branch performs.\n")
        root = self.repository(self.source(before), self.source(after))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertTrue(case_named(cases, "Given_A_When_B_Then_C").declaration.written_here)

    def test_Given_ARenamedAndEditedTestFile_When_TheDroppedFilesAreRead_Then_TheOldPathIsAmongThem(self):
        # Arrange -- git pairs the two halves and reports R, so neither the added nor the deleted
        # filter names anything and the base tree ends up holding both copies of one fixture class.
        moved = "Packages/p/Runtime/B/Tests/Editor/ProbeTests.cs"
        root = self.repository(self.source(), self.source(first="Assert.Fail()"), rename_to=moved)

        # Act
        dropped = base_red_check.deleted_files(root, "HEAD~1")

        # Assert
        self.assertEqual(dropped, [self.FIXTURE])


class RepositoryTests(unittest.TestCase):
    """The reader against every test file this repository has, rather than against invented ones."""

    def test_Given_EveryCSharpFixtureHere_When_ItIsRead_Then_EachYieldsACase(self):
        # Arrange
        files = csharp_test_files()

        # Act
        silent = [relative for relative, text in files if not base_red_check.csharp_cases(text)]

        # Assert -- the count rides along because an empty corpus reports nothing silent.
        self.assertEqual((len(files) > 100, silent), (True, []))

    def test_Given_EveryFixtureUnityCompiles_When_ItIsClassified_Then_EachIsCarriedOntoTheBase(self):
        # Arrange
        fixtures = imported_csharp_fixtures()

        # Act
        production = [relative for relative in fixtures
                      if not base_red_check.is_test_side(relative)]

        # Assert -- a fixture read as production is carried onto no base tree and its cases are in
        # scope of nothing, so a branch that rewrites one is measured against nothing and passes.
        # The count rides along because an empty corpus reports nothing misread.
        self.assertEqual((len(fixtures) > 100, production), (True, []))

    def test_Given_EveryCSharpFixtureHere_When_ItIsRead_Then_NoTwoCaseSpansOverlap(self):
        # Act
        overlapping = []
        for relative, text in csharp_test_files():
            spans = sorted((case.first_line, case.last_line)
                           for case in base_red_check.csharp_cases(text))
            for earlier, later in zip(spans, spans[1:]):
                if earlier[1] >= later[0]:
                    overlapping.append("{}: {} runs into {}".format(relative, earlier, later))

        # Assert -- an overlap attributes one author's line to another author's case.
        self.assertEqual(overlapping, [])

    def test_Given_EveryCSharpFixtureHere_When_ItIsRead_Then_NoCaseEndsPastTheFile(self):
        # Act
        past = []
        for relative, text in csharp_test_files():
            length = len(text.splitlines())
            past += ["{}: {}".format(relative, case.name)
                     for case in base_red_check.csharp_cases(text) if case.last_line > length]

        # Assert
        self.assertEqual(past, [])

    def test_Given_EveryPythonModuleHere_When_ItIsRead_Then_EachYieldsACase(self):
        # Arrange
        files = python_test_files()

        # Act
        silent = [relative for relative, text in files if not base_red_check.python_cases(text)]

        # Assert
        self.assertEqual((len(files) > 3, silent), (True, []))

    def test_Given_EveryDeclarationHere_When_ItIsRead_Then_NoneIsMalformed(self):
        # Act
        complaints = []
        for relative, text in csharp_test_files() + python_test_files():
            for case in base_red_check.cases_in(relative, text):
                if case.declaration and case.declaration.complaint:
                    complaints.append("{}: {}".format(case.name, case.declaration.complaint))

        # Assert -- a malformed declaration reads as an opt-out and is not one.
        self.assertEqual(complaints, [])

    def test_Given_EveryCaseHere_When_ItIsNamedAsTheRunnerWould_Then_AConcreteClassCarriesIt(self):
        # Arrange -- a name no concrete class answers to is one the base run reports nothing for,
        # which reads as a case the base could not build and passes. Silence, not a failure.
        # Read over the cases as written rather than over what the naming returned: that output holds
        # only names whose class segment resolved, so a case it dropped and a case it misnamed are
        # both outside anything computed from it, and both are exactly what this is looking for.
        corpus = dict(csharp_test_files())
        heirs = base_red_check.concrete_heirs(corpus)

        # Act
        homeless = sorted(set(unanswered_names(corpus, heirs)))

        # Assert
        self.assertEqual(homeless, [])

    def test_Given_EveryDeclarationHere_When_ItIsCounted_Then_NoneIsOrphaned(self):
        # Arrange -- one written over a helper, or too far above a case, silences nothing and looks
        # like it does. Per file rather than over the tree: a total cancels a file that writes one
        # nothing carries against a file that carries one nothing wrote, and reports two as none.
        # Act
        uneven = [(relative, base_red_check.orphaned_declarations(relative, text))
                  for relative, text in csharp_test_files() + python_test_files()]

        # Assert
        self.assertEqual([name for name, (wrote, carries) in uneven if wrote != carries], [])


if __name__ == "__main__":
    unittest.main(verbosity=2)

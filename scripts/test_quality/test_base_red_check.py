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
import pathlib
import shutil
import subprocess
import sys
import tempfile
import threading
import traceback
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


def two_commit_repo(test, base, branch):
    """(a repository whose first commit is `base` and second is `branch`, the merge base's sha).

    A `branch` entry whose text is None is a file the second commit deletes, which is what a caller
    reading `deleted_files` needs and what writing-only fixtures cannot produce.
    """
    holder = tempfile.mkdtemp(prefix="base-red-tree-")
    test.addCleanup(shutil.rmtree, holder, ignore_errors=True)
    root = Path(holder)
    run = lambda *arguments: subprocess.run(["git", "-C", holder, *arguments], check=True,
                                            capture_output=True, text=True)
    run("init", "-q")
    run("config", "user.email", "probe@example.com")
    run("config", "user.name", "probe")
    for label, files in (("base", base), ("branch", branch)):
        for relative, text in files.items():
            path = root / relative
            if text is None:
                path.unlink(missing_ok=True)
                continue
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text)
        run("add", "-A")
        run("commit", "-qm", label)
    return root, run("rev-parse", "HEAD^").stdout.strip()


def worktree_beside(test, root):
    """Where a base tree of `root` may be built, removed again however the test leaves."""
    tree = Path(str(root) + "-tree")
    test.addCleanup(subprocess.run, ["git", "-C", str(root), "worktree", "remove", "--force",
                                     str(tree)], capture_output=True)
    return tree


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

    def test_Given_AConstructionReasonNamingNothing_When_TheDeclarationIsChecked_Then_ItIsComplainedAbout(self):
        # Arrange — enough words, a known category, and no perturbation a reader could apply.
        reason = "both sides here are the base's own content"

        # Act
        complaint = base_red_check.Declaration("construction", reason).complaint

        # Assert
        self.assertIn("perturbation", complaint)

    def test_Given_AConstructionReasonNamingOne_When_TheDeclarationIsChecked_Then_ThereIsNoComplaint(self):
        # Act / Assert
        self.assertIsNone(base_red_check.Declaration(
            "construction", "misspell `WalkRoot` in the guide and this is the case that reddens").complaint)

    def test_Given_AConstructionReasonNamingOneOnAContinuationLine_When_Checked_Then_ThereIsNoComplaint(self):
        # Arrange — the first line is the claim the word floor reads; the perturbation may be named
        # anywhere in the folded reason, which is what the rule is asked of.
        first = "the guide and the script agree because both are the base's"
        folded = first + " own content; drop `--lane` from the command and this reddens"

        # Act / Assert
        self.assertIsNone(
            base_red_check.Declaration("construction", folded, claim=first).complaint)

    # GREEN_ON_BASE(characterization): the base already lets a backtick-free reason through, and this
    # says the new rule did not widen to it. A rule written on the category set rather than on one
    # member passes the three cases above and fails here.
    def test_Given_ACharacterizationReasonNamingNothing_When_Checked_Then_ThereIsNoComplaint(self):
        # Arrange — the control: the rule is on `construction` alone, because 263 of the 286
        # declarations that existed when it was added carry no backtick.
        # Act / Assert
        self.assertIsNone(base_red_check.Declaration(
            "characterization", "the ordering the base already commits to").complaint)


class NeighbourDuringTheRunTests(unittest.TestCase):
    """A second editor arriving after the one quiet check is recorded rather than missed.

    The wait at the top of the lane covers the instant it happens in, and the lane then runs
    platforms x rounds without looking again. A neighbour arriving after it can redden a
    timing-sensitive case on the base tree, and `red on the base` is exactly the evidence this
    harness is asked for -- so contention there produces a confident wrong verdict rather than a
    confusing one. `neuter_check.run_suite` already samples for its run's whole life.
    """

    @staticmethod
    def reading():
        """What one run reports, as a flat tuple -- a bare float is a run that reported one thing."""
        result = base_red_check.run_unity(
            sys.executable, ".", "EditMode", ["X"], Path("/dev/null"), Path("/dev/null"), 30)
        return tuple(result) if isinstance(result, tuple) else (result,)

    def test_Given_ARunThatMetNoNeighbour_When_ItIsRead_Then_ThePeakIsZero(self):
        # Arrange -- a command that exits at once, so the loop samples and finds only this run.
        reading = self.reading()

        # Act / Assert -- the wall clock rides along, since a peak of zero from a run that never
        # started says nothing.
        self.assertEqual((len(reading), reading[-1], reading[0] >= 0), (2, 0, True))

    def test_Given_TheRunner_When_ItReturns_Then_ItCarriesBothReadings(self):
        # Arrange -- read as a shape rather than unpacked, so a tree reporting one thing fails here
        # instead of raising: a case that raises carries no reading either way.
        # Act / Assert
        self.assertEqual(len(self.reading()), 2)


class ExhaustedLoopTests(unittest.TestCase):
    """What the withdrawing loop says when it runs out of rounds having compiled nothing.

    The generic line beside this one is what a single round writes, and its remedy is to run the loop
    -- advice a reader who ran the loop has already taken. What separates the two readings is the
    loop's own history and the flag that changes it, since the budget is what binds. Whether the budget
    is what ended the run is the caller's to know; these are about what the message carries.
    """

    def test_Given_NoRoundRan_When_TheReasonIsBuilt_Then_ItSaysNothing(self):
        # Arrange — the Python-only lane starts no editor, so there is no loop to report on.
        # Act / Assert
        self.assertEqual(base_red_check.exhausted_reason(0, set(), 12), "")

    def test_Given_RoundsThatCompiledNothing_When_TheReasonIsBuilt_Then_ItCountsThem(self):
        # Act
        said = base_red_check.exhausted_reason(8, {"a.cs", "b.cs"}, 24)

        # Assert
        self.assertIn("8 round(s) compiled nothing", said)

    def test_Given_FilesPutBack_When_TheReasonIsBuilt_Then_ItNamesThem(self):
        # Arrange — the withdrawn set is the evidence, so a count without the names is a claim the
        # reader cannot check.
        # Act
        said = base_red_check.exhausted_reason(3, {"one.cs", "two.cs"}, 9)

        # Assert
        self.assertEqual(("one.cs" in said, "two.cs" in said), (True, True))

    def test_Given_MoreFilesThanItPrints_When_TheReasonIsBuilt_Then_ItSaysHowManyItLeftOut(self):
        # Arrange — eight withdrawn, six printed.
        # Act
        said = base_red_check.exhausted_reason(8, {"f%d.cs" % n for n in range(8)}, 30)

        # Assert
        self.assertIn("and 2 more", said)

    def test_Given_AFileWithdrawnAndThenTakenOut_When_TheReasonIsBuilt_Then_ItIsCountedOnceAndSaidToBeOut(self):
        # Arrange -- the run withdrew both, and took one of those out afterwards. A count that
        # subtracted the second would say it withdrew one, against its own transcript, which named
        # both as it withdrew them.
        # Act
        said = base_red_check.exhausted_reason(8, {"a.cs", "b.cs"}, 24, removed={"b.cs"})

        # Assert
        self.assertEqual(("withdrawn 2 of the 24 carried file(s)" in said,
                          "1 of them taken out of the tree" in said, "b.cs" in said),
                         (True, True, True))

    def test_Given_MoreCarriedFilesThanTheMultiplierReaches_When_TheReasonIsBuilt_Then_TheFloorClearsTheCarriedCount(self):
        # Arrange -- a run whose rounds the log explains nothing about withdraws one silent file each,
        # so it spends a round per file holding a live case and then needs one more to ask the tree
        # it has emptied. Advice equal to the carried count sends the reader back for the same
        # message; the count is the bound on those files, so one past it is the advice that reaches.
        # Act
        said = base_red_check.exhausted_reason(1, {"a.cs"}, 5)

        # Assert
        self.assertIn("--max-rounds 6", said)

    # GREEN_ON_BASE(characterization): the multiplier is the base's own, four times what was spent.
    # This branch halved it once with nothing going red, so the case is what holds it there.
    def test_Given_ARunThatSpentMoreThanTheCarriedCount_When_TheReasonIsBuilt_Then_TheMultiplierCarriesTheAdvice(self):
        # Arrange -- past the floor the advice is the multiplier's, and how deep a round's put-backs
        # go is what it guesses at; the branch halved it once with nothing going red.
        # Act
        said = base_red_check.exhausted_reason(4, {"a.cs"}, 3)

        # Assert
        self.assertIn("--max-rounds 16", said)

    def test_Given_RoundsThatCompiledNothing_When_TheReasonIsBuilt_Then_ItNamesTheFlagThatRaisesThem(self):
        # Arrange — the budget is what binds, so the message names the flag that raises it.
        # Act / Assert
        self.assertIn("--max-rounds", base_red_check.exhausted_reason(8, {"a.cs"}, 24))



class CarriedAssemblyTests(unittest.TestCase):
    """A carried file whose assembly the base has not got.

    Unity gives a source to the nearest asmdef at or above it, and an asmdef is not a `.cs` so it is
    never carried. A branch adding a test assembly therefore lands its files on the base under whatever
    asmdef is next up -- the runtime one, for a fixture under `Runtime/` -- and an `AssemblyInfo.cs`
    there is a duplicate attribute that takes the whole compile down. The name comparison beside this
    cannot see it: such a file spells no type at all.
    """

    def tree(self, *relatives):
        """A directory holding each named path as an empty file."""
        root = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, str(root), True)
        for relative in relatives:
            path = root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text("")
        return root

    def test_Given_ACarriedFileWhoseAsmdefTheBaseHasNot_When_Asked_Then_ItNamesThatAsmdef(self):
        # Arrange
        branch = self.tree("A/Tests/Editor/Own.asmdef", "A/Tests/Editor/AssemblyInfo.cs")
        base = self.tree("A/Velvet.asmdef")

        # Act
        owner = base_red_check.assembly_absent_on_base(
            branch, base, "A/Tests/Editor/AssemblyInfo.cs")

        # Assert
        self.assertEqual(owner, "A/Tests/Editor/Own.asmdef")

    def test_Given_ACarriedFileWhoseAsmdefTheBaseHas_When_Asked_Then_ItNamesNothing(self):
        # Arrange — the control: an ordinary change to a fixture in an assembly both trees hold.
        branch = self.tree("A/Tests/Editor/Own.asmdef", "A/Tests/Editor/SomeTests.cs")
        base = self.tree("A/Tests/Editor/Own.asmdef")

        # Act / Assert
        self.assertIsNone(base_red_check.assembly_absent_on_base(
            branch, base, "A/Tests/Editor/SomeTests.cs"))

    def test_Given_AFileWhoseNearestAsmdefIsAboveIt_When_Asked_Then_ItReadsThatOne(self):
        # Arrange — no asmdef beside the file, so the search walks up, which is the rule Unity applies.
        branch = self.tree("A/Own.asmdef", "A/Tests/Editor/SomeTests.cs")
        base = self.tree("Elsewhere/Other.asmdef")

        # Act
        owner = base_red_check.assembly_absent_on_base(branch, base, "A/Tests/Editor/SomeTests.cs")

        # Assert
        self.assertEqual(owner, "A/Own.asmdef")

    def test_Given_AFileUnderNoAsmdefAtAll_When_Asked_Then_ItNamesNothing(self):
        # Arrange — nothing to be absent, so nothing to withdraw for.
        branch = self.tree("A/Loose.cs")
        base = self.tree("A/Loose.cs")

        # Act / Assert
        self.assertIsNone(base_red_check.assembly_absent_on_base(branch, base, "A/Loose.cs"))


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

    def test_Given_AnAnalyzerErrorRatherThanACompilerOne_When_TheLogIsRead_Then_TheSourceIsBlamed(self):
        # Arrange -- the analyzers under Generators~ report at error severity, and a build they stop
        # writes no results file either.
        log = "Packages/x/Runtime/A/Tests/Editor/BTests.cs(9,5): error VEL501: a hook outside a component\n"

        # Act / Assert
        self.assertEqual(base_red_check.compile_error_files(log),
                         ["Packages/x/Runtime/A/Tests/Editor/BTests.cs"])


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


class PythonNamedSurfaceTests(unittest.TestCase):
    """A name only the branch binds, in the spellings a run says a module has not got one in.

    Three spellings say it here: the AttributeError of an attribute read, the ImportError of
    `from module import name`, and what `mock.patch` raises for a patch by name. The module any of
    them names may be one a `sys.path.insert` put on the path rather than a sibling of the case.
    """

    SIBLING_CASE = "scripts/release/test_mine.py"
    BASE_NOTES = {"scripts/release/notes.py": "OPEN = 1\n"}
    BRANCH_NOTES = {"scripts/release/notes.py": "OPEN = 1\nUNRELEASED = 2\n"}

    HOOK_CASE = "scripts/hooks/test_mine.py"
    HOOK_INSERT = ("import sys\n"
                   "from pathlib import Path\n"
                   "REPO_ROOT = Path(__file__).resolve().parents[2]\n"
                   "HOOK_LIBRARY = REPO_ROOT / '.claude/hooks/lib'\n"
                   "sys.path.insert(0, str(HOOK_LIBRARY))\n")

    SCRIPT_CASE = "scripts/pr/test_mine.py"
    SCRIPT_INSERT = ("import sys\n"
                     "from pathlib import Path\n"
                     "sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'release'))\n")

    # The target computed in the function that inserts it, which is the other place a name a fold
    # has to resolve can be bound.
    LOCAL_INSERT = ("import sys\n"
                    "from pathlib import Path\n"
                    "def _reach():\n"
                    "    release = Path(__file__).resolve().parent.parent / 'release'\n"
                    "    sys.path.insert(0, str(release))\n"
                    "_reach()\n")

    @staticmethod
    def case_module(spells, preamble=""):
        """A module of one case reaching `spells`, which is the reach the tolerance asks about."""
        return ("import unittest\n" + preamble + "\n\nclass T(unittest.TestCase):\n"
                "    def test_a(self):\n"
                "        self.assertIsNotNone({})\n".format(spells))

    def read(self, output, case_path, base, branch, spells, preamble=""):
        """What the gate makes of `output`, over trees holding the files given and the case itself."""
        holder = tempfile.mkdtemp(prefix="base-red-pyname-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        for label, files in (("base", base), ("branch", branch)):
            held = dict(files)
            held.setdefault(case_path, self.case_module(spells, preamble))
            for relative, text in held.items():
                path = root / label / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text)
        return base_red_check.added_python_surface(
            output, root / "base", root / "branch",
            base_red_check.Case("T.test_a", case_path, 1, 2))

    def test_Given_ASuiteLoadingItsSubjectByPath_When_ItReachesAnAddedName_Then_TheReachIsPlaced(self):
        # Arrange -- the shape 18 of this repository's 22 such suites take: the module is loaded through
        # `spec_from_file_location` under a name of its own and nothing is put on `sys.path`, so the
        # directory join has only the case's own folder to offer and the file is never found.
        preamble = ("import importlib.util\n"
                    "from pathlib import Path\n"
                    "_spec = importlib.util.spec_from_file_location(\n"
                    "    'subject', Path(__file__).resolve().parents[1] / 'lib' / 'subject.py')\n"
                    "subject = importlib.util.module_from_spec(_spec)\n"
                    "_spec.loader.exec_module(subject)\n")
        base = {"lib/subject.py": "OLD = 1\n"}
        branch = {"lib/subject.py": "OLD = 1\nADDED = 2\n"}

        # Act
        placed = self.read("AttributeError: module 'subject' has no attribute 'ADDED'",
                           "suite/test_subject.py", base, branch, "subject.ADDED", preamble)

        # Assert
        self.assertTrue(placed)

    def raised_importing(self, module, files):
        """The exception line a real import leaves, rather than one transcribed into this file.

        The readings below turn on words Python chooses, and a transcription is a mirror nothing
        updates. The circular case is why it matters which way that fails: were its wording to
        change, `MISSING_MEMBER` would begin matching a circular import, the gate would begin
        tolerating one, and a transcribed case would go on passing.
        """
        holder = tempfile.mkdtemp(prefix="base-red-raise-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        for name, text in files.items():
            (Path(holder) / name).write_text(text)
        sys.path.insert(0, holder)
        self.addCleanup(sys.path.remove, holder)
        for name in files:
            self.addCleanup(sys.modules.pop, name[:-len(".py")], None)
        try:
            importlib.import_module(module)
        except ImportError as raised:
            return "".join(traceback.format_exception_only(type(raised), raised)).strip()
        return ""

    def test_Given_ANameOnlyTheBranchBinds_When_ItsImportRaises_Then_ThatIsEvidence(self):
        # Arrange -- the raise is a real one, taken off an import of a module that does not bind the
        # name, so the reading is held to the words Python chose rather than to a transcription.
        raised = self.raised_importing(
            "notes_user", {"notes.py": "OPEN = 1\n",
                           "notes_user.py": "from notes import UNRELEASED\n"})

        # Act
        found = self.read(raised, self.SIBLING_CASE, self.BASE_NOTES, self.BRANCH_NOTES,
                          "UNRELEASED")

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a name neither tree binds is a misspelling.
    # Reading ImportError at all is what this change adds, and a case that raised for a reason the
    # branch did not create took no reading, so it goes on counting against the branch.
    def test_Given_ANameNeitherTreeBinds_When_ItsImportRaises_Then_ThatIsNotEvidence(self):
        # Arrange -- the same trees as the case above, and a case that reaches the misspelling, so
        # what refuses this is the comparison rather than the reach beside it.
        # Act
        found = self.read(
            "ImportError: cannot import name 'UNRELESED' from 'notes' "
            "(/tmp/base-red/tree/scripts/release/notes.py)",
            self.SIBLING_CASE, self.BASE_NOTES, self.BRANCH_NOTES, "UNRELESED")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a name the base already binds is not the branch's to claim.
    # It is the half of the comparison that answers about the base, and the tolerance rests on it:
    # without it any name the branch happens to bind excuses any raise that named it.
    def test_Given_ANameBothTreesBind_When_ItsImportRaises_Then_ThatIsNotEvidence(self):
        # Arrange -- a base that binds the name too, which is the one arrangement that separates the
        # two halves of the comparison.
        # Act
        found = self.read(
            "ImportError: cannot import name 'UNRELEASED' from 'notes' "
            "(/tmp/base-red/tree/scripts/release/notes.py)",
            self.SIBLING_CASE, self.BRANCH_NOTES, self.BRANCH_NOTES, "UNRELEASED")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a circular import leaves no reading either way.
    # Widening this to ImportError must not start tolerating one, so the reading is held to the
    # sentence that quotes the module directly.
    def test_Given_AHalfInitialisedModule_When_TheNameCannotBeImported_Then_ThatIsNotEvidence(self):
        # Arrange -- a real circular import, and trees in which the name it names is the branch's
        # own, so what separates this from the case at the head of the fixture is the wording.
        raised = self.raised_importing(
            "circular_a", {"circular_a.py": "import circular_b\n\nUNRELEASED = 1\n",
                           "circular_b.py": "from circular_a import UNRELEASED\n"})

        # Act
        found = self.read(raised, self.SIBLING_CASE,
                          {"scripts/release/circular_a.py": "import circular_b\n"},
                          {"scripts/release/circular_a.py": "import circular_b\n\nUNRELEASED = 1\n"},
                          "UNRELEASED")

        # Assert -- the clause rides along, since an import that raised nothing reads as no evidence
        # too and would leave this passing with the wording it exists to hold unread.
        self.assertEqual((found, "partially initialized module" in raised), (False, True))

    def test_Given_AModuleUnderAPathInsert_When_OnlyTheBranchHoldsIt_Then_ThatIsEvidence(self):
        # Arrange -- no test module sits under `.claude/hooks/lib`, so a module the branch adds
        # there is a sibling of no case at all.
        # Act
        found = self.read(
            "ModuleNotFoundError: No module named 'shell_commands'", self.HOOK_CASE,
            {}, {".claude/hooks/lib/shell_commands.py": "FLAGS = ()\n"},
            "shell_commands", self.HOOK_INSERT + "import shell_commands\n")

        # Assert
        self.assertTrue(found)

    def test_Given_AnAttributeUnderAPathInsert_When_OnlyTheBranchBindsIt_Then_ThatIsEvidence(self):
        # Arrange -- another spelling a non-sibling is reached by here, one script putting a second
        # script's directory on the path, under the exception an attribute read leaves.
        # Act
        found = self.read(
            "AttributeError: module 'published_check' has no attribute 'OPEN_VERSIONS'",
            self.SCRIPT_CASE,
            {"scripts/release/published_check.py": "CLOSED = 1\n"},
            {"scripts/release/published_check.py": "CLOSED = 1\nOPEN_VERSIONS = 2\n"},
            "OPEN_VERSIONS", self.SCRIPT_INSERT)

        # Assert
        self.assertTrue(found)

    def test_Given_AnInsertTargetBoundWhereItIsInserted_When_TheTraceIsRead_Then_ThatIsEvidence(self):
        # Arrange -- the same reach, with the path bound in the function that hands it over rather
        # than above it, which is the binding a fold reading only the top level would miss.
        # Act
        found = self.read(
            "AttributeError: module 'published_check' has no attribute 'OPEN_VERSIONS'",
            self.SCRIPT_CASE,
            {"scripts/release/published_check.py": "CLOSED = 1\n"},
            {"scripts/release/published_check.py": "CLOSED = 1\nOPEN_VERSIONS = 2\n"},
            "OPEN_VERSIONS", self.LOCAL_INSERT)

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a module under a directory the run never searches.
    # The branch adding a file by that name somewhere else in the tree is not what the case reached
    # for, so what a module resolves against stays the case's own directory and the ones its file
    # names.
    def test_Given_AModuleOutsideEveryDirectorySearched_When_ItsImportRaises_Then_ThatIsNotEvidence(
            self):
        # Arrange -- the branch adds `elsewhere`, under neither the case's directory nor the one it
        # inserts, and the case reaches it, so the directories are what refuse this.
        # Act
        found = self.read(
            "ModuleNotFoundError: No module named 'elsewhere'", self.HOOK_CASE,
            {}, {"scripts/other/elsewhere.py": "X = 1\n"},
            "elsewhere", self.HOOK_INSERT + "import elsewhere\n")

        # Assert
        self.assertFalse(found)

    def test_Given_APatchedNameOnlyTheBranchBinds_When_ThePatchRaises_Then_ThatIsEvidence(self):
        # Arrange -- `mock.patch` reports a name it could not replace in words of its own, so a
        # constant patched by name reaches this reading under neither of the spellings above.
        # Act
        found = self.read(
            "AttributeError: <module 'notes' from '/tmp/base-red/tree/scripts/release/notes.py'> "
            "does not have the attribute 'UNRELEASED'",
            self.SIBLING_CASE, self.BASE_NOTES, self.BRANCH_NOTES, "UNRELEASED")

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a patch names its target as a string.
    # A misspelling reaches this reading looking exactly like a name the branch added, so the
    # comparison is the whole of what separates them.
    def test_Given_APatchedNameNeitherTreeBinds_When_ThePatchRaises_Then_ThatIsNotEvidence(self):
        # Arrange -- the same trees as the case above, patched for a name that is on neither of them.
        # Act
        found = self.read(
            "AttributeError: <module 'notes' from '/tmp/base-red/tree/scripts/release/notes.py'> "
            "does not have the attribute 'UNRELESED'",
            self.SIBLING_CASE, self.BASE_NOTES, self.BRANCH_NOTES, "UNRELESED")

        # Assert
        self.assertFalse(found)


class PythonSurfaceReachTests(unittest.TestCase):
    """Which case a module-level import took down, over a file where it took down every one.

    `from module import name` is evaluated once for the file. Reading the raise as evidence for each
    case in it answers for cases that never touched the branch's surface, and a reading nobody took
    recorded as a pass is what this whole check exists to refuse.
    """

    CASE = "scripts/release/test_mine.py"
    BASE = {"scripts/release/notes.py": "OPEN = 1\n"}
    BRANCH = {"scripts/release/notes.py": "OPEN = 1\nADDED = 2\nALSO = 3\n"}
    RAISED = ("ImportError: cannot import name 'ADDED' from 'notes' "
              "(/tmp/base-red/tree/scripts/release/notes.py)")

    def read(self, module, case="T.test_mine"):
        """What the gate makes of one case of `module`, which is a file the base cannot load."""
        holder = tempfile.mkdtemp(prefix="base-red-pyreach-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        for label, files in (("base", self.BASE), ("branch", self.BRANCH)):
            for relative, text in list(files.items()) + [(self.CASE, module)]:
                path = root / label / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text)
        return base_red_check.added_python_surface(
            self.RAISED, root / "base", root / "branch",
            base_red_check.Case(case, self.CASE, 1, 2))

    # GREEN_ON_BASE(characterization): the base takes no reading off an ImportError at all.
    # It answers no to this for want of the question, where the branch answers no because the case
    # reached nothing -- and the second answer is the one a file-wide reading would lose.
    def test_Given_TheNameSpelledOnlyByTheImport_When_ACaseIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the case reaches nothing the branch added; the import line above it does, and
        # that line is what the base died on.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    def test_Given_TheNameSpelledByAnInheritedSetUp_When_ACaseIsRead_Then_ThatIsEvidence(self):
        # Arrange -- a base class is where unittest puts scaffolding two fixtures share, and its
        # setUp runs for every case of every heir.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class Shared(unittest.TestCase):\n"
                          "    def setUp(self):\n"
                          "        self.seen = ADDED\n\n\n"
                          "class T(Shared):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertTrue(found)

    def test_Given_TheNameSpelledByAGrandparentsSetUp_When_ACaseIsRead_Then_ThatIsEvidence(self):
        # Arrange -- two levels of ancestry, which is what makes this ask about the walk rather than
        # about the first base a fixture names.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class Root(unittest.TestCase):\n"
                          "    def setUp(self):\n"
                          "        self.seen = ADDED\n\n\n"
                          "class Mid(Root):\n"
                          "    pass\n\n\n"
                          "class T(Mid):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertTrue(found)

    def test_Given_TheNameInThisCasesOwnDecorator_When_ACaseIsRead_Then_ThatIsEvidence(self):
        # Arrange -- an argument written above the `def` is code of that case, and the range this is
        # read over has to open where the decorator does rather than at the keyword below it.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    @unittest.skipIf(ADDED, 'x')\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a sibling's decorator is a sibling's code.
    # It sits above the `def` the subtraction is taken from, so leaving the range to open there
    # hands the rest of that fixture, and its heirs, a reach they never had.
    def test_Given_TheNameOnlyInASiblingsDecorator_When_ThisOneIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the decorator of the case beside this one, which runs at import for the file
        # rather than for either case.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    @unittest.skipIf(ADDED, 'x')\n"
                          "    def test_other(self):\n"
                          "        pass\n\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a sibling fixture's decorator is that fixture's code.
    # A class carries decorators above its `class` line just as a method does above its `def`, and
    # the two subtractions have to open in the same place or one of them hands out a reach.
    def test_Given_TheNameOnlyInASiblingClasssDecorator_When_ACaseIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the decorator of the fixture beside this one, which the case derives nothing
        # from.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "@unittest.skipIf(ADDED, 'x')\n"
                          "class Other(unittest.TestCase):\n"
                          "    def test_other(self):\n"
                          "        pass\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a dotted base names a class in another file.
    # Its last segment is not a spelling of an in-file class, and resolving it as one would give a
    # fixture the reach of whatever the file happens to declare under that name.
    def test_Given_ADottedBaseSharingAnInFileName_When_ACaseIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the fixture derives from `helpers.Shared`, and an unrelated `Shared` beside it
        # is what spells the name.
        # Act
        found = self.read("import unittest\n"
                          "import helpers\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class Shared(unittest.TestCase):\n"
                          "    SEEN = ADDED\n\n\n"
                          "class T(helpers.Shared):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a definition opens at the first of its stacked decorators.
    # Reading the last one instead leaves the ones above it outside the range, so a name written in
    # the outer decorator of a sibling case reads as this case's reach.
    def test_Given_TheNameInASiblingsOuterStackedDecorator_When_ItIsRead_Then_ThatIsNotEvidence(
            self):
        # Arrange -- two decorators on the case beside this one, and the name in the outer of them.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    @unittest.skipIf(ADDED, 'x')\n"
                          "    @unittest.expectedFailure\n"
                          "    def test_other(self):\n"
                          "        pass\n\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    def test_Given_TheNameReachedThroughGetattr_When_ACaseIsRead_Then_ThatIsEvidence(self):
        # Arrange -- the name is a string here, which is why a string is read for one at all; the
        # docstring case below is what that costs and where the line is drawn instead.
        # Act
        found = self.read("import unittest\n"
                          "import notes\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(getattr(notes, \"ADDED\"), 2)\n")

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a docstring looks up no name it spells.
    # It is prose that happens to sit where code is read, and a string is read for a name because
    # `getattr` reaches one that way, so this is where that reading stops.
    def test_Given_TheNameSpelledOnlyInADocstring_When_ACaseIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the fixture's own docstring names it and nothing else outside the import does.
        # Act
        found = self.read('import unittest\n'
                          'from notes import ADDED, OPEN\n\n\n'
                          'class T(unittest.TestCase):\n'
                          '    """What the module binds, ADDED among them."""\n\n'
                          '    def test_mine(self):\n'
                          '        self.assertEqual(OPEN, 1)\n')

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a comment looks up no name it spells either.
    # The same sentence written as prose above the code rather than inside it, so the two spellings
    # answer alike rather than by which quotation mark was used.
    def test_Given_TheNameSpelledOnlyInAComment_When_ACaseIsRead_Then_ThatIsNotEvidence(self):
        # Arrange -- the mention sits in a comment inside the case body itself.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        # ADDED is not used here\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertFalse(found)

    # GREEN_ON_BASE(characterization): a case answers for its own reach and for no sibling's.
    # The base agrees by reading no ImportError at all, so the arrangement rather than the verdict is
    # what separates this from a run that credits the whole file.
    def test_Given_TheNameSpelledByAnotherCaseOfTheFile_When_ThisOneIsRead_Then_ThatIsNotEvidence(
            self):
        # Arrange -- one case of the file does depend on the branch, and the reading it earns is its
        # own; the case beside it is what a file-wide answer would throw away.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n\n"
                          "    def test_other(self):\n"
                          "        self.assertEqual(ADDED, 2)\n")

        # Assert
        self.assertFalse(found)

    def test_Given_TheNameSpelledByTheFixturesSetUp_When_ACaseIsRead_Then_ThatIsEvidence(self):
        # Arrange -- the case body reaches nothing the branch added; the setUp its fixture shares
        # reaches the name on its behalf.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, OPEN\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    @classmethod\n"
                          "    def setUpClass(cls):\n"
                          "        cls.seen = ADDED\n\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(OPEN, 1)\n")

        # Assert
        self.assertTrue(found)

    def test_Given_ASecondAddedNameTheRaiseDidNotName_When_ACaseReachesIt_Then_ThatIsEvidence(self):
        # Arrange -- the raise names ADDED and the case reaches ALSO, which the branch adds as well.
        # Act
        found = self.read("import unittest\n"
                          "from notes import ADDED, ALSO\n\n\n"
                          "class T(unittest.TestCase):\n"
                          "    def test_mine(self):\n"
                          "        self.assertEqual(ALSO, 3)\n")

        # Assert
        self.assertTrue(found)


class CSharpSurfaceEvidenceTests(unittest.TestCase):
    """The C# spelling of the same comparison, which no traceback carries.

    A compile failure stops the editor before it writes a results file, so the reading a single round
    can take of it is the empty directory a crash leaves as well. What separates them is this.
    """

    def surface(self, text, base=("Idle", "Blocked"), added=("Proceeding",)):
        return base_red_check.added_csharp_surface(text, set(base), set(added))

    def test_Given_ACarriedFileSpellingANameOnlyTheBranchAdds_When_ItIsRead_Then_ThatNameIsEvidence(self):
        # Arrange -- the shape a fixture for a new enum member has: the branch declares it in the
        # production file it changed, and the base resolves nothing by that name.
        # Act
        found = self.surface("class T { void M() { var x = Status.Proceeding; } }")

        # Assert
        self.assertEqual(found, "Proceeding")

    def test_Given_ANameTheBaseSpellsToo_When_ItIsRead_Then_ItIsNotEvidence(self):
        # Arrange -- the counterpart, so the reading above is not every name in the file: a name the
        # base carries resolves there, and a file spelling only those compiles.
        # Act
        found = self.surface("class T { void M() { var x = Status.Blocked; } }",
                             base=("Idle", "Blocked", "Proceeding"))

        # Assert
        self.assertIsNone(found)

    def test_Given_ANameNeitherTreeDeclares_When_ItIsRead_Then_ItIsNotEvidence(self):
        # Arrange -- the first use of a type that lives in an assembly rather than in this repository.
        # It is absent from the base's sources for the same reason it is absent from the branch's, and
        # reading absence alone would withdraw a file that builds there perfectly well.
        # Act
        found = self.surface("class T { void M() { var pool = ArrayPool<int>.Shared; } }")

        # Assert
        self.assertIsNone(found)

    def test_Given_TheNameSpelledOnlyInAComment_When_ItIsRead_Then_ItIsNotEvidence(self):
        # Arrange -- a comment naming what the branch adds is not a reference to it, and a file whose
        # only mention is one compiles on the base.
        # Act
        found = self.surface("class T { /* Proceeding is next */ void M() { } }")

        # Assert
        self.assertIsNone(found)


class DroppedFromTheBaseTreeTests(unittest.TestCase):
    """What `drop` unlinks, and what the narrowing keeps out of it.

    The direction that matters is the one where the narrowing stops being applied: a production file the
    branch deleted would then be unlinked from the base tree, that tree would not compile, and a base
    tree that cannot compile is what this harness reports as "the base could not answer" — the reading
    #622 was filed for. `two_commit_repo` could not reach any of this until it could delete a file.
    """

    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    PRODUCTION = "Packages/p/Runtime/A/Thing.cs"

    def tree_of(self, base, branch, drop):
        root, since = two_commit_repo(self, base, branch)
        tree = worktree_beside(self, root)
        base_red_check.build_base_tree(root, since, tree, [], drop)
        return root, since, tree

    # GREEN_ON_BASE(characterization): the base already unlinks what `drop` names, and nothing said so
    # — the only test-side call passed an empty list. Measured: deleting the unlink fails this and no
    # other case.
    def test_Given_AFileInDrop_When_TheBaseTreeIsBuilt_Then_ItIsNotThere(self):
        # Arrange — the base has it and the branch deleted it, which is the shape `drop` is built from.
        base = {self.FIXTURE: "// a fixture\n", self.PRODUCTION: "// a type\n"}
        branch = {self.FIXTURE: None}

        # Act
        _, _, tree = self.tree_of(base, branch, [self.FIXTURE])

        # Assert — the production file rides along, because a tree missing everything would satisfy the
        # left side having built nothing at all.
        self.assertEqual(((tree / self.FIXTURE).exists(), (tree / self.PRODUCTION).exists()),
                         (False, True))

    # GREEN_ON_BASE(characterization): the base already narrows the drop to the test side. Measured:
    # replacing `is_test_side(name)` with `True` there fails this and no other case, where before it
    # broke the base compile and nothing went red.
    def test_Given_AProductionDeletion_When_TheDropIsNarrowed_Then_ItIsNotInIt(self):
        # Arrange — the branch deletes one of each. Only the test-side one may be unlinked: taking the
        # other out of the base tree is what leaves that tree unable to compile.
        base = {self.FIXTURE: "// a fixture\n", self.PRODUCTION: "// a type\n"}
        branch = {self.FIXTURE: None, self.PRODUCTION: None}
        root, since = two_commit_repo(self, base, branch)

        # Act
        deleted = base_red_check.deleted_files(root, since)
        narrowed = sorted(name for name in deleted if base_red_check.is_test_side(name))

        # Assert — both halves: the deletion of the production file has to be seen and then left out,
        # and a reading that saw neither would satisfy the right side alone.
        self.assertEqual((sorted(deleted), narrowed),
                         ([self.FIXTURE, self.PRODUCTION], [self.FIXTURE]))


class UnbuildableOnBaseTests(unittest.TestCase):
    """Which carried files the reading withdraws, over a real base tree rather than an invented one."""

    ENUM = "Packages/p/Runtime/A/Status.cs"
    HELPER = "Packages/p/TestUtilities/Probe.cs"
    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    CASE = "scripts/test_probe.py"

    def fixture(self, body):
        return ("namespace N\n{\n    class ProbeTests\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => " + body + ";\n    }\n}\n")

    def trees(self, base, branch):
        """(project, merge base, the base tree the branch's test files were carried onto)."""
        root, since = two_commit_repo(self, base, branch)
        carry = sorted(name for name in base_red_check.changed_lines_by_file(root, since)
                       if base_red_check.is_test_side(name))
        tree = worktree_beside(self, root)
        base_red_check.build_base_tree(root, since, tree, carry, [])
        return root, since, tree, carry

    def read(self, base, branch):
        root, since, tree, carry = self.trees(base, branch)
        return base_red_check.unbuildable_on_base(root, since, tree, carry)

    def test_Given_AFixtureNamingAMemberTheBranchAdds_When_TheTreeIsRead_Then_ItIsUnbuildable(self):
        # Arrange -- the file compiles on the branch and cannot on the base, which is the strongest
        # evidence this check takes and the one a silent round throws away.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.FIXTURE: self.fixture("Assert.Pass()")},
                          {self.ENUM: "enum Status { Idle, Proceeding }\n",
                           self.FIXTURE: self.fixture("Assert.That(Status.Proceeding, Is.Not.Null)")})

        # Assert
        self.assertEqual(found, {self.FIXTURE: "Proceeding"})

    def test_Given_AFixtureTheBaseCanBuild_When_TheTreeIsRead_Then_NothingIsWithdrawn(self):
        # Arrange -- the counterpart, and the one that decides whether this is safe to act on before
        # a run: withdrawing a file the base builds hands its cases a verdict nothing measured.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.FIXTURE: self.fixture("Assert.Pass()")},
                          {self.ENUM: "enum Status { Idle, Proceeding }\n",
                           self.FIXTURE: self.fixture("Assert.That(Status.Idle, Is.Not.Null)")})

        # Assert
        self.assertEqual(found, {})

    def test_Given_AMemberNoChangedProductionFileSpells_When_TheTreeIsRead_Then_ItIsNotWithdrawn(self):
        # Arrange -- the base's copy of the helper has not got Reach either, so the base's own text
        # is not what leaves this file in the run. `unbuildable_on_base` records what the reading
        # costs where a production change does spell the member.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.HELPER: "class Probe { public static int Ready; }\n",
                           self.FIXTURE: self.fixture("Assert.That(Probe.Ready, Is.Zero)")},
                          {self.ENUM: "enum Status { Idle, Proceeding }\n",
                           self.HELPER: "class Probe { public static int Ready, Reach; }\n",
                           self.FIXTURE: self.fixture("Assert.That(Probe.Reach, Is.Zero)")})

        # Assert
        self.assertEqual(found, {})

    def test_Given_AProductionChangeSpellingTheFixturesOwnName_When_TheTreeIsRead_Then_ItIsNotWithdrawn(self):
        # Arrange -- the fixture spelled Badge before this branch and the base builds it. Read over
        # every base file but this one, its own declaration is thrown away and what is left is a
        # collision with the production change rather than a name that cannot resolve.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.FIXTURE: self.fixture("Assert.That(Badge(), Is.Not.Null)")},
                          {self.ENUM: "enum Status { Idle, Badge }\n",
                           self.FIXTURE: self.fixture("Assert.That(Badge(), Is.Null)")})

        # Assert
        self.assertEqual(found, {})

    def test_Given_ANameOnlyAnotherCarriedFilesBaseCopySpells_When_TheTreeIsRead_Then_NothingIsWithdrawn(self):
        # Arrange -- the same reading one file over, and the fixture's own base copy does not spell
        # Reach: only the helper's does. Reading back just the file being judged would close the case
        # above and leave this one withdrawing a fixture the base builds.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.HELPER: "class Probe { public static int Reach; }\n",
                           self.FIXTURE: self.fixture("Assert.Pass()")},
                          {self.ENUM: "enum Status { Idle, Reach }\n",
                           self.HELPER: "class Probe { public static int Reach, Extra; }\n",
                           self.FIXTURE: self.fixture("Assert.That(Probe.Reach, Is.Not.Null)")})

        # Assert
        self.assertEqual(found, {})

    def test_Given_TheAddedNameSpelledOnlyInAProductionComment_When_TheTreeIsRead_Then_NothingIsWithdrawn(self):
        # Arrange -- the counterpart of the masked read taken over the carried file, on the side that
        # supplies the names: a production file whose only mention of Proceeding is a comment adds
        # nothing, and withdrawing on it is a fixture excused by prose.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n",
                           self.FIXTURE: self.fixture("Assert.Pass()")},
                          {self.ENUM: "enum Status { Idle } // Proceeding is next\n",
                           self.FIXTURE: self.fixture("Assert.That(Status.Proceeding, Is.Not.Null)")})

        # Assert
        self.assertEqual(found, {})

    def test_Given_ACarriedPythonCaseSpellingAnAddedName_When_TheTreeIsRead_Then_ItIsNotWithdrawn(self):
        # Arrange -- the Python lane reports its own missing surface off a traceback, and reading a
        # module as C# hands its cases a compile verdict no compiler took.
        # Act
        found = self.read({self.ENUM: "enum Status { Idle }\n", self.CASE: "Proceeding = 0\n"},
                          {self.ENUM: "enum Status { Idle, Proceeding }\n",
                           self.CASE: "Proceeding = 1\n\n\nclass T:\n"
                                      "    def test_Given_A_When_B_Then_C(self):\n        pass\n"})

        # Assert
        self.assertEqual(found, {})


class EmittedBaseTreeTests(unittest.TestCase):
    """What `--emit` leaves behind for an editor it does not itself run.

    The reading and the run are separate invocations with a base tree and one JSON file between them,
    so a withdrawal decided here and not carried out changes nothing: the tree still holds the file
    the module docstring says takes its whole assembly down with it, and the empty artifacts directory
    that follows is the one an editor that never started leaves too.
    """

    ENUM = "Packages/p/Runtime/A/Status.cs"
    UNBUILDABLE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    BESIDE = "Packages/p/Runtime/A/Tests/Editor/OtherTests.cs"

    def fixture(self, name, body):
        return ("namespace N\n{\n    class " + name + "\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => " + body + ";\n    }\n}\n")

    def emitted(self):
        """(the base tree `--emit` built and read, everything that invocation printed)."""
        base = {self.ENUM: "enum Status { Idle }\n",
                self.UNBUILDABLE: self.fixture("ProbeTests", "Assert.Pass()"),
                self.BESIDE: self.fixture("OtherTests", "Assert.Pass()")}
        branch = {self.ENUM: "enum Status { Idle, Proceeding }\n",
                  self.UNBUILDABLE: self.fixture(
                      "ProbeTests", "Assert.That(Status.Proceeding, Is.Not.Null)"),
                  self.BESIDE: self.fixture("OtherTests", "Assert.That(Status.Idle, Is.Not.Null)")}
        root, since = two_commit_repo(self, base, branch)
        tree = worktree_beside(self, root)
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--base", since, "--lane", "csharp",
             "--emit", str(root / "plan.json"), "--base-tree", str(tree)],
            capture_output=True, text=True)
        if printed.returncode != 0:
            raise RuntimeError("--emit did not run:\n" + printed.stdout + printed.stderr)
        return tree, printed.stdout

    def test_Given_AFileTheReadingProvedUnbuildable_When_TheTreeIsEmitted_Then_ItHoldsTheBasesOwnText(self):
        # Arrange -- the reading and the withdrawal are one step apart, and the second one is the
        # whole layer: deciding a file cannot build there changes no verdict downstream, since every
        # neighbour is already reading the silence the same unbuilt assembly leaves.
        tree, _ = self.emitted()

        # Act
        carried = (tree / self.UNBUILDABLE).read_text()

        # Assert
        self.assertEqual(carried, self.fixture("ProbeTests", "Assert.Pass()"))

    def test_Given_AFileTheReadingProvedUnbuildable_When_TheTreeIsEmitted_Then_ItsFixtureIsNotAsked(self):
        # Arrange -- what stands in its place is the base's own text under the same fixture name, so
        # the round would be spent on a result nothing consults. Both halves in one comparison, since
        # a line naming no fixture at all satisfies the first on its own.
        _, printed = self.emitted()
        asked = next(line for line in printed.splitlines() if line.startswith("fixtures="))

        # Act
        named = ("N.ProbeTests" in asked, "N.OtherTests" in asked)

        # Assert
        self.assertEqual(named, (False, True))


class AddedKeywordTests(unittest.TestCase):
    """A parameter the branch added, which is a surface a name comparison cannot see.

    Measured over one branch head before any of this existed: of the Python cases wrongly counted red
    on the base, several died on an argument count rather than on a name.
    """

    HELPER = "scripts/helper.py"
    CASE = "scripts/test_mine.py"

    INSERT_CASE = "scripts/pr/test_mine.py"
    INSERT = ("import sys\n"
              "from pathlib import Path\n\n"
              "sys.path.insert(0, str(Path(__file__).resolve().parent.parent / 'release'))\n")

    # A call is what names a keyword, so a case has to make one for the comparison to be what
    # answers rather than the reach beside it.
    CALLING = ("\n\nclass T(unittest.TestCase):\n"
               "    def test_a(self):\n"
               "        self.assertIsNotNone(report(cases, unbuildable=True))\n")
    # The same call made where a class body runs it, which is at import and for no case in
    # particular.
    ELSEWHERE = ("\n\nclass Other(unittest.TestCase):\n"
                 "    SPEC = report(cases, unbuildable=True)\n\n\n"
                 "class T(unittest.TestCase):\n"
                 "    def test_a(self):\n"
                 "        self.assertEqual(1, 1)\n")

    def trees(self, base, branch, module):
        holder = tempfile.mkdtemp(prefix="base-red-keyword-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        for name, text in (("base", base), ("branch", branch)):
            for relative, held in ((self.HELPER, text), (self.CASE, "import unittest\n" + module)):
                path = root / name / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(held)
        return root / "base", root / "branch"

    def read(self, base, branch, output, module=None):
        return base_red_check.added_python_surface(
            output, *self.trees(base, branch, module or self.CALLING),
            base_red_check.Case("T.test_a", self.CASE, 1, 2))

    def read_under_insert(self, base, branch, output):
        """The same reading, over a case whose helper sits behind a `sys.path.insert`."""
        holder = tempfile.mkdtemp(prefix="base-red-keyword-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        for name, helper in (("base", base), ("branch", branch)):
            for relative, text in ((self.INSERT_CASE,
                                    "import unittest\n" + self.INSERT + self.CALLING),
                                   ("scripts/release/helper.py", helper)):
                path = root / name / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text)
        return base_red_check.added_python_surface(
            output, root / "base", root / "branch",
            base_red_check.Case("T.test_a", self.INSERT_CASE, 1, 2))

    # GREEN_ON_BASE(characterization): the case's own directory is one the run imports from.
    # Widening the search past it must leave a sibling callee reading as it did, and the case that
    # shows so is one the base answers too.
    def test_Given_AParameterOnlyTheBranchAccepts_When_TheTraceIsRead_Then_ItIsEvidence(self):
        # Arrange -- the call names the parameter and Python names nothing else, so which module
        # defines it is read by looking, over the directories the case resolves against.
        # Act
        found = self.read("def report(cases):\n    return cases\n",
                          "def report(cases, unbuildable=None):\n    return cases\n",
                          "TypeError: report() got an unexpected keyword argument 'unbuildable'")

        # Assert
        self.assertTrue(found)

    def test_Given_AParameterTheBaseAcceptsToo_When_TheTraceIsRead_Then_ItIsNotEvidence(self):
        # Arrange -- the counterpart, and the one that keeps a misspelling out: a base that takes the
        # keyword raised for some other reason, and that reason is a non-answer.
        # Act
        found = self.read("def report(cases, unbuildable=None):\n    return cases\n",
                          "def report(cases, unbuildable=None):\n    return cases\n",
                          "TypeError: report() got an unexpected keyword argument 'unbuildable'")

        # Assert
        self.assertFalse(found)

    def test_Given_ABaseDefinitionTakingACatchAll_When_TheTraceIsRead_Then_ItIsNotEvidence(self):
        # Arrange -- `**kwargs` takes every keyword, so this base would have taken the call and the
        # TypeError came from somewhere else. Recording the catch-all under its own name instead
        # leaves a keyword nobody added reading as one the branch did.
        # Act
        found = self.read("def report(cases, **kwargs):\n    return cases\n",
                          "def report(cases, unbuildable=None):\n    return cases\n",
                          "TypeError: report() got an unexpected keyword argument 'unbuildable'")

        # Assert
        self.assertFalse(found)

    def test_Given_AParameterOnAModuleBehindAnInsert_When_TheTraceIsRead_Then_ItIsEvidence(self):
        # Arrange -- the same reading over the same directories a module name resolves against,
        # since the callee sits behind the insert rather than beside the case.
        # Act
        found = self.read_under_insert(
            "def report(cases):\n    return cases\n",
            "def report(cases, unbuildable=None):\n    return cases\n",
            "TypeError: report() got an unexpected keyword argument 'unbuildable'")

        # Assert
        self.assertTrue(found)


class KeywordAtClassScopeTests(unittest.TestCase):
    """What the ungated keyword reading costs, recorded rather than left to be rediscovered.

    A call at class scope runs at import and so raises for every case of the file, and this reading
    answers yes for all of them. Gating it on the keyword refuses the shape three of this
    repository's suites are built out of -- a case driving its subject through a helper class its
    fixture does not derive from -- and `COULD_NOT_ANSWER` is a failing verdict no declaration
    clears, so the author of a correct test would have nothing to do about it.
    """

    HELPER = "scripts/helper.py"
    CASE = "scripts/test_mine.py"
    AT_CLASS_SCOPE = ("import unittest\n\n\nclass Other(unittest.TestCase):\n"
                      "    SPEC = report(cases, unbuildable=True)\n\n\n"
                      "class T(unittest.TestCase):\n"
                      "    def test_a(self):\n"
                      "        self.assertEqual(1, 1)\n")
    THROUGH_A_HELPER = ("import unittest\n\n\nclass Support:\n"
                        "    @staticmethod\n"
                        "    def build():\n"
                        "        return report(cases, unbuildable=True)\n\n\n"
                        "class T(unittest.TestCase):\n"
                        "    def test_a(self):\n"
                        "        self.assertIsNotNone(Support.build())\n")

    def read(self, module):
        holder = tempfile.mkdtemp(prefix="base-red-kwscope-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        for name, helper in (("base", "def report(cases):\n    return cases\n"),
                             ("branch", "def report(cases, unbuildable=None):\n    return cases\n")):
            for relative, held in ((self.HELPER, helper), (self.CASE, module)):
                path = root / name / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(held)
        return base_red_check.added_python_surface(
            "TypeError: report() got an unexpected keyword argument 'unbuildable'",
            root / "base", root / "branch", base_red_check.Case("T.test_a", self.CASE, 1, 2))

    # GREEN_ON_BASE(characterization): the keyword reading answers for the file, not the case.
    # The name readings are case-scoped and this one is not, and the two cases here are the pair
    # that says why: a gate cannot tell them apart, since the keyword sits at the call site in both.
    def test_Given_ACallAtClassScope_When_ACaseOfThatFileIsRead_Then_ItIsEvidenceAnyway(self):
        # Arrange -- the case reaches nothing the branch added.
        # Act
        found = self.read(self.AT_CLASS_SCOPE)

        # Assert
        self.assertTrue(found)

    # GREEN_ON_BASE(characterization): a case can drive its subject through a helper class.
    # Its fixture derives nothing from that class, so the call site lies outside every line the case
    # reaches. The base answers yes for want of a gate and the branch by keeping none, so what this
    # records is the case a gate would refuse with no verdict an author could clear.
    def test_Given_ACallThroughAHelperTheCaseDrives_When_ItIsRead_Then_ItIsEvidence(self):
        # Arrange -- the case does reach the branch's surface, through a class its fixture does not
        # derive from, which is how `Polled`, `Workspace` and `StubbedCampaign` are written.
        # Act
        found = self.read(self.THROUGH_A_HELPER)

        # Assert
        self.assertTrue(found)


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


class PlatformSelectionTests(unittest.TestCase):
    """Whether `--platform` named a lane the branch has a case on.

    Reachable only by running a platform lane directly: CI's `--emit` step prints no fixtures and
    skips the editor. That is what a contributor reproducing a lane by hand does, and what they got
    was every case reporting "the base tree cannot answer" — the verdict for a run that happened and
    produced nothing usable.
    """

    def test_Given_APlatformNoChangedCaseIsOn_When_TheSelectionIsRead_Then_NothingWasAsked(self):
        # Act / Assert
        self.assertTrue(base_red_check.asked_about_nothing(["EditMode"], ["PlayMode"]))

    def test_Given_APlatformOneChangedCaseIsOn_When_TheSelectionIsRead_Then_SomethingWasAsked(self):
        # Arrange — the control, without which a reading that answered yes to everything would
        # satisfy the case above.
        # Act / Assert
        self.assertFalse(base_red_check.asked_about_nothing(["EditMode", "PlayMode"], ["PlayMode"]))

    def test_Given_NoPlatformNamed_When_TheSelectionIsRead_Then_SomethingWasAsked(self):
        # Arrange — the ordinary run, which names none and asks about both.
        # Act / Assert
        self.assertFalse(base_red_check.asked_about_nothing(["EditMode"], []))

    def test_Given_ABranchWithNoCsharpCaseAtAll_When_TheSelectionIsRead_Then_SomethingWasAsked(self):
        # Arrange — the Python lane is the answer there, and this would talk over it.
        # Act / Assert
        self.assertFalse(base_red_check.asked_about_nothing([], ["PlayMode"]))


class WarmLibraryTests(unittest.TestCase):
    """What a warm Library may carry into the base tree.

    A base run that reuses an assembly the branch compiled reports the branch's behaviour as the
    base's. The two directions are not equally exposed: a leaked assembly that makes a case pass
    there is reported as an undeclared pass and fails the run, while one that makes a case fail hands
    the branch the verdict it wanted, for its own code — and `red on the base` is the answer the lane
    exists to produce, so a wrong one looks like a right one.
    """

    def cloned(self):
        """A Library holding a compiled assembly and some import state, copied as the lane copies it."""
        source = Path(tempfile.mkdtemp(prefix="warm-src-")) / "Library"
        (source / "ScriptAssemblies").mkdir(parents=True)
        (source / "ScriptAssemblies" / "Velvet.dll").write_bytes(b"the branch compiled this")
        (source / "PackageCache").mkdir()
        (source / "PackageCache" / "kept.txt").write_text("import state the base rebuilds identically")
        destination = Path(tempfile.mkdtemp(prefix="warm-dst-")) / "Library"
        self.addCleanup(shutil.rmtree, source.parent, ignore_errors=True)
        self.addCleanup(shutil.rmtree, destination.parent, ignore_errors=True)
        base_red_check.clone_tree(source, destination)
        return destination

    def test_Given_AWarmLibrary_When_ItIsCloned_Then_TheBranchsAssembliesStayBehind(self):
        # Act / Assert
        self.assertFalse((self.cloned() / "ScriptAssemblies").exists())

    # GREEN_ON_BASE(characterization): the base copies this across too, by copying everything. It is
    # the half the exclusion could take with it, and only running it says whether it did.
    def test_Given_AWarmLibrary_When_ItIsCloned_Then_TheImportStateComesAcross(self):
        # Arrange — the control: an exclusion that took the whole Library would satisfy the case
        # above and put back the import this flag exists to avoid.
        # Act / Assert
        self.assertTrue((self.cloned() / "PackageCache" / "kept.txt").exists())


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
    """What withdrawing does to the tree, which is not the same thing for every carried file."""

    def tree(self, base, branch):
        root, _ = two_commit_repo(self, base, branch)
        tree = worktree_beside(self, root)
        subprocess.run(["git", "-C", str(root), "worktree", "add", "--quiet", "--detach",
                        str(tree), "HEAD~1"], check=True, capture_output=True)
        for relative, text in branch.items():
            (tree / relative).parent.mkdir(parents=True, exist_ok=True)
            (tree / relative).write_text(text)
        return tree

    # GREEN_ON_BASE(characterization): the base removes such a file too, its docstring says so.
    # What a message about withdrawn files may claim of them is what this pins the reason for.
    def test_Given_AFileTheBaseNeverHad_When_ItIsWithdrawn_Then_ItLeavesTheTree(self):
        # Arrange -- a new test file has no text at the base to be put back to, so what a withdrawal
        # leaves is nothing at all. A message calling every withdrawal a file standing at the base's
        # text is false of this one.
        added = "Packages/p/Runtime/A/Tests/Editor/NewTests.cs"
        tree = self.tree({"Packages/p/Runtime/A/Old.cs": "class Old {}\n"},
                         {"Packages/p/Runtime/A/Old.cs": "class Old {}\n", added: "class NewTests {}\n"})

        # Act
        base_red_check.withdraw(tree, added)

        # Assert
        self.assertFalse((tree / added).exists())


class WithdrawalPolicyTests(unittest.TestCase):
    """Which carried files go back, and which come out, when a round of the base run wrote nothing."""

    HELPER = "Packages/p/TestUtilities/PanelTestBase.cs"
    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    OTHER = "Packages/p/Runtime/A/Tests/Editor/OtherTests.cs"

    def log(self, *blamed):
        return "".join("/tmp/base/{}(12,34): error CS0117: 'V' has no definition for 'Portal'\n"
                       .format(name) for name in blamed)

    def test_Given_TheLogBlamesTwoCarriedFiles_When_TheRoundIsPlanned_Then_BothGoBackAtOnce(self):
        # Arrange -- the compiler named each for its own text, so neither is standing next to the
        # other the way a silent file is.
        # Act
        restore, _ = base_red_check.withdrawals_for(
            self.log(self.FIXTURE, self.OTHER), carry=[self.FIXTURE, self.OTHER, self.HELPER],
            already=set())

        # Assert
        self.assertEqual(restore, [self.FIXTURE, self.OTHER])

    def test_Given_TheLogBlamesACarriedHelper_When_TheRoundIsPlanned_Then_TheHelperGoesBack(self):
        # Arrange -- the helper holds no case, so a choice made over case-bearing files alone cannot
        # name it, and every fixture in its assembly is silent behind it.
        # Act
        restore, _ = base_red_check.withdrawals_for(
            self.log(self.HELPER), carry=[self.HELPER, self.FIXTURE], already=set())

        # Assert
        self.assertEqual(restore, [self.HELPER])

    def test_Given_ABlamedFileAlreadyAtTheBasesText_When_TheRoundIsPlanned_Then_ItComesOutRatherThanGoingBackAgain(self):
        # Arrange -- the base's own text of it failed against what the branch still carries, and its
        # cases already read as unbuildable; putting back what it failed against would cost every
        # file building against the branch's text its reading, for a file no round asks about.
        # Act
        restore, take_out = base_red_check.withdrawals_for(
            self.log(self.FIXTURE), carry=[self.FIXTURE, self.HELPER, self.OTHER],
            already={self.FIXTURE})

        # Assert
        self.assertEqual((restore, take_out), ([], [self.FIXTURE]))

    def test_Given_ABlamedFileTheBranchDidNotCarry_When_TheRoundIsPlanned_Then_NothingGoesBack(self):
        # Arrange -- the base's own source; putting a carried file back does not reach it.
        # Act
        chosen = base_red_check.withdrawals_for(
            self.log("Packages/p/Runtime/A/Status.cs"), carry=[self.FIXTURE], already=set())

        # Assert
        self.assertEqual(chosen, ([], []))


class LocalLoopTests(unittest.TestCase):
    """The withdrawing loop itself, with the editor replaced by what its log would say.

    `main` is driven the way the command line drives it, over a repository whose base holds a
    fixture the branch changed; `run_unity` writes the log the case describes and no results.
    """

    ENUM = "Packages/p/Runtime/A/Status.cs"
    FIXTURE = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    CANARY = "Packages/p/Runtime/A/Tests/Editor/CanaryTests.cs"

    def fixture(self, name, body):
        return ("namespace N\n{\n    class " + name + "\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => " + body + ";\n    }\n}\n")

    def loop(self, log_for, writes_from=None):
        """(what the loop printed, the tree's state each round as `log_for` saw it) over a branch that
        changed ProbeTests, with the editor writing `log_for(tree, attempt)` and, from attempt
        `writes_from` on, results passing every fixture asked."""
        base = {self.ENUM: "enum Status { Idle }\n",
                self.CANARY: self.fixture("CanaryTests", "Assert.Pass()"),
                self.FIXTURE: self.fixture("ProbeTests", "Assert.Pass()")}
        branch = dict(base, **{self.FIXTURE: self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))")})
        root, since = two_commit_repo(self, base, branch)
        rounds = []

        def fake_run_unity(unity, tree, platform, fixtures, results, log, timeout):
            rounds.append((tree / self.FIXTURE).exists())
            Path(log).write_text(log_for(tree, len(rounds)))
            if writes_from is not None and len(rounds) >= writes_from:
                Path(results).write_text('<test-run>' + "".join(
                    '<test-case fullname="{}.Given_A_When_B_Then_C" result="Passed" />'.format(name)
                    for name in fixtures) + '</test-run>')
            return 1.0, 0

        held = io.StringIO()
        argv, run_unity, wait = sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet
        sys.argv = ["base_red_check.py", "--project", str(root), "--base", since, "--lane", "csharp",
                    "--platform", "EditMode", "--output", str(root / "out"), "--max-rounds", "4"]
        base_red_check.run_unity, base_red_check.wait_for_quiet = fake_run_unity, lambda seconds: True
        try:
            with contextlib.redirect_stdout(held):
                base_red_check.main()
        finally:
            sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet = argv, run_unity, wait
        return held.getvalue(), rounds

    PLAY_FIXTURE = "Packages/p/Runtime/A/Tests/PlayMode/PlayProbeTests.cs"
    PLAY_CANARY = "Packages/p/Runtime/A/Tests/PlayMode/PlayCanaryTests.cs"

    def rounds_over_both_platforms(self, max_rounds=4, bases_own=False, writes_on=None):
        """(what the loop printed, (platform, the fixtures asked) per round) over a branch that changed
        a fixture on each platform. The first round's log blames the PlayMode file -- or the base's
        own file, with `bases_own` -- and the rounds after it write results."""
        base = {self.ENUM: "enum Status { Idle }\n",
                self.CANARY: self.fixture("CanaryTests", "Assert.Pass()"),
                self.FIXTURE: self.fixture("ProbeTests", "Assert.Pass()"),
                self.PLAY_CANARY: self.fixture("PlayCanaryTests", "Assert.Pass()"),
                self.PLAY_FIXTURE: self.fixture("PlayProbeTests", "Assert.Pass()")}
        branch = dict(base, **{self.FIXTURE: self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))"),
                               self.PLAY_FIXTURE: self.fixture("PlayProbeTests", "Assert.That(2, Is.EqualTo(2))")})
        root, since = two_commit_repo(self, base, branch)
        rounds = []

        def fake_run_unity(unity, tree, platform, fixtures, results, log, timeout):
            rounds.append((platform, sorted(fixtures)))
            if writes_on is not None:
                Path(log).write_text("")
                if platform == writes_on:
                    # The branch's own case red there, its canary green: that platform is answered
                    # and carries no failing verdict, so what the run exits on is the other one.
                    Path(results).write_text('<test-run>' + "".join(
                        '<test-case fullname="{}.Given_A_When_B_Then_C" result="{}" />'.format(
                            name, "Failed" if "Probe" in name else "Passed")
                        for name in fixtures) + '</test-run>')
                return 1.0, 0
            if bases_own:
                Path(log).write_text("Scripts have compiler errors\n" + self.ENUM + "(1,1): error CS0103: x\n")
            elif len(rounds) == 1:
                Path(log).write_text("Scripts have compiler errors\n"
                                     + self.PLAY_FIXTURE + "(1,1): error CS0012: x\n")
            else:
                Path(log).write_text("")
                Path(results).write_text('<test-run>' + "".join(
                    '<test-case fullname="{}.Given_A_When_B_Then_C" result="Passed" />'.format(name)
                    for name in fixtures) + '</test-run>')
            return 1.0, 0

        held = io.StringIO()
        argv, run_unity, wait = sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet
        sys.argv = ["base_red_check.py", "--project", str(root), "--base", since, "--lane", "csharp",
                    "--platform", "EditMode", "--platform", "PlayMode", "--output", str(root / "out"),
                    "--max-rounds", str(max_rounds)]
        base_red_check.run_unity, base_red_check.wait_for_quiet = fake_run_unity, lambda seconds: True
        try:
            with contextlib.redirect_stdout(held):
                self.status = base_red_check.main()
        finally:
            sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet = argv, run_unity, wait
        return held.getvalue(), rounds

    def test_Given_AnEditModeRoundPutAPlayModeFileBack_When_ThePlayModeRoundRuns_Then_ItAsksOnlyTheCanary(self):
        # Arrange -- one tree for both platforms, so a file the EditMode round put back to the base's
        # text is the base's text when PlayMode asks; asking its fixture would read the base's own
        # result as the branch's case. The canary is what the round is left with.
        # Act
        _, rounds = self.rounds_over_both_platforms()

        # Assert
        self.assertEqual([fixtures for platform, fixtures in rounds if platform == "PlayMode"],
                         [["N.PlayCanaryTests"]])

    def test_Given_TheEditModeBudgetSpentOnTheRoundThatPutItBack_When_PlayModeRuns_Then_ItStillDoesNotAskIt(self):
        # Arrange -- a put-back made by the attempt that exhausts the budget is the one a reading
        # taken at the top of the next attempt never sees.
        # Act
        _, rounds = self.rounds_over_both_platforms(max_rounds=1)

        # Assert
        self.assertEqual([fixtures for platform, fixtures in rounds if platform == "PlayMode"],
                         [["N.PlayCanaryTests"]])

    def test_Given_TheLogBlamesTheBasesOwnFile_When_TwoPlatformsAreAsked_Then_TheSecondIsNotRun(self):
        # Arrange -- a tree that does not build its own sources does not build for the next
        # platform either, and a second editor run would only repeat the reading.
        # Act
        printed, rounds = self.rounds_over_both_platforms(bases_own=True)

        # Assert
        self.assertEqual((len(rounds), printed.count("the base's own")), (1, 1))

    def test_Given_AFileTheLoopPutBack_When_ItRuns_Then_ItSaysWhichFileAndWhatBlamedIt(self):
        # Arrange -- said rather than carried into the verdict: a put-back is what the loop did, and
        # the case's own verdict is the run's to give.
        # Act
        printed, _ = self.loop(lambda tree, attempt: "Scripts have compiler errors\n"
                               + self.FIXTURE + "(1,1): error CS1929: x\n" if attempt == 1 else "",
                               writes_from=2)

        # Assert
        self.assertIn("withdrawn: " + self.FIXTURE + " -- the compiler blamed it (CS1929)", printed)

    # GREEN_ON_BASE(characterization): the base fails such a run through its own canaries.
    # This change must not take that reading away.
    def test_Given_APlatformWhoseEditorNeverWrote_When_AnotherPlatformDid_Then_TheRunStillFails(self):
        # Arrange -- EditMode answers and PlayMode's editor writes nothing, so the one flag saying
        # some platform wrote is set and what fails PlayMode is its own canaries. A verdict handed
        # the loop's put-backs would outrank them and pass the platform that answered nothing.
        # Act
        self.rounds_over_both_platforms(writes_on="EditMode")

        # Assert
        self.assertEqual(self.status, 1)

    def test_Given_TheLogBlamesTheBasesOwnFile_When_TheLoopRuns_Then_ItStopsAfterOneRoundAndSaysSo(self):
        # Arrange -- a round spent withdrawing a silent file cannot reach a base that does not build
        # its own sources, and the budget is not what binds there.
        # Act
        printed, rounds = self.loop(lambda tree, attempt: "Scripts have compiler errors\n"
                                    + self.ENUM + "(1,1): error CS0103: x\n")

        # Assert
        self.assertEqual((len(rounds), "the base's own" in printed), (1, True))

    def test_Given_ALoopThatSpentItsBudgetTakingAFileOut_When_ItReports_Then_ItCountsTheWithdrawalAndTheRemoval(self):
        # Arrange -- the loop holds what it withdrew and what it took out, and the reason it prints
        # has to be handed the second or it names no removal at all.
        # Act
        printed, _ = self.loop(lambda tree, attempt: "Scripts have compiler errors\n"
                               + self.FIXTURE + "(1,1): error CS1929: x\n")

        # Assert
        self.assertIn("withdrawn 1 of the 1 carried file(s), 1 of them taken out", printed)

    def test_Given_TheLoopStoppedOnTheBasesOwnFileAtTheBudget_When_ItReports_Then_TheBudgetIsNotTheRemedy(self):
        # Arrange -- a budget of one, spent on the round that found the base's own file: raising the
        # budget is not what a run stopped for a cause wants told.
        base = {self.ENUM: "enum Status { Idle }\n",
                self.CANARY: self.fixture("CanaryTests", "Assert.Pass()"),
                self.FIXTURE: self.fixture("ProbeTests", "Assert.Pass()")}
        branch = dict(base, **{self.FIXTURE: self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))")})
        root, since = two_commit_repo(self, base, branch)

        def fake_run_unity(unity, tree, platform, fixtures, results, log, timeout):
            Path(log).write_text("Scripts have compiler errors\n" + self.ENUM + "(1,1): error CS0103: x\n")
            return 1.0, 0

        held = io.StringIO()
        argv, run_unity, wait = sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet
        sys.argv = ["base_red_check.py", "--project", str(root), "--base", since, "--lane", "csharp",
                    "--platform", "EditMode", "--output", str(root / "out"), "--max-rounds", "1"]
        base_red_check.run_unity, base_red_check.wait_for_quiet = fake_run_unity, lambda seconds: True
        try:
            with contextlib.redirect_stdout(held):
                base_red_check.main()
        finally:
            sys.argv, base_red_check.run_unity, base_red_check.wait_for_quiet = argv, run_unity, wait

        # Act / Assert
        self.assertEqual(("the base's own" in held.getvalue(), "--max-rounds" in held.getvalue()), (True, False))

    def test_Given_TheLogBlamesTheSameFileAtTheBasesText_When_TheLoopRuns_Then_ItSaysTheFileCameOut(self):
        # Arrange -- the other half of what the loop says as it goes: a removal is not a put-back,
        # and a reader of the run has only what it printed.
        # Act
        printed, _ = self.loop(lambda tree, attempt: "Scripts have compiler errors\n"
                               + self.FIXTURE + "(1,1): error CS1929: x\n")

        # Assert
        self.assertIn("removed: " + self.FIXTURE + " -- its base text did not build", printed)

    def test_Given_TheLogBlamesTheSameFileAtTheBasesText_When_TheLoopRuns_Then_TheFileComesOut(self):
        # Arrange -- round one puts ProbeTests back to the base's text; round two blames it again,
        # so round three runs without it rather than with the branch's helpers put back.
        # Act
        _, rounds = self.loop(lambda tree, attempt: "Scripts have compiler errors\n"
                              + self.FIXTURE + "(1,1): error CS1929: x\n")

        # Assert -- present in rounds one and two, gone by the round after the second blame.
        self.assertEqual(rounds[:3], [True, True, False])

    # GREEN_ON_BASE(characterization): the one-silent-file fallback the loop always had.
    # A round the log does not explain reaches it on the base as it does here.
    def test_Given_ALogNamingNothing_When_TheLoopRuns_Then_ItWithdrawsOneSilentFileAndAsksAgain(self):
        # Arrange -- a round the log does not explain: the silent file goes back to the base's text
        # on its own, one per round, and the round after it finds nothing left to ask.
        # Act
        _, rounds = self.loop(lambda tree, attempt: "Aborting batchmode due to failure:\n")

        # Assert
        self.assertEqual(len(rounds), 2)


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


class WithdrawnFileVerdictTests(unittest.TestCase):
    """What the deciding half does with the files the reading half took out before the run."""

    UNBUILDABLE = "Packages/p/Runtime/A/Tests/Editor/NewTests.cs"
    BESIDE = "Packages/p/Runtime/A/Tests/Editor/OldTests.cs"
    PASSED_CANARY = {"N.CanaryTests.Given_X_When_Y_Then_Z": "Passed"}

    def cases(self):
        return [base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.UNBUILDABLE, 1, 2),
                base_red_check.Case("N.OldTests.Given_D_When_E_Then_F", self.BESIDE, 1, 2)]

    def report_over(self, reported, wrote):
        cases = self.cases()
        with contextlib.redirect_stdout(io.StringIO()):
            base_red_check.report(cases, [], reported, {"EditMode": ["N.CanaryTests"]}, wrote,
                                  {self.UNBUILDABLE: "Proceeding"}, single_round="0" * 40)
        return cases

    def verdicts(self, reported, wrote):
        return {case.path: case.verdict for case in self.report_over(reported, wrote)}

    def test_Given_AWithdrawnFileBesideOneTheRunSimplyMissed_When_TheVerdictsAreTaken_Then_OnlyOneNamesASymbol(self):
        # Arrange -- both come back COULD_NOT_COMPILE, so the verdict alone separates nothing and the
        # detail is the whole difference: one names what proved it, the other is a fixture gone
        # missing for a reason nobody wrote down and an author cannot act on. Its neighbour's detail
        # is the comparison, since a passing canary is what leaves that one reading the run at all.
        cases = self.report_over(self.PASSED_CANARY, wrote=True)

        # Act
        said = tuple(case.detail for case in
                     sorted(cases, key=lambda case: case.path != self.UNBUILDABLE))

        # Assert
        self.assertEqual(said, ("the base has no Proceeding", "the base built none of this fixture"))

    def test_Given_AWithdrawnFileWhoseFixtureAnsweredGreen_When_TheVerdictsAreTaken_Then_OnlyItsNeighbourIsCredited(self):
        # Arrange -- the base's own text stands under the withdrawn file's fixture name, so a run
        # asked for it answers green about a case whose body this branch wrote. Its neighbour is the
        # same green off the base's own tree: one comparison over both is what says the withdrawal
        # decided the first, since either verdict alone is reachable without it.
        reported = dict(self.PASSED_CANARY,
                        **{"N.NewTests.Given_A_When_B_Then_C": "Passed",
                           "N.OldTests.Given_D_When_E_Then_F": "Passed"})

        # Act
        verdicts = self.verdicts(reported, wrote=True)

        # Assert
        self.assertEqual((verdicts[self.UNBUILDABLE], verdicts[self.BESIDE]),
                         (base_red_check.COULD_NOT_COMPILE, base_red_check.PASSED_ON_BASE))

    def test_Given_ACaseBesideAWithdrawnOne_When_ItPassedOnTheBase_Then_ItStillFails(self):
        # Arrange -- what decides whether withdrawing one file credits the run: its neighbour was
        # measured on a tree that built, and green there separates nothing.
        reported = dict(self.PASSED_CANARY,
                        **{"N.OldTests.Given_D_When_E_Then_F": "Passed"})

        # Act
        verdicts = self.verdicts(reported, wrote=True)

        # Assert
        self.assertEqual(verdicts[self.BESIDE], base_red_check.PASSED_ON_BASE)

    def test_Given_AWithdrawnFileAndAnEditorThatWroteNothing_When_TheVerdictsAreTaken_Then_BothFail(self):
        # Arrange -- the withdrawal is a static approximation of what a compiler would say, and the
        # run beside it is what makes it safe to act on. A round that wrote nothing leaves it
        # unaccompanied, so it stops outranking: a branch every changed case of which sits in a
        # withdrawn file would otherwise pass on a crash, a timeout or a missing licence. Both
        # verdicts in one comparison, since the neighbour already failed before this.
        # Act
        verdicts = self.verdicts({}, wrote=False)

        # Assert
        self.assertEqual((verdicts[self.UNBUILDABLE], verdicts[self.BESIDE]),
                         (base_red_check.BASE_UNSOUND, base_red_check.BASE_UNSOUND))

    def test_Given_AWithdrawalTheReadingTook_When_ADifferentProcessDecides_Then_ItStillReadsIt(self):
        # Arrange -- the two halves are separate invocations with a JSON file between them, and a
        # field the emitting half writes that the deciding half never asks for is a silent fail-closed
        # on exactly the branches this exists to let through. Built by `as_plan` for the reason its
        # own round trip is: a literal here would go on carrying what the emitter had stopped writing.
        # The canary reports and fails, so the platform is withdrawn and only the plan's own field
        # can leave the case anything but a failing verdict.
        holder = tempfile.mkdtemp(prefix="base-red-roundtrip-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.UNBUILDABLE, 1, 2)
        Path(holder, "plan.json").write_text(json.dumps(base_red_check.as_plan(
            "0" * 40, [case], [], {}, {"EditMode": ["N.CanaryTests"]},
            withdrawn={self.UNBUILDABLE: "Proceeding"})))
        Path(holder, "results").mkdir()
        Path(holder, "results", "r.xml").write_text(
            '<test-run><test-case fullname="N.CanaryTests.Given_X_When_Y_Then_Z" result="Failed" />'
            '</test-run>')

        # Act
        status = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True).returncode

        # Assert
        self.assertEqual(status, 0)


class RemedyTests(unittest.TestCase):
    """What a refusal tells its author to do, which is the half a gate is useless without."""

    def cases(self):
        return [base_red_check.Case("N.C.Given_A_When_B_Then_C",
                                    "Packages/p/Runtime/A/Tests/Editor/CTests.cs", 1, 2)]

    def printed(self, single_round):
        held = io.StringIO()
        with contextlib.redirect_stdout(held):
            base_red_check.report(self.cases(), [], {}, {"EditMode": ["N.CanaryTests"]},
                                  wrote=False, single_round=single_round)
        return held.getvalue()

    def test_Given_ASilentRoundSomebodyElseRan_When_TheRefusalIsPrinted_Then_ItNamesTheLoop(self):
        # Arrange -- one round cannot say which carried file silenced the platform, and the loop that
        # can is a command. Without it the refusal names a state and no way out of it.
        # Act
        printed = self.printed("c0ffee")

        # Assert
        self.assertIn("--lane csharp --platform EditMode --base c0ffee", printed)

    def test_Given_ARunThatLoopedHere_When_TheRefusalIsPrinted_Then_TheLoopIsNotOfferedAgain(self):
        # Arrange -- the counterpart, so the line above is not printed unconditionally: this
        # invocation ran the loop to its own limit, and sending its author back to it is advice to
        # repeat what just failed.
        # Act
        printed = self.printed(None)

        # Assert
        self.assertNotIn("--warm-library", printed)


class PipeOutputTests(unittest.TestCase):
    """Whether the harness speaks while it is still working, which is only in question under a pipe.

    Every phase this script runs takes minutes and prints nothing until it is over, so a reader with
    a block-buffered stream cannot tell a run in progress from a wedged one. Both readings have been
    acted on here.
    """

    PROBE = ("import importlib.util, sys, time\n"
             "spec = importlib.util.spec_from_file_location('brc', {path!r})\n"
             "module = importlib.util.module_from_spec(spec)\n"
             "spec.loader.exec_module(module)\n"
             "{prologue}\n"
             "print('phase')\n"
             "time.sleep(60)\n")

    def first_line(self, prologue, seconds=8.0):
        """The first line a child prints through a pipe before it is killed, or None."""
        code = self.PROBE.format(
            path=str(REPO_ROOT / "scripts/test_quality/base_red_check.py"), prologue=prologue)
        child = subprocess.Popen([sys.executable, "-c", code], stdout=subprocess.PIPE, text=True)
        self.addCleanup(child.stdout.close)
        self.addCleanup(child.wait)
        self.addCleanup(child.kill)
        held = []
        reader = threading.Thread(target=lambda: held.append(child.stdout.readline()))
        reader.daemon = True
        reader.start()
        reader.join(seconds)
        return held[0].rstrip("\n") if held else None

    def test_Given_AHarnessThatPrintsAndKeepsWorking_When_ItsOutputIsAPipe_Then_TheLineArrivesFirst(self):
        # Arrange -- the control is the reading, not decoration: a pipe that delivers the line anyway
        # would pass the second half on its own, and this would report a flush nobody performed. Both
        # halves in one comparison, since the instrument is what the first one measures.
        # Act
        buffered = self.first_line("")
        flushed = self.first_line("module.speak_under_a_pipe()")

        # Assert
        self.assertEqual((buffered, flushed), (None, "phase"))


class AuthorshipTests(unittest.TestCase):
    """Which cases a branch wrote, where the diff's answer and the file's answer come apart.

    Which lines a diff calls changed is git's choice: text that only moved is described as deleted
    and re-added, and the cases in it then read as this branch's.
    """

    FIXTURE = "scripts/test_probe.py"

    def block(self, name, body):
        return ("class {name}Tests(unittest.TestCase):\n"
                "    def test_Given_A_When_B_Then_{name}(self):\n"
                "        # Arrange\n"
                "        value = {body!r}\n"
                "        # Act / Assert\n"
                "        self.assertEqual(value, {body!r})\n\n\n").format(name=name, body=body)

    def changed(self, before, after):
        holder = tempfile.mkdtemp(prefix="base-red-authorship-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        root = Path(holder)
        run = lambda *arguments: subprocess.run(["git", "-C", holder, *arguments], check=True,
                                                capture_output=True, text=True)
        run("init", "-q")
        run("config", "user.email", "probe@example.com")
        run("config", "user.name", "probe")
        path = root / self.FIXTURE
        path.parent.mkdir(parents=True, exist_ok=True)
        for label, text in (("base", before), ("branch", after)):
            path.write_text("import unittest\n\n\n" + text)
            run("add", "-A")
            run("commit", "-qm", label)
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "python")
        return [case.name for case in cases]

    def test_Given_ACaseTheBranchOnlyMoved_When_TheBranchIsRead_Then_ItIsNotOneOfItsOwn(self):
        # Arrange -- git describes the swap as one class deleted and re-added, so a line reading calls
        # the re-added one this branch's. Its text is the base's, and green on the base over it asks
        # an author to sharpen a case they did not write.
        kept, moved = self.block("Kept", "kept"), self.block("Moved", "moved")

        # Act
        changed = self.changed(kept + moved, moved + kept)

        # Assert
        self.assertEqual(changed, [])

    # GREEN_ON_BASE(characterization): a case whose own body differs from the base's is this branch's,
    # which is what the reading beside it narrows rather than replaces.
    def test_Given_ACaseTheBranchRewrote_When_TheBranchIsRead_Then_ItIsOneOfItsOwn(self):
        # Arrange -- the counterpart, so the reading above is not the reading refusing every case:
        # a body that differs is this branch's whatever the diff made of the lines around it.
        kept, moved = self.block("Kept", "kept"), self.block("Moved", "moved")

        # Act
        changed = self.changed(kept + moved, moved + self.block("Kept", "rewritten"))

        # Assert
        self.assertEqual(changed, ["KeptTests.test_Given_A_When_B_Then_Kept"])


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

    def test_Given_ABranchThatChangedOnlyATable_When_ItIsRead_Then_TheFixturesCasesAreSelected(self):
        # Arrange -- a static readonly the cases drive, changed outside every case body. Nothing was
        # selected before this and the lane exited 0 having measured a genuinely base-red change as
        # nothing at all. Measured on a branch adding rows to a table in CommitGuardParsingTests,
        # and 88 of this repository's 276 C# fixtures declare such data outside every case body.
        table = '        static readonly string[] Rows = { "one" };\n'
        root = self.repository(self.source().replace("        [SetUp]", table + "        [SetUp]"),
                               self.source().replace("        [SetUp]",
                                                     table.replace('"one"', '"one", "two"')
                                                     + "        [SetUp]"))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertEqual(sorted(case.name for case in cases),
                         ["N.ProbeTests.Given_A_When_B_Then_C", "N.ProbeTests.Given_D_When_E_Then_F"])

    # GREEN_ON_BASE(characterization): the control for the reading beside it. The base selects the
    # one case whose body moved and nothing else, which is what this narrows rather than replaces --
    # a control that reddened would mean the widening had taken every fixture with a shared line.
    def test_Given_ABranchThatChangedOneCaseAndATable_When_ItIsRead_Then_OnlyThatCaseIsSelected(self):
        # Arrange -- the control, and the reading this must not widen into: selecting the file whole
        # whenever a shared line moved puts every case a fixture has on trial for a line in SetUp,
        # which `outside` reports instead. Only where nothing at all was selected does this apply.
        table = '        static readonly string[] Rows = { "one" };\n'
        root = self.repository(self.source().replace("        [SetUp]", table + "        [SetUp]"),
                               self.source(first="Assert.Fail()").replace(
                                   "        [SetUp]", table.replace('"one"', '"one", "two"')
                                   + "        [SetUp]"))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertEqual([case.name for case in cases], ["N.ProbeTests.Given_A_When_B_Then_C"])

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

    # GREEN_ON_BASE(refactor): the declaration reading this holds is the base's own.
    # What moved is the arrangement: the branch now edits the case as well as declaring it, since a
    # branch that wrote nothing but a comment no longer has the case in scope to read a declaration
    # off. The evidence is that edit taken back out, measured: the case is not selected at all.
    def test_Given_ADeclarationTheBranchWrote_When_TheCaseIsRead_Then_ItIsThisBranchs(self):
        # Arrange -- the counterpart, so the case above is not passing for want of reading any
        # declaration at all.
        declared = "        // " + MARKER + "(refactor): a pure rename of the applier.\n"
        root = self.repository(self.source(), self.source(declared, first="Assert.Fail()"))

        # Act
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", "csharp")

        # Assert
        self.assertTrue(case_named(cases, "Given_A_When_B_Then_C").declaration.written_here)

    # GREEN_ON_BASE(refactor): the wrapped-reason reading this holds is the base's own.
    # Its arrangement moved for the same reason the case above it did, and the evidence is the same
    # one: the code edit taken back out leaves the case unselected.
    def test_Given_ABranchThatRewroteTheWrappedHalfOfAReason_When_ItIsRead_Then_TheDeclarationIsIts(self):
        # Arrange -- the reason spans both comment lines, and this branch rewrote the second. Asking
        # only whether the marker's own line moved answers "the base's own" for a declaration this
        # branch wrote half of, and the remedy it then prints is to restate what was just restated.
        before = ("        // " + MARKER + "(refactor): a pure rename\n"
                  "        // of the applier the base already carries.\n")
        after = ("        // " + MARKER + "(refactor): a pure rename\n"
                 "        // of the applier this branch performs.\n")
        root = self.repository(self.source(before), self.source(after, first="Assert.Fail()"))

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


class CommentOnlyBranchTests(unittest.TestCase):
    """A branch whose edit inside a case body is not code, which a line reading cannot separate.

    Nothing separating such a case from its absence moved, so it is not a claim the branch is making.
    A line reading poses it anyway, and an audit of one fixture's remarks then arrives at the gate
    with every case those remarks landed in. Which of a line is code is read per language, so each
    lane is asked here.
    """

    CSHARP = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    PYTHON = "scripts/test_probe.py"
    BODY = ('            var value = "one";\n'
            '            Assert.That(value, Is.EqualTo("one"));\n')

    def csharp(self, body):
        return ("namespace N\n{\n    class ProbeTests\n    {\n"
                "        [Test]\n        public void Given_A_When_B_Then_C()\n        {\n"
                + body + "        }\n    }\n}\n")

    def block(self, name):
        return ('        [Test]\n        public void Given_A_When_B_Then_{0}()\n        {{\n'
                '            Assert.That("{0}", Is.EqualTo("{0}"));\n        }}\n\n').format(name)

    def swapped(self, blocks):
        return "namespace N\n{\n    class ProbeTests\n    {\n" + blocks + "    }\n}\n"

    def python(self, body):
        return ("import unittest\n\n\nclass ProbeTests(unittest.TestCase):\n"
                "    def test_Given_A_When_B_Then_C(self):\n" + body)

    def scope(self, relative, lane, before, after):
        root, _ = two_commit_repo(self, {relative: before}, {relative: after})
        _, cases, _, _, _ = base_red_check.collect(root, "HEAD~1", lane)
        return [case.name for case in cases]

    def planned(self, root):
        return subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--base", "HEAD~1", "--lane", "csharp", "--plan"],
            capture_output=True, text=True).stdout

    def test_Given_ARemarkRewrittenInACase_When_TheBranchIsRead_Then_TheCaseIsNotInScope(self):
        # Arrange -- the replacement is as long as what it replaces and sits on its own line, so
        # nothing but the blanking of comments separates the two readings of this branch.
        before = self.csharp("            // aaa\n" + self.BODY)
        after = self.csharp("            // bbb\n" + self.BODY)

        # Act
        scope = self.scope(self.CSHARP, "csharp", before, after)

        # Assert
        self.assertEqual(scope, [])

    def test_Given_ARemarkThatGrewByALine_When_TheBranchIsRead_Then_TheCaseIsNotInScope(self):
        # Arrange -- blanking keeps the line count, so the branch's copy of the case carries a line of
        # spaces the base's copy has not got.
        before = self.csharp("            // one\n" + self.BODY)
        after = self.csharp("            // one\n            // and another\n" + self.BODY)

        # Act
        scope = self.scope(self.CSHARP, "csharp", before, after)

        # Assert
        self.assertEqual(scope, [])

    def test_Given_ARemarkTakenOffAStatement_When_TheBranchIsRead_Then_TheCaseIsNotInScope(self):
        # Arrange -- the shape a comment audit here produces: the statement is byte-identical and the
        # diff calls its line changed. Blanking leaves the spaces the remark occupied standing after it.
        before = self.csharp('            var value = "one"; // the value under test\n'
                             '            Assert.That(value, Is.EqualTo("one"));\n')

        # Act
        scope = self.scope(self.CSHARP, "csharp", before, self.csharp(self.BODY))

        # Assert
        self.assertEqual(scope, [])

    # GREEN_ON_BASE(characterization): the base selects a case whose compared value moved.
    # It selects it because the text moved with the value; what this holds is the narrowing beside
    # it not taking that back, and the evidence is the mask swapped for the one that blanks literals
    # as well, measured: this case alone goes red.
    def test_Given_ACaseWhoseComparedValueChanged_When_TheBranchIsRead_Then_ItIsInScope(self):
        # Arrange -- the expected value is all that moves here, and a reading that blanked literals
        # along with comments would call the case untouched.
        after = ('            var value = "one";\n'
                 '            Assert.That(value, Is.EqualTo("two"));\n')

        # Act
        scope = self.scope(self.CSHARP, "csharp", self.csharp(self.BODY), self.csharp(after))

        # Assert
        self.assertEqual(scope, ["N.ProbeTests.Given_A_When_B_Then_C"])

    def test_Given_APythonCaseWhoseRemarkChanged_When_TheBranchIsRead_Then_ItIsNotInScope(self):
        # Arrange -- `#` and `//` are two readings, and the lane that answers for one answers nothing
        # about the other. The replacement is as long as what it replaces, as above.
        tail = "        value = 1\n        self.assertEqual(value, 1)\n"

        # Act
        scope = self.scope(self.PYTHON, "python", self.python("        # aaa\n" + tail),
                           self.python("        # bbb\n" + tail))

        # Assert
        self.assertEqual(scope, [])

    def test_Given_ABranchThatPlannedNothing_When_ItIsRead_Then_ItNamesWhatItKeptOut(self):
        # Arrange -- an empty plan is what a branch that changed no test file at all leaves too, and
        # nothing else in the output separates them. Both halves in one comparison, because a
        # case named as kept out while the plan also poses it is the report and the plan reading the
        # branch differently, and the naming on its own does not show that.
        root, _ = two_commit_repo(
            self, {self.CSHARP: self.csharp("            // aaa\n" + self.BODY)},
            {self.CSHARP: self.csharp("            // bbb\n" + self.BODY)})

        # Act
        printed = self.planned(root)

        # Assert
        self.assertEqual([line.strip() for line in printed.splitlines()[1:]],
                         ["out of scope: 1 case(s) of {} hold a line this branch changed and no "
                          "code it changed".format(self.CSHARP),
                          "no changed test case in scope of --lane csharp"])

    # GREEN_ON_BASE(characterization): the base names no kept-out case at all, so it satisfies this
    # by printing nothing rather than by holding what it says. The evidence is the reporting arm
    # widened to every case with unchanged code, measured: this case alone goes red.
    def test_Given_ACaseTheBranchOnlyMoved_When_ItIsRead_Then_ItIsNotNamedAsKeptOut(self):
        # Arrange -- git aligns the swap line by line, so both cases hold changed lines while each
        # one's own text is the base's. Naming those reports as kept out a case nobody wrote over.
        first, second = self.block("C"), self.block("F")
        root, _ = two_commit_repo(self, {self.CSHARP: self.swapped(first + second)},
                                  {self.CSHARP: self.swapped(second + first)})

        # Act
        printed = self.planned(root)

        # Assert
        self.assertNotIn("out of scope", printed)


class BusyWaitScopeTests(unittest.TestCase):
    """Which scopes make this lane wait for a quiet machine, which is what it starts an editor for.

    The harness count in the unity-tests skill excludes a `--lane python` line on the strength of
    this: a Python-only run never waits and never launches an editor, so an agent reading it off the
    count holds a run against nothing.
    """

    def platforms_for(self, *paths):
        """The platform set the wait is guarded on, derived as the lane derives it."""
        cases = [base_red_check.Case("N.C.Given_A_When_B_Then_C", path, 1, 2) for path in paths]
        return sorted({base_red_check.platform_of(case.path) for case in cases
                       if base_red_check.kind_of(case.path) == "csharp"})

    # GREEN_ON_BASE(characterization): the derivation is unchanged here. It is what the skill's
    # narrowed count now rests on, and nothing said so before.
    def test_Given_APythonOnlyScope_When_ThePlatformsAreDerived_Then_ThereAreNone(self):
        # Act / Assert — an empty set is what leaves `wait_for_quiet` unasked.
        self.assertEqual(self.platforms_for("scripts/hooks/test_probe.py"), [])

    # GREEN_ON_BASE(characterization): the derivation is unchanged here. It is what the skill's
    # narrowed count now rests on, and nothing said so before.
    def test_Given_AScopeHoldingACsharpCase_When_ThePlatformsAreDerived_Then_OneIsNamed(self):
        # Arrange — the control: without it, a derivation that returned nothing for everything would
        # satisfy the case above having read nothing.
        # Act / Assert
        self.assertEqual(
            self.platforms_for("Packages/com.velvet.core/Runtime/A/Tests/Editor/ProbeTests.cs"),
            ["EditMode"])


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


class UnbuiltBaseTreeTests(unittest.TestCase):
    """What the verdict lane says about a base run that wrote no results file.

    The results file's absence separates none of the ways a run comes to write none — which is why
    the message named the cases and not the cause. The editor log does: an activation failure leaves
    none, a crash leaves one blaming nothing, a tree the carried files took down leaves one blaming
    them.
    """

    PRODUCTION = "Packages/p/Runtime/A/Old.cs"
    CARRIED = "Packages/p/Runtime/A/Tests/Editor/NewTests.cs"

    def setUp(self):
        self.project = Path(tempfile.mkdtemp(prefix="base-red-unbuilt-"))
        self.addCleanup(shutil.rmtree, self.project, ignore_errors=True)
        subprocess.run(["git", "-C", str(self.project), "init", "-q", "-b", "main"], capture_output=True)
        self.write(self.PRODUCTION, "class Old {}\n")
        self.commit("base")
        self.since = subprocess.run(["git", "-C", str(self.project), "rev-parse", "HEAD"],
                                    capture_output=True, text=True).stdout.strip()
        self.write(self.PRODUCTION, "class Old { int x; }\n")
        self.write(self.CARRIED, "class NewTests {}\n")
        self.commit("branch")

    def write(self, relative, text):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def commit(self, message):
        for command in (["add", "-A", "."],
                        ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", message]):
            subprocess.run(["git", "-C", str(self.project), *command], capture_output=True)

    def said(self, log, since=None, withdrawn=(), removed=()):
        # By keyword, so a tree without the parameter fails on its name, which the base-red lane reads
        # as a surface only the branch provides.
        return base_red_check.unbuilt_reason(self.project, since or self.since, log,
                                             withdrawn=withdrawn, removed=removed)

    def test_Given_EveryBlamedFileIsOneTheBranchCarried_When_TheLogIsRead_Then_ItSaysTheBaseWasNeverAsked(self):
        # Arrange -- the phrase the other reading shares is not enough: a carried set read as empty
        # sends every blamed file down the other branch, which also says "carried onto it".
        # Act / Assert
        self.assertIn("So the base was never asked", self.said(self.CARRIED + "(1,1): error CS0012: x\n"))

    def test_Given_FilesTakenOutBeforeABaseFileFailed_When_TheLogIsRead_Then_TheReadingNamesThem(self):
        # Arrange -- a base file failing after removals may be missing what they declared, and the
        # reading that blames the base is where that has to be said.
        # Act / Assert
        self.assertIn("may be missing what they declared:\n  gone.cs",
                      self.said(self.PRODUCTION + "(1,1): error CS0103: x\n", removed={"gone.cs"}))

    def test_Given_FilesTakenOutBeforeACarriedFileFailed_When_TheLogIsRead_Then_TheReadingDoesNotSayIt(self):
        # Arrange -- the note is about a file of the base's; beside a reading that blames the
        # branch's own files it would contradict the reading it follows.
        # Act / Assert
        self.assertNotIn("may be missing", self.said(self.CARRIED + "(1,1): error CS0012: x\n", removed={"gone.cs"}))

    def test_Given_ABlamedProductionFileTheBranchChanged_When_TheLogIsRead_Then_ItSaysTheBaseIsTheProblem(self):
        # Act / Assert -- the branch changed it, and the base tree still holds its own text of it:
        # only the test side is carried, so a blame there is the base failing to build itself.
        self.assertIn("not ones this\nbranch carried", self.said(self.PRODUCTION + "(1,1): error CS0103: x\n"))

    def test_Given_NineBlamedProductionFiles_When_TheLogIsRead_Then_EveryOneIsNamed(self):
        # Arrange -- the other list, which the same reader would otherwise stop short on.
        names = ["Packages/p/Runtime/A/P{}.cs".format(n) for n in range(9)]
        for name in names:
            self.write(name, "class P {}\n")
        self.commit("more")

        # Act
        said = self.said("".join(name + "(1,1): error CS0103: x\n" for name in names))

        # Assert
        self.assertEqual([name for name in names if name not in said], [])

    def test_Given_NineBlamedFiles_When_TheLogIsRead_Then_EveryOneIsNamed(self):
        # Arrange -- "every file the compiler blamed" over a list that stopped short would send the
        # reader to withdraw the ones shown and leave the rest in the tree.
        names = ["Packages/p/Runtime/A/Tests/Editor/T{}Tests.cs".format(n) for n in range(9)]
        for name in names:
            self.write(name, "class T {}\n")
        self.commit("more")

        # Act
        said = self.said("".join(name + "(1,1): error CS0012: x\n" for name in names))

        # Assert
        self.assertEqual([name for name in names if name not in said], [])

    def test_Given_ACheckoutThatCannotDiff_When_TheLogIsRead_Then_ItSaysSoRatherThanBlamingTheBase(self):
        # Act / Assert -- a diff that fails leaves nothing carried, and a reading over that would
        # call every blamed file the base's own.
        self.assertIn("cannot be read here",
                      self.said(self.CARRIED + "(1,1): error CS0012: x\n", since="0" * 40))

    def test_Given_ABuildStoppedBlamingNoSourceLine_When_TheLogIsRead_Then_ItSaysTheLogHasTheRest(self):
        # Act / Assert
        self.assertIn("blamed no line", self.said(base_red_check.BUILD_STOPPED + ".\n"))

    def test_Given_TheEditorsOwnLines_When_TheConstantIsRead_Then_ItSeparatesAStoppedBuildFromACompiledOne(self):
        # Arrange -- two lines the editor writes, read from its logs on this machine and on the
        # runner: the one a stopped build writes, and the one every compiled run carries. A constant
        # a typo away from the first reads every stopped build as a crash; one shortened to a word
        # both share reads every crash as a stopped build.
        stopped, compiled = "Scripts have compiler errors.\n", "DisplayProgressbar: Compiling Scripts\n"

        # Act / Assert
        self.assertEqual((base_red_check.BUILD_STOPPED in stopped, base_red_check.BUILD_STOPPED in compiled),
                         (True, False))

    def test_Given_ALogBlamingNoErrorAndNoStoppedBuild_When_ItIsRead_Then_ItSaysTheEditorStopped(self):
        # Act / Assert -- a run that got as far as compiling and no further.
        self.assertIn("ends where the editor stopped", self.said("DisplayProgressbar: Compiling Scripts\n"))

    def test_Given_NoEditorLogAtAll_When_TheReasonIsBuilt_Then_ItSaysTheEditorWasNeverStarted(self):
        # Act / Assert -- an absent log is the shape an activation failure leaves, not a log naming nothing.
        self.assertIn("never started", self.said(""))

    def test_Given_ABlamedFileAlreadyWithdrawn_When_TheLogIsRead_Then_ItSaysWhatFailedIsBesideIt(self):
        # Act / Assert -- the base's own text of a withdrawn file stands in the tree, so a blame there
        # is not that file's to fix.
        self.assertIn("already stood at the base's text",
                      self.said(self.CARRIED + "(1,1): error CS0012: x\n", withdrawn={self.CARRIED}))

    def test_Given_AWithdrawnFileBlamedBesideACarriedOne_When_TheLogIsRead_Then_TheRestAreNamedAsTheBranchs(self):
        # Arrange -- the standing file and the rest are two lists with two remedies, and one list
        # would send the reader to withdraw what already stands at the base's text.
        other = "Packages/p/Runtime/A/Tests/Editor/OtherTests.cs"
        self.write(other, "class OtherTests {}\n")
        self.commit("other")

        # Act
        said = self.said(self.CARRIED + "(1,1): error CS0012: x\n" + other + "(1,1): error CS0012: x\n",
                         withdrawn={self.CARRIED})

        # Assert
        self.assertIn("The rest are this branch's; make them build against the base or withdraw them:\n  "
                      + other, said)

    def test_Given_FilesTakenOutInTheReading_When_TheVerdictLaneRuns_Then_ItNamesThemBesideABaseFailure(self):
        # Arrange -- the reading knows what the rounds took out, and a base file failing after that
        # may be missing what they declared; the lane is what carries it there.
        holder = tempfile.mkdtemp(prefix="base-red-removed-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.CARRIED, 1, 2)
        plan = base_red_check.as_plan(self.since, [case], [], {}, {"EditMode": ["N.CanaryTests"]})
        plan["removed"] = {"Packages/p/Runtime/A/Tests/Editor/GoneTests.cs": "took it out"}
        Path(holder, "plan.json").write_text(json.dumps(plan))
        Path(holder, "results").mkdir()
        Path(holder, "results", "editmode.log").write_text(self.PRODUCTION + "(1,1): error CS0103: x\n")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(self.project), "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True)

        # Assert
        self.assertEqual((printed.returncode, "GoneTests.cs" in printed.stdout), (1, True))

    def test_Given_AFileTheStaticReadingWithdrewAndARoundTookOut_When_TheVerdictLaneRuns_Then_ItIsCounted(self):
        # Arrange -- `replan` reaches a removal from the static withdrawal as well as from its own,
        # so a file can be in `removed` and in no other record; a count off the per-round withdrawals
        # alone says the rounds took out more files than they touched.
        holder = tempfile.mkdtemp(prefix="base-red-static-out-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.CARRIED, 1, 2)
        plan = base_red_check.as_plan(self.since, [case], [], {}, {"EditMode": ["N.CanaryTests"]},
                                      withdrawn={self.CARRIED: "Proceeding"})
        plan["rounds"] = 2
        plan["removed"] = {self.CARRIED: "taken out in round 1"}
        Path(holder, "plan.json").write_text(json.dumps(plan))
        Path(holder, "results").mkdir()
        Path(holder, "results", "editmode.log").write_text("")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(self.project), "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True)

        # Assert
        self.assertIn("withdrew 1 carried file(s), 1 of them taken out", printed.stdout)

    def test_Given_AFileAnEarlierRoundPutBack_When_TheLastRoundBlamesIt_Then_ItIsNotTheReadersToWithdraw(self):
        # Arrange -- the last round has no replan after it, so its log reaches the verdict directly;
        # a reason built off the static withdrawals alone tells the reader to withdraw a file the
        # run itself put back, one line above the line saying it did.
        holder = tempfile.mkdtemp(prefix="base-red-blamed-again-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.CARRIED, 1, 2)
        plan = base_red_check.as_plan(self.since, [case], [], {}, {"EditMode": ["N.CanaryTests"]})
        plan["rounds"] = 4
        plan["blamed"] = {self.CARRIED: "the compiler blamed it (CS0246) in round 3"}
        Path(holder, "plan.json").write_text(json.dumps(plan))
        Path(holder, "results").mkdir()
        Path(holder, "results", "editmode.log").write_text(self.CARRIED + "(1,1): error CS0246: x\n")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(self.project), "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True)

        # Assert
        self.assertIn("already stood at the base's text", printed.stdout)

    def test_Given_AFileWithdrawnAndThenTakenOut_When_TheVerdictLaneRuns_Then_ItCountsItOnce(self):
        # Arrange -- `replan` records a removal without taking the file out of what it withdrew, so
        # a file in both records is one file and the reader is told two.
        holder = tempfile.mkdtemp(prefix="base-red-counted-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.CARRIED, 1, 2)
        plan = base_red_check.as_plan(self.since, [case], [], {}, {"EditMode": ["N.CanaryTests"]})
        plan["rounds"] = 3
        plan["blamed"] = {self.CARRIED: "blamed in round 1"}
        plan["removed"] = {self.CARRIED: "taken out in round 2"}
        Path(holder, "plan.json").write_text(json.dumps(plan))
        Path(holder, "results").mkdir()
        Path(holder, "results", "editmode.log").write_text("")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(self.project), "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True)

        # Assert
        self.assertIn("withdrew 1 carried file(s), 1 of them taken out", printed.stdout)

    def test_Given_AWithdrawnFileInTheReading_When_TheVerdictLaneRuns_Then_ItReadsTheWithdrawal(self):
        # Arrange -- the plan's field has to reach the reason through the lane, or the message
        # sends the reader to withdraw a file the reading already withdrew.
        holder = tempfile.mkdtemp(prefix="base-red-standing-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.CARRIED, 1, 2)
        Path(holder, "plan.json").write_text(json.dumps(base_red_check.as_plan(
            self.since, [case], [], {}, {"EditMode": ["N.CanaryTests"]},
            withdrawn={self.CARRIED: "Proceeding"})))
        Path(holder, "results").mkdir()
        Path(holder, "results", "editmode.log").write_text(self.CARRIED + "(1,1): error CS0012: x\n")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(self.project), "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True)

        # Assert
        self.assertEqual((printed.returncode, "already stood at the base's text" in printed.stdout),
                         (1, True))


class ReplanTests(unittest.TestCase):
    """What `--replan` prepares between a round that wrote nothing and the one the workflow may run.

    The first round's editor log is the only reader of which file did not build; the reading half
    ran before that round and the deciding half sees a results directory with no results in it.
    """

    ENUM = "Packages/p/Runtime/A/Status.cs"
    BLAMED = "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs"
    BESIDE = "Packages/p/Runtime/A/Tests/Editor/OtherTests.cs"

    def fixture(self, name, body):
        return ("namespace N\n{\n    class " + name + "\n    {\n        [Test]\n"
                "        public void Given_A_When_B_Then_C() => " + body + ";\n    }\n}\n")

    def emitted(self):
        """(the repository, the base tree `--emit` built) with both fixtures carried and neither
        statically withdrawable: no name is added, so the compiler is the only reader left."""
        base = {self.ENUM: "enum Status { Idle }\n",
                self.BLAMED: self.fixture("ProbeTests", "Assert.Pass()"),
                self.BESIDE: self.fixture("OtherTests", "Assert.Pass()")}
        branch = {self.ENUM: "enum Status { Idle }\n",
                  self.BLAMED: self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))"),
                  self.BESIDE: self.fixture("OtherTests", "Assert.That(2, Is.EqualTo(2))")}
        root, since = two_commit_repo(self, base, branch)
        tree = worktree_beside(self, root)
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--base", since, "--lane", "csharp",
             "--emit", str(root / "plan.json"), "--base-tree", str(tree)],
            capture_output=True, text=True)
        if printed.returncode != 0:
            raise RuntimeError("--emit did not run:\n" + printed.stdout + printed.stderr)
        return root, tree

    def replanned(self, log, wrote=False):
        """(the repository, the base tree, what the replan printed) over a round that left `log`.

        In this process rather than through the flag, so that a tree without the reading fails on the
        name it lacks rather than on an exit status: the base-red lane reads the first as a surface
        only the branch provides and the second as a case that answered nothing.
        """
        root, tree = self.emitted()
        results = root / "results"
        results.mkdir()
        (results / "editmode.log").write_text(log)
        if wrote:
            (results / "editmode-results.xml").write_text("<test-run></test-run>")
        held = io.StringIO()
        with contextlib.redirect_stdout(held):
            base_red_check.replan(root, root / "plan.json", results, tree)
        return root, tree, held.getvalue()

    def asked(self, printed):
        return [line for line in printed.splitlines() if line.startswith("fixtures=")]

    def test_Given_TheLogBlamesACarriedFile_When_Replanned_Then_TheTreeHoldsTheBasesOwnText(self):
        # Arrange / Act
        _, tree, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n")

        # Assert
        self.assertEqual((tree / self.BLAMED).read_text(), self.fixture("ProbeTests", "Assert.Pass()"))

    def test_Given_TheLogBlamesACarriedFile_When_Replanned_Then_TheSecondRoundAsksOnlyItsNeighbour(self):
        # Arrange -- both halves in one comparison, since a line naming no fixture satisfies the
        # first on its own.
        _, _, printed = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n")
        asked = self.asked(printed)[0]

        # Act
        named = ("N.ProbeTests" in asked, "N.OtherTests" in asked)

        # Assert
        self.assertEqual(named, (False, True))

    def test_Given_TheLogBlamesACarriedFile_When_Replanned_Then_TheReadingRecordsTheCode(self):
        # Arrange / Act
        root, _, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n")

        # Assert
        self.assertEqual(json.loads((root / "plan.json").read_text())["blamed"],
                         {self.BLAMED: "the compiler blamed it (CS0012) in round 1"})

    def test_Given_TheLogNamesTheFileUnderTheRunnersOwnPath_When_Replanned_Then_ItIsStillTheCarriedOne(self):
        # Arrange -- the runner's editor writes the path from its own root, and the branch's names
        # are repository-relative; the reading is over the latter.
        # Act
        root, _, _ = self.replanned(
            "/github/workspace/base-tree/" + self.BLAMED + "(6,9): error CS0012: x\n")

        # Assert
        self.assertEqual(sorted(json.loads((root / "plan.json").read_text())["blamed"]), [self.BLAMED])

    def test_Given_TheLogBlamesTheBasesOwnFile_When_Replanned_Then_NoSecondRoundIsPrepared(self):
        # Arrange / Act
        _, _, printed = self.replanned(self.ENUM + "(1,1): error CS0103: x\n")

        # Assert
        self.assertEqual(self.asked(printed), [])

    def test_Given_TheLogBlamesTheBasesOwnFileBesideACarriedOne_When_Replanned_Then_TheCarriedOneStays(self):
        # Arrange -- a round without the carried file would not build either, so withdrawing it
        # would spend the round on a tree that answers nothing about it.
        # Act
        _, tree, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n"
                                    + self.ENUM + "(1,1): error CS0103: x\n")

        # Assert
        self.assertEqual((tree / self.BLAMED).read_text(),
                         self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))"))

    def test_Given_ALogNamingNoSource_When_Replanned_Then_NoSecondRoundIsPrepared(self):
        # Arrange -- what a licence failure, a crash and a timeout leave.
        # Act
        _, _, printed = self.replanned("Failed to activate license\n")

        # Assert
        self.assertEqual(self.asked(printed), [])

    def blamed_again(self):
        """(the repository, the base tree, the carried helper) after a round blamed ProbeTests at the
        base's text: the first round put it back, and its base text then failed against the helper
        the branch carries beside it."""
        root, tree = self.emitted()
        helper = "Packages/p/TestUtilities/Helper.cs"
        (root / helper).parent.mkdir(parents=True, exist_ok=True)
        (root / helper).write_text("static class Helper { public static int Two => 2; }\n")
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True, capture_output=True)
        subprocess.run(["git", "-C", str(root), "commit", "-qm", "helper"], check=True, capture_output=True)
        (tree / helper).parent.mkdir(parents=True, exist_ok=True)
        (tree / helper).write_text((root / helper).read_text())
        plan = json.loads((root / "plan.json").read_text())
        plan["blamed"] = {self.BLAMED: "the compiler blamed it (CS0012) in round 1"}
        plan["rounds"] = 2
        (root / "plan.json").write_text(json.dumps(plan))
        results = root / "results"
        results.mkdir()
        (results / "editmode.log").write_text(self.BLAMED + "(6,9): error CS1929: x\n")
        with contextlib.redirect_stdout(io.StringIO()):
            base_red_check.replan(root, root / "plan.json", results, tree)
        return root, tree, helper

    def test_Given_ABlamedFileAlreadyAtTheBasesText_When_Replanned_Then_ItComesOutOfTheTree(self):
        # Act
        _, tree, _ = self.blamed_again()

        # Assert
        self.assertFalse((tree / self.BLAMED).exists())

    def test_Given_ABlamedFileAlreadyAtTheBasesText_When_Replanned_Then_TheCarriedHelperStays(self):
        # Arrange -- the helper is what the file's base text failed against, and what every file
        # still building holds its reading through.
        # Act
        _, tree, helper = self.blamed_again()

        # Assert
        self.assertTrue((tree / helper).exists())

    def test_Given_ABlamedFileAlreadyAtTheBasesText_When_Replanned_Then_TheReadingRecordsTheRemoval(self):
        # Act
        root, _, _ = self.blamed_again()

        # Assert
        self.assertEqual(sorted(json.loads((root / "plan.json").read_text())["removed"]), [self.BLAMED])

    def test_Given_TheFlag_When_ItIsInvoked_Then_ItRunsTheReplan(self):
        # Arrange -- the one case that goes through the command line, since the rest reach the
        # reading directly and would not notice a flag wired to nothing.
        root, tree = self.emitted()
        results = root / "results"
        results.mkdir()
        (results / "editmode.log").write_text(self.BLAMED + "(6,9): error CS0012: x\n")

        # Act
        printed = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--project", str(root), "--replan", str(root / "plan.json"),
             "--results", str(results), "--base-tree", str(tree)], capture_output=True, text=True)

        # Assert
        self.assertEqual((printed.returncode, "N.OtherTests" in printed.stdout), (0, True))

    def test_Given_ARoundIsPrepared_When_Replanned_Then_TheResultsItReadAreMovedAside(self):
        # Arrange -- the next round writes where the verdict reads.
        # Act
        root, _, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n")

        # Assert
        self.assertEqual(((root / "results").exists(), (root / "results.round1").is_dir()), (False, True))

    def test_Given_ARoundIsPrepared_When_Replanned_Then_TheReadingCountsIt(self):
        # Act
        root, _, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n")

        # Assert
        self.assertEqual(json.loads((root / "plan.json").read_text())["rounds"], 2)

    def test_Given_ARoundThatWrote_When_Replanned_Then_NothingIsWithdrawn(self):
        # Arrange -- a results file beside a log that blames something is a round that ran, and
        # what it blamed is that run's to report.
        # Act
        _, tree, _ = self.replanned(self.BLAMED + "(6,9): error CS0012: x\n", wrote=True)

        # Assert
        self.assertEqual((tree / self.BLAMED).read_text(),
                         self.fixture("ProbeTests", "Assert.That(1, Is.EqualTo(1))"))


class BlamedFileVerdictTests(unittest.TestCase):
    """What the deciding half does with the files `--replan` took out between the rounds."""

    BLAMED = "Packages/p/Runtime/A/Tests/Editor/NewTests.cs"
    PASSED_CANARY = {"N.CanaryTests.Given_X_When_Y_Then_Z": "Passed"}

    def decided(self, wrote):
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.BLAMED, 1, 2)
        with contextlib.redirect_stdout(io.StringIO()):
            base_red_check.report([case], [], self.PASSED_CANARY if wrote else {},
                                  {"EditMode": ["N.CanaryTests"]}, wrote, None,
                                  single_round="0" * 40,
                                  blamed={self.BLAMED: "the compiler blamed it (CS0012) in round 1"})
        return case

    def test_Given_AFileTheCompilerBlamed_When_TheSecondRoundWrote_Then_ItsCaseCarriesTheCode(self):
        # Arrange / Act
        case = self.decided(wrote=True)

        # Assert
        self.assertEqual((case.verdict, case.detail),
                         (base_red_check.COULD_NOT_COMPILE,
                          "the compiler blamed it (CS0012) in round 1"))

    def test_Given_AFileTheCompilerBlamed_When_TheSecondRoundWroteNothing_Then_ItStillFails(self):
        # Arrange -- the withdrawal stands beside a round, and a round that wrote nothing leaves it
        # unaccompanied for the reason a static one is.
        # Act
        case = self.decided(wrote=False)

        # Assert
        self.assertEqual(case.verdict, base_red_check.BASE_UNSOUND)

    def test_Given_ABlamedFileInTheReading_When_ADifferentProcessDecides_Then_ItStillReadsIt(self):
        # Arrange -- the field `--replan` writes is one `--verdict` has to ask for. The canary
        # reports and fails, so the platform is withdrawn and only that field can leave the case
        # anything but a failing verdict; beside a passing canary the fixture's silence alone
        # would excuse it, and the field could go unread without this noticing.
        holder = tempfile.mkdtemp(prefix="base-red-blamed-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        case = base_red_check.Case("N.NewTests.Given_A_When_B_Then_C", self.BLAMED, 1, 2)
        plan = base_red_check.as_plan("0" * 40, [case], [], {}, {"EditMode": ["N.CanaryTests"]})
        plan["blamed"] = {self.BLAMED: "the compiler blamed it (CS0012) in round 1"}
        Path(holder, "plan.json").write_text(json.dumps(plan))
        Path(holder, "results").mkdir()
        Path(holder, "results", "r.xml").write_text(
            '<test-run><test-case fullname="N.CanaryTests.Given_X_When_Y_Then_Z" result="Failed" />'
            '</test-run>')

        # Act
        status = subprocess.run(
            [sys.executable, str(REPO_ROOT / "scripts/test_quality/base_red_check.py"),
             "--verdict", str(Path(holder, "plan.json")),
             "--results", str(Path(holder, "results"))], capture_output=True, text=True).returncode

        # Assert
        self.assertEqual(status, 0)


if __name__ == "__main__":
    unittest.main(verbosity=2)

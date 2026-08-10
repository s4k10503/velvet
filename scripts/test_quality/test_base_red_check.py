#!/usr/bin/env python3
"""Unit tests for base_red_check.py's reading, plus guards over this repository's own test files.

Reading a branch is separable from the base run that answers it, so it is tested here and needs no
licence. Everything the run reports rests on this half: a case the reader misses is a case nothing
asks about, and a case whose span it gets wrong is attributed to a line somebody else changed. Both
failures are silent -- the run comes back green having measured the wrong thing.

Run: python3 scripts/test_quality/test_base_red_check.py
"""

import importlib.util
import json
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

# Split so the repository scan below, which reads this file among the rest, does not find the sample
# declaration in the fixture text and report it as one nothing carries.
MARKER = "GREEN_ON" "_BASE"


def load_module():
    """Imports base_red_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "base_red_check", Path(__file__).resolve().with_name("base_red_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


base_red_check = load_module()

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

    def test_Given_APlatformTheBaseTreeOffersNoCanary_When_ItIsRead_Then_ItIsNotWithdrawn(self):
        # Arrange -- with nothing to read the tree by there is no reading, and inventing a verdict
        # from its absence would refuse every branch on a tree that holds no other fixture.
        # Act / Assert
        self.assertEqual(base_red_check.unsound_platforms({"EditMode": []}, {}), {})


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

    PLAN = {
        "since": "0" * 40, "shared": {}, "canaries": {"EditMode": ["N.CanaryTests"]},
        "cases": [{"name": "N.ProbeTests.Given_A_When_B_Then_C",
                   "path": "Packages/p/Runtime/A/Tests/Editor/ProbeTests.cs",
                   "key": "N.ProbeTests.Given_A_When_B_Then_C", "fixture": "N.ProbeTests",
                   "declaration": None}],
        "control": [],
    }

    def verdict_over(self, *results):
        """The exit status of the --verdict lane over a directory holding `results`."""
        holder = tempfile.mkdtemp(prefix="base-red-verdict-")
        self.addCleanup(shutil.rmtree, holder, ignore_errors=True)
        plan = Path(holder, "plan.json")
        plan.write_text(json.dumps(self.PLAN))
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

    def test_Given_ARunThatWroteAnEmptyResultsFile_When_TheVerdictsAreTaken_Then_TheCanaryFailsIt(self):
        # Arrange -- the other half of the same bar, kept beside it: a file that parses to no case is
        # a run that happened and built nothing, and the canary is what reads that.
        cases = self.cases()

        # Act
        offenders = base_red_check.report(cases, [], {}, {"EditMode": ["N.CanaryTests"]}, wrote=True)

        # Assert
        self.assertEqual([case.verdict for case in offenders], [base_red_check.BASE_UNSOUND])


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
        # like it does.
        written, carried = 0, 0
        for relative, text in csharp_test_files() + python_test_files():
            written += sum(1 for line in text.splitlines()
                           if base_red_check.DECLARATION.search(line))
            carried += sum(1 for case in base_red_check.cases_in(relative, text) if case.declaration)

        # Act / Assert
        self.assertEqual(written, carried)


if __name__ == "__main__":
    unittest.main(verbosity=2)

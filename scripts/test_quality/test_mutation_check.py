#!/usr/bin/env python3
"""Unit tests for mutation_check.py's mutant generation, its verdict, and what it holds while it runs.

Generation is separable from the Unity runs that kill or spare a mutant, so it is tested here and runs
without a licence. What a run then reports rests entirely on this half being right: a mutant that comes
back uncompilable is indistinguishable from one nothing tested, so an operator that emits broken C# does
not report a false survivor — it hides a real one behind noise.

Two halves need no editor either. Which survivors the run signs off is decided from the declarations in
the tree, and a campaign's hold on the working tree is a question about ordering rather than about
anything Unity does — so the cases below drive `main` with the editor stubbed out, which is the only way
to ask what the tree holds at a moment the campaign is inside.

Run: python3 scripts/test_quality/test_mutation_check.py
"""

import argparse
import contextlib
import fcntl
import hashlib
import importlib.util
import inspect
import io
import json
import os
import re
import signal
import shutil
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path
from unittest import mock

REPO_ROOT = Path(__file__).resolve().parents[2]
RUNTIME = REPO_ROOT / "Packages/com.velvet.core/Runtime"
GUIDE = REPO_ROOT / "CONTRIBUTING.md"


def load_module():
    """Imports mutation_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "mutation_check", Path(__file__).with_name("mutation_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mutation_check = load_module()

# The stub's one-mutant body with that mutant's line answered for, which is what separates a mutant
# nothing measured from a survivor: the declaration passes the one and not the other.
DECLARED_BODY = textwrap.dedent("""\
    namespace Velvet
    {
        internal static class Probe
        {
            // MUTANT_SURVIVES(equivalent): every caller clamps the operand already.
            internal static bool Ready(int a, int b) => a <= b;
        }
    }
    """)

GREEN_RESULTS = '<test-run total="1" passed="1" failed="0" inconclusive="0" />'
FAILING_RESULTS = ('<test-run total="1" passed="0" failed="1" inconclusive="0">'
                  '<test-case fullname="N.C.Kills" result="Failed" /></test-run>')


def mutants_of(text, operator=None):
    lines = set(range(1, len(text.splitlines()) + 1))
    found = mutation_check.mutations_for(Path("probe.cs"), text, lines)
    return [mutant for mutant in found if operator is None or mutant.operator == operator]


def applied(text, mutant):
    return mutation_check.apply_mutation(text, mutant).splitlines()[mutant.line - 1].strip()


def encloses_separator(fragment):
    """Whether a cut carries punctuation belonging to the construct around it rather than to itself.

    A comma at the fragment's own depth ended a member of an initialiser or an argument list; a `?`
    with a later `:` was a ternary whose branches the chain sat inside. Taking either leaves C# that
    compiles nowhere, and an uncompilable mutant asks the suite nothing.
    """
    mask = mutation_check.code_mask(fragment)
    seen = "".join(fragment[offset] for offset in range(len(fragment)) if mask[offset])
    depth = 0
    questions, colons = [], []
    for position, character in enumerate(seen):
        if character == "(":
            depth += 1
        elif character == ")":
            depth -= 1
        elif depth:
            continue
        elif character == ",":
            return True
        elif character == "?" and seen[position + 1:position + 2] not in (".", "?", "[", ">"):
            questions.append(position)
        elif character == ":" and ":" not in (seen[position + 1:position + 2], seen[position - 1:position]):
            colons.append(position)
    return any(colon > question for question in questions for colon in colons)


def code_parens(fragment):
    """How far the fragment's parentheses are out of balance, counting only what the compiler sees."""
    mask = mutation_check.code_mask(fragment)
    seen = [fragment[offset] for offset in range(len(fragment)) if mask[offset]]
    return seen.count("(") - seen.count(")")


def survivor(path, line, verdict=None, operator="equality"):
    mutant = mutation_check.Mutant(path, line, 0, "a", "b", operator)
    mutant.verdict = verdict or mutation_check.SURVIVED
    return mutant


def declaration(line, category="equivalent", reason="a reason of four words", written_here=True,
                operator=None):
    """A declaration, naming an operator where the tree under test can carry one.

    A tree whose `Declaration` takes no operator gets one that answers for its whole line, which is
    the state the cases below exist to separate — so they compare against it and fail, rather than
    raising and separating nothing.
    """
    try:
        return mutation_check.Declaration(category, reason, line, written_here=written_here,
                                          operator=operator)
    except TypeError:
        return mutation_check.Declaration(category, reason, line, written_here=written_here)


class NeighbourDuringAMutantTests(unittest.TestCase):
    """A second editor arriving after the wait is recorded rather than missed.

    The campaign waits before every mutant, and then waits on the child without sampling. A
    neighbour arriving ten seconds in can redden a timing-sensitive case, `counts["failed"]` is
    non-zero, and the mutant is recorded killed -- a mutant that actually survived, which is a hole
    in the tests, reported as covered. `wait_for_quiet`'s own docstring states the invariant this
    breaks, and enforces it at one instant.
    """

    @staticmethod
    def reading():
        """What one run reports, as a tuple, so a shorter one fails rather than raises."""
        return tuple(mutation_check.run_suite(
            sys.executable, ".", "EditMode", ["-c", "pass"],
            Path("/dev/null"), Path("/dev/null"), 30))

    def test_Given_ARunThatMetNoNeighbour_When_ItIsRead_Then_ThePeakIsZero(self):
        # Arrange -- a command that exits at once, so the loop samples and finds only this run.
        reading = self.reading()

        # Act / Assert -- the other two readings ride along, since a peak of zero from a run that
        # never started says nothing.
        self.assertEqual((len(reading), reading[-1], reading[1], reading[0] >= 0),
                         (3, 0, False, True))

    def test_Given_TheRunner_When_ItReturns_Then_ItCarriesAllThreeReadings(self):
        # Arrange -- read as a shape, so a tree reporting a pair fails here rather than raising.
        # Act / Assert
        self.assertEqual(len(self.reading()), 3)


class TextReadingKillerTests(unittest.TestCase):
    """A mutant killed only by a fixture that walks this tree's text was killed by nothing.

    A mutation edits a source file, so such a fixture reddens on the edit itself. Measured on the
    campaign for `Hooks.cs:1603`: the clause-removal mutant was recorded killed by three cases, and
    the first of them walks an identifier allowlist against the sources. Where a mutated line carries
    an identifier the allowlist names, that fixture kills every mutant on it -- and those are the
    lines most worth mutating, since an identifier the documentation names is one that is public.
    """

    READERS = {"DocumentationDriftTests", "CrefTargetTests"}

    def test_Given_ATextReaderBesideABehaviourCase_When_TheKillersAreRead_Then_TheBehaviourOneStands(self):
        # Arrange -- the ordinary kill, which must keep counting.
        names = ["Velvet.Tests.DocumentationDriftTests.Given_X", "Velvet.Tests.HookTests.Given_Y"]

        # Act / Assert
        self.assertEqual(mutation_check.killed_by_behaviour(names, self.READERS),
                         ["Velvet.Tests.HookTests.Given_Y"])

    def test_Given_OnlyTextReaders_When_TheKillersAreRead_Then_NothingKilledIt(self):
        # Arrange -- the shape the campaign reported as covered.
        names = ["Velvet.Tests.DocumentationDriftTests.Given_X",
                 "Velvet.Tests.CrefTargetTests.Given_Z"]

        # Act / Assert
        self.assertEqual(mutation_check.killed_by_behaviour(names, self.READERS), [])

    def test_Given_TheFixturesOfThisPackage_When_TheyAreDerived_Then_TheDriftOneIsAmongThem(self):
        # Arrange -- derived from what references the corpus rather than listed, so a fixture written
        # that way later is covered by having been written that way.
        found = mutation_check.text_reading_fixtures(REPO_ROOT / "Packages/com.velvet.core")

        # Act / Assert
        self.assertEqual(("DocumentationDriftTests" in found, "HookBailoutEqualityTests" in found),
                         (True, False))

    def test_Given_AProjectInsideADirectoryNamedLibrary_When_TheReadersAreDerived_Then_TheyAreFound(self):
        # Arrange — the Library a scan skips is the project's own, not a directory the checkout sits in.
        root = Path(tempfile.mkdtemp(prefix="text-readers-"))
        self.addCleanup(shutil.rmtree, root, ignore_errors=True)
        project = root / "Library" / "velvet"
        fixture = project / "Packages/com.velvet.core/Runtime/Tests/Editor/DriftTests.cs"
        fixture.parent.mkdir(parents=True)
        fixture.write_text("internal sealed class DriftTests { DocumentationCorpus Corpus; }\n")

        # Act / Assert
        self.assertEqual(mutation_check.text_reading_fixtures(project), {"DriftTests"})


class RewritesThatCannotCompile(unittest.TestCase):
    """Two of the three mechanisms behind a mutant that cannot compile from a rewrite.

    `-` has no string overload, so a `+` joining a literal rewrites into CS0019; and `while (true)`
    is how a method with no other returning path returns at all, so `while (false)` leaves it with
    none and the file stops compiling with CS0161. Neither is behaviour a test could notice.

    Measured over this package's production sources: arithmetic 807 to 743, literal 1709 to 1707 --
    66 of the 208 the issue counts. The third mechanism, a flip leaving a pattern variable
    unassigned, is 113 of them and is not a line reading.
    """

    def operators(self, line, wanted):
        lines = set(range(1, line.count("\n") + 2))
        return [m for m in mutation_check.mutations_for("C.cs", line, lines)
                if m.operator == wanted]

    def test_Given_APlusJoiningALiteral_When_TheMutantsAreRead_Then_ItIsNotOffered(self):
        # Arrange -- `"a" - name` is not an expression.
        # Act / Assert
        self.assertEqual(self.operators('Log("a" + name);\n', "arithmetic"), [])

    # GREEN_ON_BASE(characterization): the control, and the base offers this one too -- that is the
    # operator working, which this narrows rather than replaces.
    def test_Given_APlusBetweenNumbers_When_TheMutantsAreRead_Then_ItIsStillOffered(self):
        # Arrange -- the control: a reading that skipped every `+` would satisfy the case above and
        # take the operator with it.
        # Act / Assert
        self.assertEqual(len(self.operators("total = a + b;\n", "arithmetic")), 1)

    def test_Given_BothOnOneLine_When_TheMutantsAreRead_Then_OnlyTheArithmeticOneIsOffered(self):
        # Arrange -- read per occurrence rather than per line: skipping the line took 650 where the
        # mechanism is 64.
        # Act / Assert
        self.assertEqual(len(self.operators('Log("a" + name, x + y);\n', "arithmetic")), 1)

    def test_Given_PrefixedStringLiteralsOnTheRight_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- each spelling is a string operand even though the quote does not open the token.
        sources = ('name + $" value={value}";', 'name + @" value";',
                   'name + $@" value={value}";', 'name + @$" value={value}";')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources],
                         [0, 0, 0, 0])

    def test_Given_AStringLiteralOnThePriorLine_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- the newline continues the expression rather than ending the left operand.
        source = 'return $"prefix {value}"\n    + suffix;\n'

        # Act / Assert
        self.assertEqual(self.operators(source, "arithmetic"), [])

    def test_Given_AStringLiteralEarlierInTheAdditionChain_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- the first addition produces a string, so each later addition does too.
        source = 'return "<tag>" + amount + unit + ">";\n'

        # Act / Assert
        self.assertEqual(self.operators(source, "arithmetic"), [])

    def test_Given_CommentsBetweenAStringAndThePlus_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- comments are trivia between an operand and its operator.
        sources = ('return "left" /* block */ + value;\n',
                   'return "left" // line\n    + value;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0])

    def test_Given_CommentsBetweenThePlusAndAString_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- the literal remains the right operand across either comment form.
        sources = ('return value + /* block */ "right";\n',
                   'return value + // line\n    "right";\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0])

    # GREEN_ON_BASE(characterization): identifier operands remain within the arithmetic operator's scope.
    def test_Given_ACommentBetweenNumericOperands_When_TheMutantsAreRead_Then_ItIsStillOffered(self):
        # Arrange -- skipping trivia must not classify an identifier as a string.
        # Act / Assert
        self.assertEqual(len(self.operators('return left + /* block */ right;\n', "arithmetic")), 1)

    def test_Given_AParenthesizedStringAddition_When_TheOuterPlusIsRead_Then_NoneIsOffered(self):
        # Arrange -- either operand can be the grouped string result.
        sources = ('return ("left" + value) + suffix;\n',
                   'return prefix + ("right" + value);\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0])

    # GREEN_ON_BASE(characterization): groups with no known string result remain within arithmetic scope.
    def test_Given_ParenthesesThatDoNotProduceAString_When_TheMutantsAreRead_Then_NumericPlusesRemain(self):
        # Arrange -- member access, invocation and an identifier group establish no string result.
        sources = ('return F("left" + value).Length + 1;\n',
                   'return F("left" + value) + 1;\n',
                   'return (left + right) + tail;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1, 1, 2])

    def test_Given_MultipleDollarRawStringPrefixes_When_TheMutantsAreRead_Then_NoneIsOffered(self):
        # Arrange -- the dollar count sets interpolation-brace depth, not the operand type.
        sources = ('return value + $$"""raw {{name}}""";\n',
                   'return value + $$$"""raw {{{name}}}""";\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0])

    # GREEN_ON_BASE(characterization): postfix results remain within the arithmetic operator's scope.
    def test_Given_AGroupedStringWithAPostfix_When_TheOuterPlusIsRead_Then_ItRemainsArithmetic(self):
        # Arrange -- each postfix changes the grouped string into a value whose type is not string.
        sources = ('return value + ("x" + suffix).Length;\n',
                   'return value + ("x" + suffix).GetHashCode();\n',
                   'return value + ("x" + suffix)[0];\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1, 1, 1])

    def test_Given_AParenthesizedKnownString_When_TheOuterPlusIsRead_Then_NoneIsOffered(self):
        # Arrange -- literals, nested groups and a conditional with two literal outcomes are known text.
        sources = ('return ("x") + value;\n',
                   'return (("x")) + value;\n',
                   'return (flag ? "x" : "y") + value;\n',
                   'return value + (("x"));\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0, 0, 0])

    # GREEN_ON_BASE(characterization): unknown grouped results remain within arithmetic scope.
    def test_Given_AParenthesizedUnknownOrNumericResult_When_TheOuterPlusIsRead_Then_ItRemainsArithmetic(self):
        # Arrange -- neither an identifier nor numeric conditional outcomes establish a string type.
        sources = ('return (value) + suffix;\n',
                   'return (flag ? left : right) + suffix;\n',
                   'return (flag ? 1 : 2) + suffix;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1, 1, 1])

    def test_Given_AParenthesizedRawOrAtDollarLiteral_When_TheOuterPlusIsRead_Then_NoneIsOffered(self):
        # Arrange -- token spelling establishes each result even when the general mask splits it.
        sources = ('return ("""x""") + value;\n',
                   'return ($$"""{{value}}""") + value;\n',
                   'return (@$"x{value}") + value;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0, 0])

    def test_Given_ALiteralWithAPostfix_When_TheOuterPlusIsRead_Then_ItRemainsArithmetic(self):
        # Arrange -- the member result, rather than the literal, is the operand of the plus.
        sources = ('return value + "x".Length;\n',
                   'return value + """x""".Length;\n',
                   'return value + @$"x{value}".Length;\n',
                   'return value + ("""x""").Length;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources],
                         [1, 1, 1, 1])

    # GREEN_ON_BASE(characterization): interpolated postfix results remain within arithmetic scope.
    def test_Given_AnInterpolatedLiteralWhoseHoleContainsQuotes_When_Postfixed_Then_ItIsArithmetic(self):
        # Arrange -- the outer quote follows the hole, so its member result is the plus operand.
        sources = ('return value + $"{F("x")}".Length;\n',
                   'return value + $@"{F("x")}".Length;\n',
                   'return value + $"{{prefix}} {F(new[] { "x" })}".Length;\n',
                   'return value + $@"{{prefix}} {F(new[] { "x" })}".Length;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources],
                         [1, 1, 1, 1])

    def test_Given_AnInterpolatedLiteralWhoseHoleContainsQuotes_When_Joined_Then_NoneIsOffered(self):
        # Arrange -- nested braces and quotes belong to the hole, while the expression remains text.
        sources = ('return value + $"{F("x")}";\n',
                   'return value + $@"{F("x")}";\n',
                   'return value + $"{{prefix}} {F(new[] { "x" })}";\n',
                   'return value + $@"{{prefix}} {F(new[] { "x" })}";\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources],
                         [0, 0, 0, 0])

    # GREEN_ON_BASE(characterization): raw-string postfix results remain within arithmetic scope.
    def test_Given_ARawInterpolationHoleWithQuotes_When_Postfixed_Then_ItIsArithmetic(self):
        # Arrange -- nested strings and comments contain the same quotes as the outer delimiter.
        sources = ('return value + $$"""{{ F("""inner""") }}""".Length;\n',
                   'return value + $$"""{{ F(/* """ */ value) }}""".Length;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1, 1])

    def test_Given_ARawInterpolationHoleWithQuotes_When_Joined_Then_NoneIsOffered(self):
        # Arrange -- skipping a false delimiter must still reach the literal's real outer end.
        sources = ('return value + $$"""{{ F("""inner""") }}""";\n',
                   'return value + $$"""{{ F(/* """ */ value) }}""";\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0, 0])

    def test_Given_ARawInterpolationRunWithLiteralBraces_When_Joined_Then_NoneIsOffered(self):
        # Arrange -- excess braces outside the innermost delimiter remain literal content.
        sources = ('return value + $$"""{{{42}}""";\n',
                   'return value + $$"""{{42}}}""";\n',
                   'return value + $$"""{{{42}}}""";\n',
                   'return value + $$$"""{{{{42}}}""";\n',
                   'return value + $$$"""{{{42}}}}""";\n',
                   'return value + $$$"""{{{{42}}}}""";\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [0] * 6)

    # GREEN_ON_BASE(characterization): raw-brace postfix results remain within arithmetic scope.
    def test_Given_ARawInterpolationRunWithLiteralBraces_When_Postfixed_Then_ItIsArithmetic(self):
        # Arrange -- literal braces around the hole do not hide the later member result.
        sources = ('return value + $$"""{{{42}}""".Length;\n',
                   'return value + $$"""{{42}}}""".Length;\n',
                   'return value + $$"""{{{42}}}""".Length;\n',
                   'return value + $$$"""{{{{42}}}""".Length;\n',
                   'return value + $$$"""{{{42}}}}""".Length;\n',
                   'return value + $$$"""{{{{42}}}}""".Length;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1] * 6)

    def test_Given_ANullForgivingString_When_Joined_Then_NoneIsOffered(self):
        # Arrange -- null-forgiving preserves the string result on either side of the plus.
        sources = ('return "x"! + value;\n',
                   'return value + "x"!;\n',
                   'return ($"x{value}")! + suffix;\n',
                   'return prefix + ($"x{value}")!;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources],
                         [0, 0, 0, 0])

    def test_Given_ANullForgivingStringWithAMember_When_Joined_Then_ItRemainsArithmetic(self):
        # Arrange -- member access after null-forgiving changes the result used by the plus.
        sources = ('return value + "x"!.Length;\n',
                   'return value + ($"x{value}")!.Length;\n',
                   'return "x"!.Length + value;\n')

        # Act / Assert
        self.assertEqual([len(self.operators(source, "arithmetic")) for source in sources], [1, 1, 1])

    def test_Given_AForeverLoop_When_TheMutantsAreRead_Then_ItsTrueIsNotFlipped(self):
        # Arrange -- `while (false)` leaves a method with no returning path.
        # Act / Assert
        self.assertEqual(self.operators("while (true)\n", "literal"), [])

    # GREEN_ON_BASE(characterization): as above, on the other operator.
    def test_Given_AnOrdinaryTrue_When_TheMutantsAreRead_Then_ItIsStillFlipped(self):
        # Arrange -- the control on the other side.
        # Act / Assert
        self.assertEqual(len(self.operators("var ready = true;\n", "literal")), 1)


class GuardRemovalReadsTheStatement(unittest.TestCase):
    """The guard operator deletes a whole line, so it takes the reading the removal operator does.

    Emitted without it, a guard nested under another `if` leaves that `if` with no body, and one
    whose next line is an `else` strands the `else` -- the two shapes `deletable_line` was written
    for. No such line is in this package today: the operator emits 396 mutants with the reading and
    396 without, so this is a ratchet against the next one rather than a count that moved.
    """

    NESTED = """class C
{
    void M(bool outer, bool inner)
    {
        if (outer)
            if (inner) return;
        Body();
    }
    void Body() { }
}
"""

    ELSE_BELOW = """class C
{
    void M(bool guard)
    {
        if (guard) return;
        else Body();
    }
    void Body() { }
}
"""

    PLAIN = """class C
{
    void M(bool guard)
    {
        if (guard) return;
        Body();
    }
    void Body() { }
}
"""

    def guards(self, source):
        lines = set(range(1, source.count("\n") + 2))
        return [mutant for mutant in mutation_check.mutations_for("C.cs", source, lines)
                if mutant.operator == "guard removed"]

    def test_Given_AGuardNestedUnderAnotherIf_When_TheMutantsAreRead_Then_ItIsNotEmitted(self):
        # Arrange -- deleting it leaves `if (outer)` with no body.
        # Act / Assert
        self.assertEqual(self.guards(self.NESTED), [])

    def test_Given_AGuardWhoseNextLineIsAnElse_When_TheMutantsAreRead_Then_ItIsNotEmitted(self):
        # Arrange -- deleting it takes the `if` and strands the `else`.
        # Act / Assert
        self.assertEqual(self.guards(self.ELSE_BELOW), [])

    # GREEN_ON_BASE(characterization): the control, and the base emits this one too -- that is the
    # operator working, which this narrows rather than replaces. One that reddened would mean the
    # narrowing had taken the operator with it.
    def test_Given_AnOrdinaryGuard_When_TheMutantsAreRead_Then_ItIsStillEmitted(self):
        # Arrange -- the control: a reading that emitted nothing would satisfy both cases above and
        # take the operator with it.
        # Act / Assert
        self.assertEqual(len(self.guards(self.PLAIN)), 1)


class CodeMaskTests(unittest.TestCase):
    """Which offsets count as code. Everything downstream of the mask reads only what it leaves.

    Nothing reports an offset wrongly blanked: a mutant is never generated there, a brace never counted,
    and both halves come back looking like a file with less in it than it has.
    """

    def masked_out(self, text, fragment):
        mask = mutation_check.code_mask(text)
        start = text.index(fragment)
        return [text[offset] for offset in range(start, start + len(fragment)) if not mask[offset]]

    def test_Given_AnApostropheInAPreprocessorDirective_When_TheMaskIsRead_Then_TheCodeBelowIsCode(self):
        # Arrange -- `#region Boundary's own tree` is free-form text after the directive, but reading it
        # as code opens a character literal that runs to the next apostrophe anywhere in the file.
        text = "#region Boundary's own tree\nif (a <= b) { }\n#endregion\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "if (a <= b) { }"), [])

    def test_Given_ALoneApostropheInCode_When_TheMaskIsRead_Then_ItSwallowsNoLaterLine(self):
        # Arrange -- twelve characters is the longest literal C# can spell, `'\\U0001F600'`, so an
        # apostrophe with no closing one inside that reach is not one and consumes nothing.
        text = "var x = a ' b;\nif (c <= d) { }\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "if (c <= d) { }"), [])

    # GREEN_ON_BASE(characterization): the masking the two cases above are read against.
    def test_Given_ARealCharacterLiteral_When_TheMaskIsRead_Then_ItIsStillNotCode(self):
        # Arrange -- the counterpart, so the two above are not passing for a mask that blanks nothing.
        text = "var separator = ';';\n"

        # Act / Assert
        self.assertEqual(self.masked_out(text, "';'"), ["'", ";", "'"])


class ClauseRemovalTests(unittest.TestCase):
    def test_Given_AThreeClauseCondition_When_MutantsAreGenerated_Then_EachTrailingClauseIsRemovable(self):
        # Arrange
        text = "if (a == null || b <= 0 || b > c.Count)\n"

        # Act
        cuts = sorted(applied(text, mutant) for mutant in mutants_of(text, "clause removed"))

        # Assert
        self.assertEqual(cuts, ["if (a == null || b <= 0)", "if (a == null || b > c.Count)"])

    def test_Given_AClauseHoldingItsOwnParentheses_When_ItIsRemoved_Then_TheLineStaysBalanced(self):
        # Arrange
        text = "if (a || Ready(x, y) && Live(z))\n"

        # Act
        balances = {applied(text, m).count("(") - applied(text, m).count(")")
                    for m in mutants_of(text, "clause removed")}

        # Assert
        self.assertEqual(balances, {0})

    def test_Given_AJoinInsideANestedGroup_When_ItIsRemoved_Then_TheOuterGroupIsUntouched(self):
        # Arrange
        text = "if (a && (b || c))\n"

        # Act
        cuts = sorted(applied(text, mutant) for mutant in mutants_of(text, "clause removed"))

        # Assert
        self.assertEqual(cuts, ["if (a && (b))", "if (a)"])

    def test_Given_AJoinInsideAStringLiteral_When_MutantsAreGenerated_Then_ItIsNotACut(self):
        # Arrange
        text = 'var message = "a && b";\n'

        # Act
        cuts = mutants_of(text, "clause removed")

        # Assert
        self.assertEqual(cuts, [])

    def test_Given_AChainsFirstClause_When_MutantsAreGenerated_Then_ItIsNotOffered(self):
        # Arrange — where the first clause begins cannot be read off the line, so it is left alone.
        text = "if (a == null || b <= 0)\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["if (a == null)"])


class GenerationAcrossLinesTests(unittest.TestCase):
    """Whether a line still generates once an earlier line in the same file has yielded a clause cut.

    Every case in this file used to sit on one line, or put the clause cut last, so all 74 of them
    were green while a file's generation stopped dead at its first `||`.
    """

    def test_Given_ALineAfterAClauseCut_When_MutantsAreGenerated_Then_ItStillGenerates(self):
        # Arrange — the first line yields a clause cut; the second is a discarded call, which is a
        # shape the operator set reaches on its own.
        text = "if (a == null || b <= 0) return;\nowners.Remove(child);\n"

        # Act
        lines = sorted({mutant.line for mutant in mutants_of(text)})

        # Assert
        self.assertEqual(lines, [1, 2])

    def test_Given_ManyLinesAfterAClauseCut_When_MutantsAreGenerated_Then_NoneOfThemGoesDark(self):
        # Arrange — the loss grew with distance from the cut, so one line after it is too weak a
        # reading: with the file's source rebound, everything below the first cut reports nothing.
        text = ("if (a == null || b <= 0) return;\n"
                + "".join("var v{0} = Compute({0} <= {0});\n".format(index) for index in range(1, 9)))

        # Act
        lines = sorted({mutant.line for mutant in mutants_of(text, "boundary")})

        # Assert
        self.assertEqual(lines, [1, 2, 3, 4, 5, 6, 7, 8, 9])

    # GREEN_ON_BASE(characterization): both floors clear on either corpus, which is the point — what
    # the widening changes is which edits can move them, and no edit here is one. Only running it says
    # whether the larger corpus still clears, and it is 12132 against 8000 and 3284 against 2500.
    def test_Given_TheRepositorysOwnSources_When_MutantsAreGenerated_Then_EachTotalHoldsItsFloor(self):
        # Arrange — floors rather than the exact numbers, which every edit to the package moves.
        #
        # The second floor is why there are two: dropping "{" and ":" from STATEMENT_BOUNDARY takes
        # `line removed` down by more than a third and leaves the total above 8000, so the aggregate
        # clears while the operator that fell has gone quiet. 2500 is under what the generator
        # produces today with room for ordinary drift, and above what that narrowing leaves.
        #
        # The corpus is what a campaign mutates rather than Runtime alone, which the name and the
        # sentence above both claimed and the reading did not: 337 removal lines sat outside it — 280
        # under Editor/, 52 under CodeGen/, 5 under Samples~/ — so an edit there that stopped the
        # generator moved neither floor. `mutable` is the same predicate `--emit-lines` hands the C#
        # guards, so this holds over what they are given.
        sources = [path for path in (REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs")
                   if mutation_check.mutable(path, REPO_ROOT)]

        # Act — one scan for both, since a second would only re-read the same files.
        mutants = [mutant
                   for path in sources
                   for mutant in mutation_check.mutations_for(
                       path, path.read_text(),
                       set(range(1, len(path.read_text().splitlines()) + 1)))]
        removed = sum(1 for mutant in mutants if mutant.operator == "line removed")

        # Assert — the source count rides along because an empty scan clears any floor by arithmetic.
        self.assertEqual((len(sources) > 200, len(mutants) > 8000, removed > 2500),
                         (True, True, True))


class StatementChainCutTests(unittest.TestCase):
    """A chain that is a whole statement rather than a condition closes no group.

    The balance check next door cannot see this: the remainder's code parentheses are balanced, and
    what it is missing is the semicolon. What such a mutant costs is measured in
    `UncompilableMutantTests` — the build stops, no test is asked anything, and the verdict is its
    own rather than a kill.
    """

    # GREEN_ON_BASE(refactor): the verdict this filters on is spelled differently on the base, which
    # selects nothing there, and an absence is what the case asserts. The case below is its control.
    def test_Given_AnExpressionBodyAfterAnArrow_When_MutantsAreGenerated_Then_ItIsNotDeleted(self):
        # Arrange — the line matches the removal shape and is the tail of a declaration, so
        # deleting it leaves a member with no body. Measured at 77 mutants across this package.
        text = "internal VNode Wrap(IEnumerable<T> items) =>\n    Fragment(List(items), key);\n"

        # Act
        deletions = mutants_of(text, "line removed")

        # Assert
        self.assertEqual(deletions, [])

    # GREEN_ON_BASE(characterization): the deletion the two refusals beside it must not swallow.
    def test_Given_ADiscardedCallAfterAStatement_When_MutantsAreGenerated_Then_ItIsDeletable(self):
        # Arrange — the counterpart, so the case above is not passing for an operator that stopped
        # generating anything at all.
        text = "var live = Track();\nowners.Remove(child);\n"

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [2])

    def test_Given_ACallGuardedByAnIfWithAnElse_When_MutantsAreGenerated_Then_ItIsNotDeleted(self):
        # Arrange — deleting it takes the `if` and strands the `else`. Six more across this package.
        # The `else` line is not this shape itself, so what is under test is line 1 alone; the case
        # above is what says the operator still generates when nothing follows.
        text = ("Setup();\n"
                "Track();\n"
                "if (commit != null) CommitLeaf(node);\n"
                "else result.Add(node);\n")

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert — line 2 stays deletable, so the `else` on line 4 is what decides line 3. A file's
        # first line has no statement above it and is never deletable, which is why line 1 is absent.
        self.assertEqual(deletions, [2])

    def test_Given_AChainThatIsAWholeStatement_When_ACutIsApplied_Then_ItKeepsTheTerminator(self):
        # Arrange
        text = 'var isRgb = !isRgba && s.StartsWith("rgb(", StringComparison.Ordinal);\n'

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var isRgb = !isRgba;"])

    def test_Given_AChainEndingAnInitialiserMember_When_ACutIsApplied_Then_ItKeepsTheComma(self):
        # Arrange — the object-initializer shape. Neither health guard next door sees it: the cut's
        # code parentheses balance, and the line ends where it did, so both stay green while the
        # member and the one after it run together.
        text = "        Memoize = forceMemoize || Registry.IsMemoized(identity),\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["Memoize = forceMemoize,"])

    def test_Given_AChainBeforeATernary_When_ACutIsApplied_Then_ItKeepsBothHalvesOfIt(self):
        # Arrange — the ternary shape, whose `?` and `:` belong to the expression around the chain.
        text = "var next = index >= 0 && count > 0 ? count - 1 - index : index;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var next = index >= 0 ? count - 1 - index : index;"])

    # GREEN_ON_BASE(characterization): the base cut this correctly, and the colon stop must not
    # take it away again.
    def test_Given_APropertyPatternInAChain_When_ACutIsApplied_Then_TheBraceGoesWithIt(self):
        # Arrange — the pattern's colon sits at the enclosing parenthesis depth, so a probe counting
        # parentheses alone reads it as the clause's own and cuts the brace away from its partner.
        text = "if (_over != null && _classes is { Length: > 0 })\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["if (_over != null)"])

    def test_Given_AChainInsideATernaryBranch_When_ACutIsApplied_Then_TheColonStays(self):
        # Arrange — the other side of the same character: here the colon really does belong to the
        # expression around the chain, and stopping at it is what keeps both branches.
        text = "var n = ok ? first && second : fallback;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var n = ok ? first : fallback;"])

    def test_Given_ANullConditionalInAChain_When_ACutIsApplied_Then_ItIsNotReadAsATernary(self):
        # Arrange — `?.` and a nullable type carry a question mark that ends nothing, and stopping on
        # them would truncate a cut that was correct.
        text = "var editor = live && element.panel?.contextType == ContextType.Editor;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var editor = live;"])

    def test_Given_EveryRuntimeSource_When_ACutIsApplied_Then_NoneCarriesAnEnclosingSeparator(self):
        # Arrange — the sweep for both shapes, because the package holds 1141 cuts and 25 of them
        # were in these two before the probe learned to stop.
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act
        carrying = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                if encloses_separator(mutant.before):
                    carrying.append(f"{path.name}:{mutant.line} {mutant.before.strip()}")

        # Assert — the source count rides along because an empty scan carries nothing by arithmetic.
        self.assertEqual((len(sources) > 200, carrying), (True, []))

    def test_Given_EveryRuntimeSource_When_ACutIsApplied_Then_NoneDropsATerminator(self):
        # Arrange — the sweep, because one hand-written line is a single shape and the operator now
        # emits 1141 cuts across the package rather than 183.
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act
        dropped = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                whole = text.splitlines()[mutant.line - 1].rstrip()
                if whole.endswith(";") and not applied(text, mutant).endswith((";", "{", "}")):
                    dropped.append(f"{path.name}:{mutant.line} {applied(text, mutant)}")

        # Assert — the source count rides along because an empty scan drops nothing by arithmetic.
        self.assertEqual((len(sources) > 200, dropped), (True, []))


class DoLoopTailTests(unittest.TestCase):
    """The one `while` the removal must not take, and the one it must.

    `while (more);` is an identifier, a parenthesised head and a semicolon, so the pattern reads it —
    and what the removal leaves, `do { ... } ;`, the C# parser refuses with `'while' expected`. An
    empty-bodied `while` loop is spelled identically and its removal compiles, so the two are
    separated by what the code above closes rather than by the line itself.

    Not live in this package today: the only `do` sits in a file `mutable()` excludes. What would
    have fired is `MutantParseabilityTests` and `MutantLineRemovalTests`, on the day somebody writes
    an ordinary `do` loop — reporting a red that reads as theirs.
    """

    def method(self, body):
        return ("namespace N\n{\n    class C\n    {\n        void M()\n        {\n"
                + body + "        }\n    }\n}\n")

    def test_Given_ADoLoopTailBelowAOneLineBlock_When_MutantsAreGenerated_Then_ItIsNotRemoved(self):
        # Arrange — the `do` and its whole block on one line, so the code above the tail is a `}` with
        # the `do` in front of it on the same line.
        body = "            do { Step(); }\n            while (more);\n"

        # Act
        deletions = mutants_of(self.method(body), "line removed")

        # Assert
        self.assertEqual([mutant.before for mutant in deletions if "while" in mutant.before], [])

    def test_Given_ADoLoopTailBelowABlock_When_MutantsAreGenerated_Then_ItIsNotRemoved(self):
        # Arrange — the `do` and the brace it opens on different lines, with the block holding braces
        # of its own, which is what the depth walk is for.
        body = ("            do\n            {\n                if (ready) { Step(); }\n"
                "            }\n            while (more);\n")

        # Act
        deletions = mutants_of(self.method(body), "line removed")

        # Assert
        self.assertEqual([mutant.before for mutant in deletions if "while" in mutant.before], [])

    # GREEN_ON_BASE(characterization): the base removes it too, and it is the half the refusal must
    # not take with it — the two spellings are identical on the line itself.
    def test_Given_AnEmptyBodiedWhileLoop_When_MutantsAreGenerated_Then_ItIsStillRemoved(self):
        # Arrange — the control: the same spelling, closing no `do`, and its removal compiles. Without
        # it a reading that refused every `while` would satisfy the two cases above.
        body = "            Prepare();\n            while (Poll());\n"

        # Act
        deletions = mutants_of(self.method(body), "line removed")

        # Assert
        self.assertEqual([mutant.before for mutant in deletions if "while" in mutant.before],
                         ["while (Poll());"])


class MutableScopeTests(unittest.TestCase):
    """What a campaign may mutate, which has to be what a Unity build compiles.

    Unity's asset database does not import a `~`-suffixed directory, so a source under one compiles
    into nothing: mutating a line there leaves every assembly byte-identical, the run scores
    `NOT_BUILT`, and the campaign fails. A branch editing one could therefore never earn a passing
    campaign, and what it was told named a build state rather than a scope rule.
    """

    def test_Given_ASourceUnderATildeDirectory_When_TheScopeIsRead_Then_ItIsNotMutable(self):
        # Arrange — the live case: the starter sample exists twice, and this is the copy nothing
        # compiles.
        path = REPO_ROOT / "Packages/com.velvet.core/Samples~/StarterApp/StarterApp.cs"

        # Act / Assert — the file's presence rides along, because a path that is not there is not
        # mutable either and would satisfy this having read nothing.
        self.assertEqual((path.exists(), mutation_check.mutable(path, REPO_ROOT)), (True, False))

    # GREEN_ON_BASE(characterization): the base mutates this too, and it is the half a widened exclusion could take with it.
    def test_Given_ASourceUnderTheRuntime_When_TheScopeIsRead_Then_ItIsStillMutable(self):
        # Arrange — the control: a rule that excluded everything would satisfy the case above.
        path = REPO_ROOT / "Packages/com.velvet.core/Runtime/Routing/RouteTree.cs"

        # Act / Assert
        self.assertEqual((path.exists(), mutation_check.mutable(path, REPO_ROOT)), (True, True))

    # GREEN_ON_BASE(characterization): the base excludes these by name, and this says the rule that replaced the name still does.
    def test_Given_TheGeneratorSources_When_TheScopeIsRead_Then_TheyAreStillOut(self):
        # Arrange — named one directory at a time before, which is the same rule read off one
        # instance of it.
        path = REPO_ROOT / "Packages/com.velvet.core/Generators~/src/Velvet.StyleTable/StyleTable.cs"

        # Act / Assert
        self.assertFalse(mutation_check.mutable(path, REPO_ROOT))


class GenericCallRemovalTests(unittest.TestCase):
    """A call whose name carries type arguments, which the removal pattern could not read.

    `target.RegisterCallback<GeometryChangedEvent>(OnGeometry);` is a call whose deletion the tests
    should notice, and it matched nothing: 109 whole statements in this package passed every other
    reading and generated no mutant. The loss was silent — a line no operator reaches is reported the
    same way as a line with nothing to mutate.
    """

    def test_Given_ACallCarryingTypeArguments_When_MutantsAreGenerated_Then_ItIsRemovable(self):
        # Arrange
        text = "Prepare();\ntarget.RegisterCallback<GeometryChangedEvent>(OnGeometry);\n"

        # Act
        deletions = [mutant.before for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertIn("target.RegisterCallback<GeometryChangedEvent>(OnGeometry);", deletions)

    def test_Given_ANestedTypeArgument_When_MutantsAreGenerated_Then_ItIsStillRemovable(self):
        # Arrange — the argument list is read as a character class, so a generic inside it is inside
        # the class rather than outside the match.
        text = "Prepare();\nRegister<Dictionary<string, int>>(store);\n"

        # Act
        deletions = [mutant.before for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertIn("Register<Dictionary<string, int>>(store);", deletions)

    # GREEN_ON_BASE(characterization): the base does not read this as a call either, and it is what the widened class could start
    # reading — only running it says whether it did.
    def test_Given_AComparisonThatLooksLikeOne_When_MutantsAreGenerated_Then_ItIsNotRemoved(self):
        # Arrange — the control: `;` and `(` are deliberately outside the character class, so a
        # statement whose angle brackets are comparisons is not read as a call.
        text = "Prepare();\nvar ok = a < b && c > (d);\n"

        # Act
        deletions = [mutant.before for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertNotIn("var ok = a < b && c > (d);", deletions)

    # GREEN_ON_BASE(characterization): the base removes this, and it is the half the widening must not lose.
    def test_Given_APlainCall_When_MutantsAreGenerated_Then_ItIsStillRemovable(self):
        # Arrange — the half that already worked, and what the widened pattern must not lose.
        text = "Prepare();\nStep(now);\n"

        # Act
        deletions = [mutant.before for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertIn("Step(now);", deletions)


class LineRemovalReadingTests(unittest.TestCase):
    """What the removal reads a line as, and what it puts back in place of it.

    The pattern is anchored at a semicolon that ends the line, so read off the raw text it stops at
    anything trailing it — and both a comment and a semicolon inside a literal are things the
    compiler sees no statement boundary in. Read off the mask instead, the shape is the same one
    either way.
    """

    def test_Given_ARemovalTheRepositoryGenerates_When_ACommentIsAppendedToIt_Then_ItStillGenerates(self):
        # Arrange — the corpus a campaign mutates rather than a list of spellings, since a list is
        # only ever the shapes its author thought of.
        sources = [path for path in (REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs")
                   if mutation_check.mutable(path, REPO_ROOT)]

        # Act
        kept, lost = 0, []
        for path in sources:
            text = path.read_text()
            numbers = set(range(1, len(text.splitlines()) + 1))
            before = {mutant.line for mutant in mutation_check.mutations_for(path, text, numbers)
                      if mutant.operator == "line removed"}
            if not before:
                continue
            lines = text.splitlines(keepends=True)
            for number in before:
                lines[number - 1] = lines[number - 1].rstrip("\r\n") + "  // a trailing note\n"
            after = {mutant.line for mutant
                     in mutation_check.mutations_for(path, "".join(lines), numbers)
                     if mutant.operator == "line removed"}
            kept += len(before & after)
            lost += ["{}:{}".format(path.name, number) for number in sorted(before - after)]

        # Assert — the surviving count rides along, because a scan that found no removal to perturb
        # loses none of them by arithmetic.
        self.assertEqual((kept > 500, lost), (True, []))

    def test_Given_ASemicolonInsideALiteral_When_MutantsAreGenerated_Then_TheLineIsStillRemovable(self):
        # Arrange — the two spellings the package puts one in are the interpolated string and the
        # character literal, with the plain string beside them.
        text = ("var live = Track();\n"
                'log.Warning("stopped; nothing to flush");\n'
                'log.Warning($"{name} stopped; nothing to flush");\n'
                "builder.Append(name).Append(';');\n")

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [2, 3, 4])

    def test_Given_ACallWhoseNameStartsWithAKeyword_When_MutantsAreGenerated_Then_OnlyTheKeywordIsSkipped(self):
        # Arrange — line 3 has the removal shape and is refused for the reason `CONTROL_KEYWORD`
        # carries; line 2 is a call the same skip read as one of them.
        text = "var live = Track();\nreturns.Add(instr);\nreturn (live, instr);\n"

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert — one comparison over both, so neither direction can be met by an operator that
        # stopped generating and neither by one that skips nothing.
        self.assertEqual(deletions, [2])

    def test_Given_ABlockCommentSpanningTheRemovedLine_When_ItIsApplied_Then_BothHalvesOfItStay(self):
        # Arrange — one comment opening on the first removable line and closing on the second, so both
        # directions are read: the line carrying its opening, and the line carrying its closing.
        text = ("var live = Track();\n"
                "owners.Remove(child); /* the reason\n"
                "   it runs first */ tracker.Flush();\n")

        # Act
        removals = [applied(text, mutant) for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(removals, ["; /* the reason", "it runs first */ ;"])


class GuardRemovalTests(unittest.TestCase):
    def test_Given_ASingleLineReturnGuard_When_ItIsRemoved_Then_OnlyTheGuardGoes(self):
        # Arrange
        text = "        if (ownCts != _cts) return;\n"

        # Act
        results = [applied(text, mutant) for mutant in mutants_of(text, "guard removed")]

        # Assert
        self.assertEqual(results, [""])

    def test_Given_AGuardWhoseBodyIsABlock_When_MutantsAreGenerated_Then_ItIsNotOffered(self):
        # Arrange — deleting the condition of a multi-line guard would leave its block dangling.
        text = "if (ownCts != _cts)\n{\n    return;\n}\n"

        # Act
        guards = mutants_of(text, "guard removed")

        # Assert
        self.assertEqual(guards, [])

    def test_Given_AnAssignmentThatMerelyMentionsReturn_When_MutantsAreGenerated_Then_ItIsNotAGuard(self):
        # Arrange
        text = "var returned = Compute(a);\n"

        # Act
        guards = mutants_of(text, "guard removed")

        # Assert
        self.assertEqual(guards, [])


class DeclarationRemovalTests(unittest.TestCase):
    """What a deletion may not carry off: a name the code below it still reads.

    Five of these were mutants the three deleting operators offered and the compiler refused; the two
    declared green on the base are the boundary the refusals must not cross, and both go red under a
    detector widened to the bare `var` and `is` tokens. `MutantDeclarationRemovalTests` in
    `Generators~` reads the same removals with the compiler's own parser; these hold the spellings
    the generator recognises without one. Every assertion names a removal the operator still emits, so
    a refusal that swallowed its whole operator would not pass here either.

    The last three are the `out` arm's exemptions, one per operator, since the text handed to the
    pattern differs at each: the whole line, the cut alone, the guard statement.
    """

    def test_Given_ADeconstructingDeclaration_When_MutantsAreGenerated_Then_ItIsNotDeleted(self):
        # Arrange — `var` satisfies the removal pattern's leading identifier, and the argument
        # list that pattern looks for runs to the initializer's last `)`, so the whole declaration
        # read as a call whose value was thrown away.
        text = "Track();\nvar (state, setState) = UseState(0);\nsetState.Invoke(1);\n"

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert — line 1 is absent for the reason `StatementChainCutTests` gives, and line 3 is what
        # says the operator still deletes.
        self.assertEqual(deletions, [3])

    def test_Given_AnOutVariableDeclaration_When_MutantsAreGenerated_Then_TheCallIsNotDeleted(self):
        # Arrange — the same removal by a different spelling: the declaration is an argument.
        text = "Track();\nStyle.TryExtract(names, out var spec);\nApply(spec);\n"

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [3])

    def test_Given_AGuardDeclaringAPatternVariable_When_MutantsAreGenerated_Then_ItIsNotRemoved(self):
        # Arrange
        text = ("if (node is not ComponentFiber fiber) return;\n"
                "if (fiber.IsDisposed) return;\n"
                "fiber.Dispose();\n")

        # Act
        removals = [mutant.line for mutant in mutants_of(text, "guard removed")]

        # Assert
        self.assertEqual(removals, [2])

    def test_Given_AClauseDeclaringAnOutVariable_When_MutantsAreGenerated_Then_ItIsNotCut(self):
        # Arrange
        text = "var ok = ready && Map.TryGetValue(key, out var hit) && hit != null;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var ok = ready && Map.TryGetValue(key, out var hit);"])

    def test_Given_AClauseDeclaringUnderAPropertyPattern_When_MutantsAreGenerated_Then_ItIsNotCut(self):
        # Arrange — a designation the type-pattern spelling does not reach, because what precedes it
        # is a brace rather than a type. This package writes it, and the parser guard is what found it.
        text = "var ok = ready && layer is { } resolved && resolved.Depth > 0;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var ok = ready && layer is { } resolved;"])

    # GREEN_ON_BASE(characterization): the null test the refusals beside it must not swallow.
    def test_Given_AClauseTestingForNull_When_MutantsAreGenerated_Then_ItIsStillCut(self):
        # Arrange — `is null` declares nothing, and this package writes its null checks that way.
        text = "var ok = ready && next is null;\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var ok = ready;"])

    # GREEN_ON_BASE(characterization): a loop variable the line that declared it carries away with it.
    def test_Given_ASingleLineForeach_When_MutantsAreGenerated_Then_ItIsStillDeleted(self):
        # Arrange — the detector shapes `var` to a deconstruction rather than taking the bare token,
        # and this is the line that decides it.
        text = "Track();\nforeach (var f in drop) owners.Remove(f);\n"

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [2])

    def test_Given_AnOutArgumentAssigningAMember_When_MutantsAreGenerated_Then_TheCallIsDeleted(self):
        # Arrange — an assignment target declares nothing, and line 3 is the shape that decides the
        # exemption cannot be a token count instead: a type carrying a comma has no bound to count to.
        text = ("Track();\n"
                "Inset.Compute(names, out binding.Left, out binding.Top);\n"
                "Map.TryGetValue(key, out Dictionary<int, string> found);\n"
                "Apply(binding, found);\n")

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [2, 4])

    def test_Given_AClauseDiscardingItsOutArgument_When_MutantsAreGenerated_Then_ItIsCut(self):
        # Arrange
        text = "var ok = ready && Map.TryGetValue(key, out _) && Map.TryGetValue(other, out var hit);\n"

        # Act
        cuts = [applied(text, mutant) for mutant in mutants_of(text, "clause removed")]

        # Assert
        self.assertEqual(cuts, ["var ok = ready && Map.TryGetValue(other, out var hit);"])

    def test_Given_AGuardDiscardingItsOutArgument_When_MutantsAreGenerated_Then_ItIsRemoved(self):
        # Arrange — the dedupe guard this package writes, whose deletion is the question worth asking
        # of it: whether anything notices the second call no longer being turned away.
        text = ("if (_bindings.TryGetValue(root, out _)) return;\n"
                "if (_bindings.TryGetValue(root, out var held)) return;\n"
                "_bindings.Add(root, new Binding(root));\n")

        # Act
        removals = [mutant.line for mutant in mutants_of(text, "guard removed")]

        # Assert
        self.assertEqual(removals, [1])


class AssignmentRemovalTests(unittest.TestCase):
    """The line removal's write arm, and the writes it refuses because the read after one could be
    left with nothing assigning it.

    A case that refuses a write also names a removal the arm still emits, so neither an arm that
    stopped emitting nor a refusal that swallowed the arm passes it.
    """

    def test_Given_AResetHelper_When_MutantsAreGenerated_Then_EachWriteIsRemovable(self):
        # Arrange — the shapes a pool reset writes: a field, a member path, an element, a compound
        # write and an increment.
        text = textwrap.dedent("""\
            void Reset(Node node, int i)
            {
                _phase = Phase.Idle;
                node.Key = null;
                _slots[i] = default;
                _mask &= ~Bit;
                _version++;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [3, 4, 5, 6, 7])

    def test_Given_ALocalDeclaredWithoutAValue_When_MutantsAreGenerated_Then_ItsWritesAreNotRemoved(self):
        # Arrange — line 13 reads `suffix` after an `if` whose arms are its only writers, so removing
        # either strands the read.
        text = textwrap.dedent("""\
            void Read(bool flag)
            {
                string suffix;
                if (flag)
                {
                    suffix = First();
                }
                else
                {
                    suffix = Second();
                }
                _count = 0;
                Use(suffix);
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [12, 13])

    def test_Given_ALocalOfAGenericTypeOverATuple_When_MutantsAreGenerated_Then_ItsWriteIsNotRemoved(self):
        # Arrange — the shape a task source settles with: line 6 is the only write before line 9
        # reads `waiting`, and the declaration's type argument is a tuple holding a generic.
        text = textwrap.dedent("""\
            void Settle()
            {
                List<(Action<object?> Continuation, object? State)> waiting;
                lock (_gate)
                {
                    waiting = _waiting;
                    _waiting = null;
                }
                Run(waiting);
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [7, 9])

    def test_Given_AnArgumentContinuingOntoALine_When_MutantsAreGenerated_Then_ItIsNotReadAsADeclaration(self):
        # Arrange — line 4 reads as `ref` declaring `BorderColor` when taken on its own, and it is the
        # tail of the call line 3 opens.
        text = textwrap.dedent("""\
            void Capture(Element element)
            {
                CaptureFace(element.Color,
                    ref BorderColor, ref BorderFromInline);
                BorderColor = default;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [5])

    def test_Given_AnArgumentContinuingOverSeveralLines_When_MutantsAreGenerated_Then_ItsMiddleIsNotADeclaration(self):
        # Arrange — line 4 closes what it opens, so only the line above it says it is no statement.
        text = textwrap.dedent("""\
            void Capture(Element element)
            {
                CaptureFace(element.Color,
                    ref BorderColor, ref BorderFromInline,
                    element.Width);
                BorderColor = default;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [6])

    def test_Given_AnOutParameterAssignedFieldByField_When_MutantsAreGenerated_Then_ItsFieldWritesAreNotRemoved(self):
        # Arrange — together lines 3 and 4 are what assigns `size` before the method returns.
        text = textwrap.dedent("""\
            static void Measure(out Size size)
            {
                size.Width = 1;
                size.Height = 2;
                _count = 0;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [5])

    def test_Given_AnOutArgumentInACallWhoseLambdaOpensTheBrace_When_MutantsAreGenerated_Then_ItIsNoParameter(self):
        # Arrange — line 3's first argument list closes before the header ends, so it is a call's.
        text = textwrap.dedent("""\
            void Run(int key)
            {
                Find(key, out var node).Then(() =>
                {
                    Use(node);
                });
                node.Value = 1;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [5, 7])

    def test_Given_AnOutArgumentInAnExpressionBodiedMember_When_MutantsAreGenerated_Then_ItIsNoParameter(self):
        # Arrange — `fiber` on line 3 is an argument's declaration, which leaves a write to one of its
        # members free; read as a parameter, it refused line 6, which writes through another `fiber`.
        text = textwrap.dedent("""\
            internal sealed class Probe
            {
                internal bool Has(int key) => _map.TryGetValue(key, out var fiber) && fiber != null;
                internal void Apply(Fiber fiber)
                {
                    fiber.Props = _props;
                }
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [6])

    def test_Given_AnOutParameter_When_MutantsAreGenerated_Then_ItsWriteIsNotRemoved(self):
        # Arrange — removing line 3 leaves the method returning with `value` unassigned.
        text = textwrap.dedent("""\
            static bool TryRead(string s, out int value)
            {
                value = 0;
                _reads = s.Length;
                return false;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [4])

    def test_Given_AnOutArgumentWrittenWhereTheCallFailed_When_MutantsAreGenerated_Then_ThatWriteIsNotRemoved(self):
        # Arrange — line 5 is the only write on the path where the lookup found nothing, and line 8
        # reads `entries` on both paths.
        text = textwrap.dedent("""\
            void Add(int key, Entry item)
            {
                if (!_map.TryGetValue(key, out var entries))
                {
                    entries = new List<Entry>();
                    _map[key] = entries;
                }
                entries.Add(item);
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [6, 8])

    def test_Given_AStructLocalAssignedFieldByField_When_MutantsAreGenerated_Then_ItsFieldWritesAreNotRemoved(self):
        # Arrange — together lines 4 and 5 are what assigns `size` before line 7 reads it whole.
        text = textwrap.dedent("""\
            void Fill()
            {
                Size size;
                size.Width = 1;
                size.Height = 2;
                _count = 0;
                Use(size);
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [6, 7])

    def test_Given_AnOutParameterWrittenThroughAnElement_When_MutantsAreGenerated_Then_OnlyTheWholeWriteIsKept(self):
        # Arrange — indexing reads `buffer`, so line 4 can never be the write that assigns it.
        text = textwrap.dedent("""\
            static void Fill(out int[] buffer)
            {
                buffer = new int[4];
                buffer[0] = 1;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [4])

    def test_Given_APatternVariable_When_MutantsAreGenerated_Then_OnlyItsWholeWriteIsKept(self):
        # Arrange — line 5 is the only write on the path where the pattern failed, and line 7 writes
        # through a variable the match or that write has already assigned whole.
        text = textwrap.dedent("""\
            void Bind(object source)
            {
                if (source is not Fiber typed)
                {
                    typed = Fallback();
                }
                typed.Ready = true;
                Use(typed);
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [7, 8])

    def test_Given_ACompoundWriteToAPatternVariable_When_MutantsAreGenerated_Then_ItIsRemovable(self):
        # Arrange — a compound write and an increment read their target first, so neither can be what
        # assigns it.
        text = textwrap.dedent("""\
            void Count(object source)
            {
                if (source is int n)
                {
                    n += 2;
                    n++;
                    Use(n);
                }
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [5, 6, 7])

    def test_Given_AStructConstructor_When_MutantsAreGenerated_Then_OnlyTheMethodsWriteIsRemoved(self):
        # Arrange — the constructor has to assign `A` before it returns; `Clear` has no such duty.
        text = textwrap.dedent("""\
            internal struct Pair
            {
                public int A;
                public Pair(int a)
                {
                    A = a;
                }
                public void Clear()
                {
                    A = 0;
                }
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [10])

    def test_Given_AStructConstraintOnAMethod_When_MutantsAreGenerated_Then_ItsLocalsAreStillRead(self):
        # Arrange — `where T : struct` names the keyword after the parameter list, and a header read
        # whole takes the method for a struct, whose locals are never read as declarations.
        text = textwrap.dedent("""\
            internal sealed class Reader
            {
                T Read<T>(bool flag) where T : struct
                {
                    T found;
                    if (flag)
                    {
                        found = Load<T>();
                    }
                    else
                    {
                        found = default;
                    }
                    _reads = 1;
                    return found;
                }
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [14])

    def test_Given_AWriteClosingAnInitializer_When_MutantsAreGenerated_Then_ItIsNotRemoved(self):
        # Arrange — the value runs to the semicolon, so line 4's write carries the brace that closes
        # line 3's initializer.
        text = textwrap.dedent("""\
            void Build()
            {
                var box = new Box {
                    Left = 1 };
                _count = 0;
            }
            """)

        # Act
        deletions = [mutant.line for mutant in mutants_of(text, "line removed")]

        # Assert
        self.assertEqual(deletions, [5])


class LogicFlipTests(unittest.TestCase):
    """Which joins the logic operator may flip: not one whose statement binds a name on only some paths.

    A flip there can leave a read of that name unassigned, which the campaign scores unmeasured and fails
    on -- the outcome `DeclarationRemovalTests` holds for a removal, reached by a rewrite. Compiled
    against the reference set Unity's own build uses, over the assembly `Runtime/` without its tests
    compiles into: 90 of the 127 flips the refusal removes there failed exactly that way, and the other
    37 are what it costs. `Generators~/README.md` carries that reading and where it stops.

    Each case declared green on the base is a boundary the refusal must not cross, and each goes red
    under its own widening of one. Between them they are also what says the operator still fires, so a
    refusal that swallowed it whole would not pass here either.
    """

    def test_Given_APatternVariableTheRightOperandReads_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the shape a branch's own campaign stopped on: under `||` the right operand runs
        # where the pattern did not match.
        text = ("if (existing is Resource typed && ObjectIs.AreEqual(typed.Key, key))\n"
                "{\n"
                "    return typed;\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    # GREEN_ON_BASE(characterization): the `out var` a first clause evaluates whichever way its join goes.
    # Refusing this one too would cost mutants nothing has shown to be broken.
    def test_Given_AnOutVariableInTheFirstClause_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("if (Map.TryGetValue(key, out var rules) && rules.Count > 0)\n"
                "{\n"
                "    Apply(rules);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, ["if (Map.TryGetValue(key, out var rules) || rules.Count > 0)"])

    def test_Given_AnOutVariablePastTheFirstClause_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the same `out var`, one clause along: `||` reaches the block without the call
        # having run, so the read there is of a local nothing assigned.
        text = ("if (routeId != null && errors.TryGetValue(routeId, out var ex))\n"
                "{\n"
                "    Report(ex);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_AChainWhoseFirstClauseBinds_When_MutantsAreGenerated_Then_NeitherJoinIsFlipped(self):
        # Arrange -- the first join's flip strands the read standing next to it and the second join's
        # strands the block's, which is why the answer is the statement's rather than each join's.
        text = ("if (a is Style style && style.Ready && Live(b))\n"
                "{\n"
                "    Apply(style);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_AnIsNotGuardJoinedByOr_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the other join spelling, where what the flip strands is the code after the guard.
        text = ("if (node is not Fiber fiber || fiber.IsDisposed) return;\n"
                "fiber.Dispose();\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_ABindingOnAnEarlierLineOfTheCondition_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the join and the binding it strands are on different lines, so a reading of the
        # line the flip lands on finds nothing to refuse.
        text = ("if (next.Operand is not FieldReference field\n"
                "    || next.Next is null)\n"
                "{\n"
                "    return NotMatched;\n"
                "}\n"
                "Use(field);\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    # GREEN_ON_BASE(characterization): a discarded argument, which binds no name for a flip to strand.
    # It is what separates the `out` arm the refusal reads from the bare token.
    def test_Given_ADiscardedOutArgumentPastTheFirstClause_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("if (ready && Map.TryGetValue(key, out _))\n"
                "{\n"
                "    Apply(key);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, ["if (ready || Map.TryGetValue(key, out _))"])

    # GREEN_ON_BASE(characterization): an `out` naming a variable the statement did not declare, past
    # a join and written above it. No ordering of the clauses leaves it unwritten, so the question the
    # flip asks -- here, whether anything notices the clip stop suppressing the shadow -- is a real one.
    def test_Given_ABareOutTheLineAboveWrites_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("var spec = default(ShadowSpec);\n"
                "var want = !clipActive && StyleShadowClass.TryExtract(classNames, out spec);\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(
            flips, ["var want = !clipActive || StyleShadowClass.TryExtract(classNames, out spec);"])

    def test_Given_ABareOutNothingAboveWrites_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- an `out` parameter of the enclosing method: the flip leaves a path returning
        # without having written it, which is `CS0177` rather than the `CS0165` a local gives.
        text = ("private static bool TryParseAngle(string cls, out float angleDeg)\n"
                "{\n"
                "    return !negative && TryDirectionAngle(cls, out angleDeg);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_ABareOutWrittenOnlyInsideABlockAbove_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the write runs on some of the paths reaching the statement and not others, so it
        # says nothing about what is written where the flip lands.
        text = ("Rect bounds;\n"
                "if (fallback)\n"
                "{\n"
                "    bounds = Rect.zero;\n"
                "}\n"
                "var want = ready && TryComputeBounds(spec, out bounds);\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_ABracelessBodyWritingTheBareOut_When_MutantsAreGenerated_Then_NoHeaderIsFlipped(self):
        # Arrange -- the same write, spelled without the braces the walk counts. Compiled against the
        # reference set Unity's own build uses, each of these bases compiles and its flip is `CS0165`.
        # A `lock`, a `using` and a braceless `do` were compiled too and are not here: their bodies run
        # on every path, so refusing those is what the lead reading costs rather than what it is for.
        headers = ("if (fallback)", "while (fallback)", "for (var i = 0; i < limit; i++)",
                   "foreach (var each in items)", "if (fallback) { Reset(); }\nelse")
        texts = {header: ("int bounds;\n" + header + "\n"
                          "    bounds = 0;\n"
                          "if (ready && TryCompute(spec, out bounds))\n"
                          "{\n"
                          "    Use(bounds);\n"
                          "}\n") for header in headers}

        # Act
        flipped = sorted(header for header, text in texts.items() if mutants_of(text, "logic"))

        # Assert
        self.assertEqual(flipped, [])

    def test_Given_ABracelessElseOnItsHeadersOwnLine_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the write shares the header's line, so no line above it is the lead the reading
        # would refuse; `else` is what stands where a declaration's type stands.
        text = ("int bounds;\n"
                "if (fallback) { Reset(); }\n"
                "else bounds = 0;\n"
                "if (ready && TryCompute(spec, out bounds))\n"
                "{\n"
                "    Use(bounds);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_AWriteInAnEarlierSwitchArm_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the write is a finished statement of the same block at the same brace depth, and
        # the arm holding it is entered where this one is not.
        text = ("int bounds;\n"
                "switch (kind)\n"
                "{\n"
                "    case Kind.First:\n"
                "        Reset();\n"
                "        bounds = 0;\n"
                "        break;\n"
                "    default:\n"
                "        if (ready && TryCompute(spec, out bounds))\n"
                "        {\n"
                "            Use(bounds);\n"
                "        }\n"
                "        break;\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_AWriteInATernarysFalseArm_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the write is a finished statement of the same block at the same brace depth, and
        # the ternary holding it runs it on one side of the condition only.
        text = ("int bounds;\n"
                "var x = fallback ? 1 :\n"
                "    bounds = 0;\n"
                "Use(x);\n"
                "if (ready && TryCompute(spec, out bounds))\n"
                "{\n"
                "    Use(bounds);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    # GREEN_ON_BASE(characterization): a write behind a finished statement keeps the flip it had.
    # Measured over the package, this lead recovers two of the twenty flips the three recover.
    def test_Given_ABareOutWrittenBehindAFinishedStatement_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("axis = default;\n"
                "value = 0f;\n"
                "var found = core.StartsWith(\"scale-\") && TryGetAxisScale(core, out value);\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(
            flips, ["var found = core.StartsWith(\"scale-\") || TryGetAxisScale(core, out value);"])

    # GREEN_ON_BASE(characterization): a write behind the brace opening its own block keeps the flip.
    # Measured over the package, that lead recovers ten of the twenty flips the three recover.
    def test_Given_ABareOutWrittenBehindAnOpeningBrace_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("internal static bool TryParse(string core, out ArbitraryStyle style)\n"
                "{\n"
                "    style = default;\n"
                "    return TryParsePaletteColor(core, out style)\n"
                "        || TryParsePresetLength(core, out style);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, ["&& TryParsePresetLength(core, out style);"])

    # GREEN_ON_BASE(characterization): a write behind a closed block keeps the flip it had.
    # Measured over the package, that lead recovers the remaining eight of those twenty.
    def test_Given_ABareOutWrittenBehindAClosedBlock_When_MutantsAreGenerated_Then_TheJoinIsStillFlipped(self):
        # Arrange
        text = ("if (!bound)\n"
                "{\n"
                "    return;\n"
                "}\n"
                "var spec = default(ShadowSpec);\n"
                "var want = !clipActive && StyleShadowClass.TryExtract(classNames, out spec);\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(
            flips, ["var want = !clipActive || StyleShadowClass.TryExtract(classNames, out spec);"])

    def test_Given_ABareOutWrittenOutsideTheBlockHoldingIt_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- this write does run on every path reaching the statement, and the reading stops at
        # the block anyway rather than deciding where the member it is walking starts.
        text = ("angleDeg = 0f;\n"
                "if (body.StartsWith(DirActivator))\n"
                "{\n"
                "    return !negative && TryDirectionAngle(body, out angleDeg);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_APatternWhoseOpeningLineEndsWithABrace_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- that brace is what ends a statement in the removal's reading, so a walk stopping
        # at it starts this statement below the `is` and finds no designation on it.
        text = ("if (fiber is {\n"
                "        IsDisposed: false\n"
                "    } live && live.Ready)\n"
                "{\n"
                "    Use(live);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_ALambdaBlockBetweenTheBindingAndTheJoin_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the same reading, reached across a lambda body rather than a pattern's braces.
        text = ("if (node is Fiber fiber && children.All(child =>\n"
                "    {\n"
                "        return child.Ready;\n"
                "    }) && fiber.Ready)\n"
                "{\n"
                "    Use(fiber);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])

    def test_Given_AJoinInFrontOfAMultiLinePattern_When_MutantsAreGenerated_Then_TheJoinIsNotFlipped(self):
        # Arrange -- the mirror of the two above, with the designation below the join rather than above
        # it, which is the other half of the walk.
        text = ("if (ready && fiber is {\n"
                "        IsDisposed: false\n"
                "    } live)\n"
                "{\n"
                "    Use(live);\n"
                "}\n")

        # Act
        flips = [applied(text, mutant) for mutant in mutants_of(text, "logic")]

        # Assert
        self.assertEqual(flips, [])


class RepositoryReachTests(unittest.TestCase):
    """The operators exist because two real defects survived every other one. Pin that they reach them."""

    def test_Given_TheNestedOutletGuard_When_MutantsAreGenerated_Then_ItsDepthClauseIsRemovable(self):
        # Arrange — a route depth past the matched chain must return null rather than index past its end.
        source = (RUNTIME / "Hooks/Hooks.cs").read_text()
        line = next(number for number, text in enumerate(source.splitlines(), 1)
                    if "depth > location.Matches.Count" in text)

        # Act
        cuts = [mutant.before.strip() for mutant
                in mutation_check.mutations_for(Path("Hooks.cs"), source, {line})
                if mutant.operator == "clause removed"]

        # Assert
        self.assertIn("|| depth > location.Matches.Count", cuts)

    def test_Given_TheSupersessionChecks_When_MutantsAreGenerated_Then_EachIsRemovable(self):
        # Arrange — a superseded loader round must not report into the live one.
        source = (RUNTIME / "Routing/RouteLoaderRunner.cs").read_text()

        # Act
        guards = {mutant.before for mutant in mutants_of(source, "guard removed")}

        # Assert
        self.assertEqual(guards, {"if (!ReferenceEquals(round, _liveRound)) return;"})


class ForwardingMemberTests(unittest.TestCase):
    """Which expression bodies read as forwarding, and the cut each one is given.

    The class body under test starts on line 5 of the source `forwards_in` builds.
    """

    @staticmethod
    def forwards_in(members):
        """(name, first line, last line, cut, declined) of each forwarding member of a class body."""
        text = "namespace Velvet\n{\n    internal sealed class Probe\n    {\n" + textwrap.indent(
            textwrap.dedent(members), " " * 8) + "    }\n}\n"
        return [(forwarder.name, forwarder.first, forwarder.last, forwarder.cut, forwarder.declined)
                for forwarder in mutation_check.forwarding_members(text)]

    def test_Given_AWrappedForwardingBody_When_MembersAreRead_Then_ItsLinesRunFromTheHeadToTheBody(self):
        # Arrange — the spelling whose tail `STATEMENT_BOUNDARY` refuses to remove.
        members = ("public bool TrySetCanceled(CancellationToken token) =>\n"
                   "    _core.TrySetCanceled(token);\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.TrySetCanceled", 5, 6, "default", None)])

    def test_Given_AVoidForward_When_MembersAreRead_Then_ItsCutIsAnEmptyBody(self):
        # Arrange — `default` is no body for a member that returns nothing.
        members = "public void Return(Item item) => _pool.Return(item);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Return", 5, 5, "{ }", None)])

    def test_Given_ABodyPassingALiteral_When_MembersAreRead_Then_ItIsNotAForward(self):
        # Arrange
        members = "public bool Set() => _core.Set(1);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ABodyPassingAKeyword_When_MembersAreRead_Then_ItIsNotAForward(self):
        # Arrange — `default` is a word the identifier chain would otherwise take as a name.
        members = "public bool TrySetResult() => _source.TrySettle(default);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ALambdaInsideAMethodBody_When_MembersAreRead_Then_ItIsNotAForward(self):
        # Arrange — the lambda's shape is a forward's; it stands in a method's braces, not the type's.
        members = ("public void Run()\n"
                   "{\n"
                   "    Action run = () => _core.Run();\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AFieldInitialisedWithALambda_When_MembersAreRead_Then_ItIsNotAForward(self):
        # Arrange — the arrow stands in the type's braces, behind the `=` of a field, and the lambda's
        # parameter is a name the head would otherwise end on.
        members = "readonly Func<Key, int> _read = key => _core.Read(key);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AForwardWithAnOutArgument_When_MembersAreRead_Then_ItIsGivenNoCut(self):
        # Arrange — neither cut assigns the out parameter.
        members = "public bool TryRead(out int value) => _core.TryRead(out value);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.TryRead", 5, 5, None, "an out parameter")])

    def test_Given_AnAsyncForward_When_MembersAreRead_Then_ItIsGivenNoCut(self):
        # Arrange — which cut fits depends on whether the task carries a result, which the head's
        # spelling does not settle.
        members = "public async VelvetTask Run() => _core.RunAsync();\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Run", 5, 5, None, "async")])

    def test_Given_AConstrainedGenericForward_When_MembersAreRead_Then_ItIsNamedForItsMethod(self):
        # Arrange — the constraint stands between the parameters and the arrow, where the head is
        # otherwise read for its name.
        members = "public T Get<T>(Key key) where T : class => _core.Get<T>(key);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Get", 5, 5, "default", None)])

    def test_Given_AForwardingConstructorWithAnInitializer_When_MembersAreRead_Then_ItsCutIsAnEmptyBody(self):
        # Arrange
        members = "internal Probe(Sheet sheet) : base(sheet) => Read(sheet);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Probe", 5, 5, "{ }", None)])

    def test_Given_AnEqualityOperator_When_MembersAreRead_Then_ItIsAForward(self):
        # Arrange — `==` is the `=` a field is told apart by, inside an operator's own token.
        members = "public static bool operator ==(Probe left, Probe right) => left.Equals(right);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.operator ==", 5, 5, "default", None)])

    def test_Given_AGetAccessor_When_MembersAreRead_Then_ItIsNamedForItsProperty(self):
        # Arrange
        members = ("public string Text\n"
                   "{\n"
                   "    get => _props.Text;\n"
                   "    set { _props.Text = value; }\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Text.get", 7, 7, "default", None)])

    def test_Given_AGenericMethodReturningAGenericType_When_MembersAreRead_Then_ItIsNamedForTheMethod(self):
        # Arrange — the return type's argument list and the method's both stand in the head, and the
        # first `<` is the return type's.
        members = "public List<T> Get<T>(int x) => Inner<T>(x);\n"

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Get", 5, 5, "default", None)])

    def test_Given_ATupleTypedPropertyWithAnAccessor_When_MembersAreRead_Then_TheAccessorIsAForward(self):
        # Arrange — the head holds a parenthesis without being a method's.
        members = ("public (int, int) Pair\n"
                   "{\n"
                   "    get => _core.Pair;\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Pair.get", 7, 7, "default", None)])

    def test_Given_APropertyWithAnAttributeArgument_When_MembersAreRead_Then_TheAccessorIsAForward(self):
        # Arrange
        members = ("[Obsolete(Reason)]\n"
                   "public int Count\n"
                   "{\n"
                   "    get => _core.Count;\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Count.get", 8, 8, "default", None)])

    def test_Given_AMethodWithTwoConstraintClauses_When_MembersAreRead_Then_ItsLocalFunctionIsNotAForward(self):
        # Arrange — `class where` across the two clauses has the spelling of a type's head.
        members = ("public void Run<T, U>()\n"
                   "    where T : class\n"
                   "    where U : struct\n"
                   "{\n"
                   "    int Read() => _core.Read();\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ANestedType_When_MembersAreRead_Then_ItIsNamedUnderItsOuterType(self):
        # Arrange
        members = ("internal sealed class Inner\n"
                   "{\n"
                   "    public short Version => _core.Version;\n"
                   "}\n")

        # Act
        found = self.forwards_in(members)

        # Assert
        self.assertEqual(found, [("Probe.Inner.Version", 7, 7, "default", None)])

    def test_Given_TypesOfOneNameDifferingInArity_When_PairsAreRead_Then_TheMemberIsPaired(self):
        # Arrange — a runner without a result beside one with a result.
        text = textwrap.dedent("""\
            internal sealed class Method<TStateMachine>
            {
                public short Version => _core.Version;
            }
            internal sealed class Method<TStateMachine, T>
            {
                public short Version => _core.Version;
            }
            """)

        # Act
        paired = mutation_check.paired_forwards(mutation_check.forwarding_members(text))

        # Assert
        self.assertEqual(paired, {("Method", "Version")})

    def test_Given_AGenericAndANonGenericTypeForwardingOneMember_When_PairsAreRead_Then_TheMemberIsPaired(self):
        # Arrange
        text = textwrap.dedent("""\
            internal sealed class Source
            {
                public short Version => _core.Version;
                public bool IsPooled => _isPooled;
            }
            internal sealed class Source<T>
            {
                public short Version => _core.Version;
            }
            """)

        # Act
        paired = mutation_check.paired_forwards(mutation_check.forwarding_members(text))

        # Assert
        self.assertEqual(paired, {("Source", "Version")})


class ForwardedBodyReachTests(unittest.TestCase):
    """`FORWARD_BODY` is the shape no operator emits inside; this is what says so of the package."""

    def test_Given_ThePackagesForwardingMembers_When_MutantsAreGenerated_Then_NoneLandsInAForwardedBody(self):
        # Arrange
        sources = [path for path in sorted((REPO_ROOT / mutation_check.PACKAGE).rglob("*.cs"))
                   if mutation_check.mutable(path, REPO_ROOT)]

        # Act
        members, inside = 0, 0
        for path in sources:
            text = path.read_text()
            forwarders = mutation_check.forwarding_members(text)
            lines = {number for forwarder in forwarders
                     for number in range(forwarder.first, forwarder.last + 1)}
            mutants = mutation_check.mutations_for(path, text, lines)
            members += len(forwarders)
            inside += len(mutation_check.inside_forwarded_bodies(text, forwarders, mutants))

        # Assert — with the count beside it, since a reading that finds no member finds no mutant in one.
        self.assertEqual((members > 100, inside), (True, 0))


class ForwardingReachReportTests(unittest.TestCase):
    """A changed forwarding member is named, since no mutant edits what it forwards."""

    DEFAULTED = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static void Run(Fiber fiber, bool twice = false) => Executor.Run(fiber, twice);
            }
        }
        """)

    def test_Given_AForwarderWhoseHeadCarriesALiteral_When_TheReachIsListed_Then_TheForwardIsNamed(self):
        # Arrange — the `false` is mutated, so the member's one line reads as reached, and the
        # unreached list says nothing about the forward standing on it.
        campaign = StubbedCampaign(self.DEFAULTED)

        # Act
        campaign.run("--list")

        # Assert
        self.assertIn("  forwards   Packages/com.velvet.core/Runtime/Probe.cs:5 Probe.Run",
                      campaign.printed.splitlines())

    # GREEN_ON_BASE(characterization): a block body calling the same member names no forward, as the base names none.
    def test_Given_ABlockBodyCallingTheSameMember_When_TheReachIsListed_Then_NoForwardIsNamed(self):
        # Arrange — the control for the case above: a body an operator does reach.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal static class Probe
                {
                    internal static void Run(Fiber fiber, bool twice = false)
                    {
                        Executor.Run(fiber, twice);
                    }
                }
            }
            """))

        # Act
        campaign.run("--list")

        # Assert
        self.assertNotIn("forwards", campaign.printed)


class ForwarderCensusTests(unittest.TestCase):
    """The inventory a cut sweep over forwarding members starts from."""

    @staticmethod
    def load_census():
        spec = importlib.util.spec_from_file_location(
            "forwarder_census", Path(__file__).with_name("forwarder_census.py"))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return module

    def test_Given_APairedForwarder_When_TheCensusIsRendered_Then_ItsRowCarriesThePairAndTheCut(self):
        # Arrange
        census = self.load_census()
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Source
                {
                    public VelvetTaskStatus GetStatus(short version) => _core.GetStatus(version);
                }
                internal sealed class Source<T>
                {
                    public VelvetTaskStatus GetStatus(short version) => _core.GetStatus(version);
                }
            }
            """))

        # Act
        rows = census.render(campaign.project, census.census(campaign.project, [campaign.source]), False)

        # Assert
        self.assertEqual(rows[0], "\t".join(("Packages/com.velvet.core/Runtime/Probe.cs:5-5",
                                             "Source.GetStatus", "member", "paired", "default", "0",
                                             "0")))

    def test_Given_AFileNamedRelativeToTheProject_When_TheCensusRunsElsewhere_Then_ItReadsThatFile(self):
        # Arrange
        census = self.load_census()
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    public short Version => _core.Version;
                }
            }
            """))
        spoken = io.StringIO()

        # Act
        with contextlib.redirect_stdout(spoken):
            census.main(["--project", str(campaign.project), StubbedCampaign.SOURCE])

        # Assert
        self.assertIn("\tProbe.Version\t", spoken.getvalue())

    def test_Given_ANamedFileThatIsMissing_When_TheCensusRuns_Then_ItExitsNonZero(self):
        # Arrange — an empty census would otherwise read as a file holding no forwarding member.
        census = self.load_census()
        campaign = StubbedCampaign()

        # Act
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            code = census.main(["--project", str(campaign.project),
                                "Packages/com.velvet.core/Runtime/Nope.cs"])

        # Assert
        self.assertNotEqual(code, 0)


class GenerationHealthTests(unittest.TestCase):
    # GREEN_ON_BASE(characterization): cuts stayed balanced before, and must across the new operator reach.
    def test_Given_EveryRuntimeSource_When_MutantsAreGenerated_Then_NoCutSpansAnUnbalancedParenthesis(self):
        # Arrange — an unbalanced cut compiles nowhere, and uncompilable noise hides real survivors.
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act — counted through the mask rather than over the raw text. A cut carrying
        # `EndsWith(")", …)` holds a parenthesis the compiler never sees, and reading it raw reports
        # a balanced cut as broken; every operator here reads code the same way.
        unbalanced = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                if code_parens(mutant.before) != 0:
                    unbalanced.append(f"{path.name}:{mutant.line} {mutant.before.strip()}")

        # Assert — the source count rides along because an empty scan satisfies "none unbalanced".
        self.assertEqual((len(sources) > 200, unbalanced), (True, []))

    def test_Given_EveryRuntimeSource_When_MutantsAreGenerated_Then_NoRemovalLeavesADanglingJoin(self):
        # Arrange
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act
        dangling = []
        for path in sources:
            text = path.read_text()
            for mutant in mutants_of(text, "clause removed"):
                remainder = applied(text, mutant)
                if remainder.endswith(("&&", "||")) or "&&)" in remainder or "||)" in remainder:
                    dangling.append(f"{path.name}:{mutant.line} {remainder}")

        # Assert
        self.assertEqual((len(sources) > 200, dangling), (True, []))

    def test_Given_EveryMutableSource_When_TheJoinRefusalIsAsked_Then_ItFiresWhereTheBaselineRecords(self):
        # Arrange — the corpus a campaign may mutate, which is what `mutable` decides and what every
        # verdict is read over; the scans above take Runtime with its tests off, which is neither.
        # A name only the branch carries is read here rather than beside the import, since one at
        # module scope stops the base tree loading any case in this file.
        recorded = (REPO_ROOT / mutation_check.REFUSAL_BASELINE).read_text().splitlines()

        # Act
        measured = mutation_check.refusal_census(REPO_ROOT)
        added = sorted(set(measured) - set(recorded))
        removed = sorted(set(recorded) - set(measured))

        # Assert — the directions rather than the two lists, since a failure has to name the source
        # that moved; the recorded length rides along because an emptied file and a silenced census
        # agree.
        self.assertEqual((len(recorded) > 40, added, removed), (True, [], []),
                         "record a deliberate change to it with\n  {} scripts/test_quality/"
                         "mutation_check.py --refusals > {}".format(sys.executable,
                                                                    mutation_check.REFUSAL_BASELINE))


class MaskDefectTests(unittest.TestCase):
    """A construct read as one it is not blanks code, and a blanked offset generates no mutant silently.

    The mask cannot be proved right about a file. What it can be held to is that it never reads a
    one-line construct as running past its line, which is the shape every miscount it has had took.
    """

    def test_Given_AnUnterminatedStringLiteral_When_TheFileIsRead_Then_TheBlankingIsReported(self):
        # Arrange — the quote runs to the end of the file, taking every line under it out of the mask.
        text = 'var greeting = "unterminated;\nif (a <= b) { }\nif (c == d) { }\n'

        # Act
        kinds = [kind for _, _, kind in mutation_check.mask_defects(text)]

        # Assert
        self.assertEqual(kinds, [mutation_check.STRING])

    def test_Given_ABlockCommentOverSeveralLines_When_TheFileIsRead_Then_NothingIsReported(self):
        # Arrange — the counterpart, so the case above is not passing for a check that reports any span.
        text = "/* a comment\n   over three\n   lines */\nif (a <= b) { }\n"

        # Act — the blanking rides along, because a report of nothing is also what a scanner that read
        # no span at all would produce, and that would pass this on an arrangement doing nothing.
        blanked = "".join("." if seen else "#" for seen in mutation_check.code_mask(text)[:12])
        defects = mutation_check.mask_defects(text)

        # Assert
        self.assertEqual((blanked, defects), ("############", []))

    def test_Given_EveryPackageSource_When_TheMaskIsRead_Then_NoneOfThemIsBlanked(self):
        # Arrange — the campaign refuses a target file the mask misreads, so one reaching main would
        # stop every branch that touches it rather than mis-measuring quietly.
        sources = [path for path in (REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs")]

        # Act
        blinded = ["{}: {}".format(path.name, mutation_check.mask_defects(path.read_text()))
                   for path in sources if mutation_check.mask_defects(path.read_text())]

        # Assert — the source count rides along because an empty scan satisfies "none blanked".
        self.assertEqual((len(sources) > 200, blinded), (True, []))


class BuildErrorTests(unittest.TestCase):
    """A build this repository's own analyzers stop writes no results file and no CS diagnostic.

    Read for CS alone it lands on the verdict for a run that wrote nothing, which reads as an editor
    that crashed and sends whoever is debugging it to the wrong half of the harness.
    """

    def blame(self, line):
        holder = Path(tempfile.mkdtemp(prefix="mutation-check-")) / "run.log"
        holder.write_text(line)
        return mutation_check.build_error(holder)

    def test_Given_AnAnalyzerErrorInTheLog_When_ItIsRead_Then_TheSourceIsBlamed(self):
        # Arrange — VEL501 is this repository's branching-complexity limit, reported as an error.
        line = "Packages/com.velvet.core/Runtime/Probe.cs(12,9): error VEL501: too many branches\n"

        # Act / Assert
        self.assertEqual(self.blame(line), "Packages/com.velvet.core/Runtime/Probe.cs")

    def test_Given_ACompilerErrorInTheLog_When_ItIsRead_Then_TheSourceIsBlamed(self):
        # Arrange — the case the check already covered, so widening it did not drop the old one.
        line = "Assets/Probe.cs(3,1): error CS1002: ; expected\n"

        # Act / Assert
        self.assertEqual(self.blame(line), "Assets/Probe.cs")

    def test_Given_AnAssertionMessageQuotingTheWords_When_ItIsRead_Then_NothingIsBlamed(self):
        # Arrange — a failing test whose message reads as a build error would turn a kill into noise.
        line = "  Expected: no error CS1002 anywhere\n  But was: Probe.cs(3,1): error CS1002\n"

        # Act / Assert
        self.assertEqual(self.blame(line), None)


class DeclarationReadingTests(unittest.TestCase):
    def test_Given_ADeclarationOverALine_When_ItIsRead_Then_ItAnswersForThatLine(self):
        # Arrange
        text = "// MUTANT_SURVIVES(equivalent): both spellings agree here.\nif (a <= b) { }\n"

        # Act
        found = [(subject, held.category) for subject, held in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(found, [(2, "equivalent")])

    def test_Given_ABlankLineUnderADeclaration_When_ItIsRead_Then_ItAnswersForNothing(self):
        # Arrange — prose over a gap belongs to whatever sits under it, and reaching past the gap
        # would let one line's answer cover a neighbour nobody wrote it for.
        text = "// MUTANT_SURVIVES(equivalent): both spellings agree here.\n\nif (a <= b) { }\n"

        # Act
        found = mutation_check.declarations_in(text)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AMarkerInsideAStringLiteral_When_ItIsRead_Then_ItAnswersForNothing(self):
        # Arrange — the marker is the material of whatever asserts over the declaration syntax, not
        # an answer for the statement beneath it. Adopted as one, it silences that statement.
        text = ('const string Sample = @"\n'
                '// MUTANT_SURVIVES(equivalent): both spellings agree here.";\n'
                'if (a <= b) { }\n')

        # Act
        found = mutation_check.declarations_in(text)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AMarkerInAStringOnALineThatAlsoComments_When_ItIsRead_Then_ItAnswersForNothing(self):
        # Arrange — the literal closes and a comment opens on the one line, so a reading that asks
        # whether the line is commented accepts a marker that sits in neither.
        text = ('const string Sample = @"\n'
                '// MUTANT_SURVIVES(equivalent): both spellings agree here."; // closing note\n'
                'if (a <= b) { }\n')

        # Act
        found = mutation_check.declarations_in(text)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AConditionBrokenOverTwoLines_When_ItIsRead_Then_TheDeclarationCoversBoth(self):
        # Arrange — mutants land on both lines, so a declaration answering for the first alone leaves
        # one survivor UNANSWERED on the second and the declaration STALE against it.
        text = ("// MUTANT_SURVIVES(equivalent): the two bounds accept the same set.\n"
                "if (a == null ||\n"
                "    b <= 0)\n"
                "{\n")

        # Act
        covered = [subject for subject, _ in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(covered, [2, 3])

    def test_Given_ASingleLineStatement_When_ItIsRead_Then_TheDeclarationCoversOnlyIt(self):
        # Arrange — the counterpart, so the case above is not passing for a reach that never stops.
        text = ("// MUTANT_SURVIVES(equivalent): the two bounds accept the same set.\n"
                "if (a <= b) return;\n"
                "Next();\n")

        # Act
        covered = [subject for subject, _ in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(covered, [2])

    def test_Given_ADeclarationHeadingACommentBlock_When_ItIsRead_Then_ItReachesPastTheRestOfIt(self):
        # Arrange
        text = ("// MUTANT_SURVIVES(equivalent): both spellings agree here.\n"
                "// A second line of the same block.\n"
                "if (a <= b) { }\n")

        # Act
        found = [subject for subject, _ in mutation_check.declarations_in(text)]

        # Assert
        self.assertEqual(found, [3])

    def test_Given_ADeclarationInsideABlockComment_When_ItIsRead_Then_ItReachesTheFollowingCode(self):
        # Arrange
        texts = [("/*\n"
                  " * MUTANT_SURVIVES(equivalent): both spellings agree here.\n"
                  " */\n"
                  "if (a <= b) { }\n"),
                 ("/* MUTANT_SURVIVES(equivalent): both spellings agree here. */\n"
                  "if (a <= b) { }\n"),
                 ("/* MUTANT_SURVIVES(equivalent): both spellings agree here. */\n"
                  "/* The boundary is shared by both paths. */\n"
                  "if (a <= b) { }\n"),
                 ("/* MUTANT_SURVIVES(equivalent): both spellings agree here.\n"
                  " */ if (a <= b) { }\n")]

        # Act
        found = [[subject for subject, _ in mutation_check.declarations_in(text)] for text in texts]

        # Assert
        self.assertEqual(found, [[4], [2], [3], [2]])

    def test_Given_AShortClaimAboveAnUnrelatedRemark_When_ItIsRead_Then_TheFloorStillRefusesIt(self):
        # Arrange
        text = ("// MUTANT_SURVIVES(equivalent): rename\n"
                "// The caller clamps this operand.\n"
                "if (a <= b) { }\n")

        # Act
        declaration = mutation_check.declarations_in(text)[0][1]

        # Assert -- one comparison over both: the reason is what a reader that never folds gets
        # wrong, and the complaint is what one that measures the floor over the fold gets wrong.
        self.assertEqual((declaration.reason, declaration.complaint is not None),
                         ("rename The caller clamps this operand.", True))


class VerdictTests(unittest.TestCase):
    """Which survivors the run signs off. Every reading here is one `base_red_check.py` takes too."""

    def decide(self, mutants, declared, deferred=frozenset()):
        unanswered, stale = mutation_check.answered(mutants, set(deferred), declared)
        return ([complaint for _, complaint in unanswered],
                [held.line for _, _, held in stale])

    def test_Given_ASurvivorNothingDeclares_When_TheRunIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]

        # Act
        unanswered, _ = self.decide(mutants, {})

        # Assert
        self.assertEqual(unanswered, ["nothing above this line answers for it"])

    def test_Given_ADeclaredSurvivor_When_TheRunIsDecided_Then_NothingIsUnanswered(self):
        # Arrange — the counterpart: without it every case here passes for a gate that refuses always.
        mutants = [survivor(Path("probe.cs"), 12)]

        # Act
        unanswered, _ = self.decide(mutants, {(Path("probe.cs"), 12): declaration(11)})

        # Assert
        self.assertEqual(unanswered, [])

    def test_Given_ASurvivorDeclaredWithACategoryTheScriptRefuses_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, category="fine")}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered, ["category 'fine' is not one of equivalent, unreachable"])

    def test_Given_ASurvivorDeclaredWithoutAReason_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, reason="see above")}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered, ["the reason's first line is under 4 words"])

    def test_Given_ASurvivorDeclaredByTheBaseRatherThanThisBranch_When_ItIsDecided_Then_ItIsUnanswered(self):
        # Arrange — a declaration answers for the change under it. One the branch did not write
        # answers for a change the base already carries.
        mutants = [survivor(Path("probe.cs"), 12)]
        declared = {(Path("probe.cs"), 12): declaration(11, written_here=False)}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered,
                         ["its declaration is the base's own; restate it for this change"])

    def test_Given_ADeclarationOverALineWhoseMutantsAllDied_When_ItIsDecided_Then_ItIsStale(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED)]

        # Act
        _, stale = self.decide(mutants, {(Path("probe.cs"), 12): declaration(11)})

        # Assert
        self.assertEqual(stale, [11])

    def test_Given_ADeclarationOverALineTheCapLeftUnrun_When_ItIsDecided_Then_ItIsNotStale(self):
        # Arrange — the cap fails the run on its own, and a line nothing ran says nothing about a
        # declaration over it.
        deferred = {(Path("probe.cs"), 12)}

        # Act
        _, stale = self.decide([], {(Path("probe.cs"), 12): declaration(11)}, deferred)

        # Assert
        self.assertEqual(stale, [])

    def test_Given_ADeclarationOverTwoLinesWithOneSurvivor_When_ItIsDecided_Then_ItIsNotStale(self):
        # Arrange — a condition broken over two lines carries mutants on both, and the declaration
        # answers for whichever of them survives.
        held = declaration(11)
        declared = {(Path("probe.cs"), 12): held, (Path("probe.cs"), 13): held}

        # Act
        _, stale = self.decide([survivor(Path("probe.cs"), 13)], declared)

        # Assert
        self.assertEqual(stale, [])

    def test_Given_ADeclarationOverTwoLinesWithNoSurvivor_When_ItIsDecided_Then_ItIsStaleOnce(self):
        # Arrange — the counterpart: with neither line surviving it is stale, and reported once
        # rather than once per line it covers.
        held = declaration(11)
        declared = {(Path("probe.cs"), 12): held, (Path("probe.cs"), 13): held}

        # Act
        _, stale = self.decide([survivor(Path("probe.cs"), 13, verdict=mutation_check.KILLED)], declared)

        # Assert
        self.assertEqual(stale, [11])

    def test_Given_ADeclarationTheBranchDidNotWriteOverAKilledLine_When_ItIsDecided_Then_ItIsNotStale(self):
        # Arrange — it answers for the base's change, and this branch is not being asked to keep it
        # current.
        declared = {(Path("probe.cs"), 12): declaration(11, written_here=False)}

        # Act
        _, stale = self.decide([survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED)], declared)

        # Assert
        self.assertEqual(stale, [])


class NamedDeclarationTests(unittest.TestCase):
    """A declaration that names the operator it answers for, and what it stops answering for.

    A line carries more than one mutant, and one keyed by line alone answers for every one. Where
    only one of them survives today the answer is unambiguous now and widens the moment a sibling
    starts surviving — and the staleness guard cannot report that, because it asks whether the line
    still produces a survivor and the one already answered for keeps it non-stale.
    """

    def decide(self, mutants, declared, deferred=frozenset()):
        unanswered, stale = mutation_check.answered(mutants, set(deferred), declared)
        return ([complaint for _, complaint in unanswered],
                [held.line for _, _, held in stale])

    def test_Given_ADeclarationNamingAnotherOperator_When_ASurvivorLands_Then_ItIsUnanswered(self):
        # Arrange — the shape the naming exists for: the removal is answered, and the boundary flip
        # beside it starts surviving.
        mutants = [survivor(Path("probe.cs"), 12, operator="boundary")]
        declared = {(Path("probe.cs"), 12): declaration(12, operator="line removed")}

        # Act
        unanswered, _ = self.decide(mutants, declared)

        # Assert
        self.assertEqual(unanswered,
                         ["the declaration above answers for 'line removed', not for this"])

    # GREEN_ON_BASE(characterization): the base answers this too, by reading the line whole. It is
    # the control the two red cases need: a reading that refused every named declaration would
    # satisfy them both.
    def test_Given_ADeclarationNamingThisOperator_When_ASurvivorLands_Then_ItIsAnswered(self):
        # Arrange — the control, without which a reading that refused every named declaration would
        # satisfy the case above.
        mutants = [survivor(Path("probe.cs"), 12, operator="boundary")]
        declared = {(Path("probe.cs"), 12): declaration(12, operator="boundary")}

        # Act
        unanswered, stale = self.decide(mutants, declared)

        # Assert
        self.assertEqual((unanswered, stale), ([], []))

    # GREEN_ON_BASE(characterization): the unnamed form is what every declaration written before
    # this uses, and the change must not move it.
    def test_Given_ADeclarationNamingNoOperator_When_ASurvivorLands_Then_ItStillAnswers(self):
        # Arrange — every declaration written before this reads the line whole, and none of them
        # changes meaning.
        mutants = [survivor(Path("probe.cs"), 12, operator="boundary")]
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        unanswered, stale = self.decide(mutants, declared)

        # Assert
        self.assertEqual((unanswered, stale), ([], []))

    def test_Given_ANamedDeclarationWhoseOwnMutantDied_When_ASiblingSurvives_Then_ItIsStale(self):
        # Arrange — the same widening read from the other side: a sibling still surviving would keep
        # a stale named declaration from being reported if staleness read the line whole.
        mutants = [survivor(Path("probe.cs"), 12, operator="boundary")]
        declared = {(Path("probe.cs"), 12): declaration(12, operator="line removed")}

        # Act
        _, stale = self.decide(mutants, declared)

        # Assert
        self.assertEqual(stale, [12])


class OnePerLineTests(unittest.TestCase):
    """The campaign keeps one mutant for each changed line, and a declaration keeps its meaning."""

    TWO_OPERATORS = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b, bool c) { return a <= b && c; }
                internal static int Sum(int a, int b) { return a + b; }
            }
        }
        """)

    def at(self, operator, column=0, line=12, path="probe.cs"):
        return mutation_check.Mutant(Path(path), line, column, "a", "b", operator)

    def decide(self, mutants, declared, deferred=frozenset()):
        unanswered, stale = mutation_check.answered(mutants, set(deferred), declared)
        return ([complaint for _, complaint in unanswered],
                [held.line for _, _, held in stale])

    def test_Given_ALineCarryingSeveralOperators_When_TheCapIsApplied_Then_OnlyTheBoundaryFlipStays(self):
        # Arrange
        every = mutants_of(self.TWO_OPERATORS)
        crowded = [mutant for mutant in every if mutant.line == 5]

        # Act
        kept = mutation_check.one_per_line(crowded)

        # Assert -- the operators the line generates ride along, so a line that generated one
        # mutant cannot satisfy it.
        self.assertEqual((sorted(mutant.operator for mutant in crowded),
                          [mutant.operator for mutant in kept]),
                         (["boundary", "clause removed", "logic"], ["boundary"]))

    def test_Given_EveryOperatorOnOneLine_When_TheCapIsApplied_Then_EachOutranksTheOnesAfterIt(self):
        # Arrange
        order = mutation_check.LINE_PRIORITY

        # Act
        chosen = [mutation_check.one_per_line([self.at(name) for name in order[index:]])[0].operator
                  for index in range(len(order))]

        # Assert
        self.assertEqual(chosen, list(order))

    def test_Given_TheOperatorsGenerationEmits_When_TheCapReadsThem_Then_EachHasARank(self):
        # Arrange
        emitted = {operator for _, _, operator in mutation_check.OPERATORS}
        emitted |= {operator for _, _, operator in mutation_check.WORD_OPERATORS}
        emitted |= {mutant.operator for mutant in mutants_of(self.TWO_OPERATORS)}
        emitted |= {"clause removed", "guard removed", "line removed"}

        # Act
        unranked = sorted(emitted - set(mutation_check.LINE_PRIORITY))

        # Assert
        self.assertEqual(unranked, [])

    def test_Given_TheSameMutantsInAnyOrder_When_TheCapIsApplied_Then_TheSameOneStays(self):
        # Arrange
        crowded = [self.at("logic", 30), self.at("boundary", 40), self.at("literal", 5),
                   self.at("boundary", 20), self.at("line removed", 1)]

        # Act
        forwards = mutation_check.one_per_line(list(crowded))[0]
        backwards = mutation_check.one_per_line(list(reversed(crowded)))[0]
        rotated = mutation_check.one_per_line(crowded[2:] + crowded[:2])[0]

        # Assert
        self.assertEqual([(m.operator, m.column) for m in (forwards, backwards, rotated)],
                         [("boundary", 20)] * 3)

    def test_Given_TwoMutantsOfTheSameOperator_When_TheCapIsApplied_Then_TheLeftmostStays(self):
        # Arrange
        right, left = self.at("arithmetic", 31), self.at("arithmetic", 9)

        # Act
        kept = [mutation_check.one_per_line([right, left])[0].column,
                mutation_check.one_per_line([left, right])[0].column]

        # Assert
        self.assertEqual(kept, [9, 9])

    def test_Given_MutantsOnSeveralLinesAndFiles_When_TheCapIsApplied_Then_OneStaysForEach(self):
        # Arrange
        every = [self.at("boundary", 1, 3), self.at("logic", 2, 3), self.at("equality", 1, 4),
                 self.at("boundary", 1, 3, "other.cs"), self.at("logic", 2, 3, "other.cs")]

        # Act
        kept = mutation_check.one_per_line(every)

        # Assert
        self.assertEqual(sorted((str(m.path), m.line, m.operator) for m in kept),
                         [("other.cs", 3, "boundary"), ("probe.cs", 3, "boundary"),
                          ("probe.cs", 4, "equality")])

    def test_Given_ALineWhoseOtherMutantsWereDropped_When_TheCapIsApplied_Then_TheSurvivorNamesThem(self):
        # Arrange
        every = [self.at("logic", 30), self.at("boundary", 40), self.at("line removed", 1)]

        # Act
        kept = mutation_check.one_per_line(every)[0]

        # Assert
        self.assertEqual((kept.operator, set(kept.shadowed)),
                         ("boundary", {"logic", "line removed"}))

    def test_Given_ALoneMutantOnItsLine_When_TheCapIsApplied_Then_NothingIsShadowed(self):
        # Arrange
        alone = self.at("logic")

        # Act
        kept = mutation_check.one_per_line([alone])[0]

        # Assert
        self.assertEqual((kept is alone, set(kept.shadowed)), (True, set()))

    def test_Given_ALineCarryingSeveralOperators_When_TheCampaignIsListed_Then_OneMutantIsPrintedForIt(self):
        # Arrange
        campaign = StubbedCampaign(self.TWO_OPERATORS)

        # Act
        campaign.run("--list")

        # Assert -- line 6 is the control: its one mutant is printed, so a list that printed
        # nothing for the crowded line could not pass.
        listed = [line for line in campaign.printed.splitlines() if "Probe.cs:" in line and " -> " in line]
        self.assertEqual([(line.split("Probe.cs:")[1].split(" ")[0], line.rsplit("(", 1)[1])
                          for line in listed],
                         [("5", "boundary)"), ("6", "arithmetic)")])

    # GREEN_ON_BASE(characterization): the base answers a declaration naming another operator than the survivor's with this complaint too.
    # It is the side the allowance for a stood-for operator must not take.
    def test_Given_ADeclarationNamingADroppedOperator_When_TheRepresentativeSurvives_Then_ItIsUnanswered(self):
        # Arrange -- a survivor of another operator is owed its own answer.
        kept = survivor(Path("probe.cs"), 12, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12, operator="logic")}

        # Act
        unanswered, _ = self.decide([kept], declared)

        # Assert
        self.assertEqual(unanswered, ["the declaration above answers for 'logic', not for this"])

    def test_Given_ADeclarationNamingTheOperatorThatStoodForTheLine_When_ItSurvives_Then_ItIsAnswered(self):
        # Arrange -- the kept mutant did not compile and the dropped logic flip was measured in its place.
        kept = survivor(Path("probe.cs"), 12, operator="arithmetic")
        kept.shadowed = frozenset({"logic"})
        kept.stood = "logic"
        declared = {(Path("probe.cs"), 12): declaration(12, operator="logic")}

        # Act
        unanswered, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual((unanswered, stale), ([], []))

    def test_Given_ADeclarationNamingTheKeptOperatorThatDidNotCompile_When_AnAlternateSurvives_Then_ItIsUnanswered(self):
        # Arrange -- the kept arithmetic mutant was no program; the survivor is the logic flip.
        kept = survivor(Path("probe.cs"), 12, operator="arithmetic")
        kept.shadowed = frozenset({"logic"})
        kept.stood = "logic"
        declared = {(Path("probe.cs"), 12): declaration(12, operator="arithmetic")}

        # Act
        unanswered, _ = self.decide([kept], declared)

        # Assert
        self.assertEqual(unanswered, ["the declaration above answers for 'arithmetic', not for this"])

    # GREEN_ON_BASE(characterization): the base answers an unnamed declaration over a survivor, which is what the name says.
    def test_Given_AnUnnamedDeclarationOverASurvivingThinnedLine_When_Decided_Then_ItAnswersAsBefore(self):
        # Arrange
        kept = survivor(Path("probe.cs"), 12, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        unanswered, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual((unanswered, stale), ([], []))

    # GREEN_ON_BASE(characterization): the base refuses a declaration naming another operator than the survivor's.
    # Its comment names it as the control for the allowance the cases around it add.
    def test_Given_ADeclarationNamingAnOperatorTheLineNeverCarried_When_TheRepresentativeSurvives_Then_ItIsUnanswered(self):
        # Arrange -- the control: naming is still checked, only what the cap dropped is let through.
        kept = survivor(Path("probe.cs"), 12, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12, operator="literal")}

        # Act
        unanswered, _ = self.decide([kept], declared)

        # Assert
        self.assertEqual(unanswered, ["the declaration above answers for 'literal', not for this"])

    def test_Given_ANamedDeclarationWhoseOperatorWasDropped_When_TheRepresentativeDies_Then_ItIsNotStale(self):
        # Arrange
        kept = survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12, operator="logic")}

        # Act
        _, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual(stale, [])

    # GREEN_ON_BASE(characterization): the base calls a declaration over a mutant that died stale.
    # It is the control for the case above it, where a dropped operator is let through.
    def test_Given_ANamedDeclarationWhoseOperatorWasNotDropped_When_TheRepresentativeDies_Then_ItIsStale(self):
        # Arrange -- the control for the case above.
        kept = survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12, operator="literal")}

        # Act
        _, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual(stale, [12])

    def test_Given_AnUnnamedDeclarationOverALineWithDroppedMutants_When_TheRepresentativeDies_Then_ItIsNotStale(self):
        # Arrange -- an unnamed declaration answered for the whole line, and some of that line was
        # never generated, so a dead representative does not show it described a past state.
        kept = survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED, operator="boundary")
        kept.shadowed = frozenset({"logic"})
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        _, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual(stale, [])

    # GREEN_ON_BASE(characterization): the base calls a declaration over a mutant that died stale.
    # It is the control for the case above it, where a dropped operator is let through.
    def test_Given_AnUnnamedDeclarationOverALineWithNothingDropped_When_TheMutantDies_Then_ItIsStale(self):
        # Arrange -- the control for the case above, and the base's own reading.
        kept = survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED, operator="boundary")
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        _, stale = self.decide([kept], declared)

        # Assert
        self.assertEqual(stale, [12])


class DeclarationFormatTests(unittest.TestCase):
    """What the guide shows and what the tree holds, against what the script will actually accept.

    A declaration whose category the script refuses is refused by it and reads to everyone else as an
    approved exemption, so the two have to agree.
    """

    # GREEN_ON_BASE(characterization): the guide and the script agreed before and agree after. What
    # this change adds is a second example in the guide, and green on both sides is what says the
    # pattern reads it — a case that went red would mean the guide had stopped being readable.
    def test_Given_TheDeclarationTheGuideShows_When_TheScriptsOwnPatternReadsIt_Then_ItIsAccepted(self):
        # Arrange
        # The reason is the pattern's last group whether or not it carries an operator group, so this
        # reads the same on a tree that has one and a tree that has not.
        shown = [declaration(0, category=match.group(1), reason=match.groups()[-1].strip(),
                             operator=(match.groups()[1] or "").strip() or None
                             if len(match.groups()) > 2 else None)
                 for match in mutation_check.DECLARATION.finditer(GUIDE.read_text())]

        # Act
        complaints = [held.complaint for held in shown if held.complaint]

        # Assert — the count rides along because a guide showing no example passes on the rest.
        self.assertEqual((len(shown) >= 1, complaints), (True, []))

    def test_Given_EveryDeclarationInThePackage_When_ItIsRead_Then_TheScriptAcceptsIt(self):
        # Arrange
        sources = list((REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs"))

        # Act
        complaints = ["{}:{} {}".format(path.name, held.line, held.complaint)
                      for path in sources
                      for _, held in mutation_check.declarations_in(path.read_text())
                      if held.complaint]

        # Assert — the source count rides along because a scan over nothing satisfies "no complaint",
        # and the tree carries no declaration yet, so that is the state this would pass in.
        self.assertEqual((len(sources) > 200, complaints), (True, []))

    def test_Given_EveryDeclarationInThePackage_When_ItsLineIsMutated_Then_ThereIsAMutantToAnswerFor(self):
        # Arrange — the campaign calls a declaration stale when the line under it produced no
        # survivor, and one over a line that generates nothing at all can never be anything else.
        # It reads as an approved exemption until a campaign happens to run over that file.
        sources = list((REPO_ROOT / "Packages/com.velvet.core").rglob("*.cs"))

        # Act
        answering = []
        for path in sources:
            text = path.read_text()
            for subject, held in mutation_check.declarations_in(text):
                if not mutation_check.mutations_for(path, text, {subject}):
                    answering.append("{}:{}".format(path.name, held.line))

        # Assert — the source count rides along for the reason the case above carries.
        self.assertEqual((len(sources) > 200, answering), (True, []))


class StubbedCampaign:
    """A project of one mutable source, with the editor replaced by a file write.

    The editor is what a campaign spends its time in and is not what any case here asks about. What
    they ask about is the working tree at a moment the campaign is inside `wait_for_quiet`, which no
    reading taken after it returns can answer.
    """

    SOURCE = "Packages/com.velvet.core/Runtime/Probe.cs"
    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b) => a <= b;
            }
        }
        """)

    def __init__(self, body=None):
        self.holder = tempfile.mkdtemp(prefix="mutation-check-")
        self.project = Path(self.holder)
        self.source = self.project / self.SOURCE
        self.source.parent.mkdir(parents=True, exist_ok=True)
        self.source.write_text(body if body is not None else self.BODY)
        (self.project / "Library" / "ScriptAssemblies").mkdir(parents=True, exist_ok=True)
        self.seen = []
        self.busy = []
        # A repository with one commit that does not hold the source, so the source is untracked and
        # the diff against HEAD takes it whole -- which is what a run not scoped by --files reads.
        for command in (["init", "-q", "-b", "main"], ["add", ".gitignore"],
                        ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]):
            (self.project / ".gitignore").write_text("Library/\nout/\n")
            subprocess.run(["git", "-C", str(self.project), *command], capture_output=True)

    printed = ""
    kills = False
    times_out = False
    no_results = False
    build_error = False
    # The editor log a mutant's build leaves, where a case needs its exact lines.
    error_log = None
    not_rebuilt = False
    # What the baseline is reported to have taken. Zero is a baseline with the whole bound to spare,
    # which is what separates a mutant that hung from a bound the suite was always going to outrun.
    baseline_seconds = 0.0
    baseline_text = GREEN_RESULTS

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        # The baseline has to be green whatever the mutants do, or the run stops before the loop.
        mutant = Path(results).name != "baseline.xml"
        wall = 0.0 if mutant else self.baseline_seconds
        if mutant and self.no_results:
            Path(log).write_text("")
            return wall, False, 0
        if self.not_rebuilt:
            (self.project / "Library" / "ScriptAssemblies" / "None.dll").write_bytes(b"same")
        if mutant and self.times_out:
            return wall, True, 0
        Path(results).write_text(FAILING_RESULTS if (mutant and self.kills) else
                                 GREEN_RESULTS if mutant else self.baseline_text)
        Path(log).write_text(
            "Packages/com.velvet.core/Runtime/Probe.cs(5,9): error VEL501: too many branches\n"
            if (mutant and self.build_error) else
            self.error_log if (mutant and self.error_log is not None) else "")
        return wall, False, 0

    def unity_busy(self):
        self.seen.append(self.source.read_text())
        return self.busy.pop(0) if self.busy else 0

    def run(self, *arguments):
        """Drives `main` over this project, scoped by --files, returning what it exited with."""
        return self.drive("--files", str(self.source), *arguments)

    def run_over_diff(self, *arguments):
        """The same, scoped by the diff -- which is the only shape that reads a declaration."""
        return self.drive("--base", "HEAD", *arguments)

    def drive(self, *arguments):
        """Runs `main` and keeps what it printed, which is where a verdict can be read by name."""
        saved = (sys.argv, mutation_check.run_suite, mutation_check.unity_busy)
        sys.argv = ["mutation_check.py", "--project", str(self.project),
                    "--output", str(self.project / "out"), *arguments]
        mutation_check.run_suite = self.run_suite
        mutation_check.unity_busy = self.unity_busy
        spoken = io.StringIO()
        try:
            with contextlib.redirect_stdout(spoken):
                return mutation_check.main()
        except SystemExit as stop:
            return stop.code
        finally:
            self.printed = spoken.getvalue()
            sys.argv, mutation_check.run_suite, mutation_check.unity_busy = saved


class QueuedCampaignTests(unittest.TestCase):
    def test_Given_ACampaignWaitingForTheEditor_When_TheTreeIsRead_Then_ItHoldsNoMutation(self):
        # Arrange — the machine is free for the baseline and busy for the first mutant, which is the
        # state both mutations found in a tree after a killed campaign were left in.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        campaign.busy = [0, 1]

        # Act — the wait fails, so what matters is only what was on disk while it was waiting.
        campaign.run("--max", "1", "--busy-timeout", "0")

        # Assert
        self.assertEqual(campaign.seen[1], original)

    def test_Given_ACampaignWaitingForTheEditor_When_TheWaitRunsOut_Then_ItStopsRatherThanSharing(self):
        # Arrange — a second editor is a second explanation for every failure the mutant run reports.
        campaign = StubbedCampaign()
        campaign.busy = [0, 1]

        # Act
        code = campaign.run("--max", "1", "--busy-timeout", "0")

        # Assert
        self.assertIn("still in flight", str(code))


class OutstandingMutationTests(unittest.TestCase):
    def test_Given_ASentinelFromAKilledCampaign_When_ANewOneStarts_Then_ItRefusesToMeasure(self):
        # Arrange — the baseline would be taken over somebody else's mutation, and the restore at the
        # end would write it back as though it were the author's own code.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, "the original text", "mutated text",
            "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("holding a mutation in this tree", str(code))

    def test_Given_ASentinelFromAKilledCampaign_When_RestoreRuns_Then_TheSourceIsPutBack(self):
        # Arrange
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        holder = mutation_check.Holder(campaign.project / mutation_check.SENTINEL)
        holder.hold(campaign.source, original, original.replace("a <= b", "a < b"),
                    "Probe.cs:5 <= -> < (boundary)")
        campaign.source.write_text(original.replace("a <= b", "a < b"))

        # Act
        campaign.run("--restore")

        # Assert
        self.assertEqual(campaign.source.read_text(), original)

    def test_Given_ASentinelFromAKilledCampaign_When_RestoreRuns_Then_TheSentinelGoesWithIt(self):
        # Arrange
        campaign = StubbedCampaign()
        sentinel = campaign.project / mutation_check.SENTINEL
        mutation_check.Holder(sentinel).hold(campaign.source, campaign.source.read_text(),
                                             "mutated", "x")

        # Act
        campaign.run("--restore")

        # Assert
        self.assertEqual(sentinel.exists(), False)

    def test_Given_ASentinelNothingCanRead_When_RestoreRuns_Then_ItSaysSoRatherThanNothingIsHeld(self):
        # Arrange — "no mutation is outstanding" over a record naming one is the reading this whole
        # mechanism exists to stop anybody taking.
        campaign = StubbedCampaign()
        (campaign.project / mutation_check.SENTINEL).write_text("{ truncated")

        # Act
        code = campaign.run("--restore")

        # Assert
        self.assertIn("could not put back", str(code))

    def test_Given_AHeldMutation_When_ACommitWouldRecordThatFile_Then_ItIsRefused(self):
        # Arrange — `git add -u` stages the mutation with everything else, and a reader cannot tell
        # it from an unfinished edit. One reached a commit this way with the record sitting beside it.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, campaign.source.read_text(), "mutated",
            "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--carried", StubbedCampaign.SOURCE, "CONTRIBUTING.md")

        # Assert — the status the hook reads, not 1: a script that failed to run also exits non-zero,
        # and a refusal naming a campaign has to be told from one that could not ask about anything.
        self.assertEqual(code, mutation_check.CARRIED_REFUSAL)

    def test_Given_AHeldMutation_When_ACommitRecordsOtherFiles_Then_ItIsAllowed(self):
        # Arrange — the counterpart: a campaign in flight must not stop every commit in the tree.
        campaign = StubbedCampaign()
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, campaign.source.read_text(), "mutated",
            "Probe.cs:5 <= -> < (boundary)")

        # Act
        code = campaign.run("--carried", "CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_NoCampaign_When_ACommitIsChecked_Then_ItIsAllowed(self):
        # Arrange — the ordinary state, so the two above are not passing for a check that refuses
        # whatever it is handed.
        campaign = StubbedCampaign()

        # Act
        code = campaign.run("--carried", StubbedCampaign.SOURCE)

        # Assert
        self.assertEqual(code, 0)

    def test_Given_TheRepository_When_TheRecordsNameIsCheckedAgainstGitignore_Then_ItIsNotIgnored(self):
        # Arrange — the record's whole reach outside this script is that `git status` names it beside
        # the file it explains, and one .gitignore line would end that with nothing failing.
        probe = REPO_ROOT / mutation_check.SENTINEL

        # Act
        ignored = subprocess.run(["git", "-C", str(REPO_ROOT), "check-ignore", "-q", str(probe)],
                                 capture_output=True)

        # Assert — 1 is git's status for a path no rule matches.
        self.assertEqual(ignored.returncode, 1)

    def test_Given_ARecordNothingCanRead_When_ACommitIsChecked_Then_ItIsRefused(self):
        # Arrange — which file it names is exactly what cannot be read, so no path can be cleared.
        # A record is damaged by things the campaign never does: somebody clearing what `git status`
        # reported, a permission change, a directory left in its place.
        campaign = StubbedCampaign()
        (campaign.project / mutation_check.SENTINEL).write_text("{ truncated")

        # Act
        code = campaign.run("--carried", "CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, mutation_check.CARRIED_REFUSAL)

    def test_Given_ARecordThatIsADirectory_When_ACommitIsChecked_Then_ItIsRefused(self):
        # Arrange — `exists()` is true for it and reading it raises, which is the other way a reader
        # that answered with a placeholder let a path through.
        campaign = StubbedCampaign()
        (campaign.project / mutation_check.SENTINEL).mkdir()

        # Act
        code = campaign.run("--carried", "CONTRIBUTING.md")

        # Assert
        self.assertEqual(code, mutation_check.CARRIED_REFUSAL)

    def test_Given_WorkDoneSinceTheKill_When_RestoreRuns_Then_ItRefusesRatherThanOverwriting(self):
        # Arrange — a record survives a SIGKILL, so an author sees a modified file and keeps working
        # before running this. The recorded original would take that work with it.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        mutated = original.replace("a <= b", "a < b")
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, original, mutated, "Probe.cs:5 <= -> < (boundary)")
        campaign.source.write_text(mutated + "// an hour of work since\n")

        # Act
        campaign.run("--restore")

        # Assert
        self.assertIn("an hour of work since", campaign.source.read_text())

    def test_Given_AnUntouchedMutation_When_RestoreRuns_Then_ItStillPutsItBack(self):
        # Arrange — the counterpart, so the case above is not passing for a restore that never writes.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        mutated = original.replace("a <= b", "a < b")
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).hold(
            campaign.source, original, mutated, "Probe.cs:5 <= -> < (boundary)")
        campaign.source.write_text(mutated)

        # Act
        campaign.run("--restore")

        # Assert
        self.assertEqual(campaign.source.read_text(), original)

    def test_Given_ARecordAlreadyHeld_When_ASecondCampaignHolds_Then_ItRefuses(self):
        # Arrange — two campaigns starting inside one baseline window both reach `hold`, and the
        # second overwriting the first ends with one restoring the other's file.
        campaign = StubbedCampaign()
        holder = mutation_check.Holder(campaign.project / mutation_check.SENTINEL)
        holder.hold(campaign.source, "first", "first mutated", "the first campaign")

        # Act / Assert
        with self.assertRaisesRegex(SystemExit, "already records a held mutation"):
            holder.hold(campaign.source, "second", "second mutated", "the second campaign")

    def test_Given_AHeldMutation_When_TheProcessIsSignalled_Then_TheSourceIsPutBack(self):
        # Arrange — SIGTERM runs no `finally` under the default handler, and it is what `TaskStop`, a
        # timeout and a killed session all send.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        script = campaign.project / "hold.py"
        script.write_text(textwrap.dedent("""\
            import importlib.util, sys, time
            spec = importlib.util.spec_from_file_location("mutation_check", sys.argv[1])
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            holder = module.Holder(sys.argv[2])
            holder.guard()
            source, original = sys.argv[3], sys.argv[4]
            holder.hold(source, original, "mutated\\n", "held")
            open(source, "w").write("mutated\\n")
            print("held", flush=True)
            time.sleep(120)
            """))

        # Act
        held = subprocess.Popen(
            [sys.executable, str(script), str(Path(mutation_check.__file__)),
             str(campaign.project / mutation_check.SENTINEL), str(campaign.source), original],
            stdout=subprocess.PIPE, text=True)
        held.stdout.readline()
        os.kill(held.pid, signal.SIGTERM)
        held.wait(timeout=30)

        # Assert
        self.assertEqual(campaign.source.read_text(), original)


class ReachTests(unittest.TestCase):
    """What the operators do not touch, which is most of a diff and was folded into a clean verdict.

    `Generators~/README.md` owns how much of a diff that is, with the window it was measured over; a
    second copy here went stale twice while the guides moved.
    """

    ASSIGNMENTS = textwrap.dedent("""\
        void Settle(int next)
        {
            _phase = Phase.Settled;
            _value = next;
            _error = null;
        }
        """)

    def test_Given_AMethodOfAssignments_When_MutantsAreGenerated_Then_OnlyTheSignatureIsUnreached(self):
        # Arrange
        numbers = set(range(1, len(self.ASSIGNMENTS.splitlines()) + 1))

        # Act
        reached = {mutant.line for mutant
                   in mutation_check.mutations_for(Path("probe.cs"), self.ASSIGNMENTS, numbers)}
        unreached = [number for number in mutation_check.code_line_numbers(self.ASSIGNMENTS, numbers)
                     if number not in reached]

        # Assert — the braces are not code lines.
        self.assertEqual(unreached, [1])

    def test_Given_ATypeAndItsMembers_When_TheBodyLinesAreRead_Then_OnlyTheMethodsStatementsAreThem(self):
        # Arrange — a namespace, a type, a field, a signature continued over two lines and an
        # expression-bodied member all sit outside a member body's braces.
        text = textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    private int _count;
                    internal void Settle(
                        int next)
                    {
                        _count = next;
                        Apply(next);
                    }
                    internal int Next => _count + 1;
                }
            }
            """)

        # Act
        found = mutation_check.body_line_numbers(text, set(range(1, len(text.splitlines()) + 1)))

        # Assert
        self.assertEqual(found, [9, 10])

    def test_Given_AFieldInitializersBraces_When_TheBodyLinesAreRead_Then_TheirEntriesAreNotThem(self):
        # Arrange — the shape of a generated table: a collection initializer whose header ends with a
        # parenthesis, as a method's does, and holds the field's `=`.
        text = textwrap.dedent("""\
            internal static class Table
            {
                private static readonly Dictionary<string, int> s_map = new Dictionary<string, int>(4)
                {
                    { "a", 1 },
                    { "b", 2 },
                };
                private static int Read()
                {
                    return s_map.Count;
                }
            }
            """)

        # Act
        found = mutation_check.body_line_numbers(text, set(range(1, len(text.splitlines()) + 1)))

        # Assert
        self.assertEqual(found, [10])

    def test_Given_ALambdaAFieldInitializerHolds_When_TheBodyLinesAreRead_Then_ItsStatementsAreThem(self):
        # Arrange — the header holds the field's `=` and ends with the lambda's arrow.
        text = textwrap.dedent("""\
            internal static class Table
            {
                private static readonly Action s_log = () =>
                {
                    Write(1);
                };
            }
            """)

        # Act
        found = mutation_check.body_line_numbers(text, set(range(1, len(text.splitlines()) + 1)))

        # Assert
        self.assertEqual(found, [5])

    def test_Given_ABodyLineOfBracketsAlone_When_TheBodyLinesAreRead_Then_ItIsNotOneOfThem(self):
        # Arrange — line 5 closes the call line 3 opened, and `code_line_numbers` counts it.
        text = textwrap.dedent("""\
            void Settle(int next)
            {
                Apply(
                    next
                );
            }
            """)

        # Act — the three lines inside the braces, so what decides is the brackets rather than the scope.
        found = mutation_check.body_line_numbers(text, {3, 4, 5})

        # Assert
        self.assertEqual(found, [3, 4])

    def test_Given_AMethodWithAnUnreachedStatement_When_TheReachIsPrinted_Then_TheBodyShareFollowsIt(self):
        # Arrange — which lines are body lines and which carry a mutant is the two readings' own
        # business, pinned beside this; what this asks is that the run prints their share at all.
        text = textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    private int _value;
                    internal void Settle(int next)
                    {
                        var label = Describe(next);
                        _value = next;
                        Use(label);
                    }
                }
            }
            """)
        campaign = StubbedCampaign(text)
        numbers = set(range(1, len(text.splitlines()) + 1))
        body = mutation_check.body_line_numbers(text, numbers)
        reached = {mutant.line for mutant in mutation_check.mutations_for(Path("probe.cs"), text, numbers)}
        hit = len([number for number in body if number in reached])

        # Act
        campaign.run("--list")

        # Assert — the counts ride along, so a reading that found nothing to share cannot satisfy it.
        self.assertEqual(
            ((hit, len(body)),
             "{} of the {} changed code line(s) inside a member body's braces, beyond brackets and "
             "separators alone, carry a mutant ({}%)".format(hit, len(body), round(100 * hit / len(body)))
             in campaign.printed.splitlines()),
            ((2, 3), True))

    def test_Given_ALineHoldingOnlyABrace_When_TheCodeLinesAreRead_Then_ItIsNotOneOfThem(self):
        # Arrange — block punctuation is not something an operator failed to reach, and counting it
        # would put a number in the denominator that no widening could ever move.
        text = "{\n}\nif (a <= b) { }\n"

        # Act
        found = mutation_check.code_line_numbers(text, {1, 2, 3})

        # Assert
        self.assertEqual(found, [3])

    def test_Given_ADocumentationComment_When_TheCodeLinesAreRead_Then_ItIsNotOneOfThem(self):
        # Arrange — the ordinary way a production diff generates nothing, and it is not a hole.
        text = "/// <summary>Does the thing.</summary>\n"

        # Act
        found = mutation_check.code_line_numbers(text, {1})

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnyMutants_When_TheReachIsReported_Then_ItNamesTheLinesNoneOfThemCovers(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 3)]

        # Act
        report = mutation_check.reach(mutants, {Path("probe.cs"): [7, 8]}, Path("/nowhere"))

        # Assert
        self.assertEqual(report.splitlines()[0],
                         "1 mutant(s) over 3 changed code line(s); 2 line(s) no operator reaches")

    def test_Given_ARefusedRemovalOnALineAnotherOperatorMutates_When_TheReachIsListed_Then_TheLineIsNotNamedUnreached(self):
        # Arrange — line 7 loses its guard removal to the declaration refusal and keeps the `false`,
        # which is the shape a narrowing hides behind: reach carries one bit per line, so a line that
        # lost a question reads the same as one that never had it. Taken off what the run prints
        # rather than off a second copy of the sum. `Generators~/README.md` ▸ Mutation testing states
        # this, and no other case holds it.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal static class Probe
                {
                    internal static bool Ready(Map map, int key)
                    {
                        if (!map.TryGetValue(key, out var found)) return false;
                    }
                }
            }
            """))

        # Act
        campaign.run("--list")

        # Assert
        self.assertEqual(
            (sorted(re.findall(r"Probe\.cs:7 .*\((.+)\)$", campaign.printed, re.MULTILINE)),
             next(line.rsplit(":", 1)[-1] for line in campaign.printed.splitlines()
                  if "unreached" in line)),
            (["literal"], "1,3,5"))


class DeclinedLineTests(unittest.TestCase):
    """A statement the line removal would take on a line of its own, beside a brace, a label or another
    statement, which the removal does not read because it reads a line only whole.

    The reach is one bit per line, so nothing it printed over `try { _state.Dispose(); }` said a
    statement there was skipped for its formatting.
    """

    def declined(self, line):
        """Line 2's reading, behind a finished statement, which is where a removal may stand."""
        return mutation_check.declined_line_numbers("Init();\n" + line + "\n", {2})

    def test_Given_ACallInsideBracesOnOneLine_When_TheLinesAreRead_Then_ItIsDeclined(self):
        # Arrange
        line = "try { _state.Dispose(); }"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [2])

    def test_Given_TwoCallsOnOneLine_When_TheLinesAreRead_Then_ItIsDeclined(self):
        # Arrange — no brace on the line, so only the cut at the first semicolon separates the calls.
        line = "Reset(); Notify();"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [2])

    def test_Given_ACallAloneOnItsLine_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — the line the removal does read, which matches the pattern as a piece and whole.
        line = "Reset();"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AForHeaderOfCalls_When_TheLinesAreRead_Then_ItsClausesAreNotDeclined(self):
        # Arrange — `More();` would match the removal pattern if the header's semicolons cut the line.
        line = "for (Init(); More(); Advance()) Step();"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_AReturnOfATupleBehindAnotherStatement_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — `return (value, true);` matches the removal pattern and is not a call. No brace,
        # because with the braces left uncut `if (done) { return (value, true);` matches it too, and
        # an `await` in front because no removal arm reads one.
        line = "await Settle(); return (value, true);"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADeconstructionInsideBraces_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — the removal refuses a declaration wherever it stands, so naming it would say the
        # formatting cost a mutant it never could have had.
        line = "if (ready) { var (a, b) = Split(); }"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ABracedCallInsideAComment_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — the line's own code is an `await`, which no removal arm reads.
        line = "await Settle(); // { Clear(); }"

        # Act
        found = self.declined(line)

        # Assert
        self.assertEqual(found, [])

    def test_Given_ACallBehindACaseLabel_When_TheLinesAreRead_Then_ItIsDeclined(self):
        # Arrange — the label is no statement, and the call alone on the line under it is removable.
        text = "switch (key)\n{\n    case 1: Reset();\n        break;\n}\n"

        # Act
        found = mutation_check.declined_line_numbers(text, {3})

        # Assert
        self.assertEqual(found, [3])

    def test_Given_ANamedArgumentContinuingACall_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — `name:` reads as a label, and the code above has not finished a statement.
        text = "Call(first,\n    name: Reset());\n"

        # Act
        found = mutation_check.declined_line_numbers(text, {2})

        # Assert
        self.assertEqual(found, [])

    def test_Given_ADoLoopsTailBehindItsBrace_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — on a line of its own the tail is refused, since the `do` block would lose it.
        text = "do\n{\n    Step();\n} while (Next());\n"

        # Act
        found = mutation_check.declined_line_numbers(text, {4})

        # Assert
        self.assertEqual(found, [])

    def test_Given_AnIfWhoseElseSharesItsLine_When_TheLinesAreRead_Then_ItIsNotDeclined(self):
        # Arrange — on a line of its own the `if` is refused, since the `else` would be stranded.
        text = "Init();\nif (ready) Reset(); else Clear();\n"

        # Act
        found = mutation_check.declined_line_numbers(text, {2})

        # Assert
        self.assertEqual(found, [])

    def test_Given_AOneLineTryAndCatch_When_Listed_Then_TheReachNamesBothLinesDeclined(self):
        # Arrange — the containment the issue measured, beside a line that does carry a mutant so the
        # run reaches the listing rather than the refusal of a change no operator reaches.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    internal static bool Ready(int a, int b) => a <= b;
                    internal void Dispose()
                    {
                        try { _state.Dispose(); }
                        catch (Exception ex) { Logger.LogError(ex.Message); }
                    }
                }
            }
            """))

        # Act
        campaign.run("--list")

        # Assert
        self.assertEqual(re.findall(r"^  declined .*$", campaign.printed, re.MULTILINE),
                         ["  declined   Packages/com.velvet.core/Runtime/Probe.cs:8,9"])


class UnreachableChangeTests(unittest.TestCase):
    def test_Given_ADiffOfCodeNoOperatorReaches_When_TheCampaignStarts_Then_ItRefusesToVerdict(self):
        # Arrange — a verdict here would be about no line at all, and the run returned 0 for it.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    private int _value;
                    internal void Settle(int next) { _value = next; }
                }
            }
            """))

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("no operator reaches any of the", str(code))

    # GREEN_ON_BASE(characterization): the quiet pass the refusal above must not widen to swallow.
    def test_Given_ADiffOfOnlyDocumentationComments_When_TheCampaignStarts_Then_ItPassesQuietly(self):
        # Arrange — the counterpart, so the case above is not passing for a refusal of every change
        # that generates nothing.
        campaign = StubbedCampaign("/// <summary>Nothing to ask about here.</summary>\n")

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertEqual(code, 0)


class NarrowedRunTests(unittest.TestCase):
    """A narrowed run must sign nothing off.

    Under `--filter` almost everything survives, so a declaration earned there is well-formed,
    written by the branch, and indistinguishable in the tree from one earned against the suite.
    """

    DECLARED = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                // MUTANT_SURVIVES(equivalent): every caller clamps the operand already.
                internal static bool Ready(int a, int b) => a <= b;
            }
        }
        """)

    def test_Given_ADeclaredSurvivor_When_TheScopeIsNarrowed_Then_OnlyTheWholeSuiteSignsItOff(self):
        # Arrange — a declaration written into the tree. Under a narrowing nearly everything
        # survives, so signing one off there earns it against a question nobody asked.
        narrowed = StubbedCampaign(self.DECLARED)
        whole = StubbedCampaign(self.DECLARED)

        # Act
        codes = (narrowed.run_over_diff("--max", "40", "--filter", "Velvet.Tests.Probe"),
                 whole.run_over_diff("--max", "40"))

        # Assert — the two together, because the narrowed run refusing is also what a run that reads
        # no declaration under any scope does, and that is what this has to separate it from.
        self.assertEqual(codes, (1, 0))

    # GREEN_ON_BASE(characterization): the scope narrowing must keep, which the first cut of it lost.
    def test_Given_AFilter_When_TheRunIsScoped_Then_ItStillTakesTheDiffsFiles(self):
        # Arrange — narrowing loses the right to sign off, not the scope: gating the diff on it made
        # `--filter` measure nothing at all.
        campaign = StubbedCampaign()

        # Act
        code = campaign.run_over_diff("--max", "1", "--filter", "Velvet.Tests.Probe")

        # Assert — a survivor is still reported, so the run had something to mutate.
        self.assertEqual(code, 1)


class EmitLinesTests(unittest.TestCase):
    """A mode a read-only reviewer is told to run, on the strength of its mutating no source."""

    def test_Given_AnEmptyEmitLinesOperand_When_TheFlagIsRead_Then_NoCampaignStarts(self):
        # Arrange — an empty operand, which read as absence and fell through to the campaign, so a
        # command contracted to write nothing put a mutation into a package source.
        campaign = StubbedCampaign()

        # Act — the emit path itself refuses an empty destination; what is asked here is what ran
        # before it got there.
        with contextlib.suppress(OSError):
            campaign.run("--emit-lines", "")

        # Assert — nothing asked whether the machine was free, so neither a baseline nor a mutant ran.
        self.assertEqual(campaign.seen, [])


PROBE_WITH_VERBATIM = """\
namespace Velvet
{
    internal static class Probe
    {
        internal static bool Ready(int a, int b) => a <= b && Name().Length > 0;

        internal static string Name() => @"a

b";
    }
}
"""


PROBE_WITH_LITERAL = """\
namespace Velvet
{
    internal static class Probe
    {
        internal static bool Ready(int a, int b) => a <= b && Name() == "ready";

        internal static string Name() => "ready";
    }
}
"""


class VerdictKeyTests(unittest.TestCase):
    """What a verdict record is keyed on, which is what lets a later run or `--collect` take it.

    The head tree cannot be the key: the campaign diffs the merge base against the **working tree**,
    so an uncommitted edit to a mutated file changes what it measured and moves no tree sha.
    """

    @staticmethod
    def key(campaign):
        since = mutation_check.merge_base_of(campaign.project, "HEAD")
        return mutation_check.scope_digest(since, {campaign.source: None}, campaign.project, "EditMode")

    def edited(self, body, before, after):
        campaign = StubbedCampaign(body)
        first = self.key(campaign)
        campaign.source.write_text(campaign.source.read_text().replace(before, after))
        return first != self.key(campaign)

    # GREEN_ON_BASE(refactor): the key's reading of this edit is unchanged; only the receipt that read it went.
    def test_Given_AMutatedFileEditedWithoutCommitting_When_ItIsKeyed_Then_TheKeyMoves(self):
        # Act / Assert
        self.assertTrue(self.edited(None, "a <= b", "a < b"))

    # GREEN_ON_BASE(refactor): the key's reading of this edit is unchanged; only the receipt that read it went.
    def test_Given_OnlyACommentAdded_When_ItIsKeyed_Then_TheKeyStays(self):
        # Arrange — a comment carries no mutant, so moving on one discards kills over a byte-identical
        # mutant set.
        # Act / Assert
        self.assertFalse(self.edited(None, "    internal static class Probe",
                                     "    // an edit after the run\n    internal static class Probe"))

    # GREEN_ON_BASE(refactor): the key's reading of this edit is unchanged; only the receipt that read it went.
    def test_Given_ABlankLineInsideAVerbatimStringGone_When_ItIsKeyed_Then_TheKeyMoves(self):
        # Arrange — an empty line inside a verbatim string is part of the value, and it is the one
        # place where removing a line changes behaviour. The digest drops the lines a comment
        # emptied, so this is the shape that drop must not reach.
        # Act / Assert
        self.assertTrue(self.edited(PROBE_WITH_VERBATIM, "a\n\nb", "a\nb"))

    # GREEN_ON_BASE(refactor): the key's reading of this edit is unchanged; only the receipt that read it went.
    def test_Given_AStringLiteralEdited_When_ItIsKeyed_Then_TheKeyMoves(self):
        # Arrange — a literal is masked for generation and kept in the digest: editing one changes
        # behaviour a mutant could have covered.
        # Act / Assert
        self.assertTrue(self.edited(PROBE_WITH_LITERAL, '"ready"', '"steady"'))


class ForkedCampaign(StubbedCampaign):
    """A branch cut from origin's main after a merge the local main has not followed."""

    def __init__(self):
        self.root = Path(tempfile.mkdtemp(prefix="mutation-check-fork-"))
        self.seen, self.busy = [], []
        self.primary = self.root / "primary"
        self.git(self.root, "init", "-q", "--bare", "-b", "main", "origin.git")
        self.git(self.root, "init", "-q", "-b", "main", "primary")
        self.git(self.primary, "remote", "add", "origin", str(self.root / "origin.git"))
        source = self.primary / self.SOURCE
        source.parent.mkdir(parents=True)
        source.write_text(self.BODY)
        (self.primary / ".gitignore").write_text("Library/\nout/\n")
        self.git(self.primary, "add", ".")
        self.git(self.primary, "commit", "-qm", "base")
        self.git(self.primary, "push", "-q", "origin", "main")
        # Recorded as a clone records it, so the `HEAD` case below has an origin ref of that name.
        self.git(self.primary, "remote", "set-head", "origin", "main")
        # Nothing holds main, so bringing it current is the refspec fetch.
        self.git(self.primary, "switch", "-q", "-c", "parked")
        self.land("first.md")
        self.project = self.root / "feature"
        self.git(self.primary, "worktree", "add", "-q", "-b", "feature", str(self.project),
                 "origin/main")
        self.source = self.project / self.SOURCE
        self.source.write_text(self.BODY.replace("a <= b", "a < b"))
        self.git(self.project, "commit", "-qam", "feature")
        (self.project / "Library" / "ScriptAssemblies").mkdir(parents=True)

    @staticmethod
    def git(cwd, *arguments):
        subprocess.run(["git", "-C", str(cwd), "-c", "user.email=t@t", "-c", "user.name=t",
                        *arguments], capture_output=True, check=True)

    def land(self, name):
        """A pull request merging on origin: origin/main moves and the local main does not."""
        self.git(self.primary, "switch", "-q", "--detach", "origin/main")
        (self.primary / name).write_text(name + "\n")
        self.git(self.primary, "add", name)
        self.git(self.primary, "commit", "-qm", name)
        self.git(self.primary, "push", "-q", "origin", "HEAD:main")
        self.git(self.primary, "switch", "-q", "parked")


class LaggingLocalBaseTests(unittest.TestCase):
    """A kill kept for a second run of the same campaign, while the local main lags origin's."""

    def setUp(self):
        self.campaign = ForkedCampaign()
        self.addCleanup(shutil.rmtree, self.campaign.root, ignore_errors=True)
        self.campaign.kills = True

    KEPT = "from a previous run of this campaign"

    # GREEN_ON_BASE(refactor): the key is unchanged; the case reads it through a resumed run where it read the receipt.
    def test_Given_ACampaignAgainstALaggingMain_When_MainIsBroughtCurrentAfterAMerge_Then_ItsKillIsKept(self):
        # Arrange — the base spelled as CONTRIBUTING.md spells it, an unrelated merge, and then the
        # local main brought current.
        ran = self.campaign.drive("--base", "main", "--max", "40")
        self.campaign.land("second.md")
        self.campaign.git(self.campaign.primary, "fetch", "-q", "origin", "main:main")

        # Act
        again = self.campaign.drive("--base", "main", "--max", "40")

        # Assert
        self.assertEqual((ran, again, self.KEPT in self.campaign.printed), (0, 0, True))

    # GREEN_ON_BASE(refactor): the key is unchanged; the case reads it through a resumed run where it read the receipt.
    def test_Given_ACampaignAgainstOriginsMain_When_TheLocalMainLagsItsForkPoint_Then_ItsKillIsKept(self):
        # Arrange — an unrelated merge after the campaign, with the local main left where it was.
        ran = self.campaign.drive("--base", "origin/main", "--max", "40")
        self.campaign.land("second.md")

        # Act
        again = self.campaign.drive("--base", "origin/main", "--max", "40")

        # Assert
        self.assertEqual((ran, again, self.KEPT in self.campaign.printed), (0, 0, True))

    # GREEN_ON_BASE(characterization): the base reads `HEAD` as this checkout's own commit.
    def test_Given_AnOriginCarryingAHead_When_TheChangeIsReadAgainstHead_Then_OnlyTheUncommittedIs(self):
        # Arrange — nothing uncommitted, and a committed change that reading origin's HEAD would take.
        project = self.campaign.project

        # Act
        read = (sorted(mutation_check.changed_files_and_lines(project, "HEAD")),
                sorted(mutation_check.changed_files_and_lines(project, "origin/main")))

        # Assert — the reading against the fork point rides along, because one that found nothing at
        # all would satisfy the first half.
        self.assertEqual(read, ([], [self.campaign.source]))


class MutationRefusalStatusTests(unittest.TestCase):
    """The two statuses the commit hook reads, held against the script that produces them.

    The hook tells a campaign holding a file from a script that could not answer, and it does that by
    the exit status. A copy of that number drifting is the hook reporting the wrong one of the two.
    """

    HOOK = REPO_ROOT / ".claude/hooks/refuse/commit_failing_fast_checks.py"

    def test_Given_TheCommitHook_When_ItsRefusalStatusIsRead_Then_ItIsTheOneTheScriptExits(self):
        # Arrange
        declared = re.search(r"^CARRIED_REFUSAL = (\d+)", self.HOOK.read_text(), re.MULTILINE)

        # Act
        mirrored = int(declared.group(1)) if declared else None

        # Assert
        self.assertEqual(mirrored, mutation_check.CARRIED_REFUSAL)

    # GREEN_ON_BASE(construction): both timeouts come from the checked-out tree; the base's larger
    # registration also exceeds the subprocess timeout in seconds. Setting its `timeout` to
    # `CARRIED_TIMEOUT` makes this case fail.
    def test_Given_TheCommitHook_When_ItsSubprocessTimeoutIsRead_Then_ItIsUnderTheRegisteredOne(self):
        # Arrange — the harness kills a hook at its registered timeout, and a subprocess outliving
        # that takes the refusal with it, which is the reading that lets the commit through.
        settings = json.loads((REPO_ROOT / ".claude/settings.json").read_text())
        registered = [held["timeout"] for entry in settings["hooks"]["PreToolUse"]
                      for held in entry["hooks"] if self.HOOK.name in held["command"]]
        declared = re.search(r"^CARRIED_TIMEOUT = (\d+)", self.HOOK.read_text(), re.MULTILINE)

        # Assert — the registered count rides along because an empty list satisfies `all`.
        self.assertEqual((len(registered), all(int(declared.group(1)) < each for each in registered)),
                         (1, True))


class CampaignVerdictTests(unittest.TestCase):
    """What the run's own exit status and its records' key are a function of.

    Each case here was written after removing the term it names left the whole suite green: the cap,
    the unmeasured mutants, and the merge base in the key.
    """

    SEVERAL = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b) => a <= b;
                internal static bool Live(int a, int b) => a >= b;
            }
        }
        """)

    def test_Given_MoreMutantsThanTheCap_When_TheRunFinishes_Then_ItFails(self):
        # Arrange — two mutants against a cap of one, so the run measured fewer than the branch has
        # and reporting a pass would be a verdict about the one it never ran.
        campaign = StubbedCampaign(self.SEVERAL)
        campaign.kills = True

        # Act
        code = campaign.run_over_diff("--max", "1")

        # Assert
        self.assertEqual(code, 1)

    # GREEN_ON_BASE(characterization): the passing run the cap case above must be told apart from.
    def test_Given_EveryMutantWithinTheCap_When_TheRunFinishes_Then_ItPasses(self):
        # Arrange — the same two mutants with the cap above them, so the case above is not passing
        # for a run that always fails.
        campaign = StubbedCampaign(self.SEVERAL)
        campaign.kills = True

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(code, 0)

    # GREEN_ON_BASE(characterization): the base refuses every timeout, so it refuses this one
    # too. What this pins is that the half still refused stays refused.
    def test_Given_AMutantTheEditorNeverRan_When_TheBaselineWasAlsoSlow_Then_ItFails(self):
        # Arrange — a mutant nobody asked about must never be a pass, and a baseline already near the
        # bound is what makes a mutant reaching it say nothing about the mutation.
        campaign = StubbedCampaign()
        campaign.times_out = True
        campaign.baseline_seconds = 500.0

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(code, 1)

    # GREEN_ON_BASE(refactor): the verdict is the base's; the case reads it without the receipt it read it through.
    def test_Given_AMutantTheEditorNeverRebuilt_When_ItsLineIsDeclared_Then_NothingSignsItOff(self):
        # Arrange — the assembly comes out byte-identical, so the suite ran the unmutated binary and
        # answered about nothing. The declaration is what tells this apart from an ordinary survivor,
        # which also exits 1 undeclared and passes declared.
        campaign = StubbedCampaign(DECLARED_BODY)
        campaign.not_rebuilt = True

        # Act
        run = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(run, 1)

    def test_Given_TwoMergeBases_When_TheScopeIsDigested_Then_TheKeysDiffer(self):
        # Arrange — the merge base is half the key, and every document leads with it. Without it a
        # kill kept against one base answers for a branch rebased onto another.
        campaign = StubbedCampaign()
        targets = {campaign.source: None}

        # Act
        keys = {mutation_check.scope_digest(base, targets, campaign.project, "EditMode")
                for base in ("aaaaaaa", "bbbbbbb")}

        # Assert
        self.assertEqual(len(keys), 2)

    def test_Given_TwoPlatforms_When_TheScopeIsDigested_Then_TheKeysDiffer(self):
        # Arrange — a PlayMode campaign is a statement about the PlayMode suite, and a record that
        # did not say so would let one answer a question the other was asked.
        campaign = StubbedCampaign()
        targets = {campaign.source: None}

        # Act
        keys = {mutation_check.scope_digest("aaaaaaa", targets, campaign.project, platform)
                for platform in ("EditMode", "PlayMode")}

        # Assert
        self.assertEqual(len(keys), 2)

    # GREEN_ON_BASE(refactor): the key is the base's; the receipt half of the reading went with the receipt.
    def test_Given_AnOlderOperatorModel_When_ItsArtifactsAreRead_Then_NeitherIsReused(self):
        # Arrange -- the source, base and platform stay fixed while the generation rules change.
        campaign = StubbedCampaign()
        targets = {campaign.source: None}
        old_digest = mutation_check.scope_digest(
            "aaaaaaa", targets, campaign.project, "EditMode")
        output = campaign.project / "out"
        output.mkdir(parents=True, exist_ok=True)
        mutant = mutation_check.Mutant(campaign.source, 5, 0, "<=", "<", "boundary")
        mutant.verdict = mutation_check.KILLED
        mutation_check.write_verdict(output, 1, old_digest, mutant, campaign.project)

        # Act
        with mock.patch.object(mutation_check, "MUTATION_MODEL_VERSION", "next", create=True):
            new_digest = mutation_check.scope_digest(
                "aaaaaaa", targets, campaign.project, "EditMode")
            reading = mutation_check.read_verdict(output, 1, new_digest, mutant, campaign.project)

        # Assert
        self.assertEqual((new_digest != old_digest, reading), (True, None))


class UncompilableMutantTests(unittest.TestCase):
    """A mutant the build rejected is one nobody asked about, and this run must not pass on it.

    It is not a survivor -- no test could fail on a binary that was never produced -- so a reading
    that only counts survivors lets it through wherever a declaration answers for the line. That is this file's own thesis, and it was open here: the earlier version of this case
    asserted the passing exit and locked it in.
    """

    # GREEN_ON_BASE(refactor): the verdict is the base's; the case reads it without the receipt it read it through.
    def test_Given_AMutantThisRepositorysAnalyzersRejected_When_ItIsDecided_Then_NothingSignsItOff(self):
        # Arrange — VEL501 is this repository's own branching-complexity limit, reported as an error
        # and invisible to a reading that matches `error CS`. The suite writes a green result either
        # way when the build produced no assembly, so without the log this reads as a survivor.
        campaign = StubbedCampaign(DECLARED_BODY)
        campaign.build_error = True

        # Act
        run = campaign.run_over_diff("--max", "40")

        # Assert — declared, because a run that treats this as an ordinary survivor also exits 1
        # undeclared, and passes declared.
        self.assertEqual(run, 1)


class InapplicableOperatorTests(unittest.TestCase):
    """A `+ -> -` rewrite the build stops on with CS0019 for its `-` produced no program, so it is
    neither a survivor nor a mutant nobody measured.

    The arithmetic reader declines a join only where a literal shows the string, and this answers for
    the rest.
    """

    SOURCE = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static string Label(Row row, int k) => row.Name
                    + k;
            }
        }
        """)

    def log(self, code, message):
        # The editor log's shape: whole copies, one with its front cut off, and the closing banner.
        whole = "Packages/com.velvet.core/Runtime/Probe.cs(5,58): error {}: {}".format(code, message)
        return "\n".join([whole, whole, whole[len("Packages/com.velvet.core/Runt"):],
                          "Scripts have compiler errors."])

    def test_Given_CS0019ForTheRewrittenMinus_When_TheCampaignDecides_Then_ItPassesAndNamesIt(self):
        # Arrange -- placed where the binary expression starts, one line above the operator, and no
        # declaration above the line: there was nothing a test could be asked.
        campaign = StubbedCampaign(self.SOURCE)
        campaign.error_log = self.log(
            "CS0019", "Operator '-' cannot be applied to operands of type 'string' and 'int'")

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual((code, "--- rewrites the compiler rejected" in campaign.printed), (0, True))

    # GREEN_ON_BASE(characterization): the base fails this too; this pins that another error on the
    # same line still fails the campaign now that CS0019 for the `-` does not.
    def test_Given_AnotherErrorForTheRewrittenLine_When_TheCampaignDecides_Then_ItFails(self):
        # Arrange -- CS0023 reads the same up to its operand count, and is a unary operator's.
        campaign = StubbedCampaign(self.SOURCE)
        campaign.error_log = self.log(
            "CS0023", "Operator '-' cannot be applied to operand of type 'string'")

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual((code, mutation_check.UNCOMPILABLE in campaign.printed), (1, True))

class RejectingJoinCampaign(StubbedCampaign):
    """`Log(a + b, ok && other);` on one line: the cap keeps the `+ -> -` rewrite, which the compiler
    rejects for strings, and drops a clause removal and a logic flip behind it. The editor is replaced
    by a function of the text on disk: CS0019 where the join was rewritten, a failing suite where the
    text holds `kills_when`, a green one otherwise."""

    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static void Run(string a, string b, bool ok, bool other) { Log(a + b, ok && other); }
            }
        }
        """)
    kills_when = None

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        mutant = Path(results).name != "baseline.xml"
        text = self.source.read_text()
        Path(log).write_text(
            "Packages/com.velvet.core/Runtime/Probe.cs(5,80): error CS0019: Operator '-' cannot be "
            "applied to operands of type 'string' and 'string'\n" if mutant and "a - b" in text else "")
        Path(results).write_text(FAILING_RESULTS if mutant and self.kills_when and self.kills_when in text
                                 else GREEN_RESULTS)
        return 0.0, False, 0

    def stood(self):
        return json.loads((self.project / "out" / "mutant-001.json").read_text())


class InapplicableFallbackTests(unittest.TestCase):
    """A line whose kept mutant is no program is asked about through the next one the cap dropped."""

    def test_Given_AJoinTheCompilerRejects_When_TheLineHoldsAnotherMutant_Then_ThatOneIsMeasured(self):
        # Arrange
        campaign = RejectingJoinCampaign()

        # Act
        campaign.run_over_diff("--max", "40")

        # Assert
        record = campaign.stood()
        self.assertEqual((record["verdict"], record.get("stood", "no such field")),
                         (mutation_check.SURVIVED, "clause removed"))

    def test_Given_AJoinTheCompilerRejects_When_TheNextMutantIsKilled_Then_TheLineIsKilledAndPasses(self):
        # Arrange
        campaign = RejectingJoinCampaign()
        campaign.kills_when = "Log(a + b, ok);"

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual((code, campaign.stood()["verdict"]), (0, mutation_check.KILLED))

    def test_Given_TheFallbackStood_When_TheVerdictIsRecorded_Then_TheKeptMutantIsTheRecordsKey(self):
        # Arrange
        campaign = RejectingJoinCampaign()

        # Act
        campaign.run_over_diff("--max", "40")

        # Assert -- the record is the kept mutant's, so the verdict job reading the plan's list finds it.
        record = campaign.stood()
        self.assertEqual((record["mutant"].endswith("+ -> - (arithmetic)"), record.get("stood", "no such field")),
                         (True, "clause removed"))

    def test_Given_NoOtherMutantOnTheLine_When_TheKeptOneIsRejected_Then_TheLineIsInapplicable(self):
        # Arrange -- the control: nothing to fall back to is today's verdict.
        campaign = RejectingJoinCampaign(RejectingJoinCampaign.BODY.replace(" ok && other", " 0"))

        # Act
        campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual((campaign.stood()["verdict"], campaign.stood().get("stood", "no such field")),
                         (mutation_check.INAPPLICABLE, None))


class HoldOrderingTests(unittest.TestCase):
    """The record is written before the mutation, which is the whole of the interruption mechanism.

    Every other case reads the tree after the campaign has finished or been signalled, and both
    orders look identical there. What separates them is what is on disk at the instant the record is
    taken, so that is what this reads.
    """

    def test_Given_ACampaignAboutToMutate_When_TheRecordIsTaken_Then_TheSourceIsStillTheOriginal(self):
        # Arrange — the other order leaves a window where a mutation is on disk and nothing names it,
        # which is the state a killed campaign was found in twice.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        seen = []
        held = mutation_check.Holder.hold

        def watch(self, source, first, mutated, description):
            seen.append(Path(source).read_text())
            return held(self, source, first, mutated, description)

        # Act
        mutation_check.Holder.hold = watch
        try:
            campaign.run_over_diff("--max", "1")
        finally:
            mutation_check.Holder.hold = held

        # Assert — the count rides along because no call at all also leaves nothing unequal.
        self.assertEqual((len(seen), seen[:1]), (1, [original]))


class DeclarationOwnershipTests(unittest.TestCase):
    """Which declarations count as this branch's, read the way the campaign reads them.

    `VerdictTests` builds `Declaration` objects by hand, so the field is set there rather than
    derived; setting `written_here` to a constant in `declarations_for` reddened none of them.
    """

    SOURCE = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                // MUTANT_SURVIVES(equivalent): every caller clamps the operand already.
                internal static bool Ready(int a, int b) => a <= b;
            }
        }
        """)

    WRAPPED = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                // MUTANT_SURVIVES(equivalent): every caller clamps the operand, so
                // both bounds accept the same set.
                internal static bool Ready(int a, int b) => a <= b;
            }
        }
        """)

    def read(self, changed_lines, source=None):
        campaign = StubbedCampaign(self.SOURCE if source is None else source)
        found = mutation_check.declarations_for(
            {campaign.source: None}, {campaign.source: changed_lines})
        return sorted({held.written_here for held in found.values()})

    def test_Given_ADeclarationOnALineTheBranchWrote_When_ItIsRead_Then_ItIsTheBranchsOwn(self):
        # Arrange — the declaration sits on line 5 of the fixture.
        # Act / Assert
        self.assertEqual(self.read({5}), [True])

    def test_Given_ADeclarationOnALineTheBranchDidNotWrite_When_ItIsRead_Then_ItIsNotTheBranchsOwn(self):
        # Arrange — the same file with the declaration's own line outside the diff, which is a
        # declaration the base already carried and does not answer for this change.
        # Act / Assert
        self.assertEqual(self.read({6}), [False])

    def test_Given_OnlyTheWrappedHalfOfAReasonRewritten_When_ItIsRead_Then_ItIsTheBranchsOwn(self):
        # Arrange — the marker sits on line 5 and its reason runs onto line 6.
        # Act / Assert
        self.assertEqual(self.read({6}, self.WRAPPED), [True])


class VerdictNamingTests(unittest.TestCase):
    """Each classification branch, read by the verdict the run names rather than by its exit status.

    Five of the six exit 1, so a case asserting the status alone passes with the branch it is named
    for deleted -- all four were green under exactly that perturbation. The tally is read rather than
    the per-mutant progress line, because only the tally spells a verdict followed by a colon.
    """

    def tally_of(self, **how):
        campaign = StubbedCampaign()
        for field, value in how.items():
            setattr(campaign, field, value)
        code = campaign.run_over_diff("--max", "40")
        named = [verdict for verdict in (mutation_check.TIMED_OUT, mutation_check.HUNG,
                                         mutation_check.UNCOMPILABLE,
                                         mutation_check.NOT_BUILT, mutation_check.SURVIVED,
                                         mutation_check.KILLED)
                 if verdict + ":" in campaign.printed]
        return code, named

    def test_Given_ABaselineWithRoomToSpare_When_AMutantTimesOut_Then_ItIsKilledAndTheRunPasses(self):
        # Arrange — over the diff with no declaration anywhere, so a pass here is one no
        # MUTANT_SURVIVES line was needed for.
        # Act / Assert
        self.assertEqual(self.tally_of(times_out=True), (0, [mutation_check.HUNG]))

    def test_Given_AMutantThatHungTheSuite_When_TheRunIsDecided_Then_ItIsListedWithWhatItTook(self):
        # Arrange — the progress line prints the detail as well, so the listing is read under its own
        # heading rather than anywhere in the output.
        campaign = StubbedCampaign()
        campaign.times_out = True

        # Act
        campaign.run_over_diff("--max", "40")

        # Assert
        listed = campaign.printed.partition("--- mutants killed by hanging the suite ---\n")[2]
        self.assertIn("the suite ran past --timeout 900s", listed.splitlines()[0] if listed else "")

    def test_Given_AResultWrittenBeforeTheTimeout_When_TheBaselineHadRoomToSpare_Then_ItIsNotMeasured(self):
        # Arrange — the editor wrote a green result and then outran the bound, which a baseline with
        # the whole bound to spare does not make the mutation's doing.
        campaign = ResultThenTimeoutCampaign()

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual((code, mutation_check.TIMED_OUT + ":" in campaign.printed), (1, True))

    def test_Given_ABaselineAlreadyNearTheBound_When_AMutantReachesIt_Then_ItIsNotMeasured(self):
        # Arrange — a bound the suite was always going to outrun says nothing about the mutation, and
        # a mutant nobody asked about must never be a pass.
        # Act / Assert
        self.assertEqual(self.tally_of(times_out=True, baseline_seconds=500.0),
                         (1, [mutation_check.TIMED_OUT]))

    def test_Given_ABuildTheAnalyzersStopped_When_TheRunIsTallied_Then_ItNamesTheUncompilable(self):
        # Act / Assert
        self.assertEqual(self.tally_of(build_error=True), (1, [mutation_check.UNCOMPILABLE]))

    def test_Given_AnAssemblyNeverRebuilt_When_TheRunIsTallied_Then_ItNamesTheNotBuilt(self):
        # Act / Assert
        self.assertEqual(self.tally_of(not_rebuilt=True), (1, [mutation_check.NOT_BUILT]))

    def test_Given_ARunnerThatWroteNoResult_When_TheRunIsTallied_Then_ItNamesTheUncompilable(self):
        # Arrange — a results file that never appeared is the shape a licence failure or a crash
        # takes, and it is classified apart from the build-error reading above it.
        # Act / Assert
        self.assertEqual(self.tally_of(no_results=True), (1, [mutation_check.UNCOMPILABLE]))

    def test_Given_AnOrdinarySurvivor_When_TheRunIsTallied_Then_ItNamesNoneOfThem(self):
        # Arrange — the counterpart, so the four above are not passing for a tally naming everything.
        # Act / Assert
        self.assertEqual(self.tally_of(), (1, [mutation_check.SURVIVED]))


class ResultThenTimeoutCampaign(StubbedCampaign):
    """A mutant editor that writes a green result and is then killed at `--timeout`."""

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        Path(results).write_text(GREEN_RESULTS)
        Path(log).write_text("")
        return 0.0, Path(results).name != "baseline.xml", 0


class StaleDeclarationTests(unittest.TestCase):
    """A declaration outliving what it describes, driven through `main` rather than through `answered`.

    "A declaration cannot outlive what it describes" is one of this branch's two headline guarantees,
    and every other case for it calls the decision function directly -- so dropping `stale` from the
    run's failure expression left the whole suite green.
    """

    DECLARED_AND_KILLED = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                // MUTANT_SURVIVES(equivalent): every caller clamps the operand already.
                internal static bool Ready(int a, int b) => a <= b;
            }
        }
        """)

    # GREEN_ON_BASE(refactor): the verdict is the base's; the case reads it without the receipt it read it through.
    def test_Given_ADeclarationOverALineWhoseMutantDied_When_TheCampaignRuns_Then_NothingSignsItOff(self):
        # Arrange — the declaration answers for a survivor, and the mutant on that line is killed, so
        # it describes a state the tree is no longer in.
        campaign = StubbedCampaign(self.DECLARED_AND_KILLED)
        campaign.kills = True

        # Act
        run = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(run, 1)

    # GREEN_ON_BASE(refactor): the verdict is the base's; the case reads it without the receipt it read it through.
    def test_Given_ADeclarationOverASurvivingLine_When_TheCampaignRuns_Then_ItSignsItOff(self):
        # Arrange — the counterpart, so the case above is not passing for a run that refuses any tree
        # carrying a declaration at all.
        campaign = StubbedCampaign(self.DECLARED_AND_KILLED)

        # Act
        run = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(run, 0)


class SignalledCampaignTests(unittest.TestCase):
    """The campaign's own restore-on-signal, rather than the helper's.

    The case next door drives a bespoke script calling `Holder.guard()` itself, so it pins the helper
    and stays green with the call removed from `main`. This runs the campaign, and two things decide
    whether it can tell the difference: the signal has to land while the mutation is on disk, and the
    stubbed editor has to write a results file the baseline can actually parse. Missing either one
    leaves the campaign stopped before the loop with the source untouched, which is the same reading
    a restored tree gives.
    """

    # GREEN_ON_BASE(characterization): pins existing restoration with a signal delivered while a mutation is held.
    def test_Given_ARunningCampaign_When_ItIsSignalled_Then_TheSourceIsPutBack(self):
        # Arrange — a campaign whose editor never returns, so the signal lands while it holds.
        campaign = StubbedCampaign()
        original = campaign.source.read_text()
        driver = campaign.project / "drive.py"
        driver.write_text(textwrap.dedent("""\
            import importlib.util, sys
            spec = importlib.util.spec_from_file_location("mutation_check", sys.argv[1])
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)

            def hang(*a, **k):
                print("holding", flush=True)
                while True:
                    pass

            results_text = sys.argv[2]

            def green(_u, _p, _pl, _s, results, log, _t, _h=None):
                open(results, "w").write(results_text)
                open(log, "w").write("")
                return 0.0, False, 0

            calls = {"n": 0}

            def suite(*a, **k):
                calls["n"] += 1
                return green(*a, **k) if calls["n"] == 1 else hang()

            module.run_suite = suite
            module.unity_busy = lambda: 0
            sys.argv = ["mutation_check.py", "--project", sys.argv[3], "--base", "HEAD",
                        "--output", sys.argv[4], "--max", "1"]
            sys.exit(module.main())
            """))

        # Act
        running = subprocess.Popen(
            [sys.executable, str(driver), str(Path(mutation_check.__file__)), GREEN_RESULTS,
             str(campaign.project), str(campaign.project / "out")],
            stdout=subprocess.PIPE, text=True)
        while True:
            said = running.stdout.readline()
            if not said or said.strip() == "holding":
                break
        held = said.strip() == "holding" and campaign.source.read_text() != original
        if running.poll() is None:
            os.kill(running.pid, signal.SIGTERM)
        running.wait(timeout=60)
        running.stdout.close()

        # Assert
        self.assertEqual((held, campaign.source.read_text()), (True, original))


class MaskRefusalTests(unittest.TestCase):
    def test_Given_ATargetFileTheMaskBlanks_When_TheCampaignStarts_Then_ItRefusesToRun(self):
        # Arrange — a blanked offset generates no mutant, so the campaign would otherwise report the
        # lines under it as having nothing to ask.
        campaign = StubbedCampaign('class Probe { string s = "unterminated;\n int a = 1 <= 2 ? 1 : 2; }\n')

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("swallows code", str(code))


class KeptVerdictTests(unittest.TestCase):
    """What a killed campaign leaves behind for the next run of the same one.

    Each verdict is recorded as it is reached, and a later run answers from the kills a failing case
    named — refusing one where the tree it measured has moved, since a verdict about other bytes is
    not a verdict — and measures the others again.

    The record the cases below refuse on their digest, their mutant or its column is a kill, because a
    kind the run measures again is refused whatever its key, and a case refusing one would pass with
    the key unread.
    """

    def setUp(self):
        self.output = Path(tempfile.mkdtemp(prefix="mutation-kept-"))
        self.addCleanup(shutil.rmtree, self.output, ignore_errors=True)
        self.project = Path(tempfile.mkdtemp(prefix="mutation-kept-project-"))
        self.addCleanup(shutil.rmtree, self.project, ignore_errors=True)
        self.mutant = mutation_check.Mutant(self.project / "A.cs", 3, 0, "a", "b", "literal")
        self.mutant.verdict = mutation_check.KILLED
        self.mutant.detail = "1 failed: Kills"
        mutation_check.write_verdict(self.output, 7, "digest-1", self.mutant, self.project)

    def read_back(self, verdicts):
        """What a later run reads of one record per verdict, each under this campaign and this mutant."""
        for index, verdict in enumerate(verdicts, start=1):
            self.mutant.verdict, self.mutant.detail = verdict, "the first run's detail"
            mutation_check.write_verdict(self.output, index, "digest-1", self.mutant, self.project)
        return [mutation_check.read_verdict(self.output, index, "digest-1", self.mutant, self.project)
                for index in range(1, len(verdicts) + 1)]

    def test_Given_ACampaignThatKilledAMutant_When_TheRecordIsRead_Then_ItNamesEveryKiller(self):
        # Arrange — the detail names three for a reader. Which cases kill which mutant is a reading
        # nothing else carries, and it is on disk here anyway.
        mutation_check.write_verdict(self.output, 8, "digest-1", self.mutant, self.project,
                                     ("N.C.b", "N.C.a", "N.C.c", "N.C.d"))

        # Act / Assert
        self.assertEqual(json.loads((self.output / "mutant-008.json").read_text())["killers"],
                         ["N.C.a", "N.C.b", "N.C.c", "N.C.d"])

    def test_Given_AMutantWhoseLaunchesRanCases_When_ItsRecordIsCollected_Then_TheMutantCarriesTheCounts(self):
        # Arrange
        self.mutant.ran = (("covered", 3), ("whole", 9624))
        mutation_check.write_verdict(self.output, 9, "digest-1", self.mutant, self.project)
        self.mutant.ran = ()

        # Act
        mutation_check.collected([self.output], 9, "digest-1", self.mutant, self.project)

        # Assert
        self.assertEqual(self.mutant.ran, (("covered", 3), ("whole", 9624)))

    # GREEN_ON_BASE(characterization): the base keeps every verdict, a kill among them.
    def test_Given_AVerdictThisCampaignWrote_When_TheSameMutantIsReached_Then_ItIsAnswered(self):
        # Act / Assert
        self.assertEqual(
            mutation_check.read_verdict(self.output, 7, "digest-1", self.mutant, self.project),
            (mutation_check.KILLED, "1 failed: Kills"))

    def test_Given_TheTreeHasMovedSince_When_TheSameMutantIsReached_Then_TheVerdictIsRefused(self):
        # Arrange — the digest covers the working tree, so an edit to a mutated file moves it.
        # Act / Assert
        self.assertIsNone(
            mutation_check.read_verdict(self.output, 7, "digest-2", self.mutant, self.project))

    def test_Given_ADifferentMutantAtThatIndex_When_ItIsReached_Then_TheVerdictIsRefused(self):
        # Arrange — an index is this mutant's only while the list is the same list.
        other = mutation_check.Mutant(self.project / "A.cs", 3, 0, "a", "b", "boundary")

        # Act / Assert
        self.assertIsNone(
            mutation_check.read_verdict(self.output, 7, "digest-1", other, self.project))

    def test_Given_ASiblingThatDescribesAlike_When_ItIsReachedAtThatIndex_Then_TheVerdictIsRefused(self):
        # Arrange — the same edit further along the same line, which describes exactly as the recorded
        # mutant does. `--files` takes a file whole where the diff takes its changed lines, so one
        # list can put this mutant at the index the other recorded.
        sibling = mutation_check.Mutant(self.project / "A.cs", 3, 5, "a", "b", "literal")

        # Act / Assert
        self.assertIsNone(
            mutation_check.read_verdict(self.output, 7, "digest-1", sibling, self.project))

    def test_Given_AVerdictThatMeasuredNothing_When_TheSameMutantIsReached_Then_ItIsMeasuredAgain(self):
        # Arrange — each kind the run fails on for want of a reading, keyed so that only the kind can
        # refuse it.
        unmeasured = [mutation_check.TIMED_OUT, mutation_check.UNCOMPILABLE, mutation_check.NOT_BUILT]

        # Act
        read = self.read_back(unmeasured)

        # Assert
        self.assertEqual(read, [None] * len(unmeasured))

    def test_Given_ASurvivorsVerdict_When_TheSameMutantIsReached_Then_ItIsMeasuredAgain(self):
        # Arrange — both kinds a survivor comes back as, keyed so that only the kind can refuse them.
        surviving = [mutation_check.SURVIVED, mutation_check.INCONCLUSIVE]

        # Act
        read = self.read_back(surviving)

        # Assert
        self.assertEqual(read, [None] * len(surviving))

    def test_Given_AVerdictTheSuiteHungOn_When_TheSameMutantIsReached_Then_ItIsMeasuredAgain(self):
        # Arrange — keyed so that only the verdict kind can refuse it.
        hung = [mutation_check.HUNG]

        # Act
        read = self.read_back(hung)

        # Assert
        self.assertEqual(read, [None])


class ResumedCampaignTests(unittest.TestCase):
    """The second of two runs over one output directory, read by the verdicts its tally names and by
    whether it says any came from the first.

    `KeptVerdictTests` reads the record directly, so a campaign that never consulted it would pass
    every case there; these drive `main` twice.
    """

    NAMED = (mutation_check.TIMED_OUT, mutation_check.HUNG, mutation_check.UNCOMPILABLE,
             mutation_check.NOT_BUILT, mutation_check.SURVIVED, mutation_check.KILLED)

    def second_run(self, first, second, first_flags=(), second_flags=()):
        """(exit status, verdicts the tally names, whether any came from the first run) of a run whose
        editor behaves as `second`, following one whose editor behaved as `first`, each run narrowed
        by the flags beside it."""
        campaign = StubbedCampaign()
        for field, value in first.items():
            setattr(campaign, field, value)
        campaign.run_over_diff("--max", "40", *first_flags)
        for field in first:
            setattr(campaign, field, getattr(StubbedCampaign, field))
        for field, value in second.items():
            setattr(campaign, field, value)
        code = campaign.run_over_diff("--max", "40", *second_flags)
        return (code, [verdict for verdict in self.NAMED if verdict + ":" in campaign.printed],
                "came from a previous run of this campaign" in campaign.printed)

    def test_Given_AMutantTheFirstRunTimedOut_When_TheCampaignRunsAgain_Then_ItIsMeasuredAgain(self):
        # Arrange — the first baseline sat near the bound, so the mutant reaching it went unmeasured;
        # this time the editor finishes, whether for a raised --timeout or a quieter machine.
        first = {"times_out": True, "baseline_seconds": 500.0}
        second = {"kills": True}

        # Act
        read = self.second_run(first, second)

        # Assert
        self.assertEqual(read, (0, [mutation_check.KILLED], False))

    def test_Given_ASurvivorATestWasWrittenFor_When_TheCampaignRunsAgain_Then_ItIsMeasuredAgain(self):
        # Arrange — the first run's survivor, and a suite that kills it by the second, which is what a
        # test written in between does.
        first = {}
        second = {"kills": True}

        # Act
        read = self.second_run(first, second)

        # Assert
        self.assertEqual(read, (0, [mutation_check.KILLED], False))

    # GREEN_ON_BASE(characterization): the base keeps every verdict, so it keeps this kill too.
    def test_Given_AMutantTheFirstRunKilled_When_TheCampaignRunsAgain_Then_TheKillIsKept(self):
        # Arrange — the second run's suite lets the mutant survive, so a kill can only be the first
        # run's. The counterpart, so the two above are not passing for a campaign that keeps nothing.
        first = {"kills": True}
        second = {}

        # Act
        read = self.second_run(first, second)

        # Assert
        self.assertEqual(read, (0, [mutation_check.KILLED], True))

    def test_Given_AKillTheWholeSuiteTook_When_ARunNarrowedByFilterFollows_Then_ItIsMeasuredAgain(self):
        # Arrange — the fixture the second run asks about does not notice the mutant, which another
        # test in the whole suite killed.
        first = {"kills": True}
        second = {}

        # Act
        read = self.second_run(first, second, second_flags=("--filter", "Suspect.Fixture"))

        # Assert
        self.assertEqual(read, (1, [mutation_check.SURVIVED], False))

    def test_Given_AKillTheWholeSuiteTook_When_ARunNarrowedByAssembliesFollows_Then_ItIsMeasuredAgain(self):
        # Arrange — the sibling narrowing, which reaches the editor as a flag of its own.
        first = {"kills": True}
        second = {}

        # Act
        read = self.second_run(first, second, second_flags=("--assemblies", "Suspect.Tests"))

        # Assert
        self.assertEqual(read, (1, [mutation_check.SURVIVED], False))

    def test_Given_AKillANarrowedRunTook_When_TheWholeSuiteFollows_Then_ItIsMeasuredAgain(self):
        # Arrange — narrowed by both flags, so that this turns on what the whole run reads rather than
        # on how either flag is recorded. The second run reads declarations, and its suite lets the
        # mutant survive.
        first = {"kills": True}
        second = {}
        narrowed = ("--assemblies", "Suspect.Tests", "--filter", "Suspect.Fixture")

        # Act
        read = self.second_run(first, second, first_flags=narrowed)

        # Assert
        self.assertEqual(read, (1, [mutation_check.SURVIVED], False))

    # GREEN_ON_BASE(characterization): the base keeps every verdict, a narrowed run's kill among them.
    def test_Given_AKillANarrowedRunTook_When_TheSameNarrowingFollows_Then_TheKillIsKept(self):
        # Arrange — the counterpart, so the three above are not passing for a campaign that keeps
        # nothing a narrowed run touched.
        first = {"kills": True}
        second = {}
        narrowed = ("--filter", "Suspect.Fixture")

        # Act
        read = self.second_run(first, second, first_flags=narrowed, second_flags=narrowed)

        # Assert
        self.assertEqual(read, (0, [mutation_check.KILLED], True))


@contextlib.contextmanager
def listed_processes(listing):
    """A `ps` first on PATH that prints `listing` whatever it is asked, and marks that it ran."""
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        (root / "listing").write_bytes(listing)
        ps = root / "ps"
        ps.write_text('#!/bin/sh\n: > "{0}/ran"\nexec cat "{0}/listing"\n'.format(root))
        ps.chmod(0o755)
        saved = os.environ["PATH"]
        os.environ["PATH"] = directory + os.pathsep + saved
        try:
            yield root / "ran"
        finally:
            os.environ["PATH"] = saved


NOT_UTF8_NEIGHBOUR = b"/usr/bin/probe --label \xff"
EDITOR = b"/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -runTests -batchmode"


class ProcessListDecodingTests(unittest.TestCase):
    """One neighbour's stray byte in the process list must not end the run that reads it."""

    def test_Given_ANeighbourWhoseCommandLineIsNotUtf8_When_EditorsAreCounted_Then_TheEditorBesideItCounts(self):
        # Arrange
        listing = NOT_UTF8_NEIGHBOUR + b"\n" + EDITOR + b"\n"

        # Act
        with listed_processes(listing) as ran:
            try:
                counted = (mutation_check.unity_busy(), ran.exists())
            except UnicodeDecodeError as error:
                counted = (repr(error), ran.exists())

        # Assert — the mark separates this listing's count from the machine's own.
        self.assertEqual(counted, (1, True))




@contextlib.contextmanager
def lingering_editor():
    """An editor that runs until something kills it, and every process the run under test starts."""
    with tempfile.TemporaryDirectory() as directory:
        editor = Path(directory) / "Unity"
        editor.write_text("#!/bin/sh\nexec sleep 60\n")
        editor.chmod(0o755)
        started = []
        real = subprocess.Popen

        def recording(*args, **kwargs):
            process = real(*args, **kwargs)
            started.append(process)
            return process

        with mock.patch.object(subprocess, "Popen", recording):
            try:
                yield str(editor), started
            finally:
                for process in started:
                    if process.poll() is None:
                        process.kill()
                        process.wait()


def failing_count():
    raise OSError(35, "Resource temporarily unavailable")


class EditorReapedWhenAnExceptionEndsTheRunTests(unittest.TestCase):
    """An editor left running over a tree the harness has put back measures a change that is gone."""

    # GREEN_ON_BASE(refactor): the branch's kill starts a `ps` listing where there is no /proc, and
    # this case now leaves those out of what it reads; the editor it pins is the base's.
    def test_Given_ANeighbourCountThatFailsWhileTheEditorRuns_When_TheRunEnds_Then_TheEditorIsNotLeftRunning(self):
        # Arrange
        with lingering_editor() as (editor, started), mock.patch.object(mutation_check, "unity_busy",
                                                                        failing_count):
            # Act
            try:
                mutation_check.run_suite(editor, ".", "EditMode", [], Path("/dev/null"), Path("/dev/null"), 30)
                raised = False
            except OSError:
                raised = True
            left_running = [process.poll() is None for process in started if editor in process.args]

        # Assert
        self.assertEqual((raised, left_running), (True, [False]))


REFUSAL = ("It looks like another Unity instance is running with this project open.\n"
           "Multiple Unity instances cannot open the same project.\n")

def lock_state(project):
    """"free" or "held": whether both kinds of lock on `project`'s lock file could be taken now."""
    path = os.path.join(project, "Temp", "UnityLockfile")
    if not os.path.exists(path):
        return "free"
    descriptor = os.open(path, os.O_RDWR)
    try:
        fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        fcntl.flock(descriptor, fcntl.LOCK_UN)
        fcntl.lockf(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        return "free"
    except OSError:
        return "held"
    finally:
        os.close(descriptor)


# A process that takes one kind of lock on a file, records its pid, says so, and holds it.
HOLD_LOCK = textwrap.dedent("""\
    import fcntl, os, sys, time
    descriptor = os.open(sys.argv[1], os.O_RDWR | os.O_CREAT)
    getattr(fcntl, sys.argv[2])(descriptor, fcntl.LOCK_EX)
    open(sys.argv[3], "w").write(str(os.getpid()))
    print("held", flush=True)
    time.sleep(float(sys.argv[4]))
    """)


@contextlib.contextmanager
def scripted_editor(body):
    """An editor running `body` over a project of its own, with `project`, `results` and `log` read off
    its command line and `holders` naming where a lock holder records its pid, so that every holder a
    case starts is reaped afterwards whatever the run under test left behind."""
    with tempfile.TemporaryDirectory() as directory:
        root = Path(directory)
        project = root / "project"
        (project / "Temp").mkdir(parents=True)
        holders = root / "holders"
        holders.mkdir()
        editor = root / "Unity"
        editor.write_text("#!{}\n".format(sys.executable) + textwrap.dedent("""\
            import fcntl, os, subprocess, sys, time
            arguments = sys.argv[1:]
            project, results, log = (arguments[arguments.index(flag) + 1]
                                     for flag in ("-projectPath", "-testResults", "-logFile"))
            holders = {!r}
            """).format(str(holders)) + inspect.getsource(lock_state) + body)
        editor.chmod(0o755)
        try:
            yield str(editor), project, root, holders
        finally:
            for record in holders.iterdir():
                try:
                    os.kill(int(record.read_text()), signal.SIGKILL)
                except (ValueError, ProcessLookupError):
                    pass


def hold_lock(project, holders, kind, seconds):
    """A process outside any editor's group holding `kind` on the lock file for `seconds`, once it has."""
    holder = subprocess.Popen([sys.executable, "-c", HOLD_LOCK, str(project / "Temp" / "UnityLockfile"),
                               kind, str(holders / "outside"), str(seconds)],
                              stdout=subprocess.PIPE, text=True)
    holder.stdout.readline()
    holder.stdout.close()
    return holder


def run_scripted(editor, project, root, timeout=30):
    with contextlib.redirect_stdout(io.StringIO()):
        mutation_check.run_suite(editor, str(project), "EditMode", [], root / "results.xml",
                                 root / "run.log", timeout)


class LaunchTreeTests(unittest.TestCase):
    """Every launch starts from the tree `measure` read before the baseline, whatever the launch before
    it left there, and nothing the campaign itself holds is put back."""

    SOURCE = mutation_check.PACKAGE + "/Editor/Record.cs"

    def two_launches(self, relative, leave, between=None, results=None, before=None, launches=2,
                     uncommitted=None):
        """What each launch read at `relative`, each writing `left` there when `leave` holds, `before`
        running ahead of the first and `between` ahead of each later one. What the campaign printed and
        what it stopped with are kept as `printed` and `stopped`, and the run's put-back copies under
        `self.stash`."""
        body = textwrap.dedent("""\
            path = os.path.join(project, {relative!r})
            with open(os.path.join(os.path.dirname(project), "seen"), "a") as seen:
                seen.write((open(path).read() if os.path.exists(path) else "-") + "\\n")
            if {leave!r}:
                os.makedirs(os.path.dirname(path), exist_ok=True)
                open(path, "w").write("left")
            open(results, "w").write("<test-run />")
            """).format(relative=relative, leave=leave)
        self.printed, self.stopped = "", None
        with scripted_editor(body) as (editor, project, root, _holders):
            (project / "ProjectSettings").mkdir()
            (project / "ProjectSettings/Graphics.asset").write_text("clean")
            source = project / self.SOURCE
            source.parent.mkdir(parents=True)
            source.write_text('const string RecordFile = "Library/VelvetRecord.txt";')
            (project / ".gitignore").write_text("/Library/\n/Temp/\n")
            git = ["git", "-C", str(project), "-c", "user.name=t", "-c", "user.email=t@t", "-c",
                   "commit.gpgsign=false"]
            subprocess.run(git + ["init", "-q"], check=True)
            subprocess.run(git + ["add", ".gitignore", "ProjectSettings", "Packages"], check=True)
            subprocess.run(git + ["commit", "-q", "-m", "tree"], check=True)
            if uncommitted is not None:
                source.write_text(uncommitted)
            holder = mutation_check.Holder(project / mutation_check.SENTINEL)
            output = project / "out"
            holder.state = mutation_check.tree_before_baseline(project, output, holder.sentinel)
            output.mkdir()
            spoken = io.StringIO()
            try:
                with contextlib.redirect_stdout(spoken):
                    for launch in range(launches):
                        hook = between if launch else before
                        if hook:
                            hook(holder, project)
                        written = (results or (lambda _project, number: root / "results-{}.xml".format(
                            number)))(project, launch)
                        mutation_check.run_suite(editor, str(project), "EditMode", [], written,
                                                 root / "run.log", 30, holder)
            except SystemExit as stop:
                self.stopped = str(stop.code)
            finally:
                holder.release()
                self.printed = spoken.getvalue()
                self.put_back = holder.state.put_back
                self.left = {relative: copy.read_text() for relative, copy in holder.state.put_back if copy}
                self.after = {relative: (project / relative).read_text() if (project / relative).exists()
                              else "-" for relative in ("ProjectSettings/Graphics.asset", "Notes.txt", self.SOURCE)}
            return (root / "seen").read_text().splitlines()

    def test_Given_AFileALaunchLeft_When_TheNextLaunchPutsItBack_Then_ItsLineNamesWhereTheCopyIs(self):
        # Act
        self.two_launches("ProjectSettings/Graphics.asset", leave=True)

        # Assert
        (relative, copy), = self.put_back
        self.assertIn(mutation_check.put_back_line(relative, copy), self.printed.splitlines())

    def test_Given_AFileALaunchLeft_When_TheNextLaunchPutsItBack_Then_TheCopyHoldsWhatTheLaunchLeft(self):
        # Act
        self.two_launches("ProjectSettings/Graphics.asset", leave=True)

        # Assert
        self.assertEqual(self.left, {"ProjectSettings/Graphics.asset": "left"})

    def test_Given_TwoRunsOverOneOutput_When_EachPutsBackTheSameFile_Then_BothCopiesAreKept(self):
        # Arrange — a stopped campaign is finished by a second run over the same --output.
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            (project / ".gitignore").write_text("/out/\n")
            subprocess.run(["git", "-C", str(project), "init", "-q"], check=True)
            output = project / "out"
            runs = [mutation_check.tree_before_baseline(project, output, project / mutation_check.SENTINEL)
                    for _ in range(2)]
            copies = []

            # Act
            for run, left in zip(runs, ("first", "second")):
                (project / "Left.txt").write_text(left)
                (_, copy), = run.restore([])
                copies.append(copy)
            kept = [copy.read_text() for copy in copies]

        # Assert
        self.assertEqual(kept, ["first", "second"])

    def test_Given_AHeldSourceReleasedThenEdited_When_TheNextLaunchStarts_Then_TheEditStays(self):
        # Arrange — edited after the campaign put it back and before the next launch.
        def hold(holder, project):
            source = project / self.SOURCE
            holder.hold(source, source.read_text(), "mutated", "a mutant")
            source.write_text("mutated")

        def release_then_edit(holder, project):
            holder.release()
            (project / self.SOURCE).write_text("an edit")

        # Act
        self.two_launches(self.SOURCE, leave=False, before=hold, between=release_then_edit)

        # Assert — the campaign stopped naming it.
        self.assertEqual((self.after[self.SOURCE], self.SOURCE in (self.stopped or "")), ("an edit", True))

    def test_Given_AnUncommittedSourceHeldAndReleased_When_TheNextLaunchStarts_Then_ItRuns(self):
        # Arrange — a local campaign over a change not yet committed, as `--base` reads one.
        def hold(holder, project):
            source = project / self.SOURCE
            holder.hold(source, source.read_text(), "mutated", "a mutant")
            source.write_text("mutated")

        # Act
        seen = self.two_launches(self.SOURCE, leave=False, before=hold, between=lambda holder, _: holder.release(),
                                 uncommitted="uncommitted")

        # Assert
        self.assertEqual((self.stopped, seen), (None, ["mutated", "uncommitted"]))

    def test_Given_APutBackUnderAStrictUmask_When_ItsCopyIsKept_Then_ItIsReadableByOthers(self):
        # Arrange — CI keeps it as root inside the editor's container and uploads it as another user.
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            (project / ".gitignore").write_text("/out/\n")
            subprocess.run(["git", "-C", str(project), "init", "-q"], check=True)
            state = mutation_check.tree_before_baseline(project, project / "out", project / mutation_check.SENTINEL)
            (project / "deep" / "er").mkdir(parents=True)
            (project / "deep" / "er" / "Left.txt").write_text("left")
            previous = os.umask(0o077)
            try:
                # Act
                state.restore([])
            finally:
                os.umask(previous)
            stash = project / "out" / "put-back"
            unreadable = sorted(str(path.relative_to(stash)) for path in [stash, *stash.rglob("*")]
                                if path.stat().st_mode & (0o005 if path.is_dir() else 0o004)
                                != (0o005 if path.is_dir() else 0o004))
            kept = sum(1 for path in stash.rglob("Left.txt"))

        # Assert
        self.assertEqual((kept, unreadable), (1, []))

    def test_Given_ATrackedFileEditedBetweenTwoLaunches_When_TheNextLaunchStarts_Then_TheEditStays(self):
        # Arrange — no launch was running when it changed.
        def edit(_holder, project):
            (project / "ProjectSettings/Graphics.asset").write_text("an edit")

        # Act
        self.two_launches("ProjectSettings/Graphics.asset", leave=False, between=edit)

        # Assert — the campaign stopped naming it.
        self.assertEqual((self.after["ProjectSettings/Graphics.asset"],
                          "ProjectSettings/Graphics.asset" in (self.stopped or "")), ("an edit", True))

    def test_Given_AFileCreatedBetweenTwoLaunches_When_TheNextLaunchStarts_Then_ItStays(self):
        # Arrange
        def create(_holder, project):
            (project / "Notes.txt").write_text("notes")

        # Act
        self.two_launches("ProjectSettings/Graphics.asset", leave=False, between=create)

        # Assert
        self.assertEqual((self.after["Notes.txt"], "Notes.txt" in (self.stopped or "")), ("notes", True))

    def test_Given_AMutationReleasedAndAnotherHeldBetweenLaunches_When_TheNextLaunchStarts_Then_ItRuns(self):
        # Arrange — the first launch ran over a held mutation the campaign then put back itself.
        def hold(holder, project):
            source = project / self.SOURCE
            holder.hold(source, source.read_text(), "mutated", "a mutant")
            source.write_text("mutated")

        def release_and_hold_another(holder, project):
            holder.release()
            other = project / "ProjectSettings/Graphics.asset"
            holder.hold(other, other.read_text(), "mutated too", "another mutant")
            other.write_text("mutated too")

        # Act
        seen = self.two_launches(self.SOURCE, leave=False, before=hold, between=release_and_hold_another)

        # Assert
        self.assertEqual((self.stopped, len(seen)), (None, 2))

    def test_Given_AFileALaunchLeftThatGitDoesNotTrack_When_TheNextLaunchStarts_Then_ItIsGone(self):
        # Act
        seen = self.two_launches("ProjectSettings/VelvetBuildSettings.json", leave=True)

        # Assert
        self.assertEqual(seen, ["-", "-"])

    def test_Given_ATrackedFileALaunchChanged_When_TheNextLaunchStarts_Then_ItIsAsCommitted(self):
        # Act
        seen = self.two_launches("ProjectSettings/Graphics.asset", leave=True)

        # Assert
        self.assertEqual(seen, ["clean", "clean"])

    def test_Given_ALibraryFileThePackageNamesLeftBehind_When_TheNextLaunchStarts_Then_ItIsGone(self):
        # Arrange — Library/ is ignored, so git reports nothing there.
        relative = "Library/VelvetRecord.txt"

        # Act
        seen = self.two_launches(relative, leave=True)

        # Assert
        self.assertEqual(seen, ["-", "-"])

    def test_Given_AMutationHeldBetweenLaunches_When_TheNextLaunchStarts_Then_ItReadsTheMutation(self):
        # Arrange
        def mutate(holder, project):
            source = project / self.SOURCE
            original = source.read_text()
            holder.hold(source, original, "mutated", "a mutant")
            source.write_text("mutated")

        # Act
        seen = self.two_launches(self.SOURCE, leave=False, between=mutate)

        # Assert
        self.assertEqual(seen[1], "mutated")

    def test_Given_TheRecordOfAHeldMutation_When_TheNextLaunchStarts_Then_ItIsStillThere(self):
        # Arrange
        def mutate(holder, project):
            source = project / self.SOURCE
            holder.hold(source, source.read_text(), "mutated", "a mutant")
            source.write_text("mutated")

        # Act
        seen = self.two_launches(mutation_check.SENTINEL, leave=False, between=mutate)

        # Assert
        self.assertNotEqual(seen[1], "-")

    def test_Given_AResultTheFirstLaunchWroteUnderTheOutput_When_TheNextLaunchStarts_Then_ItIsKept(self):
        # Arrange — under a directory git reports, as an --output inside the project is.
        def results(project, number):
            return project / "out" / "results-{}.xml".format(number)

        # Act
        seen = self.two_launches("out/results-0.xml", leave=False, results=results)

        # Assert
        self.assertEqual(seen[1], "<test-run />")


class EditedSourceCampaign(StubbedCampaign):
    """A campaign whose mutated source is edited while it waits for the machine before a mutant."""

    EDIT = "// somebody's edit\n"

    def unity_busy(self):
        busy = super().unity_busy()
        if len(self.seen) == 2:
            self.source.write_text(self.EDIT)
        return busy


class EditedSourceTests(unittest.TestCase):
    def test_Given_ASourceEditedBeforeItsMutantIsWritten_When_TheCampaignRuns_Then_ItStopsAndTheEditStays(self):
        # Arrange
        campaign = EditedSourceCampaign()

        # Act
        stopped = campaign.run("--max", "1", "--launch-per-mutant")

        # Assert
        self.assertEqual((campaign.source.read_text(), "changed since the campaign read it" in str(stopped)),
                         (EditedSourceCampaign.EDIT, True))


class PutBackSummaryCampaign(StubbedCampaign):
    """A campaign whose baseline editor leaves a file git reports, through a real `launch`."""

    LEFT = "ProjectSettings/Left.json"

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        path = self.project / self.LEFT
        script = ("import os; os.makedirs({!r}, exist_ok=True); open({!r}, 'w').write('left')".format(
            str(path.parent), str(path)) if Path(results).name == "baseline.xml" else "pass")
        mutation_check.launch([sys.executable, "-c", script], 30, holder)
        return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)


class PutBackSummaryTests(unittest.TestCase):
    def test_Given_ALaunchThatLeftAFile_When_TheCampaignEnds_Then_ItsSummaryNamesTheCopy(self):
        # Arrange
        campaign = PutBackSummaryCampaign()

        # Act
        campaign.run("--max", "1", "--launch-per-mutant")

        # Assert — the one copy of what the baseline left, listed after the closing heading.
        copies = sorted((campaign.project / "out" / "put-back").glob("**/" + PutBackSummaryCampaign.LEFT))
        closing = campaign.printed.partition("put back during this campaign")[2].splitlines()
        self.assertEqual((len(copies), [line for line in closing if PutBackSummaryCampaign.LEFT in line]),
                         (1, [mutation_check.put_back_line(PutBackSummaryCampaign.LEFT, copy.resolve())
                              for copy in copies]))


class ProjectLockTests(unittest.TestCase):
    """An editor launched while the project's lock is held aborts before it measures anything, which
    a campaign read as a mutant that could not be built."""

    def launched_behind(self, kind):
        """What an editor found when it started, launched while another process held `kind` on the
        lock for two seconds."""
        with scripted_editor('open(os.path.join(project, "seen"), "w").write(lock_state(project))\n') as (
                editor, project, root, holders):
            holder = hold_lock(project, holders, kind, 2)
            try:
                run_scripted(editor, project, root)
            finally:
                holder.wait()
            return (project / "seen").read_text()

    def test_Given_AnotherProcessHoldingAFlockOnTheProject_When_TheEditorIsLaunched_Then_ItStartsOnceReleased(self):
        # Act
        seen = self.launched_behind("flock")

        # Assert
        self.assertEqual(seen, "free")

    def test_Given_AnotherProcessHoldingARecordLockOnTheProject_When_TheEditorIsLaunched_Then_ItStartsOnceReleased(self):
        # Act
        seen = self.launched_behind("lockf")

        # Assert
        self.assertEqual(seen, "free")

    def test_Given_AnEditorKilledAtItsBound_When_TheRunReturns_Then_NothingItStartedStillHoldsTheLock(self):
        # Arrange — the editor starts a process that takes the lock and holds it past any wait the run
        # makes, then never exits itself. The mark it leaves once the lock is taken rides along, since
        # a lock nobody took is free either way.
        body = textwrap.dedent("""\
            holding = subprocess.Popen([sys.executable, "-c", {!r},
                                        os.path.join(project, "Temp", "UnityLockfile"), "flock",
                                        os.path.join(holders, "inside"), "600"],
                                       stdout=subprocess.PIPE, text=True)
            holding.stdout.readline()
            open(os.path.join(project, "taken"), "w").write("")
            time.sleep(60)
            """).format(HOLD_LOCK)
        with scripted_editor(body) as (editor, project, root, _):
            # Act — the lock is read until it comes free or ten seconds pass, since what the kill
            # leaves behind is a process that is gone or one that is not, not how soon it goes.
            run_scripted(editor, project, root, timeout=1)
            deadline = time.time() + 10
            while lock_state(str(project)) == "held" and time.time() < deadline:
                time.sleep(0.2)
            state = ((project / "taken").exists(), lock_state(str(project)))

        # Assert
        self.assertEqual(state, (True, "free"))

    def lock_after_a_kill(self, starter):
        """Whether the editor's lock holder had taken the lock, and the lock's state up to ten seconds
        after a run that killed the editor at its bound returned; then what the run printed. The editor
        runs the holder through `starter`, a script handed HOLD_LOCK's command line."""
        body = textwrap.dedent("""\
            holding = subprocess.Popen([sys.executable, "-c", {!r}, sys.executable, "-c", {!r},
                                        os.path.join(project, "Temp", "UnityLockfile"), "flock",
                                        os.path.join(holders, "inside"), "600"],
                                       stdout=subprocess.PIPE, text=True)
            holding.stdout.readline()
            open(os.path.join(project, "taken"), "w").write("")
            time.sleep(60)
            """).format(starter, HOLD_LOCK)
        with scripted_editor(body) as (editor, project, root, _):
            printed = io.StringIO()
            with contextlib.redirect_stdout(printed):
                mutation_check.run_suite(editor, str(project), "EditMode", [], root / "results.xml",
                                         root / "run.log", 3)
            deadline = time.time() + 10
            while lock_state(str(project)) == "held" and time.time() < deadline:
                time.sleep(0.2)
            return ((project / "taken").exists(), lock_state(str(project))), printed.getvalue()

    # A holder outside the editor's process group. On CI an editor killed at its bound went on writing
    # its log and its results for minutes, which no process the kill reached could have done.
    LEFT_THE_GROUP = ("import subprocess, sys\n"
                      "sys.exit(subprocess.call(sys.argv[1:], start_new_session=True))\n")

    def test_Given_AnEditorWhoseLockHolderLeftItsProcessGroup_When_TheRunKillsTheEditorAtItsBound_Then_TheLockIsFree(self):
        # Act
        state, _ = self.lock_after_a_kill(self.LEFT_THE_GROUP)

        # Assert
        self.assertEqual(state, (True, "free"))

    def test_Given_AnEditorWhoseLockHolderLeftItsProcessGroup_When_TheRunKillsTheEditorAtItsBound_Then_ItSaysSo(self):
        # Act
        _, printed = self.lock_after_a_kill(self.LEFT_THE_GROUP)

        # Assert
        self.assertIn("killed 1 process(es) the editor left outside its process group", printed)

    @unittest.skipUnless(sys.platform.startswith("linux"), "the watchdog becomes a subreaper on Linux alone")
    def test_Given_ALockHolderOrphanedBeforeTheEditorIsKilled_When_TheRunKillsTheEditorAtItsBound_Then_TheLockIsFree(self):
        # Arrange — what started the holder exits at once, so by the kill the holder's parent is no
        # process the editor started.
        starter = ("import subprocess, sys\n"
                   "subprocess.Popen(sys.argv[1:], start_new_session=True)\n")

        # Act
        state, _ = self.lock_after_a_kill(starter)

        # Assert
        self.assertEqual(state, (True, "free"))

    def test_Given_AnEditorRefusedTheLockOnce_When_ItIsRun_Then_TheNextLaunchsResultStands(self):
        # Arrange
        body = textwrap.dedent("""\
            launches = os.path.join(project, "launches")
            count = len(open(launches).read()) if os.path.exists(launches) else 0
            open(launches, "a").write("x")
            open(log, "w").write("editor log\\n")
            if count == 0:
                sys.stdout.write({!r})
                sys.exit(1)
            open(results, "w").write({!r})
            """).format(REFUSAL, GREEN_RESULTS)
        with scripted_editor(body) as (editor, project, root, _):
            # Act
            run_scripted(editor, project, root)
            counts = mutation_check.read_counts(root / "results.xml")

        # Assert
        self.assertEqual(counts, {"total": 1, "passed": 1, "failed": 0, "inconclusive": 0})

    def test_Given_AnEditorRefusedTheLockOnEveryLaunch_When_ItIsRun_Then_ItsLogEndsSayingSo(self):
        # Arrange
        body = textwrap.dedent("""\
            open(log, "w").write("editor log\\n")
            sys.stdout.write({!r})
            sys.exit(1)
            """).format(REFUSAL)
        with scripted_editor(body) as (editor, project, root, _):
            # Act
            run_scripted(editor, project, root)
            last = (root / "run.log").read_text().rstrip().splitlines()[-1]

        # Assert
        self.assertEqual(last, getattr(mutation_check, "LOCK_REFUSED_LINE", None))


    # GREEN_ON_BASE(refactor): the base passes this too, since only the editor's bound changed here,
    # from one second to ten; what the case pins is unchanged.
    def test_Given_AnEditorRefusedTheLockThatOutlivesItsBoundEachTime_When_ItIsRun_Then_ItIsNotReadAsHung(self):
        # Arrange — a refusal followed by an editor that does not exit, so each launch ends at the
        # bound rather than at the refusal. The editor is given ten seconds rather than one: at one,
        # this case errored on a loaded machine.
        body = textwrap.dedent("""\
            open(log, "w").write("editor log\\n")
            sys.stdout.write({!r})
            sys.stdout.flush()
            time.sleep(60)
            """).format(REFUSAL)
        with scripted_editor(body) as (editor, project, root, _):
            # Act
            with contextlib.redirect_stdout(io.StringIO()):
                reading = mutation_check.run_suite(editor, str(project), "EditMode", [],
                                                   root / "results.xml", root / "run.log", 10)
            last = (root / "run.log").read_text().rstrip().splitlines()[-1]

        # Assert
        self.assertEqual((reading[1], last), (False, getattr(mutation_check, "LOCK_REFUSED_LINE", None)))


HARNESS = textwrap.dedent("""\
    import importlib.util, os, signal, subprocess, sys
    from pathlib import Path
    spec = importlib.util.spec_from_file_location("mutation_check", sys.argv[1])
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    editor, project, root, mode = sys.argv[2:6]
    holder = module.Holder(Path(root) / "sentinel.json")
    holder.guard()
    if mode == "signal-while-starting":
        # A leader that only runs the editor, so what ends it here is the handler and not the watchdog.
        module.WATCHDOG = "import subprocess, sys\\nsys.exit(subprocess.call(sys.argv[2:]))\\n"
        real = subprocess.Popen

        def starting(*arguments, **options):
            process = real(*arguments, **options)
            # Once: what the handler then starts itself is not an editor starting.
            subprocess.Popen = real
            os.kill(os.getpid(), signal.SIGTERM)
            return process

        subprocess.Popen = starting
    module.run_suite(editor, project, "EditMode", [], Path(root) / "results.xml", Path(root) / "run.log",
                     120, holder)
    """)


class EditorOutlivingItsHarnessTests(unittest.TestCase):
    """An editor left running after its campaign has gone measures a tree nobody is holding."""

    EDITOR = textwrap.dedent("""\
        print("the editor was here", flush=True)
        open(os.path.join(holders, "editor"), "w").write(str(os.getpid()))
        time.sleep(600)
        """)

    # A process the editor starts in a session of its own, which records itself where the editor's
    # pid would go, so that it is what `left_running` reads.
    ESCAPING = textwrap.dedent("""\
        subprocess.Popen([sys.executable, "-c",
                          "import os, sys, time\\n"
                          "open(sys.argv[1], 'w').write(str(os.getpid()))\\n"
                          "time.sleep(600)\\n",
                          os.path.join(holders, "editor")], start_new_session=True)
        time.sleep(600)
        """)

    def left_running(self, mode, stop, body=EDITOR):
        """Whether what `body` recorded was still up ten seconds after `stop` was done to a harness in
        `mode`."""
        with scripted_editor(body) as (editor, project, root, holders):
            harness = subprocess.Popen([sys.executable, "-c", HARNESS, str(Path(mutation_check.__file__)),
                                        editor, str(project), str(root), mode],
                                       stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            record = holders / "editor"
            deadline = time.time() + 30
            while not record.exists() and harness.poll() is None and time.time() < deadline:
                time.sleep(0.1)
            stop(harness)
            harness.wait(timeout=60)
            # An editor started just before its harness died can still be on its way to the record.
            deadline = time.time() + 5
            while not (record.exists() and record.read_text()) and time.time() < deadline:
                time.sleep(0.1)
            pid = int(record.read_text()) if record.exists() and record.read_text() else None
            deadline = time.time() + 10
            while pid is not None and time.time() < deadline:
                try:
                    os.kill(pid, 0)
                except ProcessLookupError:
                    return False
                time.sleep(0.2)
            return pid is not None

    # GREEN_ON_BASE(characterization): the editor's output reaches the job log while it runs.
    # On the base the editor wrote there itself. What reddens this on the branch is `relay` holding
    # a line back until the launch ends.
    def test_Given_AHarnessStoppedWhileItsEditorRuns_When_ItsOutputIsRead_Then_WhatTheEditorPrintedIsThere(self):
        # Arrange
        with scripted_editor(self.EDITOR) as (editor, project, root, holders):
            harness = subprocess.Popen([sys.executable, "-c", HARNESS, str(Path(mutation_check.__file__)),
                                        editor, str(project), str(root), "plain"],
                                       stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True)
            record = holders / "editor"
            deadline = time.time() + 30
            while not record.exists() and time.time() < deadline:
                time.sleep(0.1)
            time.sleep(0.5)

            # Act
            harness.terminate()
            printed, _ = harness.communicate(timeout=60)

        # Assert
        self.assertIn("the editor was here", printed)

    def test_Given_AHarnessKilledWithSigkill_When_ItsEditorIsRunning_Then_TheEditorGoesWithIt(self):
        # Act
        running = self.left_running("plain", lambda harness: harness.kill())

        # Assert
        self.assertFalse(running)

    def test_Given_ASignalArrivingWhileTheEditorStarts_When_TheHarnessDiesOfIt_Then_TheEditorGoesWithIt(self):
        # Act — the harness sends itself the signal inside the call that starts the editor.
        running = self.left_running("signal-while-starting", lambda harness: None)

        # Assert
        self.assertFalse(running)

    def test_Given_AHarnessKilledWithSigkill_When_ItsEditorStartedAProcessOutsideItsGroup_Then_ThatProcessGoesToo(self):
        # Act — nothing in the harness runs after a SIGKILL, so this is the watchdog's own kill.
        running = self.left_running("plain", lambda harness: harness.kill(), self.ESCAPING)

        # Assert
        self.assertFalse(running)

    def test_Given_AHarnessEndedBySigterm_When_ItsEditorStartedAProcessOutsideItsGroup_Then_ThatProcessGoesToo(self):
        # Act — the harness's handler kills the watchdog before the watchdog can see its parent go.
        running = self.left_running("plain", lambda harness: harness.terminate(), self.ESCAPING)

        # Assert
        self.assertFalse(running)


class LockRefusedCampaign(StubbedCampaign):
    """A campaign whose mutant launch leaves no result and a log `run_suite` ended as it ends one
    whose every launch the editor refused for the lock."""

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        if Path(results).name == "baseline.xml":
            return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)
        Path(log).write_text("editor log\n{}\n".format(getattr(mutation_check, "LOCK_REFUSED_LINE", "")))
        return 0.0, False, 0


class LockVerdictTests(unittest.TestCase):
    """A mutant whose every launch was refused for the lock was never measured, and says so."""

    def test_Given_AMutantEveryLaunchOfWhichWasRefused_When_Recorded_Then_ItIsNotMeasuredForTheLock(self):
        # Arrange
        campaign = LockRefusedCampaign()

        # Act
        campaign.run_over_diff()

        # Assert
        record = json.loads((campaign.project / "out" / "mutant-001.json").read_text())
        self.assertEqual(record.get("verdict"), getattr(mutation_check, "LOCKED", None))

    def test_Given_AShardThatRecordedAVerdictNoDecisionKnows_When_Collected_Then_TheCampaignFails(self):
        # Arrange — as the case below, with a verdict no version of this script writes.
        campaign = ParityKilledCampaign(ParityKilledCampaign.BODY.replace(
            "        internal static bool Other",
            "        // MUTANT_SURVIVES(equivalent): no caller reaches the bound, so both agree.\n"
            "        internal static bool Other"))
        campaign.shard("0/2", "out0")
        campaign.shard("1/2", "out1")
        record = campaign.project / "out0" / "mutant-001.json"
        held = json.loads(record.read_text())
        held["verdict"] = "killed-ish"
        record.write_text(json.dumps(held))

        # Act
        code = campaign.run_over_diff("--collect", str(campaign.project / "out0"),
                                      str(campaign.project / "out1"))

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AShardThatRecordedTheLockVerdict_When_Collected_Then_TheCampaignFails(self):
        # Arrange — every other mutant is killed or declared, so the lock verdict is the only thing
        # that can fail the decision.
        campaign = ParityKilledCampaign(ParityKilledCampaign.BODY.replace(
            "        internal static bool Other",
            "        // MUTANT_SURVIVES(equivalent): no caller reaches the bound, so both agree.\n"
            "        internal static bool Other"))
        campaign.shard("0/2", "out0")
        campaign.shard("1/2", "out1")
        record = campaign.project / "out0" / "mutant-001.json"
        held = json.loads(record.read_text())
        held["verdict"] = "not measured (the project lock was held)"
        record.write_text(json.dumps(held))

        # Act
        code = campaign.run_over_diff("--collect", str(campaign.project / "out0"),
                                      str(campaign.project / "out1"))

        # Assert
        self.assertEqual(code, 1)


# Lines of the editor log a CI campaign recorded for a mutant launch the build system stopped: no
# compiler diagnostic, and the banner a compile error also ends with.
BUILD_SYSTEM_LOG = textwrap.dedent("""\
    [ScriptCompilation] Requested script compilation because: Assetdatabase observed changes in script compilation related files
    Starting: /opt/unity/Editor/Data/Tools/BuildPipeline/bee_backend --ipc --defer-dag-verification --dagfile="Library/Bee/2400b0aEDbg.dag" --continue-on-failure --profile="Library/Bee/backend1.traceevents" ScriptAssemblies
    WorkingDir: /github/workspace
    DisplayProgressbar: Compiling Scripts
    Internal build system error. Request cancelled while reading the binlog.
    The backend process appears to still be running.
    Caught exception:
    Cannot access a closed pipe.
    AssetDatabase: script compilation time: 0.765877s
    Total cache size 753824
    Total cache size after purge 753824, took 00:00:00.0119856
    Scripts have compiler errors.

    SDL leaked 2 allocations
    """)

ANALYZER_ERROR = "Packages/com.velvet.core/Runtime/Probe.cs(5,9): error VEL501: too many branches\n"


class BuildSystemFailureTests(unittest.TestCase):
    """A launch the build system stopped with no diagnostic in its log measured nothing about the
    mutation, and is made again."""

    # An editor below that outlives its bound is given ten seconds rather than one: at one, these cases
    # errored on a loaded machine.
    def launched(self, body, timeout=30):
        """How many launches `run_suite` made of an editor running `body`, the counts it left, the
        log's last line, what `run_suite` returned, and what the editor wrote to `seen`. `launch` in
        `body` is which launch this is, from zero."""
        prelude = textwrap.dedent("""\
            launches = os.path.join(project, "launches")
            launch = len(open(launches).read()) if os.path.exists(launches) else 0
            open(launches, "a").write("x")
            seen = os.path.join(project, "seen")
            """)
        with scripted_editor(prelude + body) as (editor, project, root, _):
            with contextlib.redirect_stdout(io.StringIO()):
                reading = mutation_check.run_suite(editor, str(project), "EditMode", [],
                                                   root / "results.xml", root / "run.log", timeout)
            made = len((project / "launches").read_text())
            counts = mutation_check.read_counts(root / "results.xml")
            last = (root / "run.log").read_text().rstrip().splitlines()[-1]
            seen = (project / "seen").read_text() if (project / "seen").exists() else None
        return made, counts, last, reading, seen

    def test_Given_ABuildSystemFailureOnTheFirstLaunchOnly_When_ItIsRun_Then_TheNextLaunchsResultStands(self):
        # Arrange
        body = textwrap.dedent("""\
            if launch == 0:
                open(log, "w").write({!r})
                sys.exit(1)
            open(log, "w").write("editor log\\n")
            open(results, "w").write({!r})
            """).format(BUILD_SYSTEM_LOG, GREEN_RESULTS)

        # Act
        _, counts, _, _, _ = self.launched(body)

        # Assert
        self.assertEqual(counts, {"total": 1, "passed": 1, "failed": 0, "inconclusive": 0})

    def test_Given_ABuildSystemFailureOnEveryLaunch_When_ItIsRun_Then_ItIsLaunchedTwiceAndItsLogEndsSayingSo(self):
        # Arrange
        body = textwrap.dedent("""\
            open(log, "w").write({!r})
            sys.exit(1)
            """).format(BUILD_SYSTEM_LOG)

        # Act
        made, _, last, _, _ = self.launched(body)

        # Assert
        self.assertEqual((made, last),
                         (2, getattr(mutation_check, "BUILD_SYSTEM_FAILED_LINE", None)))

    def test_Given_ABuildSystemFailureThatOutlivesItsBound_When_ItIsRun_Then_ItIsNotReadAsHung(self):
        # Arrange -- the failure followed by an editor that does not exit, so the launch ends at the
        # bound rather than at the failure.
        body = textwrap.dedent("""\
            open(log, "w").write({!r})
            time.sleep(60)
            """).format(BUILD_SYSTEM_LOG)
        with scripted_editor(body) as (editor, project, root, _):
            # Act
            with contextlib.redirect_stdout(io.StringIO()):
                reading = mutation_check.run_suite(editor, str(project), "EditMode", [],
                                                   root / "results.xml", root / "run.log", 10)
            last = (root / "run.log").read_text().rstrip().splitlines()[-1]

        # Assert
        self.assertEqual((reading[1], last),
                         (False, getattr(mutation_check, "BUILD_SYSTEM_FAILED_LINE", None)))

    # GREEN_ON_BASE(characterization): the base launches a failed build once whatever it does after;
    # this pins that the relaunch it now gets is not made after the bound has already been spent.
    def test_Given_ABuildSystemFailureThatOutlivesItsBound_When_ItIsRun_Then_ItIsNotLaunchedAgain(self):
        # Arrange
        body = textwrap.dedent("""\
            open(log, "w").write({!r})
            time.sleep(60)
            """).format(BUILD_SYSTEM_LOG)

        # Act
        made, _, _, _, _ = self.launched(body, timeout=10)

        # Assert
        self.assertEqual(made, 1)

    def test_Given_AnEditorAppendingToItsLog_When_TheRelaunchHangs_Then_ItIsReadAsTimedOut(self):
        # Arrange -- the relaunch leaves the first launch's build-system line in the log it appends to.
        body = textwrap.dedent("""\
            if launch == 0:
                open(log, "a").write({!r})
                sys.exit(1)
            open(log, "a").write("tests started\\n")
            time.sleep(60)
            """).format(BUILD_SYSTEM_LOG)

        # Act
        _, _, _, reading, _ = self.launched(body, timeout=10)

        # Assert
        self.assertTrue(reading[1])

    def test_Given_AFailedLaunchLeavingAProcessThatFinishes_When_ItIsRelaunched_Then_TheRelaunchStartsAfterIt(self):
        # Arrange -- launch 0 leaves a process in its group that marks `finished` two seconds later.
        body = textwrap.dedent("""\
            finished = os.path.join(project, "finished")
            if launch == 0:
                lingering = subprocess.Popen(
                    [sys.executable, "-c",
                     "import sys, time; time.sleep(2); open(sys.argv[1], 'w').write('')", finished],
                    stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                open(os.path.join(holders, "lingering"), "w").write(str(lingering.pid))
                open(log, "w").write({!r})
                sys.exit(1)
            open(log, "w").write("editor log\\n")
            open(seen, "w").write("finished" if os.path.exists(finished) else "running")
            open(results, "w").write({!r})
            """).format(BUILD_SYSTEM_LOG, GREEN_RESULTS)

        # Act
        _, _, _, _, seen = self.launched(body)

        # Assert
        self.assertEqual(seen, "finished")

    def test_Given_AFailedLaunchLeavingAProcessThatOutlivesTheWait_When_ItIsRelaunched_Then_ThatProcessIsGone(self):
        # Arrange
        body = textwrap.dedent("""\
            record = os.path.join(project, "lingering")
            if launch == 0:
                lingering = subprocess.Popen([sys.executable, "-c", "import time; time.sleep(600)"],
                                             stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                open(os.path.join(holders, "lingering"), "w").write(str(lingering.pid))
                open(record, "w").write(str(lingering.pid))
                open(log, "w").write({!r})
                sys.exit(1)
            open(log, "w").write("editor log\\n")
            try:
                os.kill(int(open(record).read()), 0)
                open(seen, "w").write("running")
            except ProcessLookupError:
                open(seen, "w").write("gone")
            open(results, "w").write({!r})
            """).format(BUILD_SYSTEM_LOG, GREEN_RESULTS)

        # Act
        with mock.patch.object(mutation_check, "BUILD_SYSTEM_SETTLE", 1):
            _, _, _, _, seen = self.launched(body)

        # Assert
        self.assertEqual(seen, "gone")

    def test_Given_AnEditorAppendingToItsLog_When_TheRelaunchWritesAResult_Then_TheLogHoldsTheRelaunchAlone(self):
        # Arrange -- the relaunch appends to whatever log it finds.
        body = textwrap.dedent("""\
            if launch == 0:
                open(log, "a").write({!r})
                sys.exit(1)
            open(log, "a").write("editor log\\n")
            open(results, "w").write({!r})
            """).format(BUILD_SYSTEM_LOG, GREEN_RESULTS)

        # Act
        _, _, last, _, _ = self.launched(body)

        # Assert
        self.assertEqual(last, "editor log")

    # GREEN_ON_BASE(characterization): the base launches a failed build once whatever its log holds;
    # this pins that a build-system line an earlier run left in the log is not read as this launch's.
    def test_Given_ABuildSystemLineAnEarlierRunLeftInTheLog_When_TheLaunchAppendsAndWritesNoResult_Then_ItIsLaunchedOnce(self):
        # Arrange
        body = textwrap.dedent("""\
            open(os.path.join(project, "launches"), "a").write("x")
            open(log, "a").write("editor log\\n")
            sys.exit(1)
            """)
        with scripted_editor(body) as (editor, project, root, _):
            (root / "run.log").write_text(BUILD_SYSTEM_LOG)

            # Act
            run_scripted(editor, project, root)
            made = len((project / "launches").read_text())

        # Assert
        self.assertEqual(made, 1)

    # GREEN_ON_BASE(characterization): the base launches a failed build once whatever its log says;
    # this pins that an analyzer's error, which carries no CS code, still keeps it to one launch.
    def test_Given_ABuildSystemLineBesideAnAnalyzerError_When_ItIsRun_Then_ItIsLaunchedOnce(self):
        # Arrange
        body = textwrap.dedent("""\
            open(log, "w").write({!r})
            sys.exit(1)
            """).format(BUILD_SYSTEM_LOG + ANALYZER_ERROR)

        # Act
        made, _, _, _, _ = self.launched(body)

        # Assert
        self.assertEqual(made, 1)


class BuildSystemFailedCampaign(StubbedCampaign):
    """A campaign whose mutant launch leaves no result and a log `run_suite` ended as it ends one whose
    last launch the build system stopped."""

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        if Path(results).name == "baseline.xml":
            return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)
        Path(log).write_text("{}\n{}\n".format(
            BUILD_SYSTEM_LOG, getattr(mutation_check, "BUILD_SYSTEM_FAILED_LINE", "")))
        return 0.0, False, 0


class BuildSystemVerdictTests(unittest.TestCase):
    """A mutant the build system stopped on its last launch was never measured, and says so rather than
    reading as code that does not compile."""

    def recorded(self):
        campaign = BuildSystemFailedCampaign()
        code = campaign.run_over_diff()
        record = json.loads((campaign.project / "out" / "mutant-001.json").read_text())
        return code, record, campaign.printed

    def test_Given_AMutantEveryLaunchOfWhichTheBuildSystemStopped_When_Recorded_Then_ItIsNotMeasured(self):
        # Act
        _, record, _ = self.recorded()

        # Assert
        self.assertEqual(record.get("verdict"), getattr(mutation_check, "BUILD_SYSTEM", None))

    def test_Given_AMutantEveryLaunchOfWhichTheBuildSystemStopped_When_Recorded_Then_ItsDetailNamesTheLine(self):
        # Act
        _, record, _ = self.recorded()

        # Assert
        self.assertIn("Internal build system error. Request cancelled while reading the binlog.",
                      record.get("detail"))

    # GREEN_ON_BASE(characterization): the base fails this campaign too, reading the mutant as
    # uncompilable; this pins that the verdict replacing that one still fails it as unmeasured.
    def test_Given_AMutantEveryLaunchOfWhichTheBuildSystemStopped_When_Decided_Then_TheCampaignFailsNamingItUnmeasured(self):
        # Act
        code, _, printed = self.recorded()

        # Assert
        self.assertEqual((code, "--- mutants nothing was asked of the suite about ---" in printed),
                         (1, True))


class SilentBaselineCampaign(StubbedCampaign):
    """A campaign whose baseline launch writes no result, over an output an earlier campaign left a
    green baseline in."""

    def __init__(self, body=None):
        super().__init__(body)
        (self.project / "out").mkdir()
        (self.project / "out" / "baseline.xml").write_text(GREEN_RESULTS)

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        if Path(results).name == "baseline.xml":
            Path(log).write_text("")
            return 0.0, False, 0
        return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)


class StaleBaselineTests(unittest.TestCase):
    """A baseline launch that wrote nothing is not read off an earlier campaign's result."""

    def test_Given_AnEarlierGreenBaselineInTheOutput_When_TheBaselineWritesNoResult_Then_TheRunStopsSayingSo(self):
        # Arrange
        campaign = SilentBaselineCampaign()

        # Act
        code = campaign.run_over_diff()

        # Assert
        self.assertIn("the baseline run wrote no result", str(code))


class ParityKilledCampaign(StubbedCampaign):
    """Two mutants on two lines, the first killed and the second surviving, so a decision that took a
    verdict from the wrong mutant or dropped one reads differently from the whole run's."""

    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b) => a <= b;
                internal static bool Other(int a, int b) => a >= b;
            }
        }
        """)

    def __init__(self, body=None):
        super().__init__(body)
        self.measured = []

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        name = Path(results).name
        self.measured.append(name)
        Path(log).write_text("")
        Path(results).write_text(FAILING_RESULTS if name == "mutant-001.xml" else GREEN_RESULTS)
        return 0.0, False, 0

    def shard(self, spec, directory):
        return self.run_over_diff("--shard", spec, "--output", str(self.project / directory))


def decision(printed):
    """What a run decided: everything from its survivor list on, less the line naming its log directory.
    A run that decided nothing yields everything it printed, so the comparison fails rather than raises."""
    start = printed.find("--- mutants no test killed ---")
    tail = printed[max(start, 0):]
    return "\n".join(line for line in tail.splitlines() if not line.startswith("logs:"))


class ShardedCampaignTests(unittest.TestCase):
    """A campaign split across jobs: each `--shard` measures a slice, `--collect` decides over them all.

    The decision is only as good as its agreement with the unsplit run, so the cases below compare
    against one.
    """

    def test_Given_ADiffSplitInTwo_When_TheShardsAreCollected_Then_TheDecisionIsTheWholeRunsDecision(self):
        # Arrange
        split, whole = ParityKilledCampaign(), ParityKilledCampaign()
        split.shard("0/2", "out0")
        split.shard("1/2", "out1")
        whole_code = whole.run_over_diff()
        whole_decision = decision(whole.printed)

        # Act
        code = split.run_over_diff("--collect", str(split.project / "out0"), str(split.project / "out1"))

        # Assert
        self.assertEqual((code, decision(split.printed)), (whole_code, whole_decision))

    def test_Given_AShard_When_ItRuns_Then_OnlyItsMutantsReachTheEditor(self):
        # Arrange
        campaign = ParityKilledCampaign()

        # Act
        campaign.shard("1/2", "out1")

        # Assert
        self.assertEqual(campaign.measured, ["baseline.xml", "mutant-002.xml"])

    def test_Given_AShardWhoseSliceHoldsAnUnansweredSurvivor_When_ItEnds_Then_ItLeavesTheDecision(self):
        # Arrange — the survivor is the second mutant's, and a declaration anywhere else in the diff
        # could be what answers it, which the shard does not hold.
        campaign = ParityKilledCampaign()

        # Act
        code = campaign.shard("1/2", "out1")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AMutantNoShardRecorded_When_Collected_Then_ItIsUnmeasuredAndTheRunFails(self):
        # Arrange — the second shard's output is missing, as it is when that job never uploaded.
        campaign = ParityKilledCampaign()
        campaign.shard("0/2", "out0")

        # Act
        code = campaign.run_over_diff("--collect", str(campaign.project / "out0"))

        # Assert
        self.assertEqual((code, mutation_check.UNRECORDED in campaign.printed), (1, True))

    def test_Given_ADeclarationAboveAMutantAnotherShardMeasured_When_Collected_Then_ItAnswersForIt(self):
        # Arrange — the survivor is on the line the second shard measured.
        campaign = ParityKilledCampaign(ParityKilledCampaign.BODY.replace(
            "        internal static bool Other",
            "        // MUTANT_SURVIVES(equivalent): no caller reaches the bound, so both agree.\n"
            "        internal static bool Other"))
        campaign.shard("0/2", "out0")
        campaign.shard("1/2", "out1")

        # Act
        code = campaign.run_over_diff("--collect", str(campaign.project / "out0"),
                                      str(campaign.project / "out1"))

        # Assert
        self.assertEqual(code, 0)


class KeyedProject(StubbedCampaign):
    """A project holding the kinds of file a campaign key is read over, each committed on the base."""

    FILES = {
        "Packages/com.velvet.core/Runtime/Other.cs":
            "namespace Velvet\n{\n    internal static class Other\n    {\n    }\n}\n",
        "Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs":
            "namespace Velvet.Tests\n{\n    internal sealed class ProbeTests\n    {\n    }\n}\n",
        "Packages/com.velvet.core/TestUtilities/Helper.cs":
            "namespace Velvet.Tests\n{\n    internal static class Helper\n    {\n    }\n}\n",
        "Packages/com.velvet.core/Runtime/Velvet.asmdef": "{\"name\": \"Velvet\"}\n",
        "Packages/com.velvet.core/CHANGELOG.md": "# Changelog\n",
        "Packages/com.velvet.core/Documentation~/guide.md": "# Guide\n",
        "Packages/com.velvet.core/Generators~/Tool.cs": "internal static class Tool { }\n",
        "Packages/manifest.json": "{}\n",
        "ProjectSettings/ProjectVersion.txt": "m_EditorVersion: 6000.3.23f1\n",
        "scripts/tool.py": "print(1)\n",
        ".github/workflows/mutation.yml": "name: Mutation campaign\n",
        ".github/workflows/test.yml": "name: Test\n",
        "CONTRIBUTING.md": "# Contributing\n",
    }

    def __init__(self, body=None):
        super().__init__(body)
        for relative, text in self.FILES.items():
            self.write(relative, text)
        subprocess.run(["git", "-C", str(self.project), "add", *self.FILES], capture_output=True)
        subprocess.run(["git", "-C", str(self.project), "-c", "user.email=t@t", "-c", "user.name=t",
                        "commit", "-qm", "files"], capture_output=True)

    def write(self, relative, text):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)

    def append(self, relative, text):
        path = self.project / relative
        path.write_text(path.read_text() + text)

    def key(self):
        """(the key, {component: digest}) the run printed."""
        self.run_over_diff("--key")
        lines = self.printed.splitlines()
        key = next((line.split("=", 1)[1] for line in lines if line.startswith("campaign-key=")), None)
        parts = {line.split()[1]: line.split()[2] for line in lines if line.startswith("key-part ")}
        return key, parts

    def moved(self, before):
        """The components whose digest differs from `before`'s."""
        _, now = self.key()
        return sorted(name for name in now if now[name] != before.get(name))


class CampaignKeyTests(unittest.TestCase):
    """What a head's campaign key moves with, and what it leaves alone.

    A key that moved with a documentation edit would never be reused; one that sat still through a
    test edit would pass a head whose tests nobody measured the mutants against.
    """

    def test_Given_TheSameTreeTwice_When_ItIsKeyed_Then_TheKeysAreEqual(self):
        # Arrange
        project = KeyedProject()

        # Act
        first, second = project.key()[0], project.key()[0]

        # Assert -- the key is a 64-digit hash, so two Nones cannot satisfy it.
        self.assertEqual((first == second, len(first or "")), (True, 64))

    def test_Given_TheSameContentInTwoCheckouts_When_ItIsKeyed_Then_TheKeysAreEqual(self):
        # Arrange
        first, second = KeyedProject(), KeyedProject()

        # Act
        keys = (first.key()[0], second.key()[0])

        # Assert -- the key is a 64-digit hash, so two Nones cannot satisfy it.
        self.assertEqual((keys[0] == keys[1], first.project != second.project, len(keys[0] or "")),
                         (True, True, 64))

    def test_Given_ProseScriptsAndOtherWorkflowsEdited_When_ItIsKeyed_Then_TheKeyIsTheSame(self):
        # Arrange
        project = KeyedProject()
        before = project.key()[0]
        project.append("Packages/com.velvet.core/CHANGELOG.md", "an entry\n")
        project.append("Packages/com.velvet.core/Documentation~/guide.md", "a paragraph\n")
        project.append("CONTRIBUTING.md", "a rule\n")
        project.append("scripts/tool.py", "print(2)\n")
        project.append(".github/workflows/test.yml", "on: push\n")

        # Act
        after = project.key()[0]

        # Assert -- the key is a 64-digit hash, so two Nones cannot satisfy it.
        self.assertEqual((after == before, len(before or "")), (True, 64))

    def test_Given_ASourceUnderADirectoryUnityDoesNotImport_When_ItIsEdited_Then_TheKeyIsTheSame(self):
        # Arrange -- the generators' solution is built by dotnet, and nothing under a `~` directory
        # reaches the editor's compile.
        project = KeyedProject()
        before = project.key()[0]
        project.append("Packages/com.velvet.core/Generators~/Tool.cs", "// edit\n")

        # Act
        after = project.key()[0]

        # Assert -- the key is a 64-digit hash, so two Nones cannot satisfy it.
        self.assertEqual((after == before, len(before or "")), (True, 64))

    def test_Given_AnEditToTheMutatedFile_When_ItIsKeyed_Then_TheMutantsAndTheFileMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()
        project.source.write_text(project.BODY.replace("a <= b", "a <= b + 1"))

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["mutants", "mutated-files"])

    def test_Given_ACommentAddedToTheMutatedFile_When_ItIsKeyed_Then_TheFileMovesTheKey(self):
        # Arrange -- full content rather than the comment-stripped form `scope_digest` takes: a
        # reused pass is a stronger claim than a resumed kill, and prose is cheap to re-measure.
        project = KeyedProject()
        _, before = project.key()
        project.source.write_text(project.BODY.replace("    {\n        internal static bool",
                                                       "    {\n        // a note\n        internal static bool", 1))

        # Act
        moved = project.moved(before)

        # Assert -- the line moves with the comment, so the mutant does too.
        self.assertEqual("mutated-files" in moved, True)

    def test_Given_ATestFileEdited_When_ItIsKeyed_Then_TheTestSourcesMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()
        project.append("Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs", "// edit\n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["test-sources"])

    def test_Given_ASharedTestHelperEdited_When_ItIsKeyed_Then_TheTestSourcesMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()
        project.append("Packages/com.velvet.core/TestUtilities/Helper.cs", "// edit\n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["test-sources"])

    def test_Given_AnotherProductionFileEdited_When_ItIsKeyed_Then_TheBuildInputsMoveTheKey(self):
        # Arrange -- a test that kills a mutant in one file can reach it through another.
        project = KeyedProject()
        _, before = project.key()
        project.append("Packages/com.velvet.core/Runtime/Other.cs", "// edit\n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["build-inputs"])

    def test_Given_AnAssemblyDefinitionOrEditorVersionEdited_When_ItIsKeyed_Then_TheBuildInputsMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()
        project.append("Packages/com.velvet.core/Runtime/Velvet.asmdef", " \n")
        project.write("ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.4.0f1\n")
        project.append("Packages/manifest.json", " \n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["build-inputs"])

    def test_Given_TheToolChanged_When_ItIsKeyed_Then_OnlyTheToolMovesTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()

        # Act
        with mock.patch.object(mutation_check, "MUTATION_MODEL_VERSION",
                               mutation_check.MUTATION_MODEL_VERSION + 1):
            moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["tool"])

    def test_Given_ThePriorityOrderChanged_When_ItIsKeyed_Then_OnlyTheToolMovesTheKey(self):
        # Arrange -- the order decides which mutant of a line is kept, so a verdict measured under
        # another order was measured over other mutants.
        project = KeyedProject()
        _, before = project.key()

        # Act
        with mock.patch.object(mutation_check, "LINE_PRIORITY",
                               tuple(reversed(mutation_check.LINE_PRIORITY))):
            moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["tool"])

    def test_Given_ASiblingModuleOfTheCampaignEdited_When_ItIsKeyed_Then_OnlyTheToolMovesTheKey(self):
        # Arrange -- a file standing in for one of `TOOL_FILES`, reached by a path from `scripts/`.
        project = KeyedProject()
        sibling = Path(project.holder) / "sibling.py"
        sibling.write_text("RUNNING = 1\n")
        scripts = Path(mutation_check.__file__).resolve().parents[1]
        stood_in = (os.path.relpath(sibling, scripts),)
        with mock.patch.object(mutation_check, "TOOL_FILES", stood_in):
            _, before = project.key()
            sibling.write_text("RUNNING = 2\n")

            # Act
            moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["tool"])

    def test_Given_TheCampaignsToolFiles_When_Listed_Then_EachExists(self):
        # Arrange
        scripts = Path(mutation_check.__file__).resolve().parents[1]

        # Act
        missing = [name for name in mutation_check.TOOL_FILES if not (scripts / name).is_file()]

        # Assert
        self.assertEqual(missing, [])

    def test_Given_TheModulesTheCampaignImports_When_ComparedWithTheToolFiles_Then_NoneIsLeftOut(self):
        # Arrange -- a sibling module the campaign or its shard wrapper imports from scripts/ is part
        # of what a verdict ran under.
        scripts = Path(mutation_check.__file__).resolve().parents[1]
        sources = [scripts / "test_quality" / name for name in ("mutation_check.py", "ci_mutation_shard.py")]
        imported = set()
        for source in sources:
            imported.update(re.findall(r"^\s*(?:import|from)\s+(\w+)", source.read_text(), re.MULTILINE))
        local = {path.relative_to(scripts).as_posix() for path in scripts.glob("*/*.py")
                 if path.stem in imported and path.name != "mutation_check.py"}

        # Act
        left_out = sorted(local - set(mutation_check.TOOL_FILES))

        # Assert
        self.assertEqual(left_out, [])

    def test_Given_ASourceInANestedFolderNamedLogs_When_ItIsEdited_Then_TheBuildInputsMoveTheKey(self):
        # Arrange -- only a folder at the project root is generated; one inside a root holds sources.
        project = KeyedProject()
        for name in ("Logs", "obj", "Temp", "Library"):
            project.write("Packages/com.velvet.core/Runtime/Foo/{}/X.cs".format(name), "class X { }\n")
        _, before = project.key()
        for name in ("Logs", "obj", "Temp", "Library"):
            project.append("Packages/com.velvet.core/Runtime/Foo/{}/X.cs".format(name), "// edited\n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["build-inputs"])

    def test_Given_TheCampaignWorkflowEdited_When_ItIsKeyed_Then_OnlyTheToolMovesTheKey(self):
        # Arrange
        project = KeyedProject()
        _, before = project.key()
        project.append(".github/workflows/mutation.yml", "# a bound moved\n")

        # Act
        moved = project.moved(before)

        # Assert
        self.assertEqual(moved, ["tool"])

    def test_Given_ADeclarationNoLongerWrittenByTheBranch_When_ItIsKeyed_Then_OnlyTheDeclarationsMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        source = project.source
        mutants = mutants_of(project.BODY)
        for mutant in mutants:
            mutant.path = source
        own = {(source, 5): declaration(5, written_here=True)}
        base = {(source, 5): declaration(5, written_here=False)}

        # Act
        _, written = mutation_check.campaign_key(mutants, own, project.project)
        _, inherited = mutation_check.campaign_key(mutants, base, project.project)

        # Assert
        self.assertEqual(sorted(name for name in written if written[name] != inherited[name]),
                         ["declarations"])

    def test_Given_OneMutantFewer_When_ItIsKeyed_Then_OnlyTheMutantsMoveTheKey(self):
        # Arrange
        project = KeyedProject()
        mutants = mutants_of(KeyedProject.BODY.replace("a <= b", "a <= b && b <= a"))
        for mutant in mutants:
            mutant.path = project.source

        # Act
        _, whole = mutation_check.campaign_key(mutants, {}, project.project)
        _, fewer = mutation_check.campaign_key(mutants[:-1], {}, project.project)

        # Assert -- the file holding them is the same, and so is everything else.
        self.assertEqual(sorted(name for name in whole if whole[name] != fewer[name]), ["mutants"])

    def test_Given_FilesNamedInsteadOfADiff_When_ItIsKeyed_Then_ItRefuses(self):
        # Arrange
        project = KeyedProject()

        # Act
        code = project.run("--key")

        # Assert
        self.assertEqual(("campaign-key=" in project.printed, "merge base" in str(code)),
                         (False, True))


class MutantListHandOffTests(unittest.TestCase):
    """The plan's mutant list, which every shard and the verdict measure and decide over."""

    def test_Given_APlanThatWroteItsList_When_TheSameTreeIsHandedIt_Then_TheRunIsNotRefused(self):
        # Arrange
        project = KeyedProject()
        listing = project.project / "mutants.json"
        project.run_over_diff("--plan", "--emit-mutants", str(listing))

        # Act
        code = project.run_over_diff("--list", "--mutants", str(listing))

        # Assert -- the list is not empty, so a plan that wrote nothing cannot satisfy it.
        written = json.loads(listing.read_text() if listing.is_file() else "[]")
        self.assertEqual((code, len(written) > 0), (0, True))

    def test_Given_APlanThatWroteItsList_When_AnotherTreeIsHandedIt_Then_TheRunIsRefused(self):
        # Arrange
        project = KeyedProject()
        listing = project.project / "mutants.json"
        project.run_over_diff("--plan", "--emit-mutants", str(listing))
        project.source.write_text(project.BODY.replace("a <= b", "a < b"))

        # Act
        code = project.run_over_diff("--list", "--mutants", str(listing))

        # Assert
        self.assertEqual("does not generate the plan's mutant list" in str(code), True)

    def test_Given_AListWithAnExtraMutant_When_ItIsComparedWithTheTree_Then_TheCountIsNamed(self):
        # Arrange
        mutants = mutants_of(KeyedProject.BODY)
        expected = [mutation_check.mutant_record(mutant, Path("."))
                    for mutant in mutants] + [{"path": "probe.cs"}]

        # Act
        mismatch = mutation_check.list_mismatch(expected, mutants, Path("."))

        # Assert
        self.assertEqual(mismatch, "the plan's list holds {} mutants and this tree generates {}".format(
            len(mutants) + 1, len(mutants)))

    def test_Given_AListWhoseNthMutantDiffers_When_ItIsCompared_Then_ThatMutantIsNamed(self):
        # Arrange
        mutants = mutants_of(KeyedProject.BODY)
        expected = [mutation_check.mutant_record(mutant, Path(".")) for mutant in mutants]
        expected[0] = dict(expected[0], operator="logic")

        # Act
        mismatch = mutation_check.list_mismatch(expected, mutants, Path("."))

        # Assert
        self.assertEqual(str(mismatch).startswith("mutant 1 is "), True)

    def test_Given_AListOfTheTreesOwnMutants_When_ItIsCompared_Then_NothingIsWrong(self):
        # Arrange
        mutants = mutants_of(KeyedProject.BODY)
        expected = [mutation_check.mutant_record(mutant, Path(".")) for mutant in mutants]

        # Act
        mismatch = mutation_check.list_mismatch(expected, mutants, Path("."))

        # Assert
        self.assertEqual((mismatch, len(expected) > 0), (None, True))


class ManyMutantsCampaign(StubbedCampaign):
    """Twelve lines of one boundary mutant each, which the plan deals to two shards, every
    mutant killed."""

    BODY = "namespace Velvet\n{\n    internal static class Probe\n    {\n" + "".join(
        "        internal static bool M{0}(int a, int b) => a <= b;\n".format(number)
        for number in range(12)) + "    }\n}\n"
    kills = True

    def shard(self, spec, directory):
        return self.run_over_diff("--shard", spec, "--output", str(self.project / directory))

    def collect(self, *directories):
        return self.run_over_diff("--collect", *(str(self.project / name) for name in directories))


class UnreportedShardTests(unittest.TestCase):
    """A shard that recorded nothing is not measured, which is neither a survivor nor a stale answer."""

    def test_Given_AShardThatRecordedNothing_When_TheRunIsCollected_Then_ItIsNamedNotMeasured(self):
        # Arrange -- the second shard's job died, so its output never uploaded.
        campaign = ManyMutantsCampaign()
        campaign.shard("0/2", "out0")

        # Act
        code = campaign.collect("out0")

        # Assert
        self.assertEqual((code, "--- not measured: 1 of 2 EditMode shard(s) recorded nothing ---" in campaign.printed,
                          "shard 1 of 2 was dealt 6 mutant(s)" in campaign.printed),
                         (1, True, True))

    # GREEN_ON_BASE(characterization): the base prints no not-measured heading for any run, so a complete one passes there.
    # The dead-shard case above is the one that separates the two.
    def test_Given_EveryShardRecorded_When_TheRunIsCollected_Then_NoShardIsNamedNotMeasured(self):
        # Arrange -- the control: the heading is the dead shard's, not every collect's.
        campaign = ManyMutantsCampaign()
        campaign.shard("0/2", "out0")
        campaign.shard("1/2", "out1")

        # Act
        code = campaign.collect("out0", "out1")

        # Assert
        self.assertEqual((code, "not measured:" in campaign.printed), (0, False))

    # GREEN_ON_BASE(characterization): the base prints no not-measured heading for any run, so a complete one passes there.
    # The dead-shard case above is the one that separates the two.
    def test_Given_ADeadShardAndTheOtherRerun_When_TheRunIsCollected_Then_ThePassIsComplete(self):
        # Arrange -- only the failed shard is run again, into the directory its first attempt left.
        campaign = ManyMutantsCampaign()
        campaign.shard("0/2", "out0")
        campaign.collect("out0")
        campaign.shard("1/2", "out1")

        # Act
        code = campaign.collect("out0", "out1")

        # Assert
        self.assertEqual((code, "not measured" in campaign.printed), (0, False))

    def test_Given_ADeclarationOverAMutantOfTheDeadShard_When_TheRunIsCollected_Then_ItIsNotStale(self):
        # Arrange -- the declaration's mutant is the last line's, dealt to the shard that never reported.
        body = ManyMutantsCampaign.BODY.replace(
            "        internal static bool M11",
            "        // MUTANT_SURVIVES(equivalent): no caller reaches the bound, so both agree.\n"
            "        internal static bool M11")
        campaign = ManyMutantsCampaign(body)
        campaign.shard("0/2", "out0")

        # Act
        campaign.collect("out0")

        # Assert
        self.assertEqual("STALE" in campaign.printed, False)

    # GREEN_ON_BASE(characterization): the base calls a declaration over a mutant that died stale.
    # It is the control for the case that leaves a dead shard's declaration alone.
    def test_Given_ADeclarationOverAKilledMutantTheShardRecorded_When_TheRunIsCollected_Then_ItIsStale(self):
        # Arrange -- the control: the same declaration over a mutant that was measured and died.
        body = ManyMutantsCampaign.BODY.replace(
            "        internal static bool M11",
            "        // MUTANT_SURVIVES(equivalent): no caller reaches the bound, so both agree.\n"
            "        internal static bool M11")
        campaign = ManyMutantsCampaign(body)
        campaign.shard("0/2", "out0")
        campaign.shard("1/2", "out1")

        # Act
        campaign.collect("out0", "out1")

        # Assert
        self.assertEqual("STALE" in campaign.printed, True)

    def test_Given_AShardPartlyRecorded_When_Counted_Then_ItIsNotNamedAsDead(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), number, verdict=verdict)
                   for number, verdict in ((1, mutation_check.KILLED), (2, mutation_check.UNRECORDED),
                                           (3, mutation_check.UNRECORDED), (4, mutation_check.UNRECORDED),
                                           (5, mutation_check.UNRECORDED), (6, mutation_check.UNRECORDED),
                                           (7, mutation_check.UNRECORDED))]

        # Act
        named, count = mutation_check.unreported_shards(list(range(1, 8)), "EditMode", mutants, project=Path("/p"))

        # Assert -- shard 0 holds a recorded mutant (indices 1, 3, 5, 7), so only shard 1 is dead.
        self.assertEqual((sorted(named), count), ([1], 2))

    def test_Given_AnUnrecordedMutantUnderADeclaration_When_Decided_Then_TheDeclarationIsNotStale(self):
        # Arrange
        mutants = [survivor(Path("probe.cs"), 12, verdict=mutation_check.UNRECORDED)]
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        _, stale = mutation_check.answered(mutants, set(), declared)

        # Assert
        self.assertEqual([held.line for _, _, held in stale], [])

    # GREEN_ON_BASE(characterization): the base calls a declaration over a mutant that died stale.
    # It is the control for the case that leaves an unrecorded mutant's declaration alone.
    def test_Given_AKilledMutantUnderADeclaration_When_Decided_Then_TheDeclarationIsStale(self):
        # Arrange -- the control for the case above.
        mutants = [survivor(Path("probe.cs"), 12, verdict=mutation_check.KILLED)]
        declared = {(Path("probe.cs"), 12): declaration(12)}

        # Act
        _, stale = mutation_check.answered(mutants, set(), declared)

        # Assert
        self.assertEqual([held.line for _, _, held in stale], [12])


class TwoPlatformCampaign(StubbedCampaign):
    """Four mutants on four lines, where the EditMode suite kills the first and third and the PlayMode
    suite kills all four, so the second and fourth are killed only where the PlayMode pass runs."""

    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b) => a <= b;
                internal static bool Other(int a, int b) => a >= b;
                internal static bool Third(int a, int b) => a < b;
                internal static bool Fourth(int a, int b) => a > b;
            }
        }
        """)

    KILLED_BY = {"EditMode": {"mutant-001.xml", "mutant-003.xml"},
                 "PlayMode": {"mutant-001.xml", "mutant-002.xml", "mutant-003.xml", "mutant-004.xml"}}

    def __init__(self, body=None):
        super().__init__(body)
        self.measured = []

    def run_suite(self, _unity, _project, platform, _scope, results, log, _timeout, _holder=None):
        name = Path(results).name
        self.measured.append((platform, name))
        Path(log).write_text("")
        Path(results).write_text(FAILING_RESULTS if name in self.KILLED_BY[platform] else GREEN_RESULTS)
        return 0.0, False, 0

    def edit_pass(self):
        return self.run_over_diff("--shard", "0/1", "--output", str(self.project / "edit"))

    def play_pass(self, *arguments):
        return self.run_over_diff("--platform", "PlayMode", "--survivors-of", str(self.project / "edit"),
                                  *arguments)

    def collect(self):
        return self.play_pass("--collect", str(self.project / "play"))


class ShardedTwoPlatformCampaign(ManyMutantsCampaign):
    """Twelve mutants the EditMode suite never kills and the PlayMode suite always does, dealt to two
    EditMode shards, each followed in its job by a PlayMode pass over its own survivors: the shape the
    workflow's verdict job collects."""

    def run_suite(self, _unity, _project, platform, _scope, results, log, _timeout, _holder=None):
        mutant = Path(results).name != "baseline.xml"
        Path(log).write_text("")
        Path(results).write_text(FAILING_RESULTS if mutant and platform == "PlayMode" else GREEN_RESULTS)
        return 0.0, False, 0

    def edit_shard(self, spec, directory):
        return self.shard(spec, directory)

    def play_shard(self, number, edit_directories, directory):
        # As the shard job's PlayMode step runs it: the survivors of EditMode shard `number` of two.
        return self.run_over_diff("--platform", "PlayMode", "--survivors-of",
                                  *(str(self.project / name) for name in edit_directories),
                                  "--survivors-shard", "{}/2".format(number), "--shard", "0/1",
                                  "--output", str(self.project / directory))

    def collect_both(self, edit_directories, play_directories):
        return self.run_over_diff("--platform", "PlayMode", "--survivors-of",
                                  *(str(self.project / name) for name in edit_directories),
                                  "--collect", *(str(self.project / name) for name in play_directories))


class UncoveredSecondPassTests(unittest.TestCase):
    def test_Given_AnEditModeMutantNoCaseRan_When_ThePlayModePassIsPlanned_Then_ItIsMeasuredThere(self):
        # Arrange — a site no EditMode case ran is one a PlayMode case may.
        campaign = StubbedCampaign()
        campaign.run_over_diff("--shard", "0/1", "--output", str(campaign.project / "edit"))
        record = campaign.project / "edit" / "mutant-001.json"
        held = json.loads(record.read_text())
        held["verdict"] = mutation_check.UNCOVERED
        record.write_text(json.dumps(held))

        # Act
        campaign.run_over_diff("--platform", "PlayMode", "--survivors-of", str(campaign.project / "edit"), "--plan")

        # Assert
        self.assertIn("mutants=1", campaign.printed)


class DeadShardReportingTests(unittest.TestCase):
    """The verdict job's shape: a PlayMode pass over the EditMode shards' survivors, collected."""

    HEADING = "--- not measured: {} of {} {} shard(s) recorded nothing ---"

    def test_Given_ADeadEditModeShard_When_BothPassesAreCollected_Then_TheEditModeShardIsNamed(self):
        # Arrange -- shard 1 of 2 never reported, so its mutants are in no pass's list.
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")

        # Act
        campaign.collect_both(["edit0"], ["play"])

        # Assert
        self.assertEqual((self.HEADING.format(1, 2, "EditMode") in campaign.printed,
                          "EditMode shard 1 of 2 was dealt 6 mutant(s)" in campaign.printed),
                         (True, True))

    def test_Given_ADeadEditModeShard_When_BothPassesAreCollected_Then_NoPlayModeShardIsCalledDead(self):
        # Arrange -- the PlayMode pass does not run when an EditMode shard died, so none of its
        # shards recorded anything for a reason that is not theirs.
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")

        # Act
        campaign.collect_both(["edit0"], ["play"])

        # Assert
        self.assertEqual((self.HEADING.format(1, 2, "EditMode") in campaign.printed,
                          "PlayMode shard(s) recorded nothing" in campaign.printed), (True, False))

    def test_Given_EveryEditModeShardAndNoPlayModeShard_When_BothPassesAreCollected_Then_ThePlayModeShardsAreNamed(self):
        # Arrange -- the control: the PlayMode pass was owed, and none of its two groups reported.
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")
        campaign.edit_shard("1/2", "edit1")

        # Act
        campaign.collect_both(["edit0", "edit1"], ["play"])

        # Assert
        self.assertEqual((self.HEADING.format(2, 2, "PlayMode") in campaign.printed,
                          "EditMode shard(s) recorded nothing" in campaign.printed), (True, False))

    def test_Given_EveryEditModeShardAndOneDeadPlayModeShard_When_BothPassesAreCollected_Then_OnlyThatShardIsNamed(self):
        # Arrange
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")
        campaign.edit_shard("1/2", "edit1")
        campaign.play_shard(0, ["edit0", "edit1"], "play0")

        # Act
        campaign.collect_both(["edit0", "edit1"], ["play0"])

        # Assert -- named by the EditMode shard whose job did not reach the PlayMode pass.
        self.assertEqual((self.HEADING.format(1, 2, "PlayMode") in campaign.printed,
                          "shard 1 of 2 was dealt 6 mutant(s)" in campaign.printed), (True, True))

    def test_Given_AnEditModeShardsSurvivors_When_ItsPlayModePassRuns_Then_OnlyThoseAreMeasured(self):
        # Arrange -- both EditMode shards recorded, six survivors each.
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")
        campaign.edit_shard("1/2", "edit1")

        # Act
        campaign.play_shard(0, ["edit0", "edit1"], "play0")

        # Assert
        self.assertEqual(sorted(path.name for path in (campaign.project / "play0").glob("mutant-*.json")),
                         ["mutant-001.json", "mutant-003.json", "mutant-005.json", "mutant-007.json",
                          "mutant-009.json", "mutant-011.json"])

    def test_Given_MoreSurvivorsInAShardThanOneJobMeasures_When_ItsPlayModePassIsPlanned_Then_ItRefuses(self):
        # Arrange -- the shard's six survivors against a PlayMode ceiling of five.
        campaign = ShardedTwoPlatformCampaign()
        campaign.edit_shard("0/2", "edit0")
        campaign.edit_shard("1/2", "edit1")

        # Act
        with mock.patch.dict(mutation_check.SHARD_CEILING, {"EditMode": 16, "PlayMode": 5}):
            code = campaign.run_over_diff("--platform", "PlayMode", "--survivors-of",
                                          str(campaign.project / "edit0"), str(campaign.project / "edit1"),
                                          "--survivors-shard", "0/2", "--plan")

        # Assert
        self.assertEqual((code, "mutants=" in campaign.printed), (mutation_check.CEILING_REFUSAL, False))


class HangingTwoPlatformCampaign(TwoPlatformCampaign):
    """The same four mutants, where the suite hangs instead of reporting on each mutant `hung_on` names
    for its platform."""

    def __init__(self, hung_on, body=None):
        super().__init__(body)
        self.hung_on = hung_on

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        name = Path(results).name
        if name not in self.hung_on.get(platform, ()):
            return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)
        self.measured.append((platform, name))
        Path(log).write_text("")
        return 0.0, True, 0


class TwoPassCampaignTests(unittest.TestCase):
    """A mutant is killed where either platform's suite fails on it: an EditMode pass over every
    mutant, then a PlayMode pass over its survivors, decided together."""

    def test_Given_MutantsOnlyThePlayModeSuiteKills_When_BothPassesAreCollected_Then_TheCampaignPasses(self):
        # Arrange
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()
        campaign.play_pass("--shard", "0/1", "--output", str(campaign.project / "play"))

        # Act
        code = campaign.collect()

        # Assert
        self.assertEqual((code, re.findall(r"^killed: \d+$", campaign.printed, re.MULTILINE)),
                         (0, ["killed: 4"]))

    def test_Given_EditModeSurvivorsThePlayModeSuiteHangsOn_When_Collected_Then_TheyAreKilledAndTheRunPasses(self):
        # Arrange — the PlayMode pass classifies a hang by the rule the EditMode pass does, against its
        # own baseline.
        campaign = HangingTwoPlatformCampaign({"PlayMode": {"mutant-002.xml", "mutant-004.xml"}})
        campaign.edit_pass()
        campaign.play_pass("--shard", "0/1", "--output", str(campaign.project / "play"))

        # Act
        code = campaign.collect()

        # Assert
        self.assertEqual((code, re.findall(r"^killed: .*$", campaign.printed, re.MULTILINE)),
                         (0, ["killed: 2, {}: 2".format(mutation_check.HUNG)]))

    # GREEN_ON_BASE(characterization): the base does not ask the PlayMode suite about a hung mutant
    # either. What this pins is that a hang-kill stands the way an EditMode kill does.
    def test_Given_AMutantTheEditModeSuiteHungOn_When_ThePlayModePassRuns_Then_ItIsNotAskedAgain(self):
        # Arrange — the second mutant is an EditMode survivor in `TwoPlatformCampaign`; here the
        # EditMode suite hangs on it instead.
        campaign = HangingTwoPlatformCampaign({"EditMode": {"mutant-002.xml"}})
        campaign.edit_pass()
        campaign.measured.clear()

        # Act
        campaign.play_pass("--shard", "0/1", "--output", str(campaign.project / "play"))

        # Assert
        self.assertEqual(campaign.measured, [("PlayMode", "baseline.xml"), ("PlayMode", "mutant-004.xml")])

    def test_Given_EditModeRecords_When_ThePlayModePassRuns_Then_OnlyTheirSurvivorsReachTheEditor(self):
        # Arrange
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()
        campaign.measured.clear()

        # Act
        campaign.play_pass("--shard", "0/1", "--output", str(campaign.project / "play"))

        # Assert
        self.assertEqual(campaign.measured, [("PlayMode", "baseline.xml"), ("PlayMode", "mutant-002.xml"),
                                             ("PlayMode", "mutant-004.xml")])

    def test_Given_SurvivorsOnIndicesOneShardWouldTakeByIndex_When_ThePlayModePassIsSplit_Then_EachShardTakesOne(self):
        # Arrange — survivors 2 and 4 share an index parity. Held against the unsplit pass
        # rather than against named mutants, so which mutants the pass chooses is left to the case above.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        def mutants_measured(*arguments):
            campaign.measured.clear()
            campaign.play_pass(*arguments)
            return [name for _, name in campaign.measured if name != "baseline.xml"]

        whole = mutants_measured("--shard", "0/1", "--output", str(campaign.project / "play"))

        # Act
        halves = [mutants_measured("--shard", spec, "--output", str(campaign.project / directory))
                  for spec, directory in (("0/2", "play0"), ("1/2", "play1"))]

        # Assert
        self.assertEqual((sorted(halves[0] + halves[1]) == sorted(whole),
                          len(halves[0]) == len(halves[1]) > 0), (True, True))

    def test_Given_EditModeSurvivorsNoPlayModeShardRecorded_When_Collected_Then_TheyAreUnmeasuredAndTheRunFails(self):
        # Arrange — no declaration above either survivor, so nothing but the missing PlayMode record
        # can fail the run.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        code = campaign.collect()

        # Assert
        self.assertEqual((code, re.findall(r"^killed: .*$", campaign.printed, re.MULTILINE)),
                         (1, ["killed: 2, {}: 2".format(mutation_check.UNRECORDED)]))

    def test_Given_EditModeSurvivors_When_ThePlayModePassIsPlanned_Then_ItCountsWhatThePassMeasures(self):
        # Arrange — held against the pass rather than against a number, so which mutants the pass
        # chooses is left to the case that names them.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()
        campaign.measured.clear()
        campaign.play_pass("--shard", "0/1", "--output", str(campaign.project / "play"))
        measured = sum(1 for _, name in campaign.measured if name != "baseline.xml")

        # Act
        code = campaign.play_pass("--plan")

        # Assert
        self.assertEqual((code, re.findall(r"^mutants=.*$", campaign.printed, re.MULTILINE)),
                         (0, ["mutants={}".format(measured)]))

    def test_Given_MoreSurvivorsThanThePlayModeShardsCarry_When_Planned_Then_ItRefuses(self):
        # Arrange — two survivors against a PlayMode ceiling of one, under an EditMode one that holds them.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        with mock.patch.object(mutation_check, "MAX_SHARDS", 1), \
                mock.patch.dict(mutation_check.SHARD_CEILING, {"EditMode": 2, "PlayMode": 1}):
            code = campaign.play_pass("--plan")

        # Assert
        self.assertEqual((code, "shards=" in campaign.printed), (mutation_check.CEILING_REFUSAL, False))

    def test_Given_SurvivorsWithinThePlayModeCeiling_When_Planned_Then_TheMutantsEditModeKilledDoNotCount(self):
        # Arrange — four mutants, two of them survivors, against a PlayMode ceiling of two.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        with mock.patch.object(mutation_check, "MAX_SHARDS", 1), \
                mock.patch.dict(mutation_check.SHARD_CEILING, {"EditMode": 4, "PlayMode": 2}):
            code = campaign.play_pass("--plan")

        # Assert
        self.assertEqual((code, re.findall(r"^shards=.*$", campaign.printed, re.MULTILINE)),
                         (0, ["shards=[0]"]))

    def test_Given_ThePlayModeShardSize_When_ThePlayModePassIsPlanned_Then_ItSizesTheShards(self):
        # Arrange — two survivors, one to a PlayMode shard where an EditMode shard would take both.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        with mock.patch.dict(mutation_check.SHARD_SIZE, {"EditMode": 3, "PlayMode": 1}):
            campaign.play_pass("--plan")

        # Assert
        self.assertEqual(re.findall(r"^shards=.*$", campaign.printed, re.MULTILINE), ["shards=[0, 1]"])

    def test_Given_SurvivorsOfTheDirectoryTheRunCollects_When_Run_Then_ItIsRefused(self):
        # Arrange
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        refused = io.StringIO()
        with contextlib.redirect_stderr(refused):
            code = campaign.play_pass("--collect", str(campaign.project / "edit"))

        # Assert
        self.assertEqual((code, "keep their records apart" in refused.getvalue()), (2, True))

    def test_Given_SurvivorsOfTheDirectoryTheRunWrites_When_Run_Then_ItIsRefused(self):
        # Arrange — one record file per index, so the second pass would write over the first's.
        campaign = TwoPlatformCampaign()
        campaign.edit_pass()

        # Act
        refused = io.StringIO()
        with contextlib.redirect_stderr(refused):
            code = campaign.play_pass("--output", str(campaign.project / "edit"))

        # Assert
        self.assertEqual((code, "keep their records apart" in refused.getvalue()), (2, True))


class EditorArgumentTests(unittest.TestCase):
    def test_Given_AnEditorArgument_When_TheCampaignRuns_Then_ItReachesTheLaunchAndNotTheVerdictsKey(self):
        # Arrange
        campaign = ParityKilledCampaign()
        launched = []
        stub = campaign.run_suite

        def recording(unity, project, platform, scope, *rest):
            launched.append(list(scope))
            return stub(unity, project, platform, scope, *rest)

        campaign.run_suite = recording

        # Act
        campaign.run_over_diff("--editor-arg=-debugCodeOptimization")

        # Assert
        record = campaign.project / "out" / "mutant-001.json"
        keyed = json.loads(record.read_text())["scope"] if record.exists() else None
        self.assertEqual((launched[:1], keyed), ([["-debugCodeOptimization"]], []))


def launches_without_the_ci_build(root=REPO_ROOT):
    """Each editor test launch the repository writes -- a line naming both -runTests and -projectPath,
    in a script's command list or a documented command line -- that does not pass
    -debugCodeOptimization, beside the files holding any launch at all."""
    tracked = subprocess.run(["git", "ls-files", "-z", "--", "*.py", "*.md"], cwd=root, check=True,
                             capture_output=True, text=True).stdout.split("\0")
    launching, missing = set(), []
    for relative in filter(None, tracked):
        if relative.endswith(".py") and Path(relative).name.startswith("test_"):
            continue
        for number, line in enumerate((root / relative).read_text(encoding="utf-8").splitlines(), 1):
            if "-runTests" in line and "-projectPath" in line:
                launching.add(relative)
                if "-debugCodeOptimization" not in line:
                    missing.append("{}:{}".format(relative, number))
    return launching, missing


class CiBuildTests(unittest.TestCase):

    def test_Given_EveryEditorTestLaunchWritten_When_Read_Then_EachAsksForTheBuildCiMeasures(self):
        # Arrange — the three harnesses AGENTS.md names are the floor: a launch reshaped so this stops
        # reading it would otherwise leave nothing missing.
        harnesses = {"scripts/test_quality/" + name
                     for name in ("mutation_check.py", "neuter_check.py", "base_red_check.py")}

        # Act
        launching, missing = launches_without_the_ci_build(REPO_ROOT)

        # Assert
        self.assertEqual((harnesses <= launching, missing), (True, []))


class CampaignPlanTests(unittest.TestCase):
    def test_Given_ADiffWithMutants_When_Planned_Then_ItNamesTheirCountAndTheShards(self):
        # Arrange
        campaign = ParityKilledCampaign()

        # Act
        code = campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual((code, re.findall(r"^(?:mutants|shards)=.*$", campaign.printed, re.MULTILINE)),
                         (0, ["mutants=2", "shards=[0]"]))

    def test_Given_NoMutableChange_When_Planned_Then_NoShardIsPlanned(self):
        # Arrange — a tree whose one change is outside the package.
        campaign = StubbedCampaign()
        campaign.source.unlink()
        (campaign.project / "README.md").write_text("prose\n")

        # Act
        code = campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual((code, re.findall(r"^(?:mutants|shards)=.*$", campaign.printed, re.MULTILINE)),
                         (0, ["mutants=0", "shards=[]"]))

    def test_Given_AChangeNoOperatorReaches_When_Planned_Then_ItPassesAndAsksThePullRequestWhy(self):
        # Arrange — a local run refuses this change; the plan passes it, naming the lines, so the
        # reason has to reach the pull request instead.
        campaign = StubbedCampaign(textwrap.dedent("""\
            namespace Velvet
            {
                internal sealed class Probe
                {
                    private int _value;
                    internal void Settle(int next) { _value = next; }
                }
            }
            """))

        # Act
        code = campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual((code, "Say in the pull request why" in campaign.printed,
                          "unreached  Packages/com.velvet.core/Runtime/Probe.cs" in campaign.printed,
                          re.findall(r"^mutants=.*$", campaign.printed, re.MULTILINE)),
                         (0, True, True, ["mutants=0"]))

    def test_Given_MoreMutantsThanTheShardsCanCarry_When_Planned_Then_ItAsksForASplit(self):
        # Arrange — two mutants against a ceiling of one.
        campaign = ParityKilledCampaign()

        # Act
        with mock.patch.object(mutation_check, "MAX_SHARDS", 1), \
                mock.patch.dict(mutation_check.SHARD_CEILING, {"EditMode": 1}):
            code = campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual((code, "Split the pull request" in campaign.printed,
                          "mutants=" in campaign.printed),
                         (getattr(mutation_check, "CEILING_REFUSAL", None), True, False))

    def test_Given_MutantCounts_When_Sharded_Then_EachShardHoldsAtMostItsShareUpToTheCap(self):
        # Arrange
        per, cap = mutation_check.SHARD_SIZE["EditMode"], mutation_check.MAX_SHARDS
        counts = [0, 1, per, per + 1, per * cap * 4]

        # Act
        shards = [mutation_check.shard_count(count) for count in counts]

        # Assert
        self.assertEqual(shards, [0, 1, 1, 2, cap])

    def test_Given_PlayModeSurvivorsUpToTheCap_When_Sharded_Then_EachTakesAJobOfItsOwn(self):
        # Arrange — a PlayMode job's setup is paid once whatever it holds, and the pass waits on its
        # slowest job, so a job holding one survivor ends one mutant's run sooner than one holding two.
        cap = mutation_check.MAX_SHARDS
        counts = [1, 2, cap, cap + 1]

        # Act
        shards = [mutation_check.shard_count(count, "PlayMode") for count in counts]

        # Assert
        self.assertEqual(shards, [1, 2, cap, cap])

    def test_Given_AShardOutsideItsCount_When_Parsed_Then_ItIsRefused(self):
        # Act / Assert
        with self.assertRaises(argparse.ArgumentTypeError):
            mutation_check.parse_shard("2/2")


class LaunchRecordingCampaign(ParityKilledCampaign):
    """The two-mutant campaign, keeping the scope of every launch beside its results file's name."""

    def __init__(self, body=None):
        super().__init__(body)
        self.scopes = []

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        self.scopes.append((Path(results).name, list(scope)))
        return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)


class TextReaderExclusionTests(unittest.TestCase):
    """A fixture reading this tree's text can move no verdict, so no launch pays for running it."""

    def test_Given_TextReadingFixtures_When_AScopeIsBuilt_Then_EachIsTakenOut(self):
        # Act
        scope = mutation_check.excluding([], {"CrefTargetTests", "DocumentationDriftTests"})

        # Assert
        self.assertEqual(scope, ["-testFilter", "!\\.CrefTargetTests$;!\\.DocumentationDriftTests$"])

    def test_Given_ACallersFilter_When_TextReadersAreTakenOut_Then_ItStillSelectsWhatItNamed(self):
        # Act
        scope = mutation_check.excluding(["-testFilter", "Velvet.Tests.HookTests"], {"CrefTargetTests"})

        # Assert
        self.assertEqual(scope, ["-testFilter", "Velvet.Tests.HookTests;!\\.CrefTargetTests$"])

    def test_Given_AReaderNamedLikeAnArea_When_ItsTermIsMatched_Then_ItTakesTheFixtureAndNotTheAssembly(self):
        # Arrange — a term matching an assembly's full name takes out every case in it, which
        # `killed_by_behaviour` does not discount.
        term = mutation_check.excluding([], {"Component"})[1][1:]

        # Act
        matched = (bool(re.search(term, "Velvet.Tests.Component")),
                   bool(re.search(term, "/github/workspace/Library/ScriptAssemblies/"
                                        "Velvet.Tests.Component.Editor.dll")))

        # Assert
        self.assertEqual(matched, (True, False))

    def test_Given_AFixtureReadingTheTreesText_When_TheCampaignRuns_Then_EveryLaunchLeavesItOut(self):
        # Arrange — a fixture that walks the corpus, under a test directory the campaign never mutates.
        campaign = LaunchRecordingCampaign()
        reader = campaign.project / "Packages/com.velvet.core/Runtime/Tests/Editor/DriftTests.cs"
        reader.parent.mkdir(parents=True)
        reader.write_text("internal sealed class DriftTests { DocumentationCorpus Corpus; }\n")

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual({tuple(scope) for _, scope in campaign.scopes},
                         {("-testFilter", "!\\.DriftTests$")})


class AssemblyScopeTests(unittest.TestCase):
    def test_Given_TwoAssemblies_When_TheCampaignLaunches_Then_TheEditorIsHandedTwoNames(self):
        # Arrange — the value is split on semicolons where it is read, so a comma list arrives as one
        # name no assembly carries.
        campaign = LaunchRecordingCampaign()

        # Act
        campaign.run_over_diff("--assemblies", "Velvet.Tests.Hooks.Editor,Velvet.Tests.Store.Editor")

        # Assert
        self.assertEqual(campaign.scopes[0][1],
                         ["-assemblyNames", "Velvet.Tests.Hooks.Editor;Velvet.Tests.Store.Editor"])


def timed_results(duration, assemblies=(), failed=()):
    """A results file carrying the run's test seconds, each assembly's, and the cases that failed."""
    suites = "".join('<test-suite type="Assembly" name="{}.dll" duration="{}" />'.format(name, seconds)
                     for name, seconds in assemblies)
    cases = "".join('<test-case fullname="{}" result="Failed" />'.format(name) for name in failed)
    return '<test-run total="2" passed="{}" failed="{}" inconclusive="0" duration="{}">{}{}</test-run>'.format(
        2 - len(failed), len(failed), duration, suites, cases)


class AreaCampaign(StubbedCampaign):
    """One mutant in an area that owns a test assembly, where the whole EditMode suite took `TOTAL`
    seconds of tests and that assembly `area_seconds` of them.

    What each launch reports is set per kind: the area's own baseline, the narrowed attempt, and the
    whole-suite run a mutant falls back to.
    """

    SOURCE = "Packages/com.velvet.core/Runtime/Reconciler/Probe.cs"
    ASSEMBLY = "Velvet.Tests.Reconciler.Editor"
    TOTAL = 100.0

    def __init__(self, body=None, area_seconds=1.0):
        super().__init__(body)
        asmdef = self.project / "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/{}.asmdef".format(
            self.ASSEMBLY)
        asmdef.parent.mkdir(parents=True)
        asmdef.write_text(json.dumps({"name": self.ASSEMBLY}))
        self.area_seconds = area_seconds
        self.launches = []
        self.bounds = {}
        self.area_green = True
        self.narrowed_fails = ("Velvet.Tests.ProbeTests.Given_X",)
        self.narrowed_times_out = False
        self.narrowed_log = ""
        self.whole_fails = ()
        self.rebuilt = True

    def run_suite(self, _unity, _project, _platform, scope, results, log, timeout, _holder=None):
        name = Path(results).name
        self.launches.append(name)
        self.bounds[name] = timeout
        Path(log).write_text(self.narrowed_log if name.endswith("-narrowed.xml") else "")
        dll = self.project / "Library" / "ScriptAssemblies" / "None.dll"
        if name == "baseline.xml" or not self.rebuilt:
            dll.write_bytes(b"unmutated")
        elif name.startswith("mutant-"):
            dll.write_bytes(b"mutated")
        if name == "baseline.xml":
            text = timed_results(self.TOTAL, [(self.ASSEMBLY, self.area_seconds), ("Velvet.Tests.Other", 50.0)])
        elif name.startswith("baseline-"):
            text = timed_results(1.0, failed=() if self.area_green else ("Velvet.Tests.ProbeTests.Given_Y",))
        elif name.endswith("-narrowed.xml"):
            text = timed_results(1.0, failed=self.narrowed_fails)
        else:
            text = timed_results(self.TOTAL, failed=self.whole_fails)
        Path(results).write_text(text)
        return 0.0, name.endswith("-narrowed.xml") and self.narrowed_times_out, 0

    def verdict(self):
        record = self.project / "out" / "mutant-001.json"
        return json.loads(record.read_text()) if record.exists() else {}


class TimedAreaCampaign(AreaCampaign):
    """An `AreaCampaign` whose launches report the wall clock `walls` gives each by results file."""

    def __init__(self, walls):
        super().__init__()
        self.walls = walls

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        _, timed_out, peak = super().run_suite(unity, project, platform, scope, results, log, timeout,
                                               holder)
        return self.walls.get(Path(results).name, 0.0), timed_out, peak


class NarrowedBoundTests(unittest.TestCase):
    """A narrowed launch is bounded by what its area's own launch took rather than by a fixed number,
    and never past the whole suite's baseline."""

    def test_Given_AnAreaWhoseOwnLaunchTook40s_When_ItsMutantIsNarrowed_Then_ItIsGivenThatTimesTheMargin(self):
        # Arrange — a mutant the area spares, so the whole-suite launch follows and the case reads
        # the narrowed bound as one launch among four.
        campaign = TimedAreaCampaign({"baseline.xml": 200.0, "baseline-Reconciler.xml": 40.0})
        campaign.narrowed_fails = ()

        # Act
        campaign.run_over_diff("--timeout", "900")

        # Assert
        self.assertEqual(campaign.bounds.get("mutant-001-narrowed.xml"),
                         40.0 * getattr(mutation_check, "NARROW_MARGIN", 0))

    def test_Given_AnAreaWhoseMarginRunsPastTheWholeBaseline_When_ItsMutantIsNarrowed_Then_TheWholeBaselineBoundsIt(self):
        # Arrange — the area's 90 s times the margin passes the 100 s the whole baseline took.
        campaign = TimedAreaCampaign({"baseline.xml": 100.0, "baseline-Reconciler.xml": 90.0})
        campaign.narrowed_fails = ()

        # Act
        campaign.run_over_diff("--timeout", "900")

        # Assert
        self.assertEqual(campaign.bounds.get("mutant-001-narrowed.xml"), 100.0)

    def test_Given_AWholeBaselineOf200s_When_TheAreasOwnLaunchIsMade_Then_ThatIsItsBound(self):
        # Arrange
        campaign = TimedAreaCampaign({"baseline.xml": 200.0, "baseline-Reconciler.xml": 40.0})

        # Act
        campaign.run_over_diff("--timeout", "900")

        # Assert
        self.assertEqual(campaign.bounds.get("baseline-Reconciler.xml"), 200.0)


class NarrowedAttemptTests(unittest.TestCase):
    """A mutant in a cheap area is asked of that area's assemblies first, and only a kill there stands:
    every other verdict is the whole suite's, so the narrowing can end a run and never decide that a
    mutant survived."""

    def test_Given_AMutantItsAreaKills_When_Measured_Then_TheWholeSuiteIsNotLaunchedForIt(self):
        # Arrange
        campaign = AreaCampaign()

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.launches, ["baseline.xml", "baseline-Reconciler.xml",
                                             "mutant-001-narrowed.xml"])

    # GREEN_ON_BASE(characterization): the whole-suite launches keep --timeout, as on the base.
    # What reddens this on the branch is a whole-suite launch handed a narrowed launch's bound.
    def test_Given_AnAreaAndAMutantItSpares_When_Launched_Then_TheWholeSuiteLaunchesKeepTheTimeout(self):
        # Arrange — a mutant the area spares, so its whole-suite launch is made. `NarrowedBoundTests`
        # holds the narrowed launches' bounds.
        campaign = TimedAreaCampaign({"baseline.xml": 200.0, "baseline-Reconciler.xml": 40.0})
        campaign.narrowed_fails = ()

        # Act
        campaign.run_over_diff("--timeout", "900")

        # Assert
        self.assertEqual({name: campaign.bounds.get(name) for name in ("baseline.xml", "mutant-001.xml")},
                         {"baseline.xml": 900, "mutant-001.xml": 900})

    def test_Given_AMutantItsAreaKills_When_Recorded_Then_ItIsKilledByTheAreasCases(self):
        # Arrange — the whole suite would have let it survive, so the kill can only be the area's.
        campaign = AreaCampaign()

        # Act
        campaign.run_over_diff()

        # Assert
        record = campaign.verdict()
        self.assertEqual((record.get("verdict"), record.get("killers"),
                          "in {}:".format(AreaCampaign.ASSEMBLY) in record.get("detail", "")),
                         (mutation_check.KILLED, ["Velvet.Tests.ProbeTests.Given_X"], True))

    def test_Given_AMutantItsAreaSpares_When_TheWholeSuiteKillsIt_Then_ThatKillIsTheVerdict(self):
        # Arrange
        campaign = AreaCampaign()
        campaign.narrowed_fails = ()
        campaign.whole_fails = ("Velvet.Tests.ElsewhereTests.Given_Z",)

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual((campaign.launches[-2:], campaign.verdict().get("killers")),
                         (["mutant-001-narrowed.xml", "mutant-001.xml"],
                          ["Velvet.Tests.ElsewhereTests.Given_Z"]))

    def test_Given_ANarrowedRunOverTheUnmutatedAssembly_When_ItFails_Then_TheWholeSuiteDecides(self):
        # Arrange — a failure over a binary the edit never reached is not the mutation's.
        campaign = AreaCampaign()
        campaign.rebuilt = False

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.launches[-2:], ["mutant-001-narrowed.xml", "mutant-001.xml"])

    def test_Given_ANarrowedRunTheBuildStopped_When_ItReportsAFailure_Then_TheWholeSuiteDecides(self):
        # Arrange
        campaign = AreaCampaign()
        campaign.narrowed_log = ("Packages/com.velvet.core/Runtime/Reconciler/Probe.cs(5,9): "
                                 "error CS0019: nope\n")

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.launches[-2:], ["mutant-001-narrowed.xml", "mutant-001.xml"])

    def test_Given_ANarrowedRunKilledAtItsBound_When_ItsCompleteResultHoldsAFailure_Then_ThatKillStands(self):
        # Arrange — the whole suite would have let it survive, so the kill can only be the area's.
        campaign = KilledAtItsBoundCampaign("mutant-001-narrowed.xml",
                                            complete_results(("Velvet.Tests.ProbeTests.Given_X",)))

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual((campaign.launches[-1], campaign.verdict().get("verdict")),
                         ("mutant-001-narrowed.xml", mutation_check.KILLED))

    # GREEN_ON_BASE(characterization): the base launches no narrowed run, so its verdict is the whole
    # suite's alone and the mutant survives there too. What reddens this on the branch is carrying
    # the narrowed launch's timeout into the whole-suite verdict, measured on this case alone.
    def test_Given_ANarrowedRunKilledAtItsBound_When_TheWholeSuiteThenPasses_Then_ItSurvivesRatherThanHangs(self):
        # Arrange — the narrowed launch's bound is its own; the hang verdict reads the whole
        # suite's run against the whole baseline, and that run finished.
        campaign = AreaCampaign()
        campaign.narrowed_times_out = True

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.verdict().get("verdict"), mutation_check.SURVIVED)

    def test_Given_ANarrowedRunWhereOnlyATextReaderFailed_When_Read_Then_TheWholeSuiteDecides(self):
        # Arrange
        campaign = AreaCampaign()
        reader = campaign.project / "Packages/com.velvet.core/Runtime/Tests/Editor/DriftTests.cs"
        reader.parent.mkdir(parents=True)
        reader.write_text("internal sealed class DriftTests { TrackedFiles Files; }\n")
        campaign.narrowed_fails = ("Velvet.Tests.DriftTests.Given_X",)

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.launches[-2:], ["mutant-001-narrowed.xml", "mutant-001.xml"])

    def test_Given_AnAreaThatFailsByItself_When_TheCampaignStarts_Then_ItsMutantsRunOnTheWholeSuite(self):
        # Arrange
        campaign = AreaCampaign()
        campaign.area_green = False

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual(campaign.launches, ["baseline.xml", "baseline-Reconciler.xml", "mutant-001.xml"])

    # GREEN_ON_BASE(characterization): a caller's filter keeps its question whole, as on the base.
    # The base narrows nothing at all, so what this pins is that the branch does not under a filter.
    def test_Given_ACallersFilter_When_Measured_Then_NothingIsNarrowed(self):
        # Arrange
        campaign = AreaCampaign()

        # Act
        campaign.run_over_diff("--filter", "Velvet.Tests.ProbeTests")

        # Assert
        self.assertEqual(campaign.launches, ["baseline.xml", "mutant-001.xml"])

    # GREEN_ON_BASE(characterization): the PlayMode pass stays whole, as everything does on the base.
    # Only the EditMode survivors reach that pass, and nothing here has measured a narrowed kill there.
    def test_Given_ThePlayModePlatform_When_Measured_Then_NothingIsNarrowed(self):
        # Arrange
        campaign = AreaCampaign()

        # Act
        campaign.run_over_diff("--platform", "PlayMode")

        # Assert
        self.assertEqual(campaign.launches, ["baseline.xml", "mutant-001.xml"])


def complete_results(failing=(), cases=2, **changes):
    """A results file as the runner closes one: `cases` cases, `failing` of them failing, with `changes`
    replacing attributes of its root and a change to None taking one away."""
    root = {"testcasecount": cases, "result": "Failed(Child)" if failing else "Passed", "total": cases,
            "passed": cases - len(failing), "failed": len(failing), "inconclusive": 0, "skipped": 0}
    root.update(changes)
    attributes = " ".join('{}="{}"'.format(key, value) for key, value in root.items() if value is not None)
    failures = "".join('<test-case fullname="{}" result="Failed" />'.format(name) for name in failing)
    return "<test-run {}>{}</test-run>".format(attributes, failures)


class KilledAtItsBoundCampaign(AreaCampaign):
    """An `AreaCampaign` whose launch writing `killed` leaves `text` as its results and is then killed
    at its bound, the file dated `age` seconds back where one is given.

    Both baselines report two cases, so two is what a complete reading of either launch holds.
    """

    def __init__(self, killed, text, age=None):
        super().__init__()
        self.killed, self.text, self.age = killed, text, age

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        wall, timed_out, peak = super().run_suite(unity, project, platform, scope, results, log, timeout,
                                                  holder)
        if Path(results).name != self.killed:
            return wall, timed_out, peak
        Path(results).write_text(self.text)
        if self.age is not None:
            then = time.time() - self.age
            os.utime(str(results), (then, then))
        return wall, True, peak


FAILING = ("Velvet.Tests.ProbeTests.Given_X",)
NARROWED = "mutant-001-narrowed.xml"
WHOLE = "mutant-001.xml"
FELL_BACK = [NARROWED, WHOLE]


class CompleteAtItsBoundTests(unittest.TestCase):
    """A launch killed at its bound after writing a complete reading is read as that reading; one short
    of complete is read as the bound ending it, as every killed launch was before."""

    @staticmethod
    def narrowed(text, age=None):
        campaign = KilledAtItsBoundCampaign(NARROWED, text, age)
        campaign.run_over_diff()
        return campaign

    @staticmethod
    def whole(text, age=None):
        campaign = KilledAtItsBoundCampaign(WHOLE, text, age)
        campaign.narrowed_fails = ()
        campaign.run_over_diff()
        return campaign

    # GREEN_ON_BASE(characterization): a narrowed pass hands the mutant to the whole suite, as before.
    # What reddens this on the branch is `narrowed_kill` taking a complete pass as a verdict.
    def test_Given_ANarrowedRunKilledAfterACompletePass_When_Read_Then_TheWholeSuiteStillDecides(self):
        # Act
        campaign = self.narrowed(complete_results())

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)

    def test_Given_AWholeSuiteRunKilledAfterACompleteFailure_When_Read_Then_TheMutantIsKilled(self):
        # Act
        record = self.whole(complete_results(FAILING)).verdict()

        # Assert
        self.assertEqual((record.get("verdict"), record.get("killers")), (mutation_check.KILLED, list(FAILING)))

    def test_Given_AWholeSuiteRunKilledAfterACompletePass_When_Read_Then_TheMutantSurvives(self):
        # Act
        record = self.whole(complete_results()).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.SURVIVED)

    # GREEN_ON_BASE(characterization): a whole-suite reading one case short is not measured, as before.
    # What reddens this on the branch is the whole-suite launch's `expected` not being the baseline's.
    def test_Given_AWholeSuiteRunKilledWithFewerCasesThanItsBaseline_When_Read_Then_ItTimedOut(self):
        # Act
        record = self.whole(complete_results(FAILING, cases=1)).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.TIMED_OUT)

    # GREEN_ON_BASE(characterization): a whole-suite reading older than its launch is not measured.
    # What reddens this on the branch is the whole-suite launch's `since` not being its own.
    def test_Given_AWholeSuiteRunKilledOverAnOlderFile_When_Read_Then_ItTimedOut(self):
        # Act
        record = self.whole(complete_results(FAILING), age=3600).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.TIMED_OUT)

    # GREEN_ON_BASE(characterization): a whole-suite launch killed mid-file hung, as before. What
    # reddens this on the branch is `complete_result` reading a file that does not parse. Asked of
    # the whole suite, since a narrowed launch's own reading refuses such a file a second time.
    def test_Given_AWholeSuiteRunKilledMidFile_When_Read_Then_ItHung(self):
        # Act
        record = self.whole(complete_results(FAILING)[:-20]).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.HUNG)

    # GREEN_ON_BASE(characterization): a whole-suite root without a count is not measured, as before.
    # What reddens this on the branch is `complete_result` defaulting a count it could not read: the
    # failed count of a pass, where a default of zero is the right number and nothing else disagrees.
    def test_Given_AWholeSuiteRunKilledWithoutItsFailedCount_When_Read_Then_ItTimedOut(self):
        # Act
        record = self.whole(complete_results(failed=None)).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.TIMED_OUT)

    # GREEN_ON_BASE(characterization): a root without its result falls back, as before. What reddens
    # this on the branch is `complete_result` no longer asking for the result attribute.
    def test_Given_ANarrowedRunKilledWithoutItsResult_When_Read_Then_TheWholeSuiteDecides(self):
        # Act
        campaign = self.narrowed(complete_results(FAILING, result=None))

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)

    # GREEN_ON_BASE(characterization): a whole-suite root that is not a test run hung, as before. What
    # reddens this on the branch is `complete_result` no longer asking for a `test-run` root, which
    # the narrowed launch's own reading asks a second time.
    def test_Given_AWholeSuiteRunKilledOverAnotherRoot_When_Read_Then_ItHung(self):
        # Act
        record = self.whole(complete_results(FAILING).replace("test-run", "test-suite")).verdict()

        # Assert
        self.assertEqual(record.get("verdict"), mutation_check.HUNG)

    # GREEN_ON_BASE(characterization): a file older than its launch falls back, as before. What
    # reddens this on the branch is the narrowed launch's `since` not being its own.
    def test_Given_ANarrowedRunKilledOverAnOlderFile_When_Read_Then_TheWholeSuiteDecides(self):
        # Act
        campaign = self.narrowed(complete_results(FAILING), age=3600)

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)

    # GREEN_ON_BASE(characterization): a reading short of its area's cases falls back, as before.
    # What reddens this on the branch is the narrowed launch's `expected` not being its area's.
    def test_Given_ANarrowedRunKilledWithFewerCasesThanItsArea_When_Read_Then_TheWholeSuiteDecides(self):
        # Act
        campaign = self.narrowed(complete_results(FAILING, cases=1))

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)

    # GREEN_ON_BASE(characterization): a run holding a case it never reported falls back, as before.
    # What reddens this on the branch is `complete_result` not comparing the two counts.
    def test_Given_ANarrowedRunKilledBeforeReportingEveryCaseItHeld_When_Read_Then_TheWholeSuiteDecides(self):
        # Act
        campaign = self.narrowed(complete_results(FAILING, testcasecount=3))

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)

    # GREEN_ON_BASE(characterization): a reading with an inconclusive case falls back, as before.
    # What reddens this on the branch is `complete_result` not asking that each case passed or failed.
    def test_Given_ANarrowedRunKilledWithAnInconclusiveCase_When_Read_Then_TheWholeSuiteDecides(self):
        # Act
        campaign = self.narrowed(complete_results(FAILING, passed=0, inconclusive=1))

        # Assert
        self.assertEqual(campaign.launches[-2:], FELL_BACK)


class NarrowableAreaTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp(prefix="narrowable-"))
        self.area = self.root / "Reconciler"
        (self.area / "Tests" / "Editor").mkdir(parents=True)
        (self.area / "Tests" / "Editor" / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.A"}))
        self.results = self.root / "baseline.xml"

    def tearDown(self):
        shutil.rmtree(self.root, ignore_errors=True)

    def narrowed(self, seconds):
        self.results.write_text(timed_results(100.0, [("Velvet.Tests.A", seconds), ("Velvet.Tests.B", 60.0)]))
        return mutation_check.narrowable(self.results, {self.area})

    def test_Given_AnAreaTakingAQuarterOfTheSuite_When_Read_Then_ItIsNarrowed(self):
        # Act / Assert
        self.assertEqual(self.narrowed(25.0), {self.area: ["Velvet.Tests.A"]})

    def test_Given_AnAreaTakingJustOverAQuarter_When_Read_Then_ItIsNarrowed(self):
        # Arrange
        seconds = 25.5

        # Act
        narrowed = self.narrowed(seconds)

        # Assert
        self.assertEqual(narrowed, {self.area: ["Velvet.Tests.A"]})

    def test_Given_AnAreaTakingTwentySevenPercent_When_Read_Then_ItIsNarrowed(self):
        # Arrange
        seconds = 27.0

        # Act
        narrowed = self.narrowed(seconds)

        # Assert
        self.assertEqual(narrowed, {self.area: ["Velvet.Tests.A"]})

    def test_Given_AnAreaTakingAThird_When_Read_Then_ItIsNarrowed(self):
        # Arrange
        seconds = 100.0 / 3.0

        # Act
        narrowed = self.narrowed(seconds)

        # Assert
        self.assertEqual(narrowed, {self.area: ["Velvet.Tests.A"]})

    # GREEN_ON_BASE(characterization): an area above a third also exceeds the base's quarter.
    # What shows the guard can fail is changing `NARROW_SHARE` to `2`.
    def test_Given_AnAreaTakingMoreThanAThird_When_Read_Then_ItIsLeftWhole(self):
        # Arrange
        seconds = 33.5

        # Act
        narrowed = self.narrowed(seconds)

        # Assert
        self.assertEqual(narrowed, {})

    def test_Given_AnAreaTakingTwentySevenPercent_When_TheSessionIsPlanned_Then_ItsAreaPrecedesTheWholeStage(self):
        # Arrange
        self.area = self.root / mutation_check.PACKAGE / "Runtime" / "Reconciler"
        (self.area / "Tests" / "Editor").mkdir(parents=True)
        (self.area / "Tests" / "Editor" / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.A"}))
        attempts = {area: ["-assemblyNames", ";".join(names)]
                    for area, names in self.narrowed(27.0).items()}
        mutant = mutation_check.Mutant(self.area / "Probe.cs", 1, 0, "<", "<=", "boundary")

        # Act
        plan, _ = mutation_check.session_plan(self.root, [mutant], [1], attempts, [], "EditMode",
                                              100, 900, self.root / "session")
        stages = next(item["stages"] for item in plan["items"] if item["id"] == 1)

        # Assert
        self.assertEqual([(stage["name"], stage["assemblyNames"]) for stage in stages],
                         [("narrowed:Reconciler", ["Velvet.Tests.A"]), ("whole", [])])

    def test_Given_AMutantWithCasesThatRanItsSite_When_TheSessionIsPlanned_Then_TheyPrecedeTheWholeStageInPlaceOfItsArea(self):
        # Arrange — the area is narrowed too, and the cases are the narrower of the two.
        self.area = self.root / mutation_check.PACKAGE / "Runtime" / "Reconciler"
        (self.area / "Tests" / "Editor").mkdir(parents=True)
        (self.area / "Tests" / "Editor" / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.A"}))
        attempts = {area: ["-assemblyNames", ";".join(names)]
                    for area, names in self.narrowed(27.0).items()}
        mutant = mutation_check.Mutant(self.area / "Probe.cs", 1, 0, "<", "<=", "boundary")
        covered = mutation_check.COVERED_STAGE

        # Act
        plan, _ = mutation_check.session_plan(self.root, [mutant], [1], attempts, [], "EditMode",
                                              100, 900, self.root / "session",
                                              selection={1: ["N.C.A", "N.C.B"]})
        stages = next(item["stages"] for item in plan["items"] if item["id"] == 1)

        # Assert
        self.assertEqual([(stage["name"], stage["testNames"]) for stage in stages],
                         [(covered + ":0", ["N.C.A", "N.C.B"]), ("whole", [])])

    def test_Given_AnAssemblyTheBaselineDidNotRun_When_Read_Then_ItIsNotTaken(self):
        # Arrange — an assembly of the other platform sits under the same area.
        (self.area / "Tests" / "PlayMode").mkdir()
        (self.area / "Tests" / "PlayMode" / "P.asmdef").write_text(json.dumps({"name": "Velvet.Tests.P"}))

        # Act / Assert
        self.assertEqual(self.narrowed(1.0), {self.area: ["Velvet.Tests.A"]})


class AreaOfTests(unittest.TestCase):
    def test_Given_SourcesInsideAndOutsideAnArea_When_Read_Then_OnlyTheOneInsideHasOne(self):
        # Arrange
        project = Path("/p")
        runtime = project / "Packages/com.velvet.core/Runtime"

        # Act
        areas = [mutation_check.area_of(path, project) for path in (
            runtime / "Reconciler/Deep/Probe.cs", runtime / "Probe.cs",
            project / "Packages/com.velvet.core/Editor/Probe.cs")]

        # Assert
        self.assertEqual(areas, [runtime / "Reconciler", None, None])


class CostDealtShardTests(unittest.TestCase):
    """`deal` gives every shard about the same expected seconds rather than the same number of mutants."""

    PROJECT = Path("/p")
    RUNTIME = PROJECT / "Packages/com.velvet.core/Runtime"

    def mutants(self, kinds):
        """One mutant per letter: "n" under an area, so its first launch is narrowed, "w" outside every area."""
        return [survivor(self.RUNTIME / ("Reconciler/N.cs" if kind == "n" else "W.cs"), number)
                for number, kind in enumerate(kinds, start=1)]

    def totals(self, kinds, count, platform="EditMode"):
        mutants = self.mutants(kinds)
        dealt = mutation_check.deal(list(range(1, len(mutants) + 1)), mutants, self.PROJECT, platform, count)
        return [sum(mutation_check.mutant_cost(mutants[index - 1], self.PROJECT, platform) for index in indices)
                for indices in dealt]

    def test_Given_AreaAndWholeSuiteMutantsOnEditMode_When_Costed_Then_OnlyTheAreaOnesAreNarrowed(self):
        # Arrange
        mutants = self.mutants("nw")

        # Act
        costs = [mutation_check.mutant_cost(mutant, self.PROJECT, "EditMode") for mutant in mutants]

        # Assert
        self.assertEqual(costs, [mutation_check.NARROWED_COST, mutation_check.WHOLE_COST["EditMode"]])

    def test_Given_AnAreaMutantAndAnotherOnPlayMode_When_Costed_Then_TheyCostTheSame(self):
        # Arrange — nothing is narrowed on PlayMode, so where a mutant sits changes no charge.
        area, other = self.mutants("nw")

        # Act
        costs = [mutation_check.mutant_cost(mutant, self.PROJECT, "PlayMode") for mutant in (area, other)]

        # Assert
        self.assertEqual(costs[0], costs[1])

    def test_Given_WholeSuiteMutantsAtEveryOtherPosition_When_Dealt_Then_TheShardsCostTheSame(self):
        # Arrange — dealt by position, the second shard would hold every narrowed mutant and the first every dear one.
        kinds = "wnwnwnwn"

        # Act
        totals = self.totals(kinds, 2)

        # Assert
        self.assertEqual(max(totals) - min(totals), 0)

    def test_Given_TheCheapMutantsFirstInTheList_When_Dealt_Then_TheDearOnesAreStillPlacedFirst(self):
        # Arrange — placed in list order the spread is 348 s; longest first it is 58 s.
        kinds = "nnnwww"

        # Act
        totals = self.totals(kinds, 2)

        # Assert
        self.assertEqual(max(totals) - min(totals), 58)

    def test_Given_ThreeMutantsOfOneCost_When_DealtToTwoShards_Then_TheTieGoesToTheLowerShardAndIndex(self):
        # Arrange
        mutants = self.mutants("www")

        # Act
        dealt = mutation_check.deal([1, 2, 3], mutants, self.PROJECT, "EditMode", 2)

        # Assert
        self.assertEqual(dealt, [[1, 3], [2]])

    # GREEN_ON_BASE(characterization): the base's ceiling of 16 is held by the same cap; deleting the `len(held[shard]) < SHARD_CEILING[platform]` test in `deal` reddens it.
    def test_Given_FewDearMutantsAndManyCheapOnes_When_DealtToTheMostShards_Then_NoShardHoldsMoreThanTheCeiling(self):
        # Arrange — 3 dear and the rest cheap fill every shard of the ceiling exactly; balanced on cost alone the
        # shards without a dear mutant would each take one cheap mutant more.
        total = mutation_check.MAX_SHARDS * mutation_check.SHARD_CEILING["EditMode"]
        kinds = "w" * 3 + "n" * (total - 3)
        mutants = self.mutants(kinds)

        # Act
        dealt = mutation_check.deal(list(range(1, total + 1)), mutants, self.PROJECT, "EditMode",
                                    mutation_check.MAX_SHARDS)

        # Assert
        self.assertLessEqual(max(len(indices) for indices in dealt), mutation_check.SHARD_CEILING["EditMode"])

    def test_Given_AMixedPlan_When_EveryShardIsRead_Then_EachMutantIsInExactlyOne(self):
        # Arrange
        mutants = self.mutants("nwnnwnwnnnwn")
        chosen = [1, 2, 3, 5, 6, 7, 9, 10, 11, 12]

        # Act
        taken = sorted(index for shard in range(3)
                       for index in mutation_check.sharded(chosen, (shard, 3), mutants=mutants, project=self.PROJECT,
                                                       platform="EditMode"))

        # Assert
        self.assertEqual(taken, chosen)

    def test_Given_PlayModeMutants_When_Dealt_Then_ShardsDifferByAtMostOneMutant(self):
        # Arrange — nothing is narrowed there, so every mutant costs the same.
        mutants = self.mutants("nwnwnnw")

        # Act
        dealt = mutation_check.deal(list(range(1, 8)), mutants, self.PROJECT, "PlayMode", 3)

        # Assert
        self.assertEqual(sorted(len(indices) for indices in dealt), [2, 2, 3])

    def test_Given_AShardWithNoShardRequested_When_Sharded_Then_EveryChosenMutantIsReturned(self):
        # Arrange
        mutants = self.mutants("nw")

        # Act
        taken = mutation_check.sharded([1, 2], None, mutants=mutants, project=self.PROJECT, platform="EditMode")

        # Assert
        self.assertEqual(taken, [1, 2])

    def test_Given_AManyMutantsPlan_When_Planned_Then_EachShardsCostAndTheSpreadArePrinted(self):
        # Arrange — twelve mutants outside every area, each charged the EditMode whole-suite figure.
        campaign = ManyMutantsCampaign()

        # Act
        campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual(re.findall(r"^(?:shard \d+:.*|expected cost:.*)$", campaign.printed, re.MULTILINE),
                         ["shard 0: 6 mutant(s), expected 2958 s", "shard 1: 6 mutant(s), expected 2958 s",
                          "expected cost: cheapest shard 2958 s, dearest 2958 s, spread 0 s"])


class ShardCeilingTests(unittest.TestCase):
    """Each platform's plan ceiling against its shard job's own timeout, which lives in the workflow."""

    # Measured on this week's EditMode shards (runs 38882765210 and 38865742861 among them): a baseline of
    # 445 to 486 s, a launch of an area's own assemblies of 180 to 231 s, and a mutant at about 561 s from
    # the end of a shard's baseline to its last verdict. The slowest of each is charged.
    BASELINE = 486

    # What a narrowed platform's shard spends before its first mutant when it makes the reach run: the whole
    # suite once under the unmutated program with the switches recording. Charged at twice the baseline --
    # an allowance, since no recording run has been timed -- in place of each area's own baseline, which a
    # shard that makes the reach run does not take in its opening.
    REACH_RUN = 2 * BASELINE

    # What the opening runs of the covered sets add: each of a shard's mutants gets one, of at most
    # 1/REACH_SHARE of the baseline in seconds, and none is shared in the worst case.
    GATE_SUITE_SECONDS = BASELINE

    # What a launch of its own spends on its area's baseline, charged once for each area the launches
    # touch; the slowest measured. A shard takes it in its opening when it takes main's path -- it places
    # fewer than `REACH_MIN_MUTANTS`, or was assigned one it declined. The same figure stands for the launch
    # of the fixtures reading IL that follows a session survivor under `Runtime/`: a build and the cases
    # of the six files that read IL, about 190, which no run has timed.
    AREA_LAUNCH = 231

    # Each case passes what a mutant is charged and a shard's setup -- the longest measured before its
    # baseline, of its baseline and after its last verdict, added together. The 561 s is a shard's measured
    # seconds per mutant. Three arms, by `arm`: "reach" is a shard that makes the reach run, for `count`
    # placed mutants, charged for the reach run and a gate for each mutant, and each mutant charged as a
    # survivor under `Runtime/`: the session's whole suite and then the launch of the fixtures reading IL;
    # "failed" the same with a reach run that never finishes, every mutant taking the whole suite; "small"
    # a shard that takes main's path, which is main's charge: every area's baseline and the whole shard.
    def fits(self, job, platform, mutant, setup, arm="reach", count=None, workflow="mutation.yml",
             step=None):
        text = (REPO_ROOT / ".github/workflows" / workflow).read_text()
        # Both platforms' measuring steps sit in `mutation-shard`, each with a timeout of its own under the
        # job's, which must hold both and the steps around them. kill-set.yml's one step is named `step`.
        found = re.search(r"^      - name: {}\n        timeout-minutes: (\d+)$".format(
            step or "Measure " + platform), text.partition("\n  {}:".format(job))[2], re.MULTILINE)
        per = mutation_check.SHARD_CEILING.get(platform)
        count = per if count is None else count
        narrowed = platform in getattr(mutation_check, "NARROWED_PLATFORMS", ())
        opening = self.REACH_RUN if narrowed else 0
        if narrowed and arm in ("reach", "failed"):
            mutant = max(mutant, self.BASELINE + self.AREA_LAUNCH)
        if narrowed and arm == "reach" and per is not None:
            opening += self.GATE_SUITE_SECONDS / mutation_check.REACH_SHARE * count
        if arm == "small":
            opening = self.areas() * self.AREA_LAUNCH
        return (per is not None and found is not None
                and setup + opening + count * mutant <= int(found.group(1)) * 60)

    @staticmethod
    def areas():
        """How many areas `narrowable` would take: it is asked, over a results file that lists the package's
        EditMode test assemblies (the ones under a `Tests/Editor` directory) as taking a second each in a
        suite of a thousand, so what counts as an area's assemblies is its rule and not a copy of it."""
        runtime = REPO_ROOT / mutation_check.PACKAGE / "Runtime"
        ran = sorted(json.loads(asmdef.read_text())["name"] for asmdef in runtime.rglob("*.asmdef")
                     if asmdef.parent.parts[-2:] == ("Tests", "Editor"))
        results = Path(tempfile.mkdtemp(prefix="areas-")) / "results.xml"
        results.write_text('<test-run duration="1000">{}</test-run>'.format("".join(
            '<test-suite type="Assembly" name="{}.dll" duration="1" />'.format(name) for name in ran)))
        return len(mutation_check.narrowable(results, [path for path in runtime.iterdir() if path.is_dir()]))

    def test_Given_AFullShardWhoseReachRunNeverFinishes_When_ItsCostIsTaken_Then_ItFitsTheJobTimeout(self):
        # Arrange — the run is stopped at REACH_BOUND times the baseline, which REACH_RUN charges; no covered
        # set is then run, and every mutant takes the whole suite at the same measured figure.
        mutant, setup = 561, 201 + 486 + 12 + 900

        # Act
        fits = self.fits("mutation-shard", "EditMode", mutant, setup, arm="failed",
                         count=mutation_check.SHARD_CEILING["EditMode"])

        # Assert
        self.assertTrue(fits)

    def test_Given_EveryCountOfPlacedMutantsTheReachRunIsMadeFor_When_EveryOneTakesTheWholeSuiteAfterwards_Then_EveryOneFits(self):
        # Arrange — n placed mutants, each charged the whole suite after the reach run and its gate:
        # 1599 + 972 + 48.6 n + 717 n, a survivor's charge: the session's whole suite, then the launch of the
        # fixtures reading IL.
        mutant, setup = 561, 201 + 486 + 12 + 900
        counts = range(mutation_check.REACH_MIN_MUTANTS, mutation_check.SHARD_CEILING["EditMode"] + 1)

        # Act
        fitting = [placed for placed in counts
                   if self.fits("mutation-shard", "EditMode", mutant, setup, count=placed)]

        # Assert
        self.assertEqual(fitting, list(counts))

    def test_Given_EveryArmAShardCanTake_When_TheMostMutantsFittingAllOfThemAreCounted_Then_ItIsTheEditModeCeiling(self):
        # Arrange — derived from the job timeout rather than read back from the constant: a shard of this many
        # fits whether it makes the reach run, its reach run never finishes, or it is on main's path.
        mutant, setup = 561, 201 + 486 + 12 + 900
        largest = max((count for count in range(1, 40)
                       if all(self.fits("mutation-shard", "EditMode", mutant, setup, arm=arm, count=count)
                              for arm in ("reach", "failed", "small"))), default=0)

        # Act
        stated = mutation_check.SHARD_CEILING["EditMode"]

        # Assert
        self.assertEqual(stated, largest)

    def test_Given_AnEditModeShardOnMainsPath_When_ItsCostIsTaken_Then_ItFitsTheJobTimeout(self):
        # Arrange — a shard below the minimum, or assigned a declined mutant: every area's baseline, then every
        # mutant the shard was assigned.
        mutant, setup = 561, 201 + 486 + 12 + 900

        # Act
        fits = self.fits("mutation-shard", "EditMode", mutant, setup, arm="small")

        # Assert — and the threshold sits inside what a shard can hold, or the arm would never be taken.
        self.assertEqual((fits, mutation_check.REACH_MIN_MUTANTS <= mutation_check.SHARD_CEILING["EditMode"]),
                         (True, True))

    def test_Given_ThePlanWhoseCostDealingWouldPileCheapMutantsOnOneShard_When_TheDearestShardIsCharged_Then_ItFitsTheJobTimeout(self):
        # Arrange — 3 whole-suite and the rest narrowed mutants fill ten shards of the ceiling exactly. Charged at
        # the 561 s a mutant the ceiling uses, the dearest shard is charged on main's path, the dearer
        # of the two arms.
        runtime = REPO_ROOT / mutation_check.PACKAGE / "Runtime"
        total = mutation_check.MAX_SHARDS * mutation_check.SHARD_CEILING["EditMode"]
        mutants = [survivor(runtime / ("Reconciler/N.cs" if number > 3 else "W.cs"), number)
                   for number in range(1, total + 1)]
        dealt = mutation_check.deal(list(range(1, total + 1)), mutants, REPO_ROOT, "EditMode",
                                    mutation_check.MAX_SHARDS)
        mutant, setup = 561, 201 + 486 + 12 + 900

        # Act
        fits = self.fits("mutation-shard", "EditMode", mutant, setup, arm="small",
                         count=max(len(indices) for indices in dealt))

        # Assert
        self.assertTrue(fits)

    # GREEN_ON_BASE(characterization): the base's 493 s whole-suite figure is also under the 561 s charged here; raising `WHOLE_COST["EditMode"]` to 600 reddens it.
    def test_Given_TheDealsCostFigures_When_ComparedWithWhatTheCeilingCharges_Then_NoMutantIsDealtDearer(self):
        # Arrange — the ceiling charges 561 s an EditMode mutant and 934 + 100 s a PlayMode one, so a mutant the
        # plan deals as narrowed that `narrowable` then declines is still within the charge.
        edit, play = 561, 934 + 100

        # Act
        within = (max(mutation_check.NARROWED_COST, mutation_check.WHOLE_COST["EditMode"]) <= edit,
                  mutation_check.WHOLE_COST["PlayMode"] <= play)

        # Assert
        self.assertEqual(within, (True, True))

    def test_Given_TheShardJobsMarkerStep_When_ItsCommandsAreRead_Then_LogsIsHandedBackBeforeTheMarkerIsWritten(self):
        # Arrange — the docker steps run as root and leave Logs/mutation-playmode-shard owned by it, so a
        # host-side write into it is refused unless the runner user takes the directory back first.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-shard:")[2].partition("\n  mutation-verdict:")[0]
        step = job.partition("- name: Measure PlayMode")[2].partition("- uses: actions/upload-artifact")[0]
        commands = step.partition("- if: always()")[2]

        # Act
        chown = commands.find('sudo chown -R "$(id -u):$(id -g)" Logs')
        write = commands.find("> Logs/mutation-playmode-shard/")

        # Assert
        self.assertEqual((chown >= 0, 0 <= chown < write), (True, True))

    def test_Given_TheShardJobsPlayModeStep_When_ItsCommandIsRead_Then_ItMeasuresItsOwnShardsSurvivorsInOneShard(self):
        # Arrange — the pass runs in the EditMode shard's job, over the survivors of that shard alone: another
        # shard's survivors would be measured twice, and no `--shard 0/1` would make the run decide the campaign.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-shard:")[2].partition("\n  mutation-verdict:")[0]
        step = job.partition("- name: Measure PlayMode")[2].partition("- if: always()")[0]

        # Act
        flags = re.findall(r"--(platform PlayMode|survivors-of|survivors-shard|shard 0/1)\b", step)

        # Assert
        self.assertEqual(sorted(flags), ["platform PlayMode", "shard 0/1", "survivors-of", "survivors-shard"])

    def test_Given_ThePlayModeStepTimeout_When_TheMostSurvivorsFittingItAreCounted_Then_ItIsThePlayModeCeiling(self):
        # Arrange — derived from the step timeout rather than read back from the constant.
        mutant, setup = 934 + 100, 202 + 625 + 11 + 900
        largest = max((count for count in range(1, 40)
                       if self.fits("mutation-shard", "PlayMode", mutant, setup, count=count)), default=0)

        # Act
        stated = mutation_check.SHARD_CEILING["PlayMode"]

        # Assert
        self.assertEqual(stated, largest)

    def test_Given_TheShardJobsTimeout_When_ComparedWithItsMeasuringSteps_Then_ItHoldsBothAndTheRest(self):
        # Arrange — the job runs the EditMode step and then the PlayMode one on one runner; what is left
        # after both is its checkout, restore and uploads, which the measured setup figures do not cover.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-shard:")[2].partition("\n  mutation-verdict:")[0]
        steps = [int(minutes) for minutes in re.findall(r"^        timeout-minutes: (\d+)$", job, re.MULTILINE)]
        found = re.search(r"^    timeout-minutes: (\d+)$", job, re.MULTILINE)

        # Act
        spare = int(found.group(1)) - sum(steps) if found else None

        # Assert
        self.assertEqual((len(steps), spare is not None and spare >= 15), (2, True))

    def test_Given_AFullPlayModeShard_When_ItsWorstMeasuredCostIsTaken_Then_ItFitsTheJobTimeout(self):
        # Arrange — the 100 s are what its five bounded cases spend where a mutant stops the frame
        # driver, 5 x 20 s measured locally; and one mutant hanging at --timeout's 900 s besides.
        mutant, setup = 934 + 100, 202 + 625 + 11 + 900

        # Act
        fits = self.fits("mutation-shard", "PlayMode", mutant, setup)

        # Assert
        self.assertTrue(fits)

    def test_Given_TheVerdictJob_When_ItsCampaignIsRead_Then_ItDecidesOverBothPasses(self):
        # Arrange — without the three, the verdict is the EditMode pass's alone, and every mutant only a
        # PlayMode fixture kills reads as a survivor.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-verdict:")[2].partition("\n\n  # ---")[0]
        step = job.partition("mutation_check.py")[2].partition("| tee")[0]

        # Act
        flags = re.findall(r"--(platform PlayMode|survivors-of|collect)\b", step)

        # Assert
        self.assertEqual(sorted(flags), ["collect", "platform PlayMode", "survivors-of"])

    def test_Given_ThePlanStep_When_ItsCeilingStatusIsRead_Then_ItIsTheOneThePlanExits(self):
        # Arrange — the step lets the ceiling through without a licence by this number, and a copy
        # drifting from the script's either fails every such fork or lets another refusal through.
        workflow = (REPO_ROOT / ".github/workflows/mutation.yml").read_text()
        job = workflow.partition("\n  mutation-plan:")[2].partition("\n  mutation-shard:")[0]

        # Act
        mirrored = re.findall(r'"\$status" -eq (\d+) \] && \[ "\$HAS_LICENSE"', job)

        # Assert
        self.assertEqual(mirrored, [str(getattr(mutation_check, "CEILING_REFUSAL", None))])


READ_IL = mutation_check.il_reading_assemblies


class SessionCampaign(StubbedCampaign):
    """A campaign whose rewriter and session are stubbed as well as its editor launches.

    `outcome` names what the session records for each mutant it measures: "killed" fails the case
    `N.C.Kills` in the whole stage, "survived" runs the whole stage green over the baseline's case
    count, or over none of them where `short`. The confirmation that closes a segment passes
    `N.C.Kills` unless `unconfirmed`; `unarmed` records no switch in any stage, and `lost` reports every
    mutant's item as one no launch finished.
    """

    REWRITTEN = "// guarded for the session\n"

    unconfirmed = False
    lost = False
    unarmed = False
    short = False
    # The cases the reach run reports, each with the mutant ids it ran: None is a reach run that did not
    # come out green, which is what a stub that knows nothing of it reports.
    reach = None
    # What an own launch whose filter names the IL-reading fixture reports instead of a green run of its one
    # case (see `il_answer`), and how the unmutated control of that filter comes out: "red" or "short".
    il_launch = None
    il_control = None
    # Whether a mutant whose text joins `a - b` fails to build, as a `+` between strings does.
    rejects_minus = False
    reach_cases = ("N.C.A", "N.C.B", "N.C.C", "N.C.D") + tuple("N.C.F{}".format(number) for number in range(36))
    # A case the reach run reports as failed, and whether it reports one case fewer than the baseline held.
    failing_case = None
    missing_case = False
    # Whether the opening run of a covered set comes out green.
    gate_green = True
    # How long each case of the reach run took (seconds), by name; the rest take `case_seconds`.
    case_seconds = 0.1
    slow_cases = {}
    # A case the reach run reports as inconclusive, and the switches it reports armed.
    inconclusive_case = None
    reach_armed = ("Probe:0",)

    def __init__(self):
        super().__init__()
        self.outcome = "killed"
        self.rewrites = []
        self.launched = []
        self.scopes = []
        self.during = None
        self.plans = []
        self.environments = []

    def reach_session(self, plan, directory):
        xml = directory / "mutant-000.xml"
        cases = self.reach_cases[1:] if self.missing_case else self.reach_cases
        xml.write_text('<test-run total="{0}" passed="{0}" failed="0" inconclusive="0">{1}</test-run>'.format(
            len(cases), "".join('<test-case fullname="{}" result="{}" duration="{}" />'.format(
                name, "Failed" if name == self.failing_case else
                "Inconclusive" if name == self.inconclusive_case else "Passed",
                self.slow_cases.get(name, self.case_seconds)) for name in cases)))
        stage = {"name": "coverage", "finished": True, "xml": str(xml), "passed": len(cases),
                 "failures": [self.failing_case] if self.failing_case else [], "armed": list(self.reach_armed)}
        (directory / "item-000.json").write_text(json.dumps({"id": 0, "stages": [stage]}))
        (directory / "reach.jsonl").write_text("".join(
            json.dumps({"name": name, "ids": sorted(ids)}) + "\n" for name, ids in self.reach.items()))
        return {0: {"launch": 1, "peak": 0}}, set(), None

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        self.launched.append(Path(results).name)
        self.scopes.append(list(_scope))
        answer = super().run_suite(_unity, _project, _platform, _scope, results, log, _timeout, _holder)
        name = Path(results).name
        if name == "baseline-il.xml" and self.il_control:
            Path(results).write_text(FAILING_RESULTS if self.il_control == "red" else
                                     '<test-run total="0" passed="0" failed="0" inconclusive="0" />')
        if self.il_launch and name.startswith("mutant-") and "ReaderTests" in " ".join(_scope):
            return self.il_answer(Path(results), Path(log), answer)
        if self.rejects_minus and name.startswith("mutant-") and "a - b" in self.source.read_text():
            Path(log).write_text("Packages/com.velvet.core/Runtime/Probe.cs(5,80): error CS0019: Operator '-' "
                                 "cannot be applied to operands of type 'string' and 'string'\n")
        return answer

    def il_answer(self, results, log, answer):
        """What a launch that runs the fixtures reading IL reports, by `il_launch`."""
        mode = self.il_launch
        if mode == "nothing":
            results.write_text('<test-run total="0" passed="0" failed="0" inconclusive="0" />')
        elif mode == "timeout_complete":
            return answer[0], True, 0
        elif mode == "timeout":
            results.unlink()
            return answer[0], True, 0
        elif mode == "none":
            results.unlink()
        elif mode == "build":
            log.write_text("Packages/com.velvet.core/Runtime/Probe.cs(5,9): error VEL501: too many branches\n")
        elif mode == "fail":
            results.write_text('<test-run total="3" passed="2" failed="1" inconclusive="0">'
                               '<test-case fullname="N.ReaderTests.A" result="Failed" /></test-run>')
        return answer

    def rewrite(self, _unity, project, request, _scratch):
        self.rewrites.append(request)
        ids = [mutant["id"] for entry in request["files"] for mutant in entry["mutants"]]
        relative = request["files"][0]["path"]
        return {"placed": ids, "declined": {}, "files": {relative: self.REWRITTEN}, "fatal": []}

    def session(self, _args, _project, plan, directory, _holder):
        self.during = self.source.read_text()
        self.plans.append(plan)
        if plan.get("reach") and self.reach is not None:
            return self.reach_session(plan, directory)
        assembly = mutation_check.assembly_of(self.source)
        for item in plan["items"]:
            number = item["id"]
            name = directory / "item-{}.json".format(mutation_check.cs_id(number))
            xml = directory / "mutant-{}.xml".format(mutation_check.cs_id(number))
            planned = {stage["name"]: stage for stage in item["stages"]}
            if number == 0 and any(stage_name.startswith("covered:") for stage_name in planned):
                name.write_text(json.dumps({"id": 0, "stages": self.gate_stages(item, directory, assembly)}))
                continue
            if any(stage_name.startswith("covered:") for stage_name in planned) and not item.get("confirm"):
                name.write_text(json.dumps({"id": number, "stages": self.covered_stages(item, xml, assembly)}))
                continue
            if item.get("confirm"):
                result = "Failed" if self.unconfirmed else "Passed"
                xml.write_text('<test-run total="1"><test-case fullname="N.C.Kills" result="{}" />'
                               '</test-run>'.format(result))
                stage = {"name": "confirm", "finished": True, "xml": str(xml), "failures": [],
                         "armed": ["{}:{}".format(assembly, number)]}
            elif self.outcome == "killed":
                stage = {"name": "whole", "finished": True, "cancelled": True, "failures": ["N.C.Kills"],
                         "armed": [] if self.unarmed else ["{}:{}".format(assembly, number)]}
            else:
                xml.write_text('<test-run total="0" passed="0" failed="0" inconclusive="0" />'
                               if self.short else
                               '<test-run total="1" passed="0" failed="0" inconclusive="1" />'
                               if self.outcome == "inconclusive" else GREEN_RESULTS)
                stage = {"name": "whole", "finished": True, "xml": str(xml), "failures": [],
                         "armed": [] if self.unarmed else ["{}:{}".format(assembly, number)]}
            name.write_text(json.dumps({"id": number, "stages": [stage]}))
        lost = {position for position, item in enumerate(plan["items"])
                if self.lost and not item.get("confirm")}
        return {position: {"launch": 1, "peak": 0} for position in range(len(plan["items"]))
                if position not in lost}, lost, None

    def gate_stages(self, item, directory, assembly):
        """The opening run of each covered set, unmutated, and of each area."""
        stages = []
        for planned in item["stages"]:
            total = len(planned["testNames"]) or 1
            xml = directory / "mutant-000-{}.xml".format(planned["name"].replace(":", "-"))
            xml.write_text('<test-run total="{0}" passed="{0}" failed="0" inconclusive="0" />'.format(total))
            red = planned["name"].startswith("covered:") and not self.gate_green
            stages.append({"name": planned["name"], "finished": True, "xml": str(xml), "passed": total,
                           "failures": ["N.C.A"] if red else [], "armed": ["{}:0".format(assembly)]})
        return stages

    def covered_stages(self, item, xml, assembly):
        """What the runner records for an item that starts with a covered stage: a kill stops there, and
        a survivor runs it out and goes on to the whole suite."""
        armed = [] if self.unarmed else ["{}:{}".format(assembly, item["id"])]
        if self.outcome == "killed":
            return [{"name": item["stages"][0]["name"], "finished": True, "cancelled": True,
                     "failures": ["N.C.Kills"], "ran": 1, "armed": armed}]
        covered = item["stages"][0]
        xml.write_text('<test-run total="{0}" passed="{0}" failed="0" inconclusive="0" />'.format(
            len(covered["testNames"])))
        whole = Path(str(xml).replace(".xml", "-whole.xml"))
        whole.write_text(self.baseline_text)
        return [{"name": covered["name"], "finished": True, "xml": str(xml), "failures": [], "armed": armed,
                 "ran": len(covered["testNames"])},
                {"name": "whole", "finished": True, "xml": str(whole), "failures": [], "armed": armed}]

    def recorded(self):
        return json.loads((self.project / "out" / "mutant-001.json").read_text())["verdict"]


class SessionRoutingBase(unittest.TestCase):
    campaign_class = SessionCampaign
    # What `REACH_MIN_MUTANTS` is held at: a campaign here places one mutant.
    reach_from = None

    def setUp(self):
        self.campaign = self.campaign_class()
        saved = (mutation_check.rewrite_schemata, mutation_check.run_session, mutation_check.response_file)
        mutation_check.rewrite_schemata = self.campaign.rewrite
        mutation_check.run_session = self.campaign.session
        mutation_check.response_file = lambda _project, _assembly: "Library/Bee/artifacts/x/A.rsp"
        self.il_read, self.il_asked = set(), []
        mutation_check.il_reading_assemblies = lambda _project, ran=None: (
            self.il_asked.append(ran), self.il_read)[1]
        self.addCleanup(self.restore, saved + (READ_IL,))
        if self.reach_from is not None:
            self.addCleanup(setattr, mutation_check, "REACH_MIN_MUTANTS", mutation_check.REACH_MIN_MUTANTS)
            mutation_check.REACH_MIN_MUTANTS = self.reach_from

    @staticmethod
    def restore(saved):
        (mutation_check.rewrite_schemata, mutation_check.run_session, mutation_check.response_file,
         mutation_check.il_reading_assemblies) = saved


class SessionRoutingTests(SessionRoutingBase):
    def test_Given_AMutantTheSessionKilled_When_TheCampaignRuns_Then_ItTakesNoLaunchOfItsOwn(self):
        # Arrange
        campaign = self.campaign

        # Act
        campaign.run("--max", "1")

        # Assert — the baseline is the only launch; the session measured the mutant.
        self.assertEqual(campaign.launched, ["baseline.xml"])

    def test_Given_AMutantTheSessionKilled_When_ItsVerdictIsRecorded_Then_ItIsAKill(self):
        # Arrange — the stubbed editor would make an own launch survive, so the kill is the session's.
        campaign = self.campaign

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.recorded(), mutation_check.KILLED)

    def test_Given_AMutantTheSessionLeftSurviving_When_ItsVerdictIsRecorded_Then_ItIsASurvivor(self):
        # Arrange — the stubbed editor would make an own launch a kill, so the survivor is the session's.
        campaign = self.campaign
        campaign.outcome = "survived"
        campaign.kills = True

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.recorded(), mutation_check.SURVIVED)

    def test_Given_AKillWhoseCaseFailsUnmutatedToo_When_TheSessionEnds_Then_TheMutantTakesItsOwnLaunch(self):
        # Arrange — a case that fails again under the unmutated program failed on what the session
        # carried, and a survivor recorded as covered is what that reading would ship.
        campaign = self.campaign
        campaign.unconfirmed = True

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_AStageThatDidNotArmTheMutant_When_TheSessionEnds_Then_TheMutantTakesItsOwnLaunch(self):
        # Arrange — the editor kept the last build it could compile, which carries no switch.
        campaign = self.campaign
        campaign.outcome = "survived"
        campaign.unarmed = True

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_ASurvivingStageShortOfTheBaselinesCases_When_TheSessionEnds_Then_TheMutantTakesItsOwnLaunch(self):
        # Arrange — a run that held other cases than the baseline's asked another question.
        campaign = self.campaign
        campaign.outcome = "survived"
        campaign.short = True

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_AnItemNoSessionLaunchFinished_When_TheSessionEnds_Then_TheMutantTakesItsOwnLaunch(self):
        # Arrange — the editor was killed at the stage's bound, and the own launch decides as it always has.
        # A survivor, since a kill would fall back on its confirmation's launch as well.
        campaign = self.campaign
        campaign.outcome = "survived"
        campaign.lost = True

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_LaunchPerMutant_When_TheCampaignRuns_Then_TheRewriterIsNotAsked(self):
        # Arrange
        campaign = self.campaign

        # Act
        campaign.run("--max", "1", "--launch-per-mutant")

        # Assert
        self.assertEqual(campaign.rewrites, [])

    def test_Given_ASession_When_ItRuns_Then_TheRewrittenSourceIsOnDisk(self):
        # Arrange
        campaign = self.campaign

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.during, SessionCampaign.REWRITTEN)


    def test_Given_ASurvivorInAnAssemblyAFixtureReadsTheILOf_When_TheSessionEnds_Then_ItTakesItsOwnLaunch(self):
        # Arrange — that fixture read every guard compiled together, not this mutant's build.
        campaign = self.campaign
        campaign.outcome = "survived"
        self.il_read = {mutation_check.assembly_of(campaign.source), "Elsewhere"}

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_ASurvivorTheSessionRead_When_ItsAssemblyIsAsked_Then_ItIsAskedOfWhatTheBaselineRan(self):
        # Arrange — only those assemblies' fixtures run beside the mutant.
        campaign = self.campaign
        campaign.outcome = "survived"
        campaign.baseline_text = ('<test-run total="1" passed="1" failed="0" inconclusive="0">'
                                  '<test-suite type="Assembly" name="Velvet.Tests.Ran.dll" duration="1" />'
                                  '</test-run>')

        # Act
        campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.il_asked, [{"Velvet.Tests.Ran"}])

    def test_Given_AnEditorArgument_When_TheCampaignRuns_Then_NoMutantIsMeasuredInASession(self):
        # Arrange — the runner starts each stage itself, so the argument would reach none of them.
        campaign = self.campaign

        # Act
        campaign.run("--max", "1", "--editor-arg=-someFlag")

        # Assert
        self.assertEqual(campaign.rewrites, [])


READER = "class ReaderTests { void A() { ModuleDefinition.ReadModule(\"x\"); } }"
ONE_READER_CASE = (
    '<test-run total="1" passed="1" failed="0" inconclusive="0">'
    '<test-suite type="Assembly" name="Velvet.Tests.Ran.dll" duration="1" />'
    '<test-case fullname="N.ReaderTests.A" classname="N.ReaderTests" result="Passed" /></test-run>')
ONLY_READERS = ["-testFilter", "[.+]ReaderTests$"]


class ILReadingBase(SessionRoutingBase):
    """A project holding one fixture that reads IL, one case of it in the baseline, and an assembly the
    session reports as read."""

    def setUp(self):
        super().setUp()
        tests = self.campaign.source.parent / "Tests" / "Editor"
        tests.mkdir(parents=True)
        (tests / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.Ran"}))
        (tests / "ReaderTests.cs").write_text(READER)
        self.campaign.baseline_text = ONE_READER_CASE
        self.campaign.outcome = "survived"
        self.il_read = {mutation_check.assembly_of(self.campaign.source)}

    def own(self):
        """The scope of each launch of mutant 1."""
        return [scope for name, scope in zip(self.campaign.launched, self.campaign.scopes)
                if name == "mutant-001.xml"]


class ILReadingSurvivorTests(ILReadingBase):
    """A session survivor in an assembly some fixture reads the IL of stands for every other fixture, so
    its own launch runs the fixtures reading IL and nothing else."""

    def ran(self):
        found = re.findall(r"s; cases run: ([^;]*);", self.campaign.printed)
        return found[-1] if found else None

    def test_Given_ASessionSurvivorInAnAssemblyAFixtureReadsTheILOf_When_ItsOwnLaunchRuns_Then_OnlyTheFixturesReadingILAreSelected(self):
        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS])

    def test_Given_TheFixturesReadingILPassAloneOnTheUnmutatedTree_When_TheCampaignRuns_Then_TheyAreRunOnceBeforeAnyMutant(self):
        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual([scope for name, scope in zip(self.campaign.launched, self.campaign.scopes)
                          if name == "baseline-il.xml"], [ONLY_READERS])

    # GREEN_ON_BASE(characterization): drop the `held["failed"]` term of the control's check in `mutation_check.py` and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_TheFixturesReadingILRedAloneOnTheUnmutatedTree_When_TheCampaignRuns_Then_TheWholeSuiteIsTheOwnLaunch(self):
        # Arrange — their failure on a mutant would not be the mutation's.
        self.campaign.il_control = "red"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])

    # GREEN_ON_BASE(characterization): drop the `held["total"] != il_cases` term of the control's check and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_AControlThatRanOtherCasesThanTheBaselineHolds_When_TheCampaignRuns_Then_TheWholeSuiteIsTheOwnLaunch(self):
        # Arrange — a filter that selected nothing proves nothing about these fixtures.
        self.campaign.il_control = "short"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])

    def test_Given_AnILReadingLaunchThatSelectsNoCase_When_ItSurvives_Then_TheWholeSuiteDecides(self):
        # Arrange — a filter that selected nothing would read every mutant as surviving the fixtures it
        # was meant to run.
        self.campaign.il_launch = "nothing"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, []])

    def test_Given_AnILReadingLaunchThatTimedOutOverACompleteResult_When_ItSurvives_Then_TheWholeSuiteDecides(self):
        # Arrange
        self.campaign.il_launch = "timeout_complete"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, []])

    def test_Given_AnILReadingLaunchThatWroteNoResult_When_ItEnds_Then_TheWholeSuiteDecides(self):
        # Arrange
        self.campaign.il_launch = "none"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, []])

    def test_Given_AnILReadingLaunchThatTimedOutWithoutAResult_When_ItEnds_Then_TheWholeSuiteDecides(self):
        # Arrange
        self.campaign.il_launch = "timeout"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, []])

    def test_Given_AnILReadingLaunchWhoseBuildStopped_When_ItEnds_Then_TheWholeSuiteDecides(self):
        # Arrange
        self.campaign.il_launch = "build"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, []])

    def test_Given_AnILReadingLaunchThatFailedACase_When_ItEnds_Then_ItIsTheVerdictAndTheWholeSuiteIsNotRun(self):
        # Arrange — it reports more cases than the baseline holds, which a failure does not make wrong.
        self.campaign.il_launch = "fail"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual((self.own(), mutation_check.KILLED in self.campaign.printed), ([ONLY_READERS], True))

    def test_Given_AnILReadingLaunchThatFellBackOnTheWholeSuite_When_TheSummaryPrints_Then_ItNamesOnlyTheWholeRun(self):
        # Arrange
        self.campaign.il_launch = "nothing"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.ran(), "whole 1")

    def test_Given_AnOwnLaunch_When_TheSummaryPrints_Then_ItNamesHowManyCasesItRan(self):
        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.ran(), "fixtures reading IL 1")

    # GREEN_ON_BASE(characterization): let the survivor-only test beside `CALL_REMOVING` pass an inconclusive reading and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_ASessionSurvivorThatWasInconclusive_When_ItsOwnLaunchRuns_Then_ItTakesTheWholeSuite(self):
        # Arrange — the session's inconclusive case may be any fixture's, not only those reading IL.
        self.campaign.outcome = "inconclusive"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])

    # GREEN_ON_BASE(characterization): delete the `if scope:` return of `il_only_classes` and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_AScopeTheCallerChose_When_ASurvivorsOwnLaunchRuns_Then_NoLaunchIsNarrowedToTheFixturesReadingIL(self):
        # Act
        self.campaign.run("--max", "1", "--filter", "N.Elsewhere")

        # Assert
        self.assertEqual([scope for scope in self.campaign.scopes if "[.+]ReaderTests$" in " ".join(scope)], [])

    # GREEN_ON_BASE(characterization): the base launches the whole suite for every such survivor, and deleting the `if not cases` test in `il_only_scope` reddens it.
    def test_Given_NoFixtureReadingIL_When_ASurvivorsAssemblyIsRead_Then_TheWholeSuiteIsTheOwnLaunch(self):
        # Arrange — the assembly is read as IL by name, but no fixture file the project holds says so.
        (self.campaign.source.parent / "Tests" / "Editor" / "ReaderTests.cs").write_text("class ReaderTests { }")

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])

    def test_Given_AReaderThatIsAlsoATextReader_When_TheScopeIsBuilt_Then_ItIsLeftOutOfIt(self):
        # Arrange
        results = self.campaign.project / "baseline.xml"
        results.write_text(ONE_READER_CASE)

        # Act
        scope = mutation_check.il_only_scope(self.campaign.project, results, {"ReaderTests"}, [])

        # Assert
        self.assertEqual(scope, (None, 0))

    def test_Given_AScopeTheCallerChose_When_TheScopeIsBuilt_Then_ThereIsNone(self):
        # Arrange
        results = self.campaign.project / "baseline.xml"
        results.write_text(ONE_READER_CASE)

        # Act
        scope = mutation_check.il_only_scope(self.campaign.project, results, set(), ["-testFilter", "X"])

        # Assert
        self.assertEqual(scope, (None, 0))


class ILReadingUncoveredTests(ILReadingBase):
    """A mutant no case ran, in an assembly some fixture reads the IL of, takes the same launch."""

    reach_from = 1

    def setUp(self):
        super().setUp()
        self.campaign.reach = {"N.C.A": {99}}

    def test_Given_AMutantNoCaseRanInAnAssemblyAFixtureReadsTheILOf_When_ItsOwnLaunchRuns_Then_OnlyTheFixturesReadingILAreSelected(self):
        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS])


class CallRemovingCampaign(SessionCampaign):
    """A mutant whose kept operator takes a call out of the method."""

    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static void Run()
                {
                    Work();
                }
            }
        }
        """)


class ILReadingCallRemovalTests(ILReadingBase):
    """A woven component's IL is read through the callees it reaches, so a survivor that removes a call is
    not narrowed to the fixtures reading IL."""

    campaign_class = CallRemovingCampaign

    # GREEN_ON_BASE(characterization): delete the `CALL_REMOVING` test on the survivor path and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_ASurvivorThatRemovedACall_When_ItsAssemblyIsReadAsIL_Then_ItsOwnLaunchIsTheWholeSuite(self):
        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])


class ILReadingCallRemovalUncoveredTests(ILReadingBase):
    campaign_class = CallRemovingCampaign
    reach_from = 1

    # GREEN_ON_BASE(characterization): delete the `CALL_REMOVING` test on the uncovered path and this reddens; the base has no IL-only launch, so it runs the whole suite here too.
    def test_Given_AnUncoveredMutantThatRemovedACall_When_ItsAssemblyIsReadAsIL_Then_ItsOwnLaunchIsTheWholeSuite(self):
        # Arrange
        self.campaign.reach = {"N.C.A": {99}}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.own(), [[]])


class RejectingSessionCampaign(SessionCampaign):
    """`Log(a + b, ok && other);`: the kept `+ -> -` rewrite does not build, and the cap holds a clause
    removal behind it."""

    BODY = RejectingJoinCampaign.BODY
    rejects_minus = True


class ILReadingStandInTests(ILReadingBase):
    """The mutant that stands for a line whose kept mutant is no program was not the session's survivor."""

    campaign_class = RejectingSessionCampaign

    def test_Given_AKeptMutantThatDoesNotBuild_When_AStandInIsMeasured_Then_ItRunsTheWholeSuite(self):
        # Act
        self.campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(self.own(), [ONLY_READERS, [], []])


class ILFixtureHazardTests(unittest.TestCase):
    """Fixtures a class filter and a case count cannot follow."""

    def classes(self, declarations):
        project = Path(tempfile.mkdtemp(prefix="il-hazard-"))
        tests = project / "Packages" / "P" / "Tests" / "Editor"
        tests.mkdir(parents=True)
        (tests / "T.asmdef").write_text(json.dumps({"name": "T"}))
        (tests / "ReaderTests.cs").write_text(declarations)
        (tests / "Other.cs").write_text("class Other : ReaderBase { }")
        return mutation_check.il_only_classes(project, {"T"}, set())

    def test_Given_APlainFixtureReadingIL_When_Named_Then_ItsClassIsReturned(self):
        # Act
        classes = self.classes(READER)

        # Assert
        self.assertEqual(classes, {"ReaderTests"})

    def test_Given_AFixtureTakingArguments_When_Named_Then_NoClassIsReturned(self):
        # Act
        classes = self.classes("[TestFixture(1)]\n" + READER)

        # Assert
        self.assertIsNone(classes)

    def test_Given_AFixtureSourceInTheReadingFile_When_Named_Then_NoClassIsReturned(self):
        # Act
        classes = self.classes("[TestFixtureSource(nameof(X))]\n" + READER)

        # Assert
        self.assertIsNone(classes)

    def test_Given_AClassSomeSourceDerivesFrom_When_Named_Then_NoClassIsReturned(self):
        # Act
        classes = self.classes("class ReaderBase { void A() { ModuleDefinition.ReadModule(\"x\"); } }")

        # Assert
        self.assertIsNone(classes)

    def test_Given_TheFixturesTheRepositoryReadsILIn_When_Named_Then_TheyAreNamedAndNoneIsParameterisedOrDerived(self):
        # Arrange — the guard that fails the day one is added, so the narrowing is declined rather than wrong.
        ran = {json.loads(path.read_text())["name"]
               for path in (REPO_ROOT / mutation_check.PACKAGE).rglob("*.asmdef")
               if path.parent.parts[-2:] == ("Tests", "Editor")}

        # Act
        classes = mutation_check.il_only_classes(REPO_ROOT, ran, set())

        # Assert
        self.assertTrue(classes)


class SessionReachTests(SessionRoutingBase):
    """What the reach run decides about a mutant's launch: against which cases it runs, whether it runs at
    all, and what a survivor of the narrowed run is owed."""

    reach_from = 1

    def setUp(self):
        super().setUp()
        self.campaign.baseline_text = '<test-run total="40" passed="40" failed="0" inconclusive="0" />'
        self.campaign.reach = {"N.C.A": {1}}
        # A tenth of this is what a mutant's cases may take together.
        self.campaign.baseline_seconds = 100.0

    def stages(self, key="name"):
        plan = self.campaign.plans[-1]
        item = next(item for item in plan["items"] if item["id"] == 1)
        return [stage[key] for stage in item["stages"]]

    # GREEN_ON_BASE(characterization): the base gives every mutant the whole suite; replacing the seconds test in `select_by_reach` with `if chosen:` reddens it.
    def test_Given_AMutantWhoseCasesTakeMoreThanATenthOfTheBaseline_When_TheCampaignRuns_Then_ItTakesTheWholeSuite(self):
        # Arrange — one case, but twenty seconds against a ten-second budget: a count would have taken it.
        self.campaign.slow_cases = {"N.C.A": 20}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base gives every mutant the whole suite; reading an unreadable
    # duration as `0.0` instead of infinity in `read_cases` reddens it.
    def test_Given_APassedCaseWithNoReadableDuration_When_TheCampaignRuns_Then_ItsMutantTakesTheWholeSuite(self):
        # Arrange — a duration that cannot be read must not weigh as nothing.
        self.campaign.slow_cases = {"N.C.A": "n/a"}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base reads no site as uncovered; making `select_by_reach` call every
    # unrun site uncovered whatever `unsure` says reddens it.
    def test_Given_AReachRunWithAnInconclusiveCase_When_TheCampaignRuns_Then_NoSiteIsReadAsUncovered(self):
        # Arrange — that case stopped where its Assume failed, so the sites after it are unrecorded, not unreached.
        self.campaign.reach = {"N.C.B": {99}}
        self.campaign.inconclusive_case = "N.C.A"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base reads no site as uncovered; cutting the `not stage.get("armed")` test in `measure_reach` reddens it.
    def test_Given_AReachRunWithNoSwitchArmed_When_TheCampaignRuns_Then_NoMutantIsReadAsUncovered(self):
        # Arrange — the editor kept a build without the guards.
        self.campaign.reach = {"N.C.B": {99}}
        self.campaign.reach_armed = ()

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base reads no site as uncovered; cutting the `endswith(":0")` test in `measure_reach` reddens it.
    def test_Given_AReachRunArmedOnAMutant_When_TheCampaignRuns_Then_NoMutantIsReadAsUncovered(self):
        # Arrange — a switch that read an id is not the unmutated program.
        self.campaign.reach = {"N.C.B": {99}}
        self.campaign.reach_armed = ("Probe:0", "Probe:7")

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    def test_Given_ATimeoutShorterThanTwiceTheBaseline_When_TheReachRunIsPlanned_Then_TheTimeoutBoundsIt(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1", "--timeout", "50")

        # Assert
        self.assertEqual([stage["bound"] for item in self.campaign.plans[0]["items"] for stage in item["stages"]],
                         [50])

    def test_Given_AReachRunToCharge_When_ItIsPlanned_Then_ItIsStoppedAtTwiceTheBaselinesTime(self):
        # Arrange — `ShardCeilingTests.REACH_RUN` charges that much for a run that never finishes.
        self.campaign.baseline_seconds = 100.0
        bound = mutation_check.REACH_BOUND * 100

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual([stage["bound"] for item in self.campaign.plans[0]["items"] for stage in item["stages"]],
                         [bound])

    def test_Given_AGateThatFailsUnmutated_When_TheMutantIsKilledInItsCoveredStage_Then_ItTakesItsOwnLaunch(self):
        # Arrange — the cases fail in that order without any mutation, so their failure is no kill.
        self.campaign.gate_green = False

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_AMutantWithCoveredCases_When_TheSessionIsPlanned_Then_TheSameCasesRunUnmutatedFirst(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1")

        # Assert
        opening = self.campaign.plans[-1]["items"][0]
        self.assertEqual((opening["id"], [(stage["name"], stage["testNames"]) for stage in opening["stages"]]),
                         (0, [(mutation_check.covered_name(0), ["N.C.A"])]))

    def test_Given_AnEditModeSession_When_ItOpens_Then_ItsOnlyOpeningWorkIsOneRunOfTheWholeSuite(self):
        # Arrange — `ShardCeilingTests.REACH_RUN` charges exactly that, in place of the areas' baselines.
        reach_stage = mutation_check.REACH_STAGE

        # Act
        self.campaign.run("--max", "1")

        # Assert
        opening = self.campaign.plans[0]
        self.assertEqual((opening["reach"], [(stage["name"], stage["assemblyNames"], stage["testNames"])
                                             for item in opening["items"] for stage in item["stages"]]),
                         (True, [(reach_stage, [], [])]))

    def test_Given_AMutantOneCaseRan_When_TheCampaignRuns_Then_ItsFirstLaunchHoldsThatCaseAlone(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages("testNames")[0], ["N.C.A"])

    def test_Given_AMutantKilledInItsCoveredStage_When_ItsVerdictIsRecorded_Then_TheRecordCarriesTheCasesItRan(self):
        # Arrange
        covered = mutation_check.COVERED_STAGE

        # Act
        self.campaign.run("--max", "1")

        # Assert
        record = json.loads((self.campaign.project / "out" / "mutant-001.json").read_text())
        self.assertEqual(record["ran"], [[covered + ":0", 1]])

    # GREEN_ON_BASE(characterization): the base records a kill's failing cases too; this holds that a kill
    # the covered stage took reaches the ledger by the same record. Dropping `killers` from the tuple
    # `session_verdict` returns for a covered kill reddens it.
    def test_Given_AMutantKilledInItsCoveredStage_When_ItsVerdictIsRecorded_Then_TheRecordNamesTheKillingCase(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1")

        # Assert
        record = json.loads((self.campaign.project / "out" / "mutant-001.json").read_text())
        self.assertEqual(record["killers"], ["N.C.Kills"])

    def test_Given_AMutantOneCaseRanThatSurvivesIt_When_TheCampaignRuns_Then_TheWholeSuiteRunsBeforeTheVerdict(self):
        # Arrange
        self.campaign.outcome = "survived"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), [mutation_check.covered_name(0), "whole"])

    def test_Given_AMutantNoCaseRan_When_TheCampaignRuns_Then_ItIsRecordedUncovered(self):
        # Arrange — the only site anything reached is mutant 99's.
        self.campaign.reach = {"N.C.A": {99}}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.campaign.recorded(), mutation_check.UNCOVERED)

    def test_Given_AMutantNoCaseRan_When_TheCampaignRuns_Then_NoItemIsPlannedForIt(self):
        # Arrange
        self.campaign.reach = {"N.C.A": {99}}

        # Act
        self.campaign.run("--max", "1")

        # Assert — the reach run's opening item is the only one any plan held.
        self.assertEqual([item["id"] for plan in self.campaign.plans for item in plan["items"]], [0])

    def test_Given_AMutantNoCaseRanInAnAssemblyAFixtureReadsTheILOf_When_TheCampaignRuns_Then_ItTakesItsOwnLaunch(self):
        # Arrange — that fixture sees the guards of every mutant, whatever the cases ran.
        self.campaign.reach = {"N.C.A": {99}}
        self.il_read = {mutation_check.assembly_of(self.campaign.source)}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.campaign.launched, ["baseline.xml", "mutant-001.xml"])

    def test_Given_AMutantRunBeforeAnyCaseStarted_When_TheCampaignRuns_Then_ItTakesTheWholeSuite(self):
        # Arrange — its site ran with the domain, so any case may read what it left.
        self.campaign.reach = {"N.C.A": {99}, mutation_check.SETUP_CASE: {1}}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base gives every mutant the whole suite, so a reach run missing
    # a case changes nothing there; cutting the `counts["total"] != total` test in `measure_reach` reddens it.
    def test_Given_AReachRunMissingACase_When_TheCampaignRuns_Then_NoMutantIsReadAsUncovered(self):
        # Arrange — which sites the missing case ran is unknown, so none can be called unreached.
        self.campaign.reach = {"N.C.A": {99}}
        self.campaign.missing_case = True

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base gives every mutant the whole suite; replacing the passed-case
    # filter in `select_by_reach` with the cases that ran the site reddens it.
    def test_Given_AReachRunWithAFailingCase_When_TheCampaignRuns_Then_ASiteOnlyThatCaseRanTakesTheWholeSuite(self):
        # Arrange
        self.campaign.failing_case = "N.C.A"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    def test_Given_AReachRunWithAFailingCase_When_TheCampaignRuns_Then_NoSiteIsReadAsUncovered(self):
        # Arrange — the failed case stopped where it failed, so the sites after that point are unrecorded.
        self.campaign.reach = {"N.C.B": {99}}
        self.campaign.failing_case = "N.C.A"

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base gives every mutant the whole suite whatever a table holds;
    # cutting the recorded-something test in `measure_reach` reddens it.
    def test_Given_AReachRunThatRecordedNothing_When_TheCampaignRuns_Then_NoMutantIsReadAsUncovered(self):
        # Arrange — tables never turned on look the same as a suite that ran no site.
        self.campaign.reach = {}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual(self.stages(), ["whole"])

    # GREEN_ON_BASE(characterization): the base never reads reach; cutting the platform test around the
    # reach run in `measure_in_session` reddens it.
    def test_Given_APlayModePass_When_TheCampaignRuns_Then_NoReachRunIsMade(self):
        # Arrange — the pass measures what the EditMode suite left, against the whole PlayMode suite.

        # Act
        self.campaign.run("--max", "1", "--platform", "PlayMode")

        # Assert
        self.assertEqual([plan.get("reach") for plan in self.campaign.plans], [None])

    def test_Given_AMutantTheSessionKilled_When_TheSummaryPrints_Then_ItNamesHowManyCasesItsLaunchRan(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertIn("[killed] {} 1".format(mutation_check.covered_name(0)), self.campaign.printed)

    def test_Given_AnUncoveredMutant_When_TheSummaryPrints_Then_ItRanNoCases(self):
        # Arrange
        self.campaign.reach = {"N.C.A": {99}}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertIn("[{}] uncovered 0".format(mutation_check.UNCOVERED), self.campaign.printed)

    def test_Given_AnUncoveredMutantAndNoDeclaration_When_TheCampaignDecides_Then_ItFailsTheRun(self):
        # Arrange — a survivor needs an answer, and a site no case ran is not exempt from asking for one.
        self.campaign.reach = {"N.C.A": {99}}

        # Act
        code = self.campaign.run_over_diff("--max", "1")

        # Assert
        self.assertEqual(code, 1)


class StylingSessionCampaign(SessionCampaign):
    """A session campaign whose mutant sits under an area with an assembly the baseline ran in a second."""

    SOURCE = "Packages/com.velvet.core/Runtime/Styling/Probe.cs"


class SessionReachAreaTests(SessionRoutingBase):
    """A mutant under an area the baseline could narrow: no session takes the area's own baseline, so the
    opening holds the covered sets and nothing else."""

    campaign_class = StylingSessionCampaign
    reach_from = 1

    def setUp(self):
        super().setUp()
        tests = self.campaign.source.parent / "Tests" / "Editor"
        tests.mkdir(parents=True)
        (tests / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.Styling.A"}))
        self.campaign.baseline_text = ('<test-run total="40" passed="40" failed="0" inconclusive="0" duration="9">'
                                       '<test-suite type="Assembly" name="Velvet.Tests.Styling.A.dll" '
                                       'duration="1" /></test-run>')
        self.campaign.baseline_seconds = 100.0
        self.campaign.reach = {"N.C.A": {1}}

    def plan_items(self):
        return [item["id"] for item in self.campaign.plans[-1]["items"]]

    def stages(self):
        item = next(item for item in self.campaign.plans[-1]["items"] if item["id"] == 1)
        return [stage["name"] for stage in item["stages"]]

    def test_Given_ACoveredMutantUnderANarrowableArea_When_TheSessionIsPlanned_Then_OnlyItsCoveredSetOpensIt(self):
        # Arrange

        # Act
        self.campaign.run("--max", "1")

        # Assert
        opening = self.campaign.plans[-1]["items"][0]
        self.assertEqual([stage["name"] for stage in opening["stages"]], [mutation_check.covered_name(0)])

    def test_Given_AWideMutantUnderANarrowableArea_When_TheSessionIsPlanned_Then_ItTakesTheWholeSuiteAlone(self):
        # Arrange — its cases take longer than the budget; the ceiling charges no area baseline for it.
        self.campaign.slow_cases = {"N.C.A": 20}

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual((self.plan_items(), self.stages()), ([1, -1], ["whole"]))

    def test_Given_FewerMutantsThanTheThreshold_When_TheSessionIsPlanned_Then_NoReachRunIsMade(self):
        # Arrange
        mutation_check.REACH_MIN_MUTANTS = 2

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual([plan.get("reach") for plan in self.campaign.plans], [None])

    def test_Given_FewerMutantsThanTheThreshold_When_TheSessionIsPlanned_Then_TheAreaOpensItAndPrecedesTheWholeSuite(self):
        # Arrange — main's path, kept for the shard too small for a reach run to pay.
        mutation_check.REACH_MIN_MUTANTS = 2

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual((self.plan_items(), self.stages()), ([0, 1, -1], ["narrowed:Styling", "whole"]))

    def test_Given_AReachRunThatDoesNotStand_When_TheSessionIsPlanned_Then_TheMutantTakesTheWholeSuiteAlone(self):
        # Arrange
        self.campaign.missing_case = True

        # Act
        self.campaign.run("--max", "1")

        # Assert
        self.assertEqual((self.plan_items(), self.stages()), ([1, -1], ["whole"]))


def probe_body(methods):
    lines = "\n".join("        internal static bool M{0}(int a, int b) => a <= b;".format(number)
                      for number in range(methods))
    return "namespace Velvet\n{\n    internal static class Probe\n    {\n" + lines + "\n    }\n}\n"


class SevenMutantCampaign(StylingSessionCampaign):
    """Seven mutants under a narrowable area, of which the session places six and declines the last."""

    BODY = probe_body(7)
    DECLINES = 1

    def rewrite(self, _unity, project, request, _scratch):
        self.rewrites.append(request)
        ids = [mutant["id"] for entry in request["files"] for mutant in entry["mutants"]]
        relative = request["files"][0]["path"]
        return {"placed": ids[:len(ids) - self.DECLINES],
                "declined": {str(number): "declined for the case" for number in ids[len(ids) - self.DECLINES:]},
                "files": {relative: self.REWRITTEN}, "fatal": []}


class SixMutantCampaign(SevenMutantCampaign):
    """Six mutants, all placed."""

    BODY = probe_body(6)
    DECLINES = 0


class SixOrSevenMutantsBase(SessionRoutingBase):
    def setUp(self):
        super().setUp()
        tests = self.campaign.source.parent / "Tests" / "Editor"
        tests.mkdir(parents=True)
        (tests / "A.asmdef").write_text(json.dumps({"name": "Velvet.Tests.Styling.A"}))
        self.campaign.baseline_text = ('<test-run total="40" passed="40" failed="0" inconclusive="0" duration="9">'
                                       '<test-suite type="Assembly" name="Velvet.Tests.Styling.A.dll" '
                                       'duration="1" /></test-run>')
        self.campaign.baseline_seconds = 100.0
        self.campaign.reach = {"N.C.A": {1}}


class DeclinedMutantBesideASessionTests(SixOrSevenMutantsBase):
    """A mutant launched on its own keeps main's area-first path, so a session with one runs no reach run."""

    campaign_class = SevenMutantCampaign

    # GREEN_ON_BASE(characterization): the base runs no reach run; dropping the `len(placed) == len(pending)`
    # test from `reaching` in `measure_in_session` reddens it.
    def test_Given_SixPlacedMutantsAndADeclinedOne_When_TheCampaignRuns_Then_NoReachRunIsMade(self):
        # Arrange — six placed meet the threshold, but a seventh needs a launch of its own.

        # Act
        self.campaign.run("--max", "7")

        # Assert
        self.assertEqual([plan.get("reach") for plan in self.campaign.plans], [None])

    # GREEN_ON_BASE(characterization): the base takes the area's baseline for a launch of its own; making
    # `measure` skip the areas of its own launches beside a session reddens it.
    def test_Given_SixPlacedMutantsAndADeclinedOne_When_TheCampaignRuns_Then_TheDeclinedOneIsPrecededByItsAreasBaseline(self):
        # Arrange

        # Act
        self.campaign.run("--max", "7")

        # Assert
        self.assertEqual([name for name in self.campaign.launched if name.startswith("baseline-")],
                         ["baseline-Styling.xml"])


class FourteenMutantCampaign(SixMutantCampaign):
    """Fourteen mutants, all placed."""

    BODY = probe_body(14)


class ManyPlacedMutantsTests(SixOrSevenMutantsBase):
    campaign_class = FourteenMutantCampaign

    def test_Given_FourteenMutantsAllPlaced_When_TheCampaignRuns_Then_TheReachRunIsMade(self):
        # Arrange

        # Act
        self.campaign.run("--max", "14")

        # Assert
        self.assertEqual(self.campaign.plans[0].get("reach"), True)


class FullyPlacedShardTests(SixOrSevenMutantsBase):
    campaign_class = SixMutantCampaign

    def test_Given_AReachRunAndAnItemNoLaunchFinished_When_TheMutantTakesItsOwnLaunch_Then_NoAreaBaselinePrecedesIt(self):
        # Arrange — the reading does not stand; the whole suite answers all its area could.
        self.campaign.lost = True
        self.campaign.outcome = "survived"

        # Act
        self.campaign.run("--max", "6")

        # Assert
        self.assertEqual([name for name in self.campaign.launched if name.startswith("baseline-")], [])

    def test_Given_SixMutantsAllPlaced_When_TheCampaignRuns_Then_TheReachRunIsMade(self):
        # Arrange — the control for the pair above: the same threshold with nothing left for a launch of its own.

        # Act
        self.campaign.run("--max", "6")

        # Assert
        self.assertEqual(self.campaign.plans[0].get("reach"), True)


class SessionReachSelectionTests(unittest.TestCase):
    SECONDS = {"N.C.A": 1.0, "N.C.B": 1.0, "N.C.C": 1.0, "N.C.D": 1.0}

    def select(self, reached, setup=(), seconds=SECONDS, budget=10.0, unsure=False):
        return mutation_check.select_by_reach([1, 2], {case: set(ids) for case, ids in reached.items()},
                                              set(setup), dict(seconds), budget, unsure)

    def test_Given_ACaseThatRanOneMutantsSite_When_Selected_Then_ThatMutantTakesTheCase(self):
        # Arrange / Act
        selection, _ = self.select({"N.C.A": {1}, "N.C.B": {2}})

        # Assert
        self.assertEqual(selection, {1: ["N.C.A"], 2: ["N.C.B"]})

    def test_Given_NoCaseThatRanAMutantsSite_When_Selected_Then_ItIsUncovered(self):
        # Arrange / Act
        _, uncovered = self.select({"N.C.A": {1}})

        # Assert
        self.assertEqual(uncovered, [2])

    def test_Given_NoCaseThatRanAMutantsSiteAndACaseThatStoppedShort_When_Selected_Then_ItIsNotUncovered(self):
        # Arrange / Act
        result = self.select({"N.C.A": {1}}, unsure=True)

        # Assert
        self.assertEqual(result, ({1: ["N.C.A"]}, []))

    def test_Given_ASiteRunBeforeAnyCase_When_Selected_Then_ItIsNeitherSelectedNorUncovered(self):
        # Arrange / Act
        result = self.select({"N.C.A": {1}, "N.C.B": {2}}, setup={2})

        # Assert
        self.assertEqual(result, ({1: ["N.C.A"]}, []))

    def test_Given_ASiteOnlyACaseThatDidNotPassRan_When_Selected_Then_ItIsNeitherSelectedNorUncovered(self):
        # Arrange / Act — the case is no candidate killer, and it did run the site, so the site is not unreached.
        result = self.select({"N.C.A": {1}, "N.C.X": {2}})

        # Assert
        self.assertEqual(result, ({1: ["N.C.A"]}, []))

    def test_Given_CasesTakingMoreSecondsThanTheBudget_When_Selected_Then_TheMutantTakesTheWholeSuite(self):
        # Arrange / Act — two cases against a budget of one and a half seconds, though a count of two would pass.
        result = self.select({"N.C.A": {1}, "N.C.B": {1}, "N.C.C": {2}}, budget=1.5)

        # Assert
        self.assertEqual(result, ({2: ["N.C.C"]}, []))

    def test_Given_ASlowCaseAmongFew_When_Selected_Then_ItsSecondsAndNotItsCountDecide(self):
        # Arrange / Act
        result = self.select({"N.C.A": {1}}, seconds={"N.C.A": 11.0}, budget=10.0)

        # Assert
        self.assertEqual(result, ({}, [2]))

    def test_Given_CasesTakingExactlyTheBudget_When_Selected_Then_TheMutantTakesThem(self):
        # Arrange / Act
        selection, _ = self.select({"N.C.A": {1}, "N.C.B": {1}}, budget=2.0)

        # Assert
        self.assertEqual(selection, {1: ["N.C.A", "N.C.B"]})


class ReachRecordTests(unittest.TestCase):
    def read(self, text):
        path = Path(tempfile.mkdtemp(prefix="reach-")) / mutation_check.REACH_FILE
        path.write_text(text)
        return mutation_check.read_reached(path)

    def test_Given_LinesOfCasesAndOfTheSetup_When_Read_Then_TheSetupIdsAreKeptApart(self):
        # Arrange
        text = ('{"name": "<setup>", "ids": [3]}\n{"name": "N.C.A", "ids": [1, 2]}\n'
                '{"name": "N.C.A", "ids": [2, 5]}\n')

        # Act
        reached, setup = self.read(text)

        # Assert
        self.assertEqual((reached, setup), ({"N.C.A": {1, 2, 5}}, {3}))

    def test_Given_SetupHitsRecordedAfterTheLastCase_When_Read_Then_TheyAreStillTheSetups(self):
        # Arrange — what ran after the last case ended is filed under the same name as what ran before the first.
        text = '{"name": "N.C.A", "ids": [1]}\n{"name": "<setup>", "ids": [1]}\n'

        # Act
        reached, setup = self.read(text)

        # Assert
        self.assertEqual((reached, setup), ({"N.C.A": {1}}, {1}))

    def test_Given_TheRunnersClose_When_TheStageEnds_Then_ItFilesWhatRanAfterTheLastCaseUnderSetup(self):
        # Arrange — the runner is not compiled here; this holds its source to the one call that routes the tail.
        source = (REPO_ROOT / "Assets" / "MutantSchemata" / "Editor" / "SchemataRunner.cs").read_text()
        close = source.partition("private static void Close(")[2].partition("private static void AwaitDrain")[0]

        # Act
        filed = "if (plan.reach) RecordReached(SetupCase);" in close

        # Assert
        self.assertTrue(filed)

    def test_Given_ALineTheEditorPartWrote_When_Read_Then_ItIsSkipped(self):
        # Arrange
        text = '{"name": "N.C.A", "ids": [1]}\n{"name": "N.C.B", "ids": [2'

        # Act
        reached, _ = self.read(text)

        # Assert
        self.assertEqual(reached, {"N.C.A": {1}})

    def test_Given_NoFile_When_Read_Then_NothingWasReached(self):
        # Arrange / Act
        result = mutation_check.read_reached(Path(tempfile.mkdtemp(prefix="reach-")) / "absent.jsonl")

        # Assert
        self.assertEqual(result, ({}, set()))


class ReadCasesTests(unittest.TestCase):
    def read(self, body):
        path = Path(tempfile.mkdtemp(prefix="cases-")) / "r.xml"
        path.write_text("<test-run>{}</test-run>".format(body))
        return mutation_check.read_cases(path)

    def test_Given_PassedAndFailedCases_When_Read_Then_OnlyThePassingOnesCarrySeconds(self):
        # Arrange / Act
        seconds, _ = self.read('<test-case fullname="A" result="Passed" duration="1.5" />'
                               '<test-case fullname="B" result="Failed" duration="2" />')

        # Assert
        self.assertEqual(seconds, {"A": 1.5})

    def test_Given_AnInconclusiveCase_When_Read_Then_TheReadingIsUnsure(self):
        # Arrange / Act
        _, unsure = self.read('<test-case fullname="A" result="Inconclusive" duration="1" />')

        # Assert
        self.assertTrue(unsure)

    def test_Given_AFailedCase_When_Read_Then_TheReadingIsUnsure(self):
        # Arrange / Act
        _, unsure = self.read('<test-case fullname="A" result="Passed" duration="1" />'
                              '<test-case fullname="B" result="Failed" duration="1" />')

        # Assert
        self.assertTrue(unsure)

    def test_Given_OnlyPassedCases_When_Read_Then_TheReadingIsSure(self):
        # Arrange / Act
        _, unsure = self.read('<test-case fullname="A" result="Passed" duration="1" />')

        # Assert
        self.assertFalse(unsure)

    def test_Given_APassedCaseWithNoDuration_When_Read_Then_ItIsInfinitelyLong(self):
        # Arrange / Act
        seconds, _ = self.read('<test-case fullname="A" result="Passed" />')

        # Assert
        self.assertEqual(seconds, {"A": float("inf")})

    def test_Given_APassedCaseWithAnUnreadableDuration_When_Read_Then_ItIsInfinitelyLong(self):
        # Arrange / Act
        seconds, _ = self.read('<test-case fullname="A" result="Passed" duration="n/a" />')

        # Assert
        self.assertEqual(seconds, {"A": float("inf")})

    def test_Given_ANameThatOccursTwice_When_Read_Then_ItsDurationsAreAdded(self):
        # Arrange / Act
        seconds, _ = self.read('<test-case fullname="A" result="Passed" duration="1" />'
                               '<test-case fullname="A" result="Passed" duration="2" />')

        # Assert
        self.assertEqual(seconds, {"A": 3.0})


class ReachMirrorTests(unittest.TestCase):
    def test_Given_TheRewritersReachVariable_When_ComparedWithTheCampaigns_Then_TheyAreTheSameSuffix(self):
        # Arrange
        source = (Path(mutation_check.__file__).parent / "schemata" / "MutantSchemata.cs").read_text()

        # Act
        spelled = re.search(r'ReachSuffix = "([^"]+)"', source)

        # Assert
        self.assertEqual(spelled and spelled.group(1), mutation_check.REACH_SUFFIX)

    def test_Given_TheRunnersReachFile_When_ComparedWithTheCampaigns_Then_TheyAreTheSameName(self):
        # Arrange
        source = (REPO_ROOT / "Assets" / "MutantSchemata" / "Editor" / "SchemataRunner.cs").read_text()

        # Act
        spelled = re.search(r'"(reach\.jsonl)"', source)

        # Assert
        self.assertEqual(spelled and spelled.group(1), mutation_check.REACH_FILE)

    def test_Given_TheRunnersSetupCase_When_ComparedWithTheCampaigns_Then_TheyAreTheSameName(self):
        # Arrange
        source = (REPO_ROOT / "Assets" / "MutantSchemata" / "Editor" / "SchemataRunner.cs").read_text()

        # Act
        spelled = re.search(r'SetupCase = "([^"]+)"', source)

        # Assert
        self.assertEqual(spelled and spelled.group(1), mutation_check.SETUP_CASE)


class ILReadingAssemblyTests(unittest.TestCase):
    def test_Given_AFixtureReadingTheModuleOfAType_When_Derived_Then_TheTypesAssemblyIsRead(self):
        # Arrange — one assembly a fixture reads by a type, one it never names.
        project = Path(tempfile.mkdtemp(prefix="il-read-"))
        for name, declares in (("Read", "public static class V { }"), ("Unread", "public static class W { }")):
            directory = project / "Packages" / name
            directory.mkdir(parents=True)
            (directory / "{}.asmdef".format(name)).write_text(json.dumps({"name": name}))
            (directory / "{}.cs".format(name)).write_text(declares)
        tests = project / "Packages" / "Read" / "Tests"
        tests.mkdir()
        (tests / "T.asmdef").write_text(json.dumps({"name": "T"}))
        (tests / "ReaderTests.cs").write_text(
            "class ReaderTests { void A() { ModuleDefinition.ReadModule(typeof(V).Assembly.Location); } }")

        # Act
        read = mutation_check.il_reading_assemblies(project)

        # Assert
        self.assertEqual(read, {"Read"})

    def read_beside(self, ran):
        """What counts as read where the one fixture reading `Read` is in the test assembly `T`, under a
        baseline that ran `ran`."""
        project = Path(tempfile.mkdtemp(prefix="il-read-"))
        directory = project / "Packages" / "Read"
        directory.mkdir(parents=True)
        (directory / "Read.asmdef").write_text(json.dumps({"name": "Read"}))
        (directory / "Read.cs").write_text("public static class V { }")
        tests = directory / "Tests" / "Editor"
        tests.mkdir(parents=True)
        (tests / "T.asmdef").write_text(json.dumps({"name": "T"}))
        (tests / "ReaderTests.cs").write_text(
            "class ReaderTests { void A() { ModuleDefinition.ReadModule(typeof(V).Assembly.Location); } }")
        return mutation_check.il_reading_assemblies(project, ran=ran)

    def test_Given_AFixtureInAnAssemblyTheBaselineDidNotRun_When_Derived_Then_NothingIsRead(self):
        # Act
        read = self.read_beside({"Other"})

        # Assert
        self.assertEqual(read, set())

    def test_Given_AFixtureInAnAssemblyTheBaselineRan_When_Derived_Then_ItsReadingCounts(self):
        # Act
        read = self.read_beside({"T"})

        # Assert
        self.assertEqual(read, {"Read"})


class SessionVerdictTests(unittest.TestCase):
    """A narrowed kill, read directly: the campaign stub runs no narrowed stage."""

    def reading(self, opening_failures):
        planned = {"id": 7, "stages": [{"name": "narrowed:Styling", "assemblyNames": ["Velvet.Tests.Styling.Editor"]},
                                       {"name": "whole", "assemblyNames": []}]}
        item = {"id": 7, "stages": [{"name": "narrowed:Styling", "finished": True, "cancelled": True,
                                     "failures": ["N.C.Kills"], "armed": ["Velvet:7"]}]}
        opening = {"narrowed:Styling": {"name": "narrowed:Styling", "finished": True, "passed": 3,
                                        "failed": len(opening_failures), "failures": opening_failures}}
        return mutation_check.session_verdict(item, planned, 7, "Velvet", {}, opening, {"N.C.Kills": True}, set())

    def test_Given_ANarrowedKillOverAGreenAreaBaseline_When_Read_Then_ItNamesTheAreasAssemblies(self):
        # Arrange

        # Act
        reading = self.reading([])

        # Assert
        self.assertEqual(reading, (mutation_check.KILLED, "1 failed in Velvet.Tests.Styling.Editor: Kills",
                                   ["N.C.Kills"]))

    def test_Given_ANarrowedKillOverAnAreaBaselineThatFailed_When_Read_Then_ItDoesNotStand(self):
        # Arrange — the area failing alone under the unmutated program fails under every mutant too.

        # Act
        reading = self.reading(["N.C.Other"])

        # Assert
        self.assertIsNone(reading)


class CoveredVerdictTests(unittest.TestCase):
    """A covered stage read directly: the campaign stub writes the stages and these write their cases."""

    def setUp(self):
        self.directory = Path(tempfile.mkdtemp(prefix="covered-"))
        self.addCleanup(shutil.rmtree, self.directory, ignore_errors=True)
        self.planned = {"id": 7, "stages": [{"name": "covered:0", "testNames": ["N.C.A", "N.C.B"]},
                                            {"name": "whole", "testNames": []}]}
        self.green_gate = {"covered:0": {"name": "covered:0", "finished": True, "passed": 2, "failed": 0,
                                         "failures": []}}

    def stage(self, name, total, failures=(), omitted=False):
        xml = self.directory / "{}.xml".format(name)
        xml.write_text('<test-run total="{0}" passed="{0}" failed="0" inconclusive="0" />'.format(total))
        return {"name": name, "finished": True, "xml": str(xml), "failures": list(failures),
                "armed": ["Velvet:7"], "omitted": omitted}

    def read(self, stages, opening=None):
        return mutation_check.session_verdict({"id": 7, "stages": stages}, self.planned, 7, "Velvet",
                                              {"whole": 9}, self.green_gate if opening is None else opening,
                                              {"N.C.Kills": True}, set())

    def test_Given_AKillInTheCoveredStage_When_Read_Then_ItNamesHowManyCasesRanTheSite(self):
        # Arrange
        covered = mutation_check.COVERED_STAGE
        stages = [{"name": covered + ":0", "finished": True, "cancelled": True, "failures": ["N.C.Kills"],
                   "armed": ["Velvet:7"]}]

        # Act
        reading = self.read(stages)

        # Assert
        self.assertEqual(reading, (mutation_check.KILLED, "1 failed among the 2 case(s) that ran the site: Kills",
                                   ["N.C.Kills"]))

    # GREEN_ON_BASE(characterization): the base reads a stage it does not know as no verdict; cutting the
    # opening-run test in `session_verdict` (`clean_stage(opening.get(...))`) for covered stages reddens it.
    def test_Given_AKillInACoveredStageWhoseSameCasesFailedUnmutated_When_Read_Then_ItIsNoVerdict(self):
        # Arrange — an order-dependent failure: the cases fail in that sequence whatever the mutation does.
        stages = [{"name": "covered:0", "finished": True, "cancelled": True, "failures": ["N.C.Kills"],
                   "armed": ["Velvet:7"]}]
        red = {"covered:0": dict(self.green_gate["covered:0"], failed=1, failures=["N.C.Kills"])}

        # Act
        reading = self.read(stages, red)

        # Assert
        self.assertIsNone(reading)

    # GREEN_ON_BASE(characterization): the base reads a stage it does not know as no verdict; cutting the
    # `last["name"] != "whole"` test in `session_verdict` reddens it.
    def test_Given_ACoveredStageThatSurvivedAndNoWholeStage_When_Read_Then_ItIsNoVerdict(self):
        # Arrange — a narrowed survivor is never a verdict.
        stages = [self.stage("covered:0", 2)]

        # Act
        reading = self.read(stages)

        # Assert
        self.assertIsNone(reading)

    def test_Given_ACoveredStageThatSurvivedAndAGreenWholeStage_When_Read_Then_ItSurvived(self):
        # Arrange
        stages = [self.stage("covered:0", 2), self.stage("whole", 9)]

        # Act
        reading = self.read(stages)

        # Assert
        self.assertEqual(reading, (mutation_check.SURVIVED, "", ()))

    # GREEN_ON_BASE(characterization): the base reads a stage it does not know as no verdict; cutting the
    # `stage_expectation` comparison in `session_verdict` reddens it.
    def test_Given_ACoveredStageShortOfTheCasesItWasHanded_When_Read_Then_ItIsNoVerdict(self):
        # Arrange — one of the two cases was not found, so the run asked another question.
        stages = [self.stage("covered:0", 1), self.stage("whole", 9)]

        # Act
        reading = self.read(stages)

        # Assert
        self.assertIsNone(reading)

    def test_Given_ACoveredStageAndAWholeStage_When_TheCasesAreCounted_Then_BothAreNamed(self):
        # Arrange
        stages = [dict(self.stage("covered:0", 2), ran=2), self.stage("whole", 9)]

        # Act
        ran = mutation_check.cases_run({"id": 7, "stages": stages})

        # Assert
        self.assertEqual(ran, (("covered:0", 2), ("whole", 9)))

    def test_Given_AStageTheRunnerCancelledAtItsFirstFailure_When_TheCasesAreCounted_Then_TheCasesItFinishedAreCounted(self):
        # Arrange
        stages = [{"name": "covered:0", "finished": True, "cancelled": True, "failures": ["N.C.Kills"], "ran": 1}]

        # Act
        ran = mutation_check.cases_run({"id": 7, "stages": stages})

        # Assert
        self.assertEqual(ran, (("covered:0", 1),))


class SessionPlanTests(unittest.TestCase):
    def plan(self, count, platform="EditMode"):
        project = Path("/p")
        mutants = [mutation_check.Mutant(project / "Packages/com.velvet.core/Runtime/Styling/A.cs", 1, 0,
                                         "<", "<=", "boundary") for _ in range(count)]
        plan, segments = mutation_check.session_plan(project, mutants, list(range(1, count + 1)), {}, [],
                                                     platform, 100, 900, Path("/out"))
        return plan, segments

    def test_Given_MoreMutantsThanOneSegmentHolds_When_Planned_Then_EachSegmentEndsInAConfirmation(self):
        # Arrange — one mutant past a full segment, so the second segment is the short tail.
        full = mutation_check.SESSION_MUTANTS

        # Act
        plan, segments = self.plan(full + 1)

        # Assert — and each launch ends where its confirmation does.
        self.assertEqual(([position for position, item in enumerate(plan["items"]) if item.get("confirm")],
                          segments), ([full, full + 2], [full + 1, full + 3]))

    def test_Given_OneSegmentOfMutants_When_Planned_Then_ItHoldsExactlyOneConfirmation(self):
        # Arrange
        count = mutation_check.SESSION_MUTANTS

        # Act
        plan, _ = self.plan(count)

        # Assert
        self.assertEqual(sum(1 for item in plan["items"] if item.get("confirm")), 1)

    def test_Given_PlayMode_When_Planned_Then_NoStageStopsAtItsFirstFailure(self):
        # Arrange — a job cancelled in play mode leaves its scene modified for the next one.

        # Act
        plan, _ = self.plan(2, "PlayMode")

        # Assert
        self.assertEqual({stage["stopAtFirstFailure"] for item in plan["items"] for stage in item["stages"]},
                         {False})

    def test_Given_EditMode_When_Planned_Then_AMutantsStagesStopAtTheirFirstFailure(self):
        # Arrange

        # Act
        plan, _ = self.plan(2)

        # Assert
        self.assertEqual([stage["stopAtFirstFailure"] for item in plan["items"] if not item.get("confirm")
                          for stage in item["stages"]], [True, True])


class SessionFilterTests(unittest.TestCase):
    def test_Given_AnEditorScope_When_ReadAsAStageFilter_Then_EachFlagSplitsOnSemicolons(self):
        # Arrange
        scope = ["-assemblyNames", "A;B", "-testFilter", "!\\.X$;!\\.Y$"]

        # Act
        read = mutation_check.session_filter(scope)

        # Assert
        self.assertEqual(read, (["A", "B"], ["!\\.X$", "!\\.Y$"]))


class HeldFilesTests(unittest.TestCase):
    def held(self):
        """A tree holding the record a session writes over two guarded sources."""
        campaign = StubbedCampaign()
        second = campaign.project / "Packages/com.velvet.core/Runtime/Second.cs"
        entries = []
        for path, original in ((campaign.source, campaign.source.read_text()), (second, "second original")):
            entries.append({"source": str(path), "original": original,
                            "original_sha": hashlib.sha256(original.encode()).hexdigest(),
                            "mutated_sha": hashlib.sha256(b"guarded").hexdigest()})
            path.write_text("guarded")
        (campaign.project / mutation_check.SENTINEL).write_text(json.dumps(
            {"sources": entries, "mutation": "2 mutant(s) guarded for one session"}))
        return campaign, second

    def test_Given_ASessionsHeldFiles_When_TheHolderReleasesThem_Then_EveryOneIsPutBack(self):
        # Arrange
        campaign, second = self.held()

        # Act
        mutation_check.Holder(campaign.project / mutation_check.SENTINEL).release()

        # Assert
        self.assertEqual(second.read_text(), "second original")

    def test_Given_ASessionsHeldFiles_When_ACommitWouldRecordAnyOfThem_Then_ItIsRefused(self):
        # Arrange — the second file, not the first: a reader of one `source` sees only the first.
        campaign, _ = self.held()

        # Act
        code = campaign.run("--carried", "Packages/com.velvet.core/Runtime/Second.cs")

        # Assert
        self.assertEqual(code, mutation_check.CARRIED_REFUSAL)

    def test_Given_ASessionsHeldFileEditedSince_When_RestoreRuns_Then_ItRefusesToOverwriteIt(self):
        # Arrange
        campaign, second = self.held()
        second.write_text("somebody's own work")

        # Act — a reader of one `source` finds none in this record and stops on an OSError instead.
        try:
            code = campaign.run("--restore")
        except OSError as failure:
            code = failure

        # Assert
        self.assertIn("holds neither the mutation", str(code))


class SessionLaunchTests(unittest.TestCase):
    """`run_session` against a runner played by a stub of `launch`, which writes what the real runner
    writes: its progress, and `runner-done` or `runner-failed`."""

    def setUp(self):
        self.directory = Path(tempfile.mkdtemp(prefix="session-launch-"))
        project = Path("/p")
        mutants = [mutation_check.Mutant(project / "Packages/com.velvet.core/Runtime/Styling/A.cs", 1, 0,
                                         "<", "<=", "boundary") for _ in range(3)]
        self.plan, segments = mutation_check.session_plan(project, mutants, [1, 2, 3], {}, [], "EditMode",
                                                          100, 900, self.directory)
        self.plan["segments"] = segments
        self.args = argparse.Namespace(unity=mutation_check.DEFAULT_UNITY, timeout=900, busy_timeout=0,
                                       editor_arg=[])
        self.commands, self.answers, self.busy = [], [], []
        self.environments = []
        self.scripts = []
        saved = (mutation_check.launch, mutation_check.unity_busy, mutation_check.wait_for_release,
                 mutation_check.wait_for_quiet)
        mutation_check.launch = self.launch
        mutation_check.wait_for_quiet = lambda _seconds: True
        mutation_check.unity_busy = lambda: self.busy.pop(0) if self.busy else 1
        mutation_check.wait_for_release = lambda _project, _seconds: True
        self.addCleanup(self.restore, saved)

    @staticmethod
    def restore(saved):
        (mutation_check.launch, mutation_check.unity_busy, mutation_check.wait_for_release,
         mutation_check.wait_for_quiet) = saved

    def progress(self, position, phase="running", age=0):
        (self.directory / "runner-progress.json").write_text(json.dumps(
            {"position": position, "id": 0, "stage": 0, "phase": phase, "since": time.time() - age}))

    # A group no process holds, so a settle over it returns at once.
    GROUP = 2 ** 22 + 7

    def launch(self, command, _timeout, _holder, _env, expired):
        self.commands.append(command)
        self.environments.append(_env)
        self.log = Path(command[command.index("-logFile") + 1])
        # A script returns (timed out, what the editor printed) where the launch was not a plain one.
        timed_out, printed = self.scripts.pop(0)(expired) or (False, "")
        return 0.0, timed_out, 0, printed, self.GROUP

    def stopped(self, expired):
        self.log.write_text(mutation_check.BUILD_SYSTEM_FAILURE + " Bee exited\n")

    def finishes(self, expired):
        self.progress(len(self.plan["items"]))
        (self.directory / "runner-done").write_text("")

    def session(self):
        return mutation_check.run_session(self.args, Path("/p"), self.plan, self.directory, None)

    def test_Given_AReachPlan_When_Launched_Then_TheReachVariableIsSet(self):
        # Arrange
        self.plan["reach"] = True
        self.scripts = [self.finishes]

        # Act
        self.session()

        # Assert
        self.assertEqual(self.environments[0].get(mutation_check.SESSION_SWITCH + mutation_check.REACH_SUFFIX), "1")

    def test_Given_AnOrdinaryPlanBesideAnInheritedReachVariable_When_Launched_Then_TheLaunchDoesNotCarryIt(self):
        # Arrange — a campaign started from a shell that had it set.
        os.environ[mutation_check.SESSION_SWITCH + mutation_check.REACH_SUFFIX] = "1"
        self.addCleanup(os.environ.pop, mutation_check.SESSION_SWITCH + mutation_check.REACH_SUFFIX, None)
        self.scripts = [self.finishes]

        # Act
        self.session()

        # Assert
        self.assertNotIn(mutation_check.SESSION_SWITCH + mutation_check.REACH_SUFFIX, self.environments[0])

    def test_Given_AnItemWhoseEditorDied_When_TheSessionGoesOn_Then_ItIsLostAndTheRestRunInANewLaunch(self):
        # Arrange — the first launch dies in the second mutant's item.
        def dies(expired):
            self.progress(2)
        self.scripts = [dies, self.finishes]

        # Act
        runs, lost, _ = self.session()

        # Assert
        self.assertEqual((sorted(lost), {position: run["launch"] for position, run in runs.items()}),
                         ([2], {0: 1, 1: 1, 3: 2}))

    def leaves_the_tree_changed(self, complete):
        def leaks(expired):
            self.progress(1)
            (self.directory / "runner-leaked").write_text(json.dumps(
                {"position": 1, "complete": complete, "changed": ["ProjectSettings/VelvetBuildSettings.json"]}))
        return leaks

    def test_Given_AWholeItemThatLeftTheTreeChanged_When_TheSessionGoesOn_Then_ItStandsAndTheRestRelaunch(self):
        # Arrange
        self.scripts = [self.leaves_the_tree_changed(complete=True), self.finishes]

        # Act
        with contextlib.redirect_stdout(io.StringIO()):
            runs, lost, _ = self.session()

        # Assert
        self.assertEqual((sorted(lost), {position: run["launch"] for position, run in runs.items()}),
                         ([], {0: 1, 1: 1, 2: 2, 3: 2}))

    # GREEN_ON_BASE(characterization): the base loses an item its launch ended in; this holds that a
    # changed tree reported before its last stage does not make it stand.
    def test_Given_AnItemThatLeftTheTreeChangedBeforeItsLastStage_When_TheSessionGoesOn_Then_ItIsLost(self):
        # Arrange
        self.scripts = [self.leaves_the_tree_changed(complete=False), self.finishes]

        # Act
        with contextlib.redirect_stdout(io.StringIO()):
            runs, lost, _ = self.session()

        # Assert
        self.assertEqual((sorted(lost), {position: run["launch"] for position, run in runs.items()}),
                         ([1], {0: 1, 2: 2, 3: 2}))

    def test_Given_AStageOlderThanItsBound_When_Asked_Then_ItHasExpired(self):
        # Arrange — the plan's stage bound plus the slack, and a second past it.
        bound = self.plan["items"][0]["stages"][0]["bound"] + mutation_check.SESSION_STAGE_SLACK
        def overdue(expired):
            self.progress(0, age=bound + 1)
            self.answers.append(expired())
            self.finishes(expired)
        self.scripts = [overdue]

        # Act
        self.session()

        # Assert
        self.assertEqual(self.answers, [True])

    def test_Given_AStageInsideItsBound_When_Asked_Then_ItHasNotExpired(self):
        # Arrange
        def young(expired):
            self.progress(0, age=1)
            self.answers.append(expired())
            self.finishes(expired)
        self.scripts = [young]

        # Act
        self.session()

        # Assert
        self.assertEqual(self.answers, [False])

    def test_Given_AReloadThatNeverCompletes_When_ItsBoundPasses_Then_TheSessionIsAbandoned(self):
        # Arrange — asked for, and past SESSION_RELOAD_BOUND though well inside the stage's own bound.
        def stuck(expired):
            self.progress(1, phase="start", age=mutation_check.SESSION_RELOAD_BOUND + 1)
            expired()
        self.scripts = [stuck, self.finishes]

        # Act
        _, _, stopped = self.session()

        # Assert — and no second launch was made.
        self.assertEqual((len(self.commands), "reload" in (stopped or "")), (1, True))

    def test_Given_ARunnerThatFailedClosed_When_TheLaunchEnds_Then_TheSessionIsAbandoned(self):
        # Arrange
        def fails(expired):
            self.progress(0)
            (self.directory / "runner-failed").write_text("no holder")
        self.scripts = [fails, self.finishes]

        # Act
        _, _, stopped = self.session()

        # Assert
        self.assertEqual((len(self.commands), "no holder" in (stopped or "")), (1, True))

    def test_Given_AnotherEditorDuringOneItem_When_TheSessionEnds_Then_OnlyThatItemCarriesIt(self):
        # Arrange — unity_busy counts the session's own editor too, so 2 is one neighbour.
        def neighbour(expired):
            self.busy = [2, 1]
            self.progress(1)
            expired()
            self.progress(2)
            expired()
            self.finishes(expired)
        self.scripts = [neighbour]

        # Act
        runs, _, _ = self.session()

        # Assert
        self.assertEqual({position: run["peak"] for position, run in runs.items()}, {0: 0, 1: 1, 2: 0, 3: 0})

    def test_Given_AMachineThatStaysBusy_When_ASessionWouldLaunch_Then_NoEditorIsStarted(self):
        # Arrange — a later segment's launch waits for the machine as the first one did.
        mutation_check.wait_for_quiet = lambda _seconds: False
        self.scripts = [self.finishes]

        # Act
        _, _, stopped = self.session()

        # Assert
        self.assertEqual((self.commands, "still in flight" in (stopped or "")), ([], True))

    def test_Given_ALaunchTheBuildSystemStopped_When_TheSessionGoesOn_Then_ItIsLaunchedAgain(self):
        # Arrange — stopped before the runner wrote anything, as run_suite's own launches can be.
        self.scripts = [self.stopped, self.finishes]

        # Act
        runs, _, failure = self.session()

        # Assert
        self.assertEqual((len(self.commands), failure, sorted(runs)), (2, None, [0, 1, 2, 3]))

    def test_Given_ABuildSystemStopThatAlsoTimedOut_When_TheLaunchEnds_Then_TheSessionIsGivenUpUnrelaunched(self):
        # Arrange — a relaunch after a timeout would charge the session a second full bound.
        def stopped_late(expired):
            self.stopped(expired)
            return True, ""
        self.scripts = [stopped_late, self.finishes]

        # Act
        _, _, failure = self.session()

        # Assert
        self.assertEqual((len(self.commands), "build system" in (failure or "")), (1, True))

    def test_Given_TheBuildSystemStoppingEveryLaunch_When_TheSessionRetries_Then_ItStopsAtTheAttempts(self):
        # Arrange
        self.scripts = [self.stopped] * (mutation_check.BUILD_SYSTEM_ATTEMPTS + 1)

        # Act
        _, _, failure = self.session()

        # Assert
        self.assertEqual((len(self.commands), "build system" in (failure or "")),
                         (mutation_check.BUILD_SYSTEM_ATTEMPTS, True))

    def test_Given_ALaunchTheBuildSystemStopped_When_ItIsRelaunched_Then_ItsGroupIsSettledFirst(self):
        # Arrange — what the stopped launch left running would hold the project against the relaunch.
        settled = []
        saved = mutation_check.settle
        mutation_check.settle = lambda group, seconds: settled.append((group, seconds))
        self.addCleanup(setattr, mutation_check, "settle", saved)
        self.scripts = [self.stopped, self.finishes]

        # Act
        self.session()

        # Assert
        self.assertEqual(settled, [(self.GROUP, mutation_check.BUILD_SYSTEM_SETTLE)])

    def test_Given_ALaunchRefusedForTheLock_When_TheSessionGoesOn_Then_ItIsLaunchedAgain(self):
        # Arrange
        def refused(expired):
            return False, mutation_check.LOCK_REFUSAL
        self.scripts = [refused, self.finishes]

        # Act
        _, _, failure = self.session()

        # Assert
        self.assertEqual((len(self.commands), failure), (2, None))

    def test_Given_ARelaunchAfterABuildSystemStop_When_ItHasNotYetWrittenProgress_Then_ItHasItsOwnFullBound(self):
        # Arrange — the first launch used most of the bound before the build system stopped it.
        clock = [1000.0]
        saved = mutation_check.time
        mutation_check.time = argparse.Namespace(time=lambda: clock[0], sleep=lambda _seconds: None,
                                                 strftime=saved.strftime)
        self.addCleanup(setattr, mutation_check, "time", saved)
        def stopped_late(expired):
            clock[0] += self.args.timeout - 10
            self.stopped(expired)
        def still_compiling(expired):
            clock[0] += 20
            self.answers.append(expired())
            self.finishes(expired)
        self.scripts = [stopped_late, still_compiling]

        # Act
        self.session()

        # Assert
        self.assertEqual(self.answers, [False])

    def test_Given_AStaleSessionLogFromAnEarlierRun_When_TheLaunchWritesNone_Then_ItIsNotReadAsThisLaunchs(self):
        # Arrange — an earlier campaign over the same output left a build-system line in the log.
        stale = self.directory / "session-000.log"
        stale.write_text(mutation_check.BUILD_SYSTEM_FAILURE + " Bee exited\n")
        self.scripts = [lambda expired: None, self.finishes]

        # Act
        _, _, failure = self.session()

        # Assert — read as a launch that never started, not as one the build system stopped.
        self.assertEqual((len(self.commands), "first stage" in (failure or "")), (1, True))

    def test_Given_TheSessionsOwnLaunch_When_ABusyCountReadsIt_Then_ItIsCounted(self):
        # Arrange — a session the campaign's own wait could not see would share the machine with the next.
        self.scripts = [self.finishes]

        # Act
        self.session()

        # Assert
        self.assertIsNotNone(re.match(mutation_check.UNITY_RUNNING, " ".join(self.commands[0])))


class SessionConfirmationTests(unittest.TestCase):
    """Which confirmation a kill stands on: its own segment's, run in its own launch."""

    def setUp(self):
        self.directory = Path(tempfile.mkdtemp(prefix="session-confirm-"))
        project = Path("/p")
        count = mutation_check.SESSION_MUTANTS + 1
        mutants = [mutation_check.Mutant(project / "Packages/com.velvet.core/Runtime/Styling/A.cs", 1, 0,
                                         "<", "<=", "boundary") for _ in range(count)]
        self.plan, _ = mutation_check.session_plan(project, mutants, list(range(1, count + 1)), {}, [],
                                                   "EditMode", 100, 900, self.directory)
        # The first segment's confirmation failed the case, the second's passed it.
        for number, result in ((-1, "Failed"), (-2, "Passed")):
            xml = self.directory / "confirm{}.xml".format(number)
            xml.write_text('<test-run><test-case fullname="N.C.Kills" result="{}" /></test-run>'.format(result))
            (self.directory / "item-{}.json".format(mutation_check.cs_id(number))).write_text(json.dumps(
                {"id": number, "stages": [{"name": "confirm", "finished": True, "xml": str(xml)}]}))
        self.found = mutation_check.confirmations(self.plan, self.directory)

    def test_Given_AKillInTheFirstSegment_When_ItsConfirmationIsRead_Then_ItIsItsOwnSegments(self):
        # Arrange — every item carried by the one launch.
        runs = {position: {"launch": 1, "peak": 0} for position in range(len(self.plan["items"]))}

        # Act
        confirmed = mutation_check.confirmed_for(self.plan, 0, runs, self.found)

        # Assert — the second segment's pass does not clear the first's failure.
        self.assertEqual(confirmed, {"N.C.Kills": False})

    def test_Given_AConfirmationFromALaterLaunch_When_ItsKillIsRead_Then_NothingConfirmsIt(self):
        # Arrange — the item's editor died after it, and its segment's confirmation ran in the next one.
        closing = next(position for position, item in enumerate(self.plan["items"]) if item.get("confirm"))
        runs = {position: {"launch": 1 if position < 3 else 2, "peak": 0} for position in range(closing + 1)}
        found = {closing: {"N.C.Kills": True}}

        # Act
        confirmed = mutation_check.confirmed_for(self.plan, 0, runs, found)

        # Assert
        self.assertEqual(confirmed, {})


# A launch's log as an editor writes it when the test runner has started and the editor then dies.
RUNNER_LINE = "Running tests for ExecutionSettings with details:\n"
FATAL_LINE = "Caught fatal signal - signo:11 code:1 errno:0 addr:0x110\n"
SOFT_OVERFLOW_LINE = "Stack overflow in unmanaged: IP: 0x41566822, fault addr: 0x7fff2b854ff8\n"
CRASHED_LOG = "editor log\n" + RUNNER_LINE + "StackOverflowException: The requested operation caused a stack overflow.\n" + FATAL_LINE


class CrashingCampaign(StubbedCampaign):
    """A campaign whose mutant launches leave no result and `log_text` as their log."""

    def __init__(self, log_text, body=None):
        super().__init__(body)
        self.log_text = log_text
        self.results_text = None

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        if Path(results).name == "baseline.xml":
            return super().run_suite(unity, project, platform, scope, results, log, timeout, holder)
        if self.not_rebuilt:
            (self.project / "Library" / "ScriptAssemblies" / "None.dll").write_bytes(b"same")
        Path(log).write_text(self.log_text)
        if self.results_text is not None:
            Path(results).write_text(self.results_text)
        return 0.0, False, 0

    def recorded(self):
        """(exit status, verdict, detail) of a run over the diff."""
        code = self.run_over_diff("--max", "40")
        record = json.loads((self.project / "out" / "mutant-001.json").read_text())
        return code, record.get("verdict"), record.get("detail")


class CrashingAreaCampaign(AreaCampaign):
    """An `AreaCampaign` whose narrowed launch leaves no result and a crashed editor's log."""

    def run_suite(self, unity, project, platform, scope, results, log, timeout, holder=None):
        reading = super().run_suite(unity, project, platform, scope, results, log, timeout, holder)
        if Path(results).name.endswith("-narrowed.xml"):
            Path(results).unlink()
            Path(log).write_text(CRASHED_LOG)
        return reading


class CrashedMutantTests(unittest.TestCase):
    """A mutant whose launch crashed the editor once the tests were running was detected: the baseline
    ran the same suite green. What cannot be laid on the mutant keeps the verdict it had."""

    def test_Given_AWholeSuiteLaunchThatCrashedOnceTheRunnerStarted_When_Recorded_Then_ItIsKilledByTheCrash(self):
        # Arrange
        campaign = CrashingCampaign(CRASHED_LOG)

        # Act
        recorded = campaign.recorded()

        # Assert — the detail is the first line saying so, which is what a reader is shown.
        self.assertEqual(recorded, (0, getattr(mutation_check, "CRASHED", None),
                                    "StackOverflowException: The requested operation caused a stack "
                                    "overflow."))

    def test_Given_AMutantThatCrashedTheEditor_When_TheRunIsDecided_Then_ItIsListedWithTheCrash(self):
        # Arrange — read under its own heading, since the progress line prints the detail as well.
        campaign = CrashingCampaign(CRASHED_LOG)

        # Act
        campaign.run_over_diff("--max", "40")

        # Assert
        listed = campaign.printed.partition("--- mutants killed by crashing the editor ---\n")[2]
        self.assertIn("StackOverflowException", listed.splitlines()[0] if listed else "")

    # GREEN_ON_BASE(characterization): a launch that wrote a passing result is read by it, as on the
    # base. What reddens this on the branch is reading the crash ahead of the result.
    def test_Given_ALaunchThatWroteAPassingResultAndThenCrashed_When_Recorded_Then_ItSurvived(self):
        # Arrange
        campaign = CrashingCampaign(CRASHED_LOG)
        campaign.results_text = GREEN_RESULTS

        # Act
        recorded = campaign.recorded()

        # Assert
        self.assertEqual(recorded[:2], (1, mutation_check.SURVIVED))

    def test_Given_EachLineThatReadsAsACrash_When_TheLogIsRead_Then_ItIsTheCrash(self):
        # Arrange
        lines = ["Caught fatal signal - signo:11 code:1 errno:0 addr:0x297",
                 "Got a SIGSEGV while executing native code. This usually indicates",
                 SOFT_OVERFLOW_LINE.strip(),
                 "Stack overflow: IP: 0x7fc31086b72e, fault addr: 0x7fff2b84cfec",
                 "StackOverflowException: The requested operation caused a stack overflow."]
        with tempfile.TemporaryDirectory() as directory:
            read = []
            for number, line in enumerate(lines):
                log = Path(directory) / "{}.log".format(number)
                log.write_text("editor log\n" + RUNNER_LINE + line + "\n")

                # Act
                read.append(mutation_check.editor_crash(log))

        # Assert
        self.assertEqual(read, lines)

    # GREEN_ON_BASE(characterization): a crash before the runner started stays uncompilable, as on
    # the base. What reddens this on the branch is reading a crash anywhere in the log.
    def test_Given_ACrashBeforeTheRunnerStarted_When_Recorded_Then_ItStaysUnmeasured(self):
        # Arrange — an editor dying in its import or its compile says nothing about the mutant's tests.
        campaign = CrashingCampaign("editor log\n" + FATAL_LINE + RUNNER_LINE)

        # Act
        recorded = campaign.recorded()

        # Assert
        self.assertEqual(recorded[:2], (1, mutation_check.UNCOMPILABLE))

    # GREEN_ON_BASE(characterization): a crash over an assembly the mutation never reached stays
    # uncompilable, as on the base. What reddens this on the branch is leaving out the rebuild check.
    def test_Given_ACrashOverTheUnmutatedAssembly_When_Recorded_Then_ItStaysUnmeasured(self):
        # Arrange
        campaign = CrashingCampaign(CRASHED_LOG)
        campaign.not_rebuilt = True

        # Act
        recorded = campaign.recorded()

        # Assert
        self.assertEqual(recorded[:2], (1, mutation_check.UNCOMPILABLE))

    def test_Given_ANarrowedLaunchThatCrashed_When_Measured_Then_TheCrashIsTheVerdictAndTheWholeSuiteIsNotLaunched(self):
        # Arrange
        campaign = CrashingAreaCampaign()

        # Act
        campaign.run_over_diff()

        # Assert
        self.assertEqual((campaign.launches[-1], campaign.verdict().get("verdict")),
                         ("mutant-001-narrowed.xml", getattr(mutation_check, "CRASHED", None)))

    def test_Given_EachLineAnEditorDiesWith_When_ALaunchReadsIt_Then_EachStopsTheLaunch(self):
        # Arrange — one log per line, each as the CI editors that crashed wrote it.
        lines = ["Caught fatal signal - signo:11 code:1 errno:0 addr:0x297\n",
                 "Got a SIGSEGV while executing native code. This usually indicates\n",
                 "Stack overflow: IP: 0x7fc31086b72e, fault addr: 0x7fff2b84cfec\n"]
        with tempfile.TemporaryDirectory() as directory:
            read = []
            for number, line in enumerate(lines):
                log = Path(directory) / "{}.log".format(number)
                log.write_text("editor log\n" + line)

                # Act
                read.append(mutation_check.fatal_since(log, 0)[0])

        # Assert
        self.assertEqual(read, [True] * len(lines))

    def test_Given_AStackOverflowARunCanGoOnPast_When_ALaunchReadsIt_Then_ItDoesNotStopTheLaunch(self):
        # Arrange — the managed overflow and the unmanaged one, either of which a run that goes on past
        # still writes the result it is read by.
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "run.log"
            log.write_text(CRASHED_LOG.replace(FATAL_LINE, SOFT_OVERFLOW_LINE))

            # Act
            fatal = mutation_check.fatal_since(log, 0)[0]

        # Assert
        self.assertFalse(fatal)

    def test_Given_AnEditorThatCrashesAndDoesNotExit_When_ItIsRun_Then_TheLaunchIsStoppedBeforeItsBound(self):
        # Arrange — the crash handler loops rather than exiting, as on CI.
        body = textwrap.dedent("""\
            open(log, "w").write({!r})
            time.sleep(60)
            """).format(CRASHED_LOG)
        with scripted_editor(body) as (editor, project, root, _):
            # Act
            with contextlib.redirect_stdout(io.StringIO()):
                reading = mutation_check.run_suite(editor, str(project), "EditMode", [],
                                                   root / "results.xml", root / "run.log", 20)

        # Assert — read off the bound rather than the wall clock: a launch left to it timed out.
        self.assertFalse(reading[1])


class EditorCountTests(unittest.TestCase):
    """The editor the CI image runs is counted as one a macOS install runs is."""

    def test_Given_TheLinuxEditor_When_EditorsAreCounted_Then_ItCounts(self):
        # Arrange — the command line the CI image's editor runs under.
        listing = b"/opt/unity/Editor/Unity -runTests -batchmode -projectPath /github/workspace\n"

        # Act
        with listed_processes(listing):
            counted = mutation_check.unity_busy()

        # Assert
        self.assertEqual(counted, 1)

    def test_Given_LinesThatOnlyNameAnEditor_When_EditorsAreCounted_Then_OnlyTheEditorsCount(self):
        # Arrange — the watchdog's line as `ps` prints it, its script on one line ahead of the editor's
        # command, and a shell spelling the editor's path; each beside an editor of the same platform.
        mac = "/Applications/Unity/Hub/Editor/6000.3.23f1/Unity.app/Contents/MacOS/Unity -runTests -batchmode"
        linux = "/opt/unity/Editor/Unity -runTests -batchmode"
        lines = [mac, linux]
        for editor, newline in ((mac, "\\012"), (linux, "?")):
            lines.append("{} -c {} 4242 {}".format(sys.executable, mutation_check.WATCHDOG.replace("\n", newline),
                                                   editor))
            lines.append("/bin/zsh -c {}".format(editor))
        listing = "\n".join(lines).encode() + b"\n"

        # Act
        with listed_processes(listing):
            counted = mutation_check.unity_busy()

        # Assert
        self.assertEqual(counted, 2)

    # GREEN_ON_BASE(construction): the base's three copies agreed too. Give neuter_check.py a copy of
    # its own spelled `"^/Applications/.*/MacOS/Unity -runTest"` and this is what reddens.
    def test_Given_TheThreeBusyCounts_When_TheirPatternsAreRead_Then_TheyAreOne(self):
        # Arrange
        modules = []
        for name in ("neuter_check", "base_red_check"):
            spec = importlib.util.spec_from_file_location(name, Path(__file__).with_name(name + ".py"))
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)
            modules.append(module)

        # Act
        patterns = {module.UNITY_RUNNING for module in [mutation_check, *modules]}

        # Assert
        self.assertEqual(patterns, {mutation_check.UNITY_RUNNING})


class LedgerCampaign(StubbedCampaign):
    """Two lines of one boundary mutant each, killed by the first and the second fixture respectively
    unless `survives` names the mutant, so that a campaign leaves a ledger of known content.

    The editor is stubbed as `StubbedCampaign` stubs it; what differs is that a killed mutant's result
    names the case that failed, the way the editor's does, and that every mutant that reached it is
    recorded in `measured`.
    """

    BODY = textwrap.dedent("""\
        namespace Velvet
        {
            internal static class Probe
            {
                internal static bool Ready(int a, int b) => a <= b;
                internal static bool Other(int a, int b) => a >= b;
            }
        }
        """)
    FIXTURES = {
        "Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs": "ProbeTests",
        "Packages/com.velvet.core/Runtime/Tests/Editor/OtherTests.cs": "OtherTests",
    }
    CASES = {"mutant-001.xml": "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe",
             "mutant-002.xml": "Velvet.Tests.OtherTests.Given_A_When_B_Then_Other"}

    def __init__(self, body=None):
        super().__init__(body)
        self.survives = set()
        # The platforms whose suite kills a mutant, where it is not both.
        self.only_on = {}
        self.measured = []
        self.ledgers = self.project / "ledgers"
        for relative, fixture in self.FIXTURES.items():
            self.write_fixture(relative, fixture, "Given_A_When_B_Then_" + fixture[:-5])

    def write_fixture(self, relative, fixture, method, base=""):
        path = self.project / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text("namespace Velvet.Tests\n{{\n    internal sealed class {}{}\n    {{\n"
                        "        [Test]\n        public void {}() {{ }}\n    }}\n}}\n".format(fixture, base, method))

    def run_suite(self, _unity, _project, platform, _scope, results, log, _timeout, _holder=None):
        name = Path(results).name
        self.measured.append(name)
        Path(log).write_text("")
        if name in self.CASES and name not in self.survives and platform in self.only_on.get(name, (platform,)):
            Path(results).write_text(
                '<test-run total="1" passed="0" failed="1" inconclusive="0">'
                '<test-case fullname="{}" result="Failed" /></test-run>'.format(self.CASES[name]))
        else:
            Path(results).write_text(GREEN_RESULTS)
        return 0.0, False, 0

    def mutants_measured(self):
        return sorted(name for name in self.measured if name.startswith("mutant-"))

    def measure(self, *arguments):
        return self.run_over_diff("--launch-per-mutant", "--shard", "0/1", "--output",
                                  str(self.project / "edit"), *arguments)

    def decide(self, *arguments):
        """The verdict job's invocation, keeping the ledger it writes under `ledgers` as run 9001."""
        with mock.patch.dict(os.environ, {"GITHUB_RUN_ID": "9001"}):
            code = self.run_over_diff("--platform", "PlayMode", "--survivors-of",
                                      str(self.project / "edit"), "--collect", str(self.project / "play"),
                                      "--emit-ledger", str(self.project / "ledger.json"), *arguments)
        return code

    def written_ledger(self):
        """The ledger `decide` wrote, or {} where it wrote none."""
        path = self.project / "ledger.json"
        return json.loads(path.read_text()) if path.exists() else {}

    def keep_ledger(self, run=9001):
        """Files the ledger `decide` wrote where a later plan reads the branch's ledgers from."""
        if not (self.project / "ledger.json").exists():
            return
        self.ledgers.mkdir(exist_ok=True)
        (self.ledgers / "{:010d}-{}.json".format(run, run)).write_text(
            (self.project / "ledger.json").read_text())

    def plan(self, *arguments):
        return self.run_over_diff("--plan", "--reuse-from", str(self.ledgers), "--emit-reuse",
                                  str(self.project / "reuse.json"), *arguments)

    def plan_output(self, name):
        return next((line.split("=", 1)[1] for line in self.printed.splitlines()
                     if line.startswith(name + "=")), None)

    def after_a_campaign(self):
        """One campaign run to its verdict and its ledger kept; the second mutant survives it."""
        self.survives = {"mutant-002.xml"}
        self.measure()
        self.run_over_diff("--launch-per-mutant", "--platform", "PlayMode", "--survivors-of",
                           str(self.project / "edit"), "--shard", "0/1", "--output",
                           str(self.project / "play"))
        self.decide()
        self.keep_ledger()
        self.survives = set()
        self.measured.clear()


class FixtureTraceTests(unittest.TestCase):
    """Which source files a killing case stood on, read off the tree rather than the run."""

    def trace(self, campaign, case):
        return mutation_check.fixture_hashes(campaign.project, case, mutation_check.test_index(campaign.project))

    def test_Given_ACaseOfAFixture_When_Traced_Then_TheFixturesFileIsHashed(self):
        # Arrange
        campaign = LedgerCampaign()

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe")

        # Assert
        self.assertEqual(sorted(traced or {}), ["Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs"])

    def test_Given_ACaseNoFixtureDeclares_When_Traced_Then_ItIsUntraceable(self):
        # Arrange
        campaign = LedgerCampaign()

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Renamed")

        # Assert
        self.assertIsNone(traced)

    def test_Given_ACaseNamedOnlyInAComment_When_Traced_Then_ItIsUntraceable(self):
        # Arrange -- the method was deleted and its name left in prose.
        campaign = LedgerCampaign()
        campaign.write_fixture("Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs", "ProbeTests",
                               "Other")
        path = campaign.project / "Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs"
        path.write_text(path.read_text() + "// Given_A_When_B_Then_Probe() was deleted\n")

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe")

        # Assert
        self.assertIsNone(traced)

    def test_Given_AFixtureDerivedFromABaseInAnotherFile_When_Traced_Then_BothFilesAreHashed(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.write_fixture("Packages/com.velvet.core/Runtime/Tests/Editor/BaseTests.cs", "BaseTests",
                               "Shared")
        campaign.write_fixture("Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs", "ProbeTests",
                               "Given_A_When_B_Then_Probe", " : BaseTests")

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe")

        # Assert
        self.assertEqual(sorted(name.rsplit("/", 1)[1] for name in traced or {}),
                         ["BaseTests.cs", "ProbeTests.cs"])

    def test_Given_AParameterisedCaseWithADottedArgument_When_Traced_Then_ItsFixtureIsFound(self):
        # Arrange -- the argument's dot must not be read as a namespace separator.
        campaign = LedgerCampaign()

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe(1.5,\"a.b\")")

        # Assert
        self.assertEqual(len(traced or {}), 1)

    def test_Given_ACaseOfANestedFixture_When_Traced_Then_TheDeclaringFileIsHashed(self):
        # Arrange
        campaign = LedgerCampaign()
        relative = "Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs"
        (campaign.project / relative).write_text(
            "namespace Velvet.Tests\n{\n    internal sealed class ProbeTests\n    {\n"
            "        internal sealed class Inner\n        {\n            public void Nested() { }\n"
            "        }\n    }\n}\n")

        # Act
        traced = self.trace(campaign, "Velvet.Tests.ProbeTests+Inner.Nested")

        # Assert
        self.assertEqual(sorted(traced or {}), [relative])


class KillStandingTests(unittest.TestCase):
    """Whether a recorded kill still stands: every way the tree can have moved under it."""

    CASE = LedgerCampaign.CASES["mutant-001.xml"]
    PROBE = "Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs"

    def killed(self):
        """(campaign, the mutant, its ledger record) for a kill by the first fixture's case."""
        campaign = LedgerCampaign()
        path = campaign.source.resolve()
        mutant = next(m for m in mutation_check.mutations_for(
            path, path.read_text(), {5}) if m.operator == "boundary")
        mutant.verdict, mutant.killers, mutant.platform = mutation_check.KILLED, (self.CASE,), "EditMode"
        index = mutation_check.test_index(campaign.project)
        record = mutation_check.ledger_record(mutant, campaign.project, index, set(), "9001")
        return campaign, mutant, record

    def problem(self, campaign, mutant, record):
        return mutation_check.kill_problem(record, mutant, campaign.project,
                                           mutation_check.test_index(campaign.project))

    def test_Given_ATreeNothingMovedIn_When_TheKillIsChecked_Then_ItStands(self):
        # Arrange
        campaign, mutant, record = self.killed()

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertEqual((problem, [held["case"] for held in record["killers"]]), (None, [self.CASE]))

    def test_Given_TheKillingFixtureEdited_When_TheKillIsChecked_Then_ItNamesTheFixtureSource(self):
        # Arrange
        campaign, mutant, record = self.killed()
        path = campaign.project / self.PROBE
        path.write_text(path.read_text().replace("{ }", "{ int added = 1; }"))

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertIn("the fixture source " + self.PROBE + " of its killing case ProbeTests.", problem or "")

    def test_Given_ACommentAddedToTheKillingFixture_When_TheKillIsChecked_Then_ItStillStands(self):
        # Arrange
        campaign, mutant, record = self.killed()
        path = campaign.project / self.PROBE
        path.write_text(path.read_text() + "// a note on the case\n")

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertIsNone(problem)

    def test_Given_TheKillingCaseDeleted_When_TheKillIsChecked_Then_ItIsNoLongerInTheTree(self):
        # Arrange
        campaign, mutant, record = self.killed()
        campaign.write_fixture(self.PROBE, "ProbeTests", "SomethingElse")

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertIn("is no longer in the tree", problem or "")

    def test_Given_TheMutatedFileEdited_When_TheKillIsChecked_Then_ItsFileChanged(self):
        # Arrange
        campaign, mutant, record = self.killed()
        campaign.source.write_text(campaign.source.read_text() + "// an edit\n")

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertEqual(problem, "its file changed since the kill was recorded")

    def test_Given_ARecordOfASurvivor_When_TheKillIsChecked_Then_OnlyAKillIsReused(self):
        # Arrange -- a survivor is never turned into a pass.
        campaign, mutant, record = self.killed()
        record["verdict"] = mutation_check.SURVIVED

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertIn("only a kill is reused", problem or "")

    def test_Given_AKillWhoseCasesWereAllUntraceable_When_TheKillIsChecked_Then_ItDoesNotStand(self):
        # Arrange
        campaign, mutant, record = self.killed()
        record["killers"] = []

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertIn("names no killing case", problem or "")

    def test_Given_ARecordMissingItsFields_When_TheKillIsChecked_Then_ItCannotBeRead(self):
        # Arrange
        campaign, mutant, _ = self.killed()

        # Act
        problem = self.problem(campaign, mutant, {"verdict": mutation_check.KILLED})

        # Assert
        self.assertEqual(problem, "the record cannot be read")

    def test_Given_ARecordOfAnotherMutant_When_TheKillIsChecked_Then_ItDoesNotStand(self):
        # Arrange
        campaign, mutant, record = self.killed()
        record["mutant"]["column"] += 1

        # Act
        problem = self.problem(campaign, mutant, record)

        # Assert
        self.assertEqual(problem, "the record is of another mutant")


class ReuseDecisionTests(unittest.TestCase):
    """Which mutants a plan hands to the shards, given the ledgers of the branch's earlier campaigns."""

    def mutants_of(self, campaign):
        path = campaign.source.resolve()
        return mutation_check.one_per_line(mutation_check.mutations_for(
            path, path.read_text(), set(range(1, len(path.read_text().splitlines()) + 1))))

    def ledger(self, campaign, run="9001"):
        return campaign.written_ledger() | {"run": run}

    def test_Given_NoLedger_When_Decided_Then_NothingIsReusedAndEveryMutantSaysWhy(self):
        # Arrange
        campaign = LedgerCampaign()

        # Act
        reused, reasons = mutation_check.decide_reuse(self.mutants_of(campaign), [], campaign.project)

        # Assert
        self.assertEqual((reused, sorted(reasons)), ({}, [1, 2]))

    def test_Given_ALedgerOfAnotherToolVersion_When_Decided_Then_NothingIsReused(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        ledger = self.ledger(campaign) | {"tool": "0" * 64}

        # Act
        reused, reasons = mutation_check.decide_reuse(self.mutants_of(campaign), [ledger], campaign.project)

        # Assert
        self.assertEqual((reused, "another version of the tool" in reasons[1]), ({}, True))

    def test_Given_ALedgerWhereOneMutantWasKilledAndOneSurvived_When_Decided_Then_OnlyTheKillIsReused(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()

        # Act
        reused, reasons = mutation_check.decide_reuse(self.mutants_of(campaign), [self.ledger(campaign)], campaign.project)

        # Assert
        self.assertEqual((sorted(reused), reasons[2]), ([1], "run 9001: it was survived when last measured, "
                                                          "and only a kill is reused"))

    def test_Given_ANewerLedgerThatSawTheMutantSurvive_When_Decided_Then_AnOlderKillIsNotReused(self):
        # Arrange -- the newest ledger that holds the mutant decides it.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        older = self.ledger(campaign, "9000")
        newer = json.loads(json.dumps(older)) | {"run": "9002"}
        for record in newer.get("records", []):
            record["verdict"] = mutation_check.SURVIVED

        # Act
        reused, _ = mutation_check.decide_reuse(self.mutants_of(campaign), [newer, older], campaign.project)

        # Assert
        self.assertEqual(reused, {})

    def test_Given_ANewerLedgerThatLacksTheMutant_When_Decided_Then_AnOlderKillIsReusedFromItsRun(self):
        # Arrange -- a newer campaign that stopped early recorded some mutants only.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        older = self.ledger(campaign, "9000")
        newer = json.loads(json.dumps(older)) | {"run": "9002", "records": []}

        # Act
        reused, _ = mutation_check.decide_reuse(self.mutants_of(campaign), [newer, older], campaign.project)

        # Assert
        self.assertEqual({number: record["reused_from"] for number, record in reused.items()}, {1: "9000"})

    def test_Given_TheMutatedFileEditedSinceTheLedger_When_Decided_Then_EveryMutantInItIsMeasured(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        ledger = self.ledger(campaign)
        campaign.source.write_text(campaign.BODY.replace("a <= b", "a <= b + 1"))

        # Act
        reused, reasons = mutation_check.decide_reuse(self.mutants_of(campaign), [ledger], campaign.project)

        # Assert
        self.assertEqual((reused, "production code or project files" in reasons[1]), ({}, True))


class LedgerContextTests(unittest.TestCase):
    """What lapses every kill of a ledger: a file no record hashes, edited since it was written."""

    SUPPORT = "Packages/com.velvet.core/Runtime/Tests/Editor/Support.cs"

    def reused_after(self, edit, before=None):
        """The mutants reused from a ledger written before `edit` was made to the tree, and the
        reasons the others are measured; `before` shapes the tree the ledger is written over."""
        campaign = LedgerCampaign()
        if before is not None:
            before(campaign)
        campaign.write_fixture(self.SUPPORT, "Support", "Help")
        (campaign.project / self.SUPPORT).write_text("namespace Velvet.Tests\n{\n    internal static class "
                                                     "Support\n    {\n    }\n}\n")
        campaign.after_a_campaign()
        edit(campaign)
        path = campaign.source.resolve()
        mutants = mutation_check.one_per_line(mutation_check.mutations_for(
            path, path.read_text(), set(range(1, len(path.read_text().splitlines()) + 1))))
        ledger = campaign.written_ledger() | {"run": "9001"}
        reused, reasons = mutation_check.decide_reuse(mutants, [ledger], campaign.project)
        return sorted(reused), reasons

    def append(self, relative, text="// edit\nclass Edited { }\n"):
        def edit(campaign):
            path = campaign.project / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text((path.read_text() if path.exists() else "") + text)
        return edit

    def test_Given_NothingEditedSinceTheLedger_When_Decided_Then_TheKillIsReused(self):
        # Act
        reused, _ = self.reused_after(lambda campaign: None)

        # Assert
        self.assertEqual(reused, [1])

    def test_Given_AnotherProductionFileEdited_When_Decided_Then_EveryKillLapses(self):
        # Arrange -- a kill can be reached through a file that holds no mutant.
        edit = self.append("Packages/com.velvet.core/Runtime/Other.cs")

        # Act
        reused, reasons = self.reused_after(edit)

        # Assert
        self.assertEqual((reused, "production code or project files" in reasons[1]), ([], True))

    def test_Given_ASharedTestHelperEdited_When_Decided_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append("Packages/com.velvet.core/TestUtilities/Helper.cs")

        # Act
        reused, reasons = self.reused_after(edit)

        # Assert
        self.assertEqual((reused, "test support files" in reasons[1]), ([], True))

    def test_Given_AnAssemblyDefinitionEdited_When_Decided_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append("Packages/com.velvet.core/Runtime/Velvet.asmdef", " \n")

        # Act
        reused, reasons = self.reused_after(edit)

        # Assert
        self.assertEqual((reused, "production code or project files" in reasons[1]), ([], True))

    def test_Given_ATestsHelperThatDeclaresNoTestEdited_When_Decided_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append(self.SUPPORT, "// a helper changed\nclass Added { }\n")

        # Act
        reused, reasons = self.reused_after(edit)

        # Assert
        self.assertEqual((reused, "test support files" in reasons[1]), ([], True))

    def test_Given_ATestFileOfANonKillingFixtureEdited_When_Decided_Then_TheKillIsStillReused(self):
        # Arrange -- the test-only fix: the survivor's fixture gains a case.
        edit = self.append("Packages/com.velvet.core/Runtime/Tests/Editor/OtherTests.cs", "class Added { }\n")

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [1])

    TESTS = "Packages/com.velvet.core/Runtime/Tests/Editor/"

    def test_Given_ANewFixtureFileAndItsMeta_When_Decided_Then_TheOtherKillsAreStillReused(self):
        # Arrange -- the test-only fix adds a fixture; Unity writes a .cs.meta beside it.
        def edit(campaign):
            self.append(self.TESTS + "NewTests.cs", "class NewTests { [Test] public void Case() { } }\n")(campaign)
            self.append(self.TESTS + "NewTests.cs.meta", "guid: 0123\n")(campaign)

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [1])

    def test_Given_ACommentEditedInASharedHelper_When_Decided_Then_TheKillIsStillReused(self):
        # Arrange
        helper = "Packages/com.velvet.core/TestUtilities/Helper.cs"
        before = self.append(helper, "class Helper { }\n")

        # Act
        reused, _ = self.reused_after(self.append(helper, "// a note\n"), before)

        # Assert
        self.assertEqual(reused, [1])

    def test_Given_AFixtureWhoseAttributeSharesAListOrIsOnlyUnityTest_When_Added_Then_TheKillIsStillReused(self):
        # Arrange -- none of these is support, whatever else the attribute list holds.
        def edit(campaign):
            self.append(self.TESTS + "A.cs", "class A { [Category(\"x\"), Test] void C() { } }\n")(campaign)
            self.append(self.TESTS + "B.cs", "class B { [Timeout(1), UnityTest] void C() { } }\n")(campaign)
            self.append(self.TESTS + "C.cs", "class C { [TestCaseSource(\"s\")] void C() { } }\n")(campaign)
            self.append(self.TESTS + "D.cs", "class D { [NUnit.Framework.Test] void C() { } }\n")(campaign)

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [1])

    def test_Given_ASetUpFixtureFileAdded_When_Decided_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append(self.TESTS + "Setup.cs", "[SetUpFixture] class Setup { [OneTimeSetUp] void S() { } [Test] void T() { } }\n")

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [])

    def test_Given_AHelperWhoseTestAttributeIsCommentedOut_When_Added_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append(self.TESTS + "Helper2.cs", "class Helper2 { // [Test]\n void Help() { } }\n")

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [])

    def test_Given_ATestDataFileEdited_When_Decided_Then_EveryKillLapses(self):
        # Arrange
        edit = self.append(self.TESTS + "data.json", "{}\n")

        # Act
        reused, _ = self.reused_after(edit)

        # Assert
        self.assertEqual(reused, [])

    def test_Given_APlanReusedUnderOneContext_When_TheVerdictReadsATreeWithOtherTestSupport_Then_TheKillLapses(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        reuse = str(campaign.project / "reuse.json")
        campaign.run_over_diff("--launch-per-mutant", "--reuse", reuse, "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))
        helper = campaign.project / "Packages/com.velvet.core/TestUtilities/Helper.cs"
        helper.parent.mkdir(parents=True, exist_ok=True)
        helper.write_text("class Helper { }\n")

        # Act
        code = campaign.decide("--reuse", reuse)

        # Assert
        self.assertEqual((code, mutation_check.REUSE_LAPSED in campaign.printed), (1, True))

    def test_Given_APlanReusedUnderOneContext_When_TheVerdictReadsAnotherTree_Then_TheKillLapses(self):
        # Arrange -- a production file changed between the plan and the verdict.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        reuse = str(campaign.project / "reuse.json")
        campaign.run_over_diff("--launch-per-mutant", "--reuse", reuse, "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))
        other = campaign.project / "Packages/com.velvet.core/Runtime/Other.cs"
        other.write_text("namespace Velvet\n{\n}\n")

        # Act
        code = campaign.decide("--reuse", reuse)

        # Assert
        self.assertEqual((code, mutation_check.REUSE_LAPSED in campaign.printed), (1, True))


class ReusedKillCampaignTests(unittest.TestCase):
    """A campaign that has run once, and the one after it that only changed a test."""

    def test_Given_ATestOnlyFixAfterASurvivor_When_Planned_Then_OnlyTheSurvivorIsLeftToMeasure(self):
        # Arrange -- the second fixture gained the test the survivor asked for.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        path = campaign.project / "Packages/com.velvet.core/Runtime/Tests/Editor/OtherTests.cs"
        path.write_text(path.read_text() + "// the test written for the survivor\n")

        # Act
        campaign.plan()

        # Assert
        self.assertEqual((campaign.plan_output("mutants"), campaign.plan_output("reusing"),
                          campaign.plan_output("shards")), ("2", "1", "[0]"))

    def test_Given_EveryKillReusable_When_Planned_Then_NoShardIsOwed(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.survives = set()
        campaign.measure()
        campaign.decide()
        campaign.keep_ledger()

        # Act
        campaign.plan()

        # Assert
        self.assertEqual((campaign.plan_output("reusing"), campaign.plan_output("shards")), ("2", "[]"))

    def test_Given_AKillOnlyThePlayModeSuiteTook_When_Planned_Then_ItIsReusedToo(self):
        # Arrange -- the EditMode pass left the second mutant surviving and the PlayMode pass killed it.
        campaign = LedgerCampaign()
        campaign.only_on = {"mutant-002.xml": ("PlayMode",)}
        campaign.measure()
        campaign.run_over_diff("--launch-per-mutant", "--platform", "PlayMode", "--survivors-of",
                               str(campaign.project / "edit"), "--shard", "0/1", "--output",
                               str(campaign.project / "play"))
        campaign.decide()
        campaign.keep_ledger()

        # Act
        campaign.plan()

        # Assert
        self.assertEqual((campaign.plan_output("reusing"), campaign.plan_output("shards"),
                          [record["platform"] for record in campaign.written_ledger().get("records", [])]),
                         ("2", "[]", ["EditMode", "PlayMode"]))

    def test_Given_AReusingPlan_When_Printed_Then_ItCountsTheReusedAndNamesWhyTheOtherIsMeasured(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()

        # Act
        campaign.plan()

        # Assert
        self.assertEqual(
            [line for line in campaign.printed.splitlines() if line.startswith(("---", "  Packages"))
             and "unreached" not in line],
            ["--- verdicts reused from earlier campaigns: 1 of 2 ---",
             "--- measured again: 1 of 2 ---",
             "  Packages/com.velvet.core/Runtime/Probe.cs:6 >= -> > (boundary): run 9001: it was survived "
             "when last measured, and only a kill is reused"])

    def test_Given_APlanThatReusedAKill_When_ThePassMeasures_Then_OnlyTheOtherMutantReachesTheEditor(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        campaign.measured.clear()

        # Act
        campaign.run_over_diff("--launch-per-mutant", "--reuse", str(campaign.project / "reuse.json"),
                               "--shard", "0/1", "--output", str(campaign.project / "again"))

        # Assert
        self.assertEqual(campaign.mutants_measured(), ["mutant-002.xml"])

    def test_Given_AReusedKillAndAMeasuredOne_When_Decided_Then_BothAreKilledAndTheRunPasses(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        reuse = str(campaign.project / "reuse.json")
        campaign.run_over_diff("--launch-per-mutant", "--reuse", reuse, "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))

        # Act
        code = campaign.decide("--reuse", reuse)

        # Assert
        self.assertEqual((code, re.findall(r"^killed: \d+$", campaign.printed, re.MULTILINE)),
                         (0, ["killed: 2"]))

    def test_Given_AReusedKillWhoseKillingCaseIsGone_When_Decided_Then_TheRunFailsAndNamesIt(self):
        # Arrange -- the plan reused the kill, and the case was deleted before the verdict read the tree.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        reuse = str(campaign.project / "reuse.json")
        campaign.run_over_diff("--launch-per-mutant", "--reuse", reuse, "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))
        campaign.write_fixture("Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs", "ProbeTests",
                               "SomethingElse")

        # Act
        code = campaign.decide("--reuse", reuse)

        # Assert
        self.assertEqual((code, mutation_check.REUSE_LAPSED in campaign.printed,
                          "its killing case ProbeTests.Given_A_When_B_Then_Probe is no longer in the tree"
                          in campaign.printed), (1, True, True))

    def test_Given_AReuseFileThatRecordsASurvivorAsKilled_When_Decided_Then_TheSurvivorIsNotAPass(self):
        # Arrange -- the second mutant survived, and the file claims a kill for it.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        records = campaign.written_ledger().get("records", [])
        survivor = next((record for record in records if record["verdict"] == mutation_check.SURVIVED), {})
        kill = next((record for record in records if record["verdict"] == mutation_check.KILLED), {})
        reuse = campaign.project / "reuse.json"
        reuse.write_text(json.dumps({"schema": mutation_check.LEDGER_SCHEMA, "reasons": {},
                                     "context": mutation_check.context_digests(campaign.project), "reused": {
            "2": dict(survivor, killers=kill.get("killers"), reused_from="9001")}}))
        campaign.survives = {"mutant-002.xml"}
        campaign.run_over_diff("--launch-per-mutant", "--reuse", str(reuse), "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))

        # Act
        code = campaign.decide("--reuse", str(reuse))

        # Assert
        self.assertEqual((code, mutation_check.REUSE_LAPSED in campaign.printed), (1, True))

    def test_Given_AReuseFileOfAnotherMutantList_When_Read_Then_TheRunRefusesToMeasureAroundIt(self):
        # Arrange -- the first mutant's kill filed under the second mutant's index.
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        kill = next((record for record in campaign.written_ledger().get("records", [])
                     if record["verdict"] == mutation_check.KILLED), {})
        reuse = campaign.project / "reuse.json"
        reuse.write_text(json.dumps({"schema": mutation_check.LEDGER_SCHEMA, "reasons": {},
                                     "context": mutation_check.context_digests(campaign.project),
                                     "reused": {"2": dict(kill, reused_from="9001")}}))

        # Act
        code = campaign.run_over_diff("--launch-per-mutant", "--reuse", str(reuse), "--shard", "0/1",
                                      "--output", str(campaign.project / "again"))

        # Assert
        self.assertIn("is not that mutant in this tree's list", str(code))

    def test_Given_AReusedKill_When_TheLedgerIsWritten_Then_ItCarriesTheRunThatMeasuredIt(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.after_a_campaign()
        campaign.plan()
        reuse = str(campaign.project / "reuse.json")
        campaign.run_over_diff("--launch-per-mutant", "--reuse", reuse, "--shard", "0/1", "--output",
                               str(campaign.project / "edit"))

        # Act
        campaign.decide("--reuse", reuse)

        # Assert
        records = campaign.written_ledger().get("records", [])
        self.assertEqual([(record["verdict"], record["measured_by"]) for record in records],
                         [(mutation_check.KILLED, "9001"), (mutation_check.KILLED, "9001")])

    def test_Given_AMeasuredKill_When_TheLedgerIsWritten_Then_ItNamesTheCaseAndItsFixtureSource(self):
        # Arrange
        campaign = LedgerCampaign()
        campaign.survives = {"mutant-002.xml"}
        campaign.measure()

        # Act
        campaign.decide()

        # Assert
        record = next(iter(campaign.written_ledger().get("records", [])), {})
        self.assertEqual(
            ([held["case"] for held in record.get("killers", [])],
             [sorted(held["files"]) for held in record.get("killers", [])], record.get("platform")),
            (["Velvet.Tests.ProbeTests.Given_A_When_B_Then_Probe"],
             [["Packages/com.velvet.core/Runtime/Tests/Editor/ProbeTests.cs"]], "EditMode"))

    # GREEN_ON_BASE(characterization): a plan asked for no reuse printed none before this change
    # either. The cases above are the ones that separate a plan reading ledgers from one that does not.
    def test_Given_APlanOfALocalRun_When_NoLedgerIsGiven_Then_NoReuseLineIsPrinted(self):
        # Arrange
        campaign = LedgerCampaign()

        # Act
        campaign.run_over_diff("--plan")

        # Assert
        self.assertEqual(("reusing=" in campaign.printed, "verdicts reused" in campaign.printed), (False, False))


class LineRangeTests(unittest.TestCase):
    """`--lines` keeps the mutants on named line ranges of the `--files` sources."""

    def test_Given_ARangeOverFourOfTwelveMethods_When_Planned_Then_OnlyThoseMutantsAreCountedAndEmitted(self):
        # Arrange — the methods sit on lines 5 to 16, one mutant each.
        campaign = StubbedCampaign(body=probe_body(12))
        listed = campaign.project / "list.json"

        # Act
        code = campaign.run("--plan", "--lines", "{}:5-8".format(campaign.source), "--emit-mutants", str(listed))

        # Assert — the emitted list is what each shard hands back to its own tree.
        emitted = json.loads(listed.read_text()) if listed.exists() else []
        self.assertEqual((code, "mutants=4" in campaign.printed, [record["line"] for record in emitted]),
                         (0, True, [5, 6, 7, 8]))

    def test_Given_ARangeOutsideTheFile_When_Planned_Then_ItIsRefused(self):
        # Arrange
        campaign = StubbedCampaign(body=probe_body(12))

        # Act
        code = campaign.run("--plan", "--lines", "{}:5-999".format(campaign.source))

        # Assert
        self.assertIn("outside", str(code))

    def test_Given_ARangeHoldingNoMutant_When_Planned_Then_ItIsRefused(self):
        # Arrange — lines 1 to 4 are the namespace and class braces.
        campaign = StubbedCampaign(body=probe_body(12))

        # Act
        code = campaign.run("--plan", "--lines", "{}:1-4".format(campaign.source))

        # Assert
        self.assertIn("no mutant", str(code))

    def test_Given_ANamedFileWithNoRange_When_Planned_Then_ItIsRefusedRatherThanDroppedUnasked(self):
        # Arrange — the second file would otherwise keep none of its mutants and the run would still plan.
        campaign = StubbedCampaign(body=probe_body(12))
        second = campaign.project / "Packages/com.velvet.core/Runtime/Other.cs"
        second.write_text(probe_body(3))

        # Act
        code = campaign.drive("--files", str(campaign.source), str(second), "--plan",
                              "--lines", "{}:5-8".format(campaign.source))

        # Assert
        self.assertIn("gives no range", str(code))


class KillSetShardTests(unittest.TestCase):
    """`.github/workflows/kill-set.yml`, whose shard job measures one tree and so is sized as
    `ShardCeilingTests` sizes `mutation-shard`'s step."""

    WORKFLOW = REPO_ROOT / ".github/workflows/kill-set.yml"

    def text(self):
        return self.WORKFLOW.read_text()

    def job(self):
        return self.text().partition("\n  shard:")[2].partition("\n  diff:")[0]

    def timeout_flag(self):
        return int(re.search(r"--timeout (\d+)", self.job()).group(1))

    def test_Given_TheKillSetStepTimeout_When_TheMostMutantsFittingAreCounted_Then_ItIsTheCampaignsCeiling(self):
        # Arrange — the campaign's own charges, with the hang allowance (the 900 in its setup) replaced by
        # the --timeout this workflow passes.
        edit = (561, 201 + 486 + 12 + self.timeout_flag())
        play = (934 + 100, 202 + 625 + 11 + self.timeout_flag())
        fits = ShardCeilingTests("fits").fits

        # Act
        largest = max((count for count in range(1, 40)
                       if all(fits("shard", "EditMode", edit[0], edit[1], arm=arm, count=count,
                                   workflow="kill-set.yml", step="Measure")
                              for arm in ("reach", "failed", "small"))), default=0)
        play_fits = fits("shard", "PlayMode", play[0], play[1], workflow="kill-set.yml", step="Measure")

        # Assert
        self.assertEqual((largest, play_fits), (mutation_check.SHARD_CEILING["EditMode"], True))

    def test_Given_TheShardJobsTimeout_When_ComparedWithItsMeasuringStep_Then_ItHoldsTheStepAndTheRest(self):
        # Arrange — one tree a job, so one step; what is left is the checkout, restore and upload.
        job = self.job()
        steps = [int(minutes) for minutes in re.findall(r"^        timeout-minutes: (\d+)$", job, re.MULTILINE)]
        found = re.search(r"^    timeout-minutes: (\d+)$", job, re.MULTILINE)

        # Act
        spare = int(found.group(1)) - sum(steps) if found else None

        # Assert
        self.assertEqual((len(steps), spare is not None and spare >= 15), (1, True))

    def test_Given_TheMeasuringStep_When_ItsTimeoutFlagIsRead_Then_ItIsThreeTimesTheBaselineAtLeast(self):
        # Arrange — a hang reads as one only where the baseline times mutation_check.HANG_MARGIN fits the flag.
        flag = self.timeout_flag()

        # Act
        floor = mutation_check.HANG_MARGIN * ShardCeilingTests.BASELINE

        # Assert
        self.assertGreaterEqual(flag, floor)

    def test_Given_TheWorkflow_When_ItsTriggersAreRead_Then_ItIsDispatchedByHandAndRequiredByNothing(self):
        # Arrange
        block = re.search(r"^on:\n((?:  .*\n|\n)*)", self.text(), re.MULTILINE).group(1)

        # Act
        read = (re.findall(r"^  ([a-z_]+):", block, re.MULTILINE), "Required checks (" in self.text())

        # Assert
        self.assertEqual(read, (["workflow_dispatch"], False))

    def test_Given_TheDiffJob_When_ItsComparisonIsRead_Then_ItReadsTheBaseDirectoryFirst(self):
        # Arrange
        job = self.text().partition("\n  diff:")[2]

        # Act
        read = re.search(r"kill_set_diff\.py (\S+) (\S+)", job).groups()

        # Assert
        self.assertEqual(read, ("Logs/kill-set-base", "Logs/kill-set-head"))


if __name__ == "__main__":
    unittest.main(verbosity=2)

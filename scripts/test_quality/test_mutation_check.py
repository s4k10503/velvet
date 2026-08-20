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

import contextlib
import importlib.util
import io
import json
import os
import re
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest
from pathlib import Path

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


def survivor(path, line, verdict=None):
    mutant = mutation_check.Mutant(path, line, 0, "a", "b", "equality")
    mutant.verdict = verdict or mutation_check.SURVIVED
    return mutant


def declaration(line, category="equivalent", reason="a reason of four words", written_here=True):
    return mutation_check.Declaration(category, reason, line, written_here=written_here)


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

    def test_Given_TheRepositorysOwnSources_When_MutantsAreGenerated_Then_TheTotalHoldsAFloor(self):
        # Arrange — the count is what moved: 2097 across these files before the loop variable stopped
        # rebinding the file's source, and over eleven thousand after. A floor rather than the exact
        # number, which every edit to the package moves.
        sources = [path for path in (RUNTIME).rglob("*.cs")
                   if "/Tests/" not in path.as_posix() and "/Plugins/" not in path.as_posix()]

        # Act
        total = sum(len(mutation_check.mutations_for(
            path, path.read_text(), set(range(1, len(path.read_text().splitlines()) + 1))))
            for path in sources)

        # Assert — the source count rides along because an empty scan clears any floor by arithmetic.
        self.assertEqual((len(sources) > 200, total > 8000), (True, True))


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


class LineRemovalReadingTests(unittest.TestCase):
    """What the removal reads a line as, and what it puts back in place of it.

    The pattern is anchored at a semicolon that ends the line, so read off the raw text it stops at
    anything trailing it — and both a comment and a semicolon inside a literal are things the
    compiler sees no statement boundary in. Read off the mask instead, the shape is the same one
    either way.
    """

    def test_Given_ARemovalTheRepositoryGenerates_When_ACommentIsAppendedToIt_Then_ItStillGenerates(self):
        # Arrange — the package's own removal lines rather than a list of spellings, each given a
        # trailing comment. A line whose removal depends on nothing following the semicolon loses it.
        sources = [path for path in RUNTIME.rglob("*.cs")
                   if "/Tests/" not in path.as_posix() and "/Plugins/" not in path.as_posix()]

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
        self.assertEqual(guards, {"if (ownCts != _cts) return;"})


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


class DeclarationFormatTests(unittest.TestCase):
    """What the guide shows and what the tree holds, against what the script will actually accept.

    A declaration whose category the script refuses is refused by it and reads to everyone else as an
    approved exemption, so the two have to agree.
    """

    def test_Given_TheDeclarationTheGuideShows_When_TheScriptsOwnPatternReadsIt_Then_ItIsAccepted(self):
        # Arrange
        shown = [mutation_check.Declaration(match.group(1), match.group(2).strip(), 0)
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

    def __init__(self, body=None):
        self.holder = tempfile.mkdtemp(prefix="mutation-check-")
        self.project = Path(self.holder)
        self.source = self.project / self.SOURCE
        self.source.parent.mkdir(parents=True, exist_ok=True)
        self.source.write_text(body if body is not None else textwrap.dedent("""\
            namespace Velvet
            {
                internal static class Probe
                {
                    internal static bool Ready(int a, int b) => a <= b;
                }
            }
            """))
        (self.project / "Library" / "ScriptAssemblies").mkdir(parents=True, exist_ok=True)
        self.seen = []
        self.busy = []
        # A repository with one commit that does not hold the source, so the source is untracked and
        # the diff against HEAD takes it whole -- which is what a run not scoped by --files reads.
        for command in (["init", "-q", "-b", "main"], ["add", ".gitignore"],
                        ["-c", "user.email=t@t", "-c", "user.name=t", "commit", "-qm", "base"]):
            (self.project / ".gitignore").write_text("Library/\nout/\n")
            subprocess.run(["git", "-C", str(self.project), *command], capture_output=True)

    def write_receipt(self, verdict):
        """A receipt for exactly this tree, written through the module so the digest cannot drift."""
        since = mutation_check.merge_base_of(self.project, "HEAD")
        digest = mutation_check.scope_digest(since, {self.source: None}, self.project, "EditMode")
        return mutation_check.write_receipt(self.project / "out", digest, "HEAD", verdict, "stub")

    printed = ""
    kills = False
    times_out = False
    no_results = False
    build_error = False
    not_rebuilt = False

    def run_suite(self, _unity, _project, _platform, _scope, results, log, _timeout, _holder=None):
        # The baseline has to be green whatever the mutants do, or the run stops before the loop.
        mutant = Path(results).name != "baseline.xml"
        if mutant and self.no_results:
            Path(log).write_text("")
            return 0.0, False
        if self.not_rebuilt:
            (self.project / "Library" / "ScriptAssemblies" / "None.dll").write_bytes(b"same")
        if mutant and self.times_out:
            return 0.0, True
        Path(results).write_text(FAILING_RESULTS if (mutant and self.kills) else GREEN_RESULTS)
        Path(log).write_text(
            "Packages/com.velvet.core/Runtime/Probe.cs(5,9): error VEL501: too many branches\n"
            if (mutant and self.build_error) else "")
        return 0.0, False

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

    A method written as a run of assignments generates nothing, and the run said only that nothing
    survived. `Generators~/README.md` owns how much of a diff that is, with the window it was
    measured over; a second copy here went stale twice while the guides moved.
    """

    ASSIGNMENTS = textwrap.dedent("""\
        void Settle(int next)
        {
            _phase = Phase.Settled;
            _value = next;
            _error = null;
        }
        """)

    def test_Given_AMethodOfAssignments_When_MutantsAreGenerated_Then_EveryLineIsUnreached(self):
        # Arrange
        numbers = set(range(1, len(self.ASSIGNMENTS.splitlines()) + 1))

        # Act
        reached = {mutant.line for mutant
                   in mutation_check.mutations_for(Path("probe.cs"), self.ASSIGNMENTS, numbers)}
        unreached = [number for number in mutation_check.code_line_numbers(self.ASSIGNMENTS, numbers)
                     if number not in reached]

        # Assert — the signature and the three assignments; the braces are not code lines.
        self.assertEqual(unreached, [1, 3, 4, 5])

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


class ReceiptTests(unittest.TestCase):
    """What asks whether the campaign was run at all.

    The head tree cannot: the campaign diffs the merge base against the **working tree**, so an
    uncommitted edit to a mutated file changes what it measured and moves no tree sha.
    """

    def test_Given_ATreeWithNoMutableChange_When_AReceiptIsAsked_Then_NoneIsOwed(self):
        # Arrange — a documentation or tooling branch has no campaign to run.
        campaign = StubbedCampaign("/// <summary>Nothing here.</summary>\n")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ACampaignThatNeverRan_When_AReceiptIsAsked_Then_ItIsRefused(self):
        # Arrange
        campaign = StubbedCampaign()

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, mutation_check.RECEIPT_REFUSAL)

    def test_Given_APassingCampaign_When_AReceiptIsAsked_Then_ItIsAccepted(self):
        # Arrange
        campaign = StubbedCampaign()
        campaign.write_receipt("pass")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AFailingCampaign_When_AReceiptIsAsked_Then_ItIsRefused(self):
        # Arrange — a campaign that ran and ended with an unanswered survivor is not a campaign that
        # satisfies this; the receipt records the verdict rather than the fact of a run.
        campaign = StubbedCampaign()
        campaign.write_receipt("fail")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, mutation_check.RECEIPT_REFUSAL)

    def test_Given_ACampaignNothingCouldAsk_When_AReceiptIsAsked_Then_ItIsAccepted(self):
        # Arrange — a change no operator reaches cannot earn a passing run, and refusing its pull
        # request outright would leave a one-line behaviour fix with no way through.
        campaign = StubbedCampaign()
        campaign.write_receipt("unreachable")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_AFinishedCampaign_When_AReceiptIsAskedAfterIt_Then_ItsOwnReadingIsAccepted(self):
        # Arrange — the two halves have to agree on the digest, and each computes it separately.
        campaign = StubbedCampaign()
        campaign.kills = True
        campaign.run_over_diff("--max", "40")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_ACampaignWithAnUnansweredSurvivor_When_AReceiptIsAskedAfterIt_Then_ItIsRefused(self):
        # Arrange — the counterpart: a run that happened is not a run that passed, and the receipt
        # records the verdict rather than the fact of a run.
        campaign = StubbedCampaign()
        campaign.run_over_diff("--max", "40")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, mutation_check.RECEIPT_REFUSAL)

    def test_Given_ANarrowedCampaign_When_AReceiptIsAskedAfterIt_Then_NoneCoversTheChange(self):
        # Arrange — a `--filter` run asks whether one fixture notices, and under it nearly everything
        # survives. A receipt from one would sign a branch off against a question nobody asked.
        campaign = StubbedCampaign()
        campaign.kills = True
        campaign.run_over_diff("--max", "40", "--filter", "Velvet.Tests.Probe")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, mutation_check.RECEIPT_REFUSAL)

    def test_Given_APassingCampaign_When_AMutatedFileIsEditedWithoutCommitting_Then_ItIsRefusedAgain(self):
        # Arrange — the reading the head tree sha cannot take: this edit moves no tree sha at all.
        campaign = StubbedCampaign()
        campaign.write_receipt("pass")
        campaign.source.write_text(campaign.source.read_text() + "// an edit after the run\n")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, mutation_check.RECEIPT_REFUSAL)


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

    def test_Given_TheCommitHook_When_ItsSubprocessTimeoutIsRead_Then_ItIsUnderTheRegisteredOne(self):
        # Arrange — the harness kills a hook at its registered timeout, and a subprocess outliving
        # that takes the refusal with it, which is the reading that lets the commit through.
        settings = json.loads((REPO_ROOT / ".claude/settings.json").read_text())
        registered = [held["timeout"] / 1000 for entry in settings["hooks"]["PreToolUse"]
                      for held in entry["hooks"] if self.HOOK.name in held["command"]]
        declared = re.search(r"^CARRIED_TIMEOUT = (\d+)", self.HOOK.read_text(), re.MULTILINE)

        # Assert — the registered count rides along because an empty list satisfies `all`.
        self.assertEqual((len(registered), all(int(declared.group(1)) < each for each in registered)),
                         (1, True))


class CampaignVerdictTests(unittest.TestCase):
    """What the run's own exit status and receipt are a function of.

    Each case here was written after removing the term it names left the whole suite green: the cap,
    the unmeasured mutants, the receipt a change nothing reaches earns, and the merge base in the key.
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

    def test_Given_AMutantTheEditorNeverRan_When_TheRunFinishes_Then_ItFails(self):
        # Arrange — a mutant whose editor was killed at --timeout is one nobody asked about, and a
        # mutation that hangs the suite is indistinguishable from a --timeout that was too short.
        campaign = StubbedCampaign()
        campaign.times_out = True

        # Act
        code = campaign.run_over_diff("--max", "40")

        # Assert
        self.assertEqual(code, 1)

    def test_Given_AMutantTheEditorNeverRebuilt_When_TheRunFinishes_Then_NothingSignsItOff(self):
        # Arrange — the assembly comes out byte-identical, so the suite ran the unmutated binary and
        # answered about nothing. Pre-existing behaviour, and it now decides a receipt as well.
        campaign = StubbedCampaign()
        campaign.not_rebuilt = True

        # Act
        run = campaign.run_over_diff("--max", "40")
        receipt = campaign.run_over_diff("--receipt")

        # Assert — both, because an ordinary unanswered survivor also exits 1: with `kills` off this
        # same mutant reclassifies and the status alone cannot tell the two apart.
        self.assertEqual((run, receipt), (1, mutation_check.RECEIPT_REFUSAL))

    def test_Given_ACampaignNoOperatorReaches_When_ItRefuses_Then_ItStillLeavesAReceipt(self):
        # Arrange — such a branch cannot earn a passing run, so without the receipt it could never
        # open a pull request at all.
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
        campaign.run_over_diff("--max", "40")

        # Act
        code = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual(code, 0)

    def test_Given_TwoMergeBases_When_TheScopeIsDigested_Then_TheKeysDiffer(self):
        # Arrange — the merge base is half the key, and every document leads with it. Without it a
        # receipt earned against one base answers for a branch rebased onto another.
        campaign = StubbedCampaign()
        targets = {campaign.source: None}

        # Act
        keys = {mutation_check.scope_digest(base, targets, campaign.project, "EditMode")
                for base in ("aaaaaaa", "bbbbbbb")}

        # Assert
        self.assertEqual(len(keys), 2)

    def test_Given_TwoPlatforms_When_TheScopeIsDigested_Then_TheKeysDiffer(self):
        # Arrange — a PlayMode campaign is a statement about the PlayMode suite, and a receipt that
        # did not say so would let one sign off a question the other was asked.
        campaign = StubbedCampaign()
        targets = {campaign.source: None}

        # Act
        keys = {mutation_check.scope_digest("aaaaaaa", targets, campaign.project, platform)
                for platform in ("EditMode", "PlayMode")}

        # Assert
        self.assertEqual(len(keys), 2)


class UncompilableMutantTests(unittest.TestCase):
    """A mutant the build rejected is one nobody asked about, and this run must not pass on it.

    It is not a survivor -- no test could fail on a binary that was never produced -- so a reading
    that only counts survivors lets it through, and the receipt it writes says the change was
    measured. That is this file's own thesis, and it was open here: the earlier version of this case
    asserted the passing exit and locked it in.
    """

    def test_Given_AMutantThisRepositorysAnalyzersRejected_When_ItIsDecided_Then_NothingSignsItOff(self):
        # Arrange — VEL501 is this repository's own branching-complexity limit, reported as an error
        # and invisible to a reading that matches `error CS`. The suite writes a green result either
        # way when the build produced no assembly, so without the log this reads as a survivor.
        campaign = StubbedCampaign()
        campaign.build_error = True

        # Act
        run = campaign.run_over_diff("--max", "40")
        receipt = campaign.run_over_diff("--receipt")

        # Assert — both together, because a run that treats this as an ordinary survivor also exits
        # 1, and the receipt is the half `gh pr create` reads.
        self.assertEqual((run, receipt), (1, mutation_check.RECEIPT_REFUSAL))


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

    Four of the six exit 1, so a case asserting the status alone passes with the branch it is named
    for deleted -- all four were green under exactly that perturbation. The tally is read rather than
    the per-mutant progress line, because only the tally spells a verdict followed by a colon.
    """

    def tally_of(self, **how):
        campaign = StubbedCampaign()
        for field, value in how.items():
            setattr(campaign, field, value)
        code = campaign.run_over_diff("--max", "40")
        named = [verdict for verdict in (mutation_check.TIMED_OUT, mutation_check.UNCOMPILABLE,
                                         mutation_check.NOT_BUILT, mutation_check.SURVIVED,
                                         mutation_check.KILLED)
                 if verdict + ":" in campaign.printed]
        return code, named

    def test_Given_AnEditorKilledAtTheTimeout_When_TheRunIsTallied_Then_ItNamesTheTimeout(self):
        # Act / Assert
        self.assertEqual(self.tally_of(times_out=True), (1, [mutation_check.TIMED_OUT]))

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

    def test_Given_ADeclarationOverALineWhoseMutantDied_When_TheCampaignRuns_Then_NothingSignsItOff(self):
        # Arrange — the declaration answers for a survivor, and the mutant on that line is killed, so
        # it describes a state the tree is no longer in.
        campaign = StubbedCampaign(self.DECLARED_AND_KILLED)
        campaign.kills = True

        # Act
        run = campaign.run_over_diff("--max", "40")
        receipt = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual((run, receipt), (1, mutation_check.RECEIPT_REFUSAL))

    def test_Given_ADeclarationOverASurvivingLine_When_TheCampaignRuns_Then_ItSignsItOff(self):
        # Arrange — the counterpart, so the case above is not passing for a run that refuses any tree
        # carrying a declaration at all.
        campaign = StubbedCampaign(self.DECLARED_AND_KILLED)

        # Act
        run = campaign.run_over_diff("--max", "40")
        receipt = campaign.run_over_diff("--receipt")

        # Assert
        self.assertEqual((run, receipt), (0, 0))


class SignalledCampaignTests(unittest.TestCase):
    """The campaign's own restore-on-signal, rather than the helper's.

    The case next door drives a bespoke script calling `Holder.guard()` itself, so it pins the helper
    and stays green with the call removed from `main`. This runs the campaign, and two things decide
    whether it can tell the difference: the signal has to land while the mutation is on disk, and the
    stubbed editor has to write a results file the baseline can actually parse. Missing either one
    leaves the campaign stopped before the loop with the source untouched, which is the same reading
    a restored tree gives.
    """

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
                return 0.0, False

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
        os.kill(running.pid, signal.SIGTERM)
        running.wait(timeout=60)

        # Assert
        self.assertEqual(campaign.source.read_text(), original)


class MaskRefusalTests(unittest.TestCase):
    def test_Given_ATargetFileTheMaskBlanks_When_TheCampaignStarts_Then_ItRefusesToRun(self):
        # Arrange — a blanked offset generates no mutant, so the campaign would otherwise report the
        # lines under it as having nothing to ask.
        campaign = StubbedCampaign('class Probe { string s = "unterminated;\n int a = 1 <= 2 ? 1 : 2; }\n')

        # Act
        code = campaign.run("--max", "1")

        # Assert
        self.assertIn("swallows code", str(code))


if __name__ == "__main__":
    unittest.main(verbosity=2)

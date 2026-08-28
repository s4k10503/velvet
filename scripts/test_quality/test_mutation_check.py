#!/usr/bin/env python3
"""Unit tests for mutation_check.py's mutant generation, plus guards over this repository's sources.

Generation is separable from the Unity runs that kill or spare a mutant, so it is tested here and runs
without a licence. What a run then reports rests entirely on this half being right: a mutant that comes
back uncompilable is indistinguishable from one nothing tested, so an operator that emits broken C# does
not report a false survivor — it hides a real one behind noise.

Run: python3 scripts/test_quality/test_mutation_check.py
"""

import importlib.util
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUNTIME = REPO_ROOT / "Packages/com.velvet.core/Runtime"


def load_module():
    """Imports mutation_check by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        "mutation_check", Path(__file__).with_name("mutation_check.py")
    )
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


mutation_check = load_module()


def code_parens(fragment):
    """How far the fragment's parentheses are out of balance, counting only what the compiler sees."""
    mask = mutation_check.code_mask(fragment)
    seen = [fragment[offset] for offset in range(len(fragment)) if mask[offset]]
    return seen.count("(") - seen.count(")")


def mutants_of(text, operator=None):
    lines = set(range(1, len(text.splitlines()) + 1))
    found = mutation_check.mutations_for(Path("probe.cs"), text, lines)
    return [mutant for mutant in found if operator is None or mutant.operator == operator]


def applied(text, mutant):
    return mutation_check.apply_mutation(text, mutant).splitlines()[mutant.line - 1].strip()


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
    def test_Given_EveryRuntimeSource_When_MutantsAreGenerated_Then_NoCutSpansAnUnbalancedParenthesis(self):
        # Arrange — an unbalanced cut compiles nowhere, and uncompilable noise hides real survivors.
        sources = [path for path in RUNTIME.rglob("*.cs") if "/Tests/" not in path.as_posix()]

        # Act — counted through the mask rather than over the raw text. A cut carrying
        # `StartsWith("rgb(", ...)` holds a parenthesis the compiler never sees, and reading it raw
        # reports a balanced cut as broken; every operator here reads code the same way.
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


if __name__ == "__main__":
    unittest.main(verbosity=2)

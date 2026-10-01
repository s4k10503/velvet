#!/usr/bin/env python3
"""Unit tests for campaign_carry.py's readings of a C# source and of a case's name.

How a carry turns on those readings is asked of `mutation_check.py --carry-to` in
test_mutation_check.py; what is asked here is each reading over the spellings the repository writes.

Run: python3 scripts/test_quality/test_campaign_carry.py
"""

import importlib.util
import unittest
from pathlib import Path


def load_module():
    spec = importlib.util.spec_from_file_location(
        "campaign_carry", Path(__file__).with_name("campaign_carry.py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


campaign_carry = load_module()

# One spelling of each construct `BEYOND_THE_FILE` names, as a source in this repository writes it.
REACHING = [
    "[assembly: InternalsVisibleTo(\"Velvet.Tests\")]",
    "[module: SkipLocalsInit]",
    "global using NUnit.Framework;",
    "internal static int Twice(this int value) => value;",
    "internal static void Poke([NotNull] this VisualElement element) { }",
    "[SetUpFixture] internal sealed class Setup { }",
    "[InitializeOnLoad] internal static class Hook { }",
    "[InitializeOnLoadMethod] private static void Hook() { }",
    "[RuntimeInitializeOnLoadMethod] private static void Hook() { }",
    "[InitializeOnEnterPlayMode] private static void Hook() { }",
    "[DidReloadScripts] private static void Hook() { }",
    "[ModuleInitializer] internal static void Hook() { }",
    "internal sealed class Importer : AssetPostprocessor { }",
    "[PrebuildSetup(typeof(Setup))] internal sealed class FooTests { }",
    "[PostBuildCleanup(typeof(Setup))] internal sealed class FooTests { }",
]

# What a fixture writes that reaches nothing past its own file.
CONTAINED = [
    "var self = Resolve(this);",
    "Assert.That(this.value, Is.EqualTo(1));",
    "internal sealed class FooTests { }",
    "using NUnit.Framework;",
]


class BeyondTheFileTests(unittest.TestCase):
    def test_Given_EachConstructReachingPastItsFile_When_Read_Then_EveryOneIsFound(self):
        # Act
        found = [bool(campaign_carry.BEYOND_THE_FILE.search(text)) for text in REACHING]

        # Assert
        self.assertEqual(found, [True] * len(REACHING))

    def test_Given_CodeReachingNothingPastItsFile_When_Read_Then_NoneIsFound(self):
        # Act
        found = [bool(campaign_carry.BEYOND_THE_FILE.search(text)) for text in CONTAINED]

        # Assert
        self.assertEqual(found, [False] * len(CONTAINED))


class DeclaredTests(unittest.TestCase):
    def test_Given_EachKindOfTypeDeclaration_When_Read_Then_EachNameIsFound(self):
        # Arrange
        text = ("internal sealed class A { }\nstruct B { }\ninterface IC { }\nenum D { }\n"
                "record E(int X);\nrecord struct F(int X);\nrecord class G(int X);\n"
                "internal delegate void H(int x);\nclass /* note */ I { }\nclass\n    J { }\n")

        # Act
        found = campaign_carry.declared(text)

        # Assert
        self.assertEqual(sorted(found), sorted(list("ABDEFGHIJ") + ["IC"]))

    def test_Given_AGenericConstraintNamingAKeyword_When_Read_Then_NoTypeIsFound(self):
        # Arrange — `class` and `struct` there are constraints, and what follows them is no name.
        text = "void M<T, U>() where T : class where U : struct { }\nvar d = delegate (int x) { };\n"

        # Act
        found = campaign_carry.declared(text)

        # Assert
        self.assertEqual(found, set())


class FixtureOfTests(unittest.TestCase):
    def test_Given_TheShapesARunnerNamesACaseIn_When_Read_Then_EachGivesItsOutermostClass(self):
        # Arrange — a plain case, arguments holding a dot and a parenthesis, a parameterised fixture and
        # a nested one.
        cases = ["Velvet.Tests.FooTests.Given_A_When_B_Then_C",
                 'Velvet.Tests.FooTests.Given_A_When_B_Then_C("a.b)c",2)',
                 "Velvet.Tests.FooTests(1.5).Given_A_When_B_Then_C",
                 "Velvet.Tests.FooTests+Nested.Given_A_When_B_Then_C"]

        # Act
        found = [campaign_carry.fixture_of(case) for case in cases]

        # Assert
        self.assertEqual(found, ["FooTests"] * len(cases))


if __name__ == "__main__":
    unittest.main(verbosity=2)

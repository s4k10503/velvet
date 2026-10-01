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

# One spelling of each read `ACROSS_ASSEMBLIES` names, as a source in this repository writes it.
ACROSS = [
    "foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())",
    "var types = TypeCache.GetTypesWithAttribute<VelvetPreviewAttribute>();",
    "var loaded = Assembly.Load(name);",
    "var loaded = Assembly.LoadFrom(path);",
    "var wanted = CompilationPipeline.GetAssemblies(AssembliesType.Player);",
]

# What reads the assembly a case runs in, or nothing at all.
WITHIN = [
    "var own = typeof(FooTests).Assembly.GetTypes();",
    "var own = Assembly.GetExecutingAssembly();",
    "var cache = new TypeCacheProbe();",
]


class AcrossAssembliesTests(unittest.TestCase):
    def test_Given_EachReadAcrossTheLoadedAssemblies_When_Read_Then_EveryOneIsFound(self):
        # Act
        found = [bool(campaign_carry.ACROSS_ASSEMBLIES.search(text)) for text in ACROSS]

        # Assert
        self.assertEqual(found, [True] * len(ACROSS))

    def test_Given_ReadsOfACasesOwnAssembly_When_Read_Then_NoneIsFound(self):
        # Act
        found = [bool(campaign_carry.ACROSS_ASSEMBLIES.search(text)) for text in WITHIN]

        # Assert
        self.assertEqual(found, [False] * len(WITHIN))


HOOKS = [
    "[InitializeOnLoad] internal static class Boot { }",
    "[UnityEditor.InitializeOnLoadAttribute] internal static class Boot { }",
    "[InitializeOnLoadMethod] private static void Boot() { }",
    "[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] private static void Boot() { }",
    "[InitializeOnEnterPlayMode] private static void Boot() { }",
    "[UnityEditor.Callbacks.DidReloadScripts] private static void Boot() { }",
    "[ModuleInitializer] internal static void Boot() { }",
]


class DomainHookTests(unittest.TestCase):
    def test_Given_EachDomainLoadHookSpelling_When_Read_Then_EveryOneIsFound(self):
        # Act
        found = [bool(campaign_carry.DOMAIN_HOOKS.search(text)) for text in HOOKS]

        # Assert
        self.assertEqual(found, [True] * len(HOOKS))


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


class TopLevelTests(unittest.TestCase):
    def test_Given_TypesNestedAndNot_When_Read_Then_OnlyThoseOutsideAnotherTypeAreFound(self):
        # Arrange — a block namespace, a file-scoped one, and members nested two deep.
        texts = ["namespace A\n{\n    internal sealed class Outer\n    {\n        private sealed class Inner { }\n"
                 "        internal delegate void Handler(int x);\n    }\n    internal enum Mode { X }\n}\n",
                 "namespace A.B;\ninternal static class Holder { private struct Item { class Deeper { } } }\n"]

        # Act
        found = [sorted(campaign_carry.top_level(text)) for text in texts]

        # Assert
        self.assertEqual(found, [["Mode", "Outer"], ["Holder"]])


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

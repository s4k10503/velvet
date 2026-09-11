#!/usr/bin/env python3
"""Unit tests for fixture_filter.py, read through the runner's matching rather than as a string.

A value is right when the runner selects exactly the fixtures its files declare, so the cases that
expect a value hand it to a model of how the runner matches one -- the rule the unity-tests skill
states for a term that selects, which is every term the helper writes: untrimmed, each is an
unanchored regular expression tried against a case's full name and the name of every suite and
assembly above it. The roster the model reads is written out by hand in the runner's spelling, `+`
before a nested fixture included, rather than derived from the C# reading the value comes from.

Run: python3 scripts/test_quality/test_fixture_filter.py
"""

import contextlib
import importlib.util
import io
import re
import shutil
import subprocess
import tempfile
import unittest
from pathlib import Path


def load_module(name):
    """Imports a sibling script by path, since scripts/test_quality is not a package."""
    spec = importlib.util.spec_from_file_location(
        name, Path(__file__).resolve().with_name(name + ".py"))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


fixture_filter = load_module("fixture_filter")

EDITOR = "Packages/com.velvet.core/Runtime/Component/Tests/Editor/"
PLAYER = "Packages/com.velvet.core/Runtime/Component/Tests/PlayMode/"
EDITOR_ASSEMBLY = "Velvet.Tests.Component.Editor.dll"
PLAYER_ASSEMBLY = "Velvet.Tests.Component.PlayMode.dll"


def chain(assembly, fixture):
    """What one case of `fixture` is matched against: an empty name for the root, the assembly, a
    suite per namespace segment, the fixture and the case. The namespace is the name up to its last
    dot, so a nested fixture sits beside its outer class rather than under it."""
    namespace = fixture[:fixture.rindex(".")].split(".")
    suites = [".".join(namespace[:end]) for end in range(1, len(namespace) + 1)]
    return ["", assembly] + suites + [fixture, fixture + ".Given_A_When_B_Then_C"]


ROSTER = [(fixture, chain(assembly, fixture)) for assembly, fixture in (
    (EDITOR_ASSEMBLY, "Velvet.Tests.ButtonPoolHelperTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.LabelPoolHelperTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.SliderPoolHelperTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.TextFieldPoolHelperTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.TogglePoolHelperTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.CommitPhaseStateWriteTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.StackedVariantBehaviorTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.StackedVariantEdgeTests+ElementLocalInner"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.StackedVariantEdgeTests+Panel"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.FocusTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.FocusTestsForPortals"),
    (EDITOR_ASSEMBLY, "Sample.RouteTests"),
    (EDITOR_ASSEMBLY, "Velvet.Sample.RouteTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.ScrollHeirTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.PointerTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.ThemeTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.KeyboardTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.GamepadTests"),
    (EDITOR_ASSEMBLY, "Velvet.Tests.WirelessGamepadTests"),
    (PLAYER_ASSEMBLY, "Velvet.Tests.PlaybackTests"),
)]

POOL_FIXTURES = ["Velvet.Tests.ButtonPoolHelperTests", "Velvet.Tests.LabelPoolHelperTests",
                 "Velvet.Tests.SliderPoolHelperTests", "Velvet.Tests.TextFieldPoolHelperTests",
                 "Velvet.Tests.TogglePoolHelperTests"]


def selected(value, roster=ROSTER):
    """The fixtures with a case the runner would run under `value`."""
    terms = [term for term in value.split(";") if term]
    return sorted({fixture for fixture, names in roster
                   if any(re.search(term, name) for term in terms for name in names)})


def fixture(name, body="", base=None, attributes=""):
    heading = "{}internal sealed class {}{}".format(
        attributes, name, " : " + base if base else "")
    return "    {}\n    {{\n{}    }}\n".format(heading, body)


CASE = "        [Test]\n        public void Given_A_When_B_Then_C() => Assert.Pass();\n"


def source(*declarations, namespace="Velvet.Tests"):
    return "using NUnit.Framework;\n\nnamespace {}\n{{\n{}}}\n".format(
        namespace, "\n".join(declarations))


POOL_BASE = source("""    public abstract class PoolHelperTestsBase<TElement> where TElement : class
    {
        [Test]
        public void Given_A_When_B_Then_C() => Assert.Pass();
    }
""")

WIDGETS = source(*(fixture(name, CASE, "PoolHelperTestsBase<{}>".format(element))
                   for name, element in (("ButtonPoolHelperTests", "Button"),
                                         ("LabelPoolHelperTests", "Label"),
                                         ("SliderPoolHelperTests", "Slider"),
                                         ("TextFieldPoolHelperTests", "TextField"),
                                         ("TogglePoolHelperTests", "Toggle"))))

NESTED = source(fixture("StackedVariantBehaviorTests", CASE), """    internal static class StackedVariantEdgeTests
    {
        internal sealed class ElementLocalInner
        {
            [Test]
            public void Given_A_When_B_Then_C() => Assert.Pass();
        }

        internal sealed class Panel
        {
            [Test]
            public void Given_A_When_B_Then_C() => Assert.Pass();
        }
    }
""")


class Project:
    """A scratch repository holding test files where the reader looks for them."""

    def __init__(self, files):
        self.root = Path(tempfile.mkdtemp(prefix="velvet-fixture-filter-"))
        for relative, text in files.items():
            path = self.root / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        for arguments in (["init", "-q"], ["add", "-A"]):
            subprocess.run(["git", "-C", str(self.root)] + arguments, check=True,
                           capture_output=True)

    def value(self, *relatives, platform=None):
        """(exit code, stdout, stderr) for one invocation over these files."""
        argv = ["--project", str(self.root)] + list(relatives)
        if platform:
            argv += ["--platform", platform]
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            code = fixture_filter.main(argv)
        return code, out.getvalue().strip(), err.getvalue()

    def close(self):
        shutil.rmtree(self.root, ignore_errors=True)


class FixtureFilterTests(unittest.TestCase):
    def project(self, files):
        made = Project(files)
        self.addCleanup(made.close)
        return made

    def test_Given_AFileDeclaringFiveFixtures_When_ItsValueIsMatched_Then_TheRunnerSelectsExactlyThose(self):
        # Arrange
        project = self.project({EDITOR + "WidgetPoolHelperTests.cs": WIDGETS,
                                EDITOR + "PoolHelperTestsBase.cs": POOL_BASE})

        # Act
        _, value, _ = project.value(EDITOR + "WidgetPoolHelperTests.cs")

        # Assert
        self.assertEqual(selected(value), POOL_FIXTURES)

    def test_Given_AFileWhoseNameNamesNoClass_When_ItsValueIsMatched_Then_TheRunnerSelectsWhatItDeclares(self):
        # Arrange
        project = self.project({EDITOR + "CommitPhaseEdgeCaseTests.cs": source(
            fixture("CommitPhaseStateWriteTests", CASE))})

        # Act
        _, value, _ = project.value(EDITOR + "CommitPhaseEdgeCaseTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.CommitPhaseStateWriteTests"])

    def test_Given_FixturesNestedInAClassWithNoCase_When_TheirValueIsMatched_Then_TheRunnerSelectsEachOne(self):
        # Arrange
        project = self.project({EDITOR + "StackedVariantEdgeTests.cs": NESTED})

        # Act
        _, value, _ = project.value(EDITOR + "StackedVariantEdgeTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.StackedVariantBehaviorTests",
                                           "Velvet.Tests.StackedVariantEdgeTests+ElementLocalInner",
                                           "Velvet.Tests.StackedVariantEdgeTests+Panel"])

    def test_Given_AnAbstractBaseWhoseHeirsLiveElsewhere_When_ItsValueIsMatched_Then_TheRunnerSelectsEveryHeir(self):
        # Arrange
        project = self.project({EDITOR + "WidgetPoolHelperTests.cs": WIDGETS,
                                EDITOR + "PoolHelperTestsBase.cs": POOL_BASE})

        # Act
        _, value, _ = project.value(EDITOR + "PoolHelperTestsBase.cs")

        # Assert
        self.assertEqual(selected(value), POOL_FIXTURES)

    def test_Given_AnHeirDeclaringNoCaseOfItsOwn_When_ItsValueIsMatched_Then_TheRunnerSelectsIt(self):
        # Arrange -- every case it runs is written in the base, so reading its own file finds none.
        project = self.project({
            EDITOR + "ScrollHeirTests.cs": source(fixture("ScrollHeirTests", "", "PoolHelperTestsBase<Scroller>")),
            EDITOR + "PoolHelperTestsBase.cs": POOL_BASE})

        # Act
        _, value, _ = project.value(EDITOR + "ScrollHeirTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.ScrollHeirTests"])

    def test_Given_AConcreteFixtureAnotherDerivesFrom_When_ItsValueIsMatched_Then_TheRunnerSelectsBoth(self):
        # Arrange
        project = self.project({
            EDITOR + "KeyboardTests.cs": source(fixture("KeyboardTests", CASE)),
            EDITOR + "GamepadTests.cs": source(fixture("GamepadTests", CASE, "KeyboardTests"))})

        # Act
        _, value, _ = project.value(EDITOR + "KeyboardTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.GamepadTests", "Velvet.Tests.KeyboardTests"])

    def test_Given_AFixtureDerivedFromTwoLevelsDown_When_ItsBasesValueIsMatched_Then_TheRunnerSelectsEveryLevel(self):
        # Arrange
        project = self.project({
            EDITOR + "KeyboardTests.cs": source(fixture("KeyboardTests", CASE)),
            EDITOR + "GamepadTests.cs": source(fixture("GamepadTests", CASE, "KeyboardTests")),
            EDITOR + "WirelessGamepadTests.cs": source(fixture("WirelessGamepadTests", "", "GamepadTests"))})

        # Act
        _, value, _ = project.value(EDITOR + "KeyboardTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.GamepadTests", "Velvet.Tests.KeyboardTests",
                                           "Velvet.Tests.WirelessGamepadTests"])

    def test_Given_AClassDerivingFromAConcreteFixtureWithNoCaseOfItsOwn_When_ItsValueIsMatched_Then_TheRunnerSelectsIt(self):
        # Arrange
        project = self.project({
            EDITOR + "KeyboardTests.cs": source(fixture("KeyboardTests", CASE)),
            EDITOR + "GamepadTests.cs": source(fixture("GamepadTests", "", "KeyboardTests"))})

        # Act
        _, value, _ = project.value(EDITOR + "GamepadTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.GamepadTests"])

    def test_Given_AnAbstractClassWithCasesAndNoConcreteHeir_When_ItsValueIsTaken_Then_NoValueIsPrinted(self):
        # Arrange -- a class deriving from it through another abstract one is not read, so the value
        # could be missing the fixture that runs these cases.
        project = self.project({EDITOR + "PoolHelperTestsBase.cs": POOL_BASE})

        # Act
        code, value, _ = project.value(EDITOR + "PoolHelperTestsBase.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_AFixtureWhoseNameBeginsAnothers_When_ItsValueIsMatched_Then_TheOtherIsNotSelected(self):
        # Arrange -- the roster holds `Velvet.Tests.FocusTestsForPortals`.
        project = self.project({EDITOR + "FocusTests.cs": source(fixture("FocusTests", CASE))})

        # Act
        _, value, _ = project.value(EDITOR + "FocusTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.FocusTests"])

    def test_Given_AFixtureWhoseNameEndsAnothers_When_ItsValueIsMatched_Then_TheOtherIsNotSelected(self):
        # Arrange -- the roster holds `Velvet.Sample.RouteTests`, whose tail this one's name is.
        project = self.project({
            EDITOR + "RouteTests.cs": source(fixture("RouteTests", CASE), namespace="Sample")})

        # Act
        _, value, _ = project.value(EDITOR + "RouteTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Sample.RouteTests"])

    def test_Given_SeveralFiles_When_TheirValueIsMatched_Then_TheRunnerSelectsTheFixturesOfEach(self):
        # Arrange
        project = self.project({
            EDITOR + "PointerTests.cs": source(fixture("PointerTests", CASE)),
            EDITOR + "CommitPhaseEdgeCaseTests.cs": source(fixture("CommitPhaseStateWriteTests", CASE))})

        # Act
        _, value, _ = project.value(EDITOR + "PointerTests.cs", EDITOR + "CommitPhaseEdgeCaseTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.CommitPhaseStateWriteTests",
                                           "Velvet.Tests.PointerTests"])

    def test_Given_FilesOnBothPlatforms_When_NoPlatformIsChosen_Then_NoValueIsPrinted(self):
        # Arrange
        project = self.project({EDITOR + "PointerTests.cs": source(fixture("PointerTests", CASE)),
                                PLAYER + "PlaybackTests.cs": source(fixture("PlaybackTests", CASE))})

        # Act
        code, value, _ = project.value(EDITOR + "PointerTests.cs", PLAYER + "PlaybackTests.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_FilesOnBothPlatforms_When_PlayModeIsChosen_Then_TheRunnerSelectsOnlyItsFixtures(self):
        # Arrange
        project = self.project({EDITOR + "PointerTests.cs": source(fixture("PointerTests", CASE)),
                                PLAYER + "PlaybackTests.cs": source(fixture("PlaybackTests", CASE))})

        # Act
        _, value, _ = project.value(EDITOR + "PointerTests.cs", PLAYER + "PlaybackTests.cs",
                                    platform="PlayMode")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.PlaybackTests"])

    def test_Given_AnAbstractBaseOnOnePlatformWithAnHeirOnTheOther_When_ItsValueIsTaken_Then_TheHeirIsNamedForItsOwn(self):
        # Arrange -- the base's cases run in the heir's assembly, which is the other platform's.
        project = self.project({
            PLAYER + "PoolHelperTestsBase.cs": POOL_BASE,
            EDITOR + "ScrollHeirTests.cs": source(fixture("ScrollHeirTests", "", "PoolHelperTestsBase<Scroller>"))})

        # Act
        _, value, _ = project.value(PLAYER + "PoolHelperTestsBase.cs", platform="EditMode")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.ScrollHeirTests"])

    def test_Given_OnlyAFileDeclaringNoFixture_When_ItsValueIsTaken_Then_NothingIsPrintedAndItSaysSo(self):
        # Arrange
        project = self.project({"Packages/com.velvet.core/Runtime/Component/Probe.cs": source()})

        # Act
        code, value, _ = project.value("Packages/com.velvet.core/Runtime/Component/Probe.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.NOTHING_DECLARED, ""))

    def test_Given_AMissingFile_When_ItsValueIsTaken_Then_NoValueIsPrinted(self):
        # Arrange -- skipped instead, a mistyped path beside a real one shrinks the value and the run
        # still goes ahead.
        project = self.project({EDITOR + "PointerTests.cs": source(fixture("PointerTests", CASE))})

        # Act
        code, value, _ = project.value(EDITOR + "PointerTests.cs", EDITOR + "PointerTest.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_AGenericFixture_When_ItsValueIsTaken_Then_NoValueIsPrinted(self):
        # Arrange -- the type argument goes through the named property, which the check for fixture
        # arguments passes, so only the generic declaration can refuse it.
        project = self.project({EDITOR + "BindingTests.cs": source("""    [TestFixture(TypeArgs = new[] { typeof(int) })]
    internal sealed class BindingTests<T>
    {
        [Test]
        public void Given_A_When_B_Then_C() => Assert.Pass();
    }
""")})

        # Act
        code, value, _ = project.value(EDITOR + "BindingTests.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_AGenericClassThatIsNoFixture_When_TheFixtureBesideItIsTaken_Then_TheRunnerSelectsTheFixture(self):
        # Arrange -- a generic helper nested in the fixture is not the fixture.
        project = self.project({EDITOR + "StoreTests.cs": source(fixture(
            "StoreTests", CASE + "\n        private sealed class ToggleStore<T>\n        {\n        }\n"))})

        # Act
        _, value, _ = project.value(EDITOR + "StoreTests.cs")

        # Assert
        self.assertEqual(selected(value, ROSTER + [("Velvet.Tests.StoreTests", chain(
            EDITOR_ASSEMBLY, "Velvet.Tests.StoreTests"))]), ["Velvet.Tests.StoreTests"])

    def test_Given_AFixtureTakingArguments_When_ItsValueIsTaken_Then_NoValueIsPrinted(self):
        # Arrange
        project = self.project({EDITOR + "ThemeTests.cs": source(
            fixture("ThemeTests", CASE, attributes='[TestFixture("dark")]\n    '))})

        # Act
        code, value, _ = project.value(EDITOR + "ThemeTests.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_AnHeirWhoseBaseTakesFixtureArguments_When_ItsValueIsTaken_Then_NoValueIsPrinted(self):
        # Arrange -- written on the base, in another file.
        project = self.project({
            EDITOR + "WidgetPoolHelperTests.cs": WIDGETS,
            EDITOR + "PoolHelperTestsBase.cs": POOL_BASE.replace(
                "    public abstract class", '    [TestFixture("dark")]\n    public abstract class')})

        # Act
        code, value, _ = project.value(EDITOR + "WidgetPoolHelperTests.cs")

        # Assert
        self.assertEqual((code, value), (fixture_filter.CANNOT_ANSWER, ""))

    def test_Given_AFixtureAttributeCarryingOnlyANamedProperty_When_ItsValueIsMatched_Then_TheRunnerSelectsIt(self):
        # Arrange -- a named property is passed over.
        project = self.project({EDITOR + "ThemeTests.cs": source(
            fixture("ThemeTests", CASE, attributes='[TestFixture(Category = "Slow")]\n    '))})

        # Act
        _, value, _ = project.value(EDITOR + "ThemeTests.cs")

        # Assert
        self.assertEqual(selected(value), ["Velvet.Tests.ThemeTests"])


if __name__ == "__main__":
    unittest.main(verbosity=2)

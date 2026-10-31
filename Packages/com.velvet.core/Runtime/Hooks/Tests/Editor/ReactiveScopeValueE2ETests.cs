using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the weaver caches the value a local is given, by counting the calls that compute it and the calls
    /// that build each label's text.
    /// <list type="bullet">
    /// <item>A derived array that cached scopes read as a whole keeps its identity while the state it is computed
    /// from is unchanged, so those scopes are not built again when something else changes.</item>
    /// <item>It is computed again when the state it reads changes, and every label shows the current value either way.</item>
    /// <item>An array the body writes into after computing it is computed on every render that reaches it, since a
    /// cached one would carry the writes of the render before.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeValueE2ETests
    {
        private VisualElement _root = null!;

        private static readonly int[] s_calls = new int[6];
        private static Action<int> s_setFirst = null!;
        private static Action<int> s_setSecond = null!;

        private const int Builds = 0;
        private const int Joins = 1;
        private const int Counts = 2;
        private const int Doubles = 5;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Array.Clear(s_calls, 0, s_calls.Length);
            s_setFirst = null!;
            s_setSecond = null!;
        }

        private static int[] Build(int seed)
        {
            s_calls[Builds]++;
            return new[] { seed, seed + 1 };
        }

        private static string Join(int[] rows)
        {
            s_calls[Joins]++;
            return string.Join(",", rows);
        }

        private static int[] Double(int[] rows)
        {
            s_calls[Doubles]++;
            return rows.Select(row => row * 2).ToArray();
        }

        private static string Count(int[] rows)
        {
            s_calls[Counts]++;
            return rows.Length.ToString();
        }

        private static string Describe(int slot, object value)
        {
            s_calls[slot]++;
            return slot + ":" + value;
        }

        private List<string> LabelTexts() => _root.Query<Label>().ToList().Select(label => label.text).ToList();

        private static void Capture(StateUpdater<int> first, StateUpdater<int> second)
        {
            s_setFirst = first;
            s_setSecond = second;
        }

        #region A derived array read by several scopes

        [Component]
        private static VNode ArrayReadByTwoLabels()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            Capture(setFirst, setSecond);
            var rows = Build(first);
            return V.Div("derived",
                V.Label(text: Join(rows)),
                V.Label(text: Count(rows)),
                V.Label(text: Describe(3, first)),
                V.Label(text: Describe(4, second)));
        }

        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_AnotherStateChanges_Then_TheArrayIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[Builds], Is.EqualTo(1));
        }

        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_AnotherStateChanges_Then_NeitherLabelReadingTheArrayIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[Joins], s_calls[Counts] }, Is.EqualTo(new[] { 1, 1 }));
        }

        // GREEN_ON_BASE(characterization): the base builds the label that reads the state which changed.
        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_AnotherStateChanges_Then_TheLabelReadingItIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[4], Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base computes the array again when the gate on the body misses.
        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_ThatStateChanges_Then_TheArrayIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));

            // Act
            s_setFirst(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[Builds], Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base shows the same texts, built from an array it computes whenever the gate on the body misses.
        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_ThatStateChanges_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));

            // Act
            s_setFirst(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "3,4", "2", "3:3", "4:0" }));
        }

        // GREEN_ON_BASE(characterization): the base shows the same texts, from an array it computes whenever the gate on the body misses.
        [Test]
        public void Given_AnArrayDerivedFromOneStateAndReadByTwoLabels_When_ThatStateChangesAfterAnotherDid_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByTwoLabels, key: "derived"));
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Act
            s_setFirst(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "3,4", "2", "3:3", "4:5" }));
        }

        #endregion

        #region An array derived from another

        [Component]
        private static VNode ArrayDerivedFromAnArray()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            Capture(setFirst, setSecond);
            var rows = Build(first);
            var shifted = second + 1;
            var doubled = Double(rows);
            return V.Div("chain",
                V.Label(text: Join(doubled)),
                V.Label(text: Describe(3, first)),
                V.Label(text: Describe(4, shifted)));
        }

        [Test]
        public void Given_AnArrayDerivedFromAnArrayAndAStatementBetweenThem_When_AnotherStateChanges_Then_NeitherArrayIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayDerivedFromAnArray, key: "chain"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[Builds], s_calls[Doubles] }, Is.EqualTo(new[] { 1, 1 }));
        }

        #endregion

        #region An array the body writes into

        [Component]
        private static VNode ArrayWrittenIntoAfterItIsBuilt()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            Capture(setFirst, setSecond);
            var rows = Build(first);
            rows[0] = second;
            return V.Div("written",
                V.Label(text: Join(rows)),
                V.Label(text: Count(rows)),
                V.Label(text: Describe(3, first)),
                V.Label(text: Describe(4, second)));
        }

        // GREEN_ON_BASE(characterization): the base computes the array whenever the gate on the body misses, so each carries only its own write.
        // A cached array would keep the label that reads it as built, with the value written on an earlier render.
        [Test]
        public void Given_AnArrayTheBodyWritesIntoAfterBuildingIt_When_AnotherStateChanges_Then_TheLabelShowsTheValueWritten()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayWrittenIntoAfterItIsBuilt, key: "written"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts()[0], Is.EqualTo("5,1"));
        }

        // GREEN_ON_BASE(characterization): the base computes the array whenever the gate on the body misses.
        [Test]
        public void Given_AnArrayTheBodyWritesIntoAfterBuildingIt_When_AnotherStateChanges_Then_TheArrayIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayWrittenIntoAfterItIsBuilt, key: "written"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[Builds], Is.EqualTo(2));
        }

        [Test]
        public void Given_AnArrayTheBodyWritesIntoAfterBuildingIt_When_Compiled_Then_TheReportSaysTheArrayIsNotCached()
        {
            // Act
            var report = ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeValueE2ETests), nameof(ArrayWrittenIntoAfterItIsBuilt));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains("is not cached: local") && note.Contains("stelem")), Is.True);
        }

        #endregion

        #region An array read by one label

        [Component]
        private static VNode ArrayReadByOneLabel()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            Capture(setFirst, setSecond);
            var rows = Build(first);
            return V.Div("single",
                V.Label(text: Join(rows)),
                V.Label(text: Describe(3, first)),
                V.Label(text: Describe(4, second)));
        }

        [Test]
        public void Given_AnArrayReadByOneLabel_When_AnotherStateChanges_Then_TheArrayIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ArrayReadByOneLabel, key: "single"));

            // Act
            s_setSecond(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_calls[Builds], Is.EqualTo(1));
        }

        #endregion
    }
}

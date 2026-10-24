using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the weaver cuts a body into scopes that are cached on their own, by counting the calls that
    /// build each label's text and reading the text each label shows.
    /// <list type="bullet">
    /// <item>A change to one state runs only the scopes that read it; a scope that reads a state which did not
    /// change keeps what it built, and every label shows the current value either way.</item>
    /// <item>A scope nested in another is kept while the one around it is built again.</item>
    /// <item>A call whose target the build cannot see, ahead of the last hook, and a hook outside the allow-lists
    /// stop the body as a whole from being keyed, but not a scope that reads neither: only the scope that takes
    /// their result is built on every render.</item>
    /// <item>A scope that reads a stable object a call may have changed is built on every render too.</item>
    /// <item>What was left uncached, and why, is on the method as a <see cref="MemoizationReportAttribute"/>.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each label's text comes from <c>Describe</c>, which counts its calls by slot, so a count says how often the
    /// scope around that call ran. The components are the shapes a body is cut at, not the shapes that decide
    /// whether it has a gate on the whole: a count read here is the same whether the compiler left the hook values
    /// in locals or on the stack.
    /// </remarks>
    [TestFixture]
    internal sealed class ReactiveScopeWovenBehaviorE2ETests
    {
        private VisualElement _root = null!;

        private static readonly int[] s_calls = new int[8];
        private static Action<int> s_setFirst = null!;
        private static Action<int> s_setSecond = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            Array.Clear(s_calls, 0, s_calls.Length);
            s_setFirst = null!;
            s_setSecond = null!;
            s_source.Set("first");
            s_force = null!;
        }

        private static string Describe(int slot, object value)
        {
            s_calls[slot]++;
            return slot + ":" + value;
        }

        private List<string> LabelTexts() => _root.Query<Label>().ToList().Select(label => label.text).ToList();

        private static MemoizationReportAttribute ReportOf(Type fixture, string methodName)
        {
            var method = fixture.GetMethod(methodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return method!.GetCustomAttribute<MemoizationReportAttribute>()!;
        }

        #region Independent states

        [Component]
        private static VNode TwoStates()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            return V.Div("two-states",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, second)),
                V.Label(text: "static"));
        }

        [Test]
        public void Given_TwoStatesReadByDifferentLabels_When_OnlyTheFirstChanges_Then_OnlyTheLabelReadingItIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoStates, key: "two-states"));

            // Act
            s_setFirst(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1] }, Is.EqualTo(new[] { 2, 1 }));
        }

        [Test]
        public void Given_TwoStatesReadByDifferentLabels_When_OnlyTheSecondChanges_Then_OnlyTheLabelReadingItIsBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoStates, key: "two-states"));

            // Act
            s_setSecond(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1] }, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_TwoStatesReadByDifferentLabels_When_OnlyTheFirstChanges_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoStates, key: "two-states"));

            // Act
            s_setFirst(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0:5", "1:0", "static" }));
        }

        [Test]
        public void Given_TwoStatesReadByDifferentLabels_When_BothChangeInTurn_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoStates, key: "two-states"));
            s_setFirst(5);
            mounted.FlushStateForTest();

            // Act
            s_setSecond(7);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0:5", "1:7", "static" }));
        }

        #endregion

        #region Nested scopes

        [Component]
        private static VNode NestedScopes()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            return V.Div("outer",
                V.Div("inner", V.Label(text: Describe(0, first)), V.Label(text: Describe(1, second))),
                V.Label(text: Describe(2, first)));
        }

        [Test]
        public void Given_AScopeNestedInAnother_When_OnlyTheOuterOnesStateChanges_Then_TheInnerScopeKeepsWhatItBuilt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedScopes, key: "nested"));

            // Act
            s_setFirst(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1], s_calls[2] }, Is.EqualTo(new[] { 2, 1, 2 }));
        }

        [Test]
        public void Given_AScopeNestedInAnother_When_OnlyTheOuterOnesStateChanges_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NestedScopes, key: "nested"));

            // Act
            s_setFirst(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0:1", "1:0", "2:1" }));
        }

        #endregion

        #region Locals holding nodes and values

        [Component]
        private static VNode NodesInLocals()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            var header = V.Label(text: Describe(0, first));
            var body = V.Label(text: Describe(1, second));
            return V.Div("locals", header, body);
        }

        [Test]
        public void Given_NodesBuiltIntoLocals_When_OnlyTheSecondChanges_Then_TheNodeReadingTheFirstIsNotBuiltAgain()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NodesInLocals, key: "locals"));

            // Act
            s_setSecond(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1] }, Is.EqualTo(new[] { 1, 2 }));
        }

        #endregion

        #region Closures

        private static readonly List<int> s_clicks = new();

        [Component]
        private static VNode ButtonOverState()
        {
            var (count, setCount) = Hooks.UseState(0);
            var (other, setOther) = Hooks.UseState(0);
            s_setFirst = setCount;
            s_setSecond = setOther;
            return V.Div("closures",
                V.Button(text: Describe(0, count), onClick: () => s_clicks.Add(count)),
                V.Label(text: Describe(1, other)));
        }

        [Test]
        public void Given_AHandlerCapturingAState_When_AnotherStateChanges_Then_TheButtonIsNotBuiltAgain()
        {
            // Arrange
            s_clicks.Clear();
            using var mounted = V.Mount(_root, V.Component(ButtonOverState, key: "closures"));

            // Act
            s_setSecond(4);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1] }, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void Given_AHandlerCapturingAState_When_ThatStateChangesAndTheButtonIsClicked_Then_TheHandlerSeesTheNewValue()
        {
            // Arrange
            s_clicks.Clear();
            using var mounted = V.Mount(_root, V.Component(ButtonOverState, key: "closures"));
            s_setFirst(9);
            mounted.FlushStateForTest();

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert
            Assert.That(s_clicks, Is.EqualTo(new[] { 9 }));
        }

        [Test]
        public void Given_AHandlerCapturingAState_When_AnotherStateChangesAndTheButtonIsClicked_Then_TheHandlerSeesTheValueItWasBuiltWith()
        {
            // Arrange
            s_clicks.Clear();
            using var mounted = V.Mount(_root, V.Component(ButtonOverState, key: "closures"));
            s_setFirst(9);
            mounted.FlushStateForTest();
            s_setSecond(4);
            mounted.FlushStateForTest();

            // Act
            _root.Q<Button>().SimulateClick();

            // Assert
            Assert.That(s_clicks, Is.EqualTo(new[] { 9 }));
        }

        #endregion

        #region A call the build cannot see, ahead of the last hook

        private interface IProvider
        {
            string Name();
        }

        private sealed class ConstantProvider : IProvider
        {
            public string Name() => "provided";
        }

        private static readonly IProvider s_provider = new ConstantProvider();

        [Component]
        private static VNode DispatchAheadOfTheHooks()
        {
            var provided = s_provider.Name();
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            return V.Div("dispatch",
                V.Label(text: Describe(0, provided)),
                V.Label(text: Describe(1, first)),
                V.Label(text: Describe(2, second)));
        }

        [Test]
        public void Given_ADispatchAheadOfTheLastHook_When_AStateChanges_Then_OnlyTheLabelTakingItsResultIsBuiltOnEveryRender()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DispatchAheadOfTheHooks, key: "dispatch"));

            // Act
            s_setFirst(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1], s_calls[2] }, Is.EqualTo(new[] { 2, 2, 1 }));
        }

        [Test]
        public void Given_ADispatchAheadOfTheLastHook_When_AStateChanges_Then_EveryLabelShowsItsCurrentValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DispatchAheadOfTheHooks, key: "dispatch"));

            // Act
            s_setFirst(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0:provided", "1:1", "2:0" }));
        }

        [Test]
        public void Given_ADispatchAheadOfTheLastHook_When_Compiled_Then_TheReportSaysTheBodyIsNotKeyedAsAWhole()
        {
            // Act
            var report = ReportOf(typeof(ReactiveScopeWovenBehaviorE2ETests), nameof(DispatchAheadOfTheHooks));

            // Assert
            Assert.That((report.BodyGate, report.Bailouts.Any(note => note.Contains("cannot see"))),
                Is.EqualTo((false, true)));
        }

        #endregion

        #region A hook outside the allow-lists

        [Component]
        private static VNode UseMutationBesideStates()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            var mutation = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (v, _) => VelvetTask.FromResult(v * 2)));
            return V.Div("mutation",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, mutation.Status)),
                V.Label(text: Describe(2, second)));
        }

        [Test]
        public void Given_AUseMutationResult_When_AnUnrelatedStateChanges_Then_OnlyTheLabelTakingItsResultIsBuiltOnEveryRender()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UseMutationBesideStates, key: "mutation"));

            // Act
            s_setSecond(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1], s_calls[2] }, Is.EqualTo(new[] { 1, 2, 2 }));
        }

        [Test]
        public void Given_AUseMutationResult_When_Compiled_Then_TheReportNamesTheHook()
        {
            // Act
            var report = ReportOf(typeof(ReactiveScopeWovenBehaviorE2ETests), nameof(UseMutationBesideStates));

            // Assert
            Assert.That(report.Bailouts.Any(note => note.Contains(nameof(Hooks.UseMutation))), Is.True);
        }

        #endregion

        #region A stable object a call changes

        private static void Append(List<int> list, int value) => list.Add(value);

        [Component]
        private static VNode ListChangedByACall()
        {
            var (first, setFirst) = Hooks.UseState(0);
            var (second, setSecond) = Hooks.UseState(0);
            s_setFirst = setFirst;
            s_setSecond = setSecond;
            var log = Hooks.UseRef<List<int>>(() => new List<int>());
            Append(log.Current!, first);
            return V.Div("appended",
                V.Label(text: Describe(0, first)),
                V.Label(text: Describe(1, log.Current!.Count)),
                V.Label(text: Describe(2, second)));
        }

        [Test]
        public void Given_AStableListAppendedToByACall_When_AnUnrelatedStateChanges_Then_OnlyTheLabelReadingTheListIsBuiltOnEveryRender()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ListChangedByACall, key: "appended"));

            // Act
            s_setSecond(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(new[] { s_calls[0], s_calls[1], s_calls[2] }, Is.EqualTo(new[] { 1, 2, 2 }));
        }

        [Test]
        public void Given_AStableListAppendedToByACall_When_AnUnrelatedStateChanges_Then_TheLabelShowsTheLengthItNowHas()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ListChangedByACall, key: "appended"));

            // Act
            s_setSecond(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "0:0", "1:2", "2:3" }));
        }

        #endregion

        #region A render that stands for something the body reads some other way

        private sealed class TextSource
        {
            private string _text = "first";

            public string Text => _text;

            public void Set(string text) => _text = text;
        }

        private static readonly TextSource s_source = new();
        private static Action<int> s_force = null!;

        [Component]
        private static VNode ForcedRenderKeptInALocal()
        {
            var (_, force) = Hooks.UseState(0);
            s_force = force;
            return V.Label(text: s_source.Text);
        }

        [Component]
        private static VNode ForcedRenderOffTheStack()
        {
            s_force = Hooks.UseState(0).Item2;
            return V.Label(text: s_source.Text);
        }

        [Test]
        public void Given_AStateWhoseValueIsNeverRead_When_TheSourceChangesAndItsSetterRuns_Then_TheLabelShowsTheNewText()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ForcedRenderKeptInALocal, key: "forced"));
            s_source.Set("second");

            // Act
            s_force(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "second" }));
        }

        [Test]
        public void Given_OnlyTheSetterReadOffTheReturnedTuple_When_TheSourceChangesAndItsSetterRuns_Then_TheLabelShowsTheNewText()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ForcedRenderOffTheStack, key: "forced"));
            s_source.Set("second");

            // Act
            s_force(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "second" }));
        }

        #endregion

        #region The report

        [Test]
        public void Given_TwoStatesReadByDifferentLabels_When_Compiled_Then_TheReportCountsTheScopesCachedOnTheirOwn()
        {
            // Act
            var report = ReportOf(typeof(ReactiveScopeWovenBehaviorE2ETests), nameof(TwoStates));

            // Assert
            Assert.That(report.Scopes, Is.GreaterThanOrEqualTo(3));
        }

        [Test]
        public void Given_AHookInsideALoop_When_Compiled_Then_TheReportSaysWhyNothingIsCached()
        {
            // Act
            var report = ReportOf(typeof(CompilerILPostProcessorE2ETests),
                nameof(CompilerILPostProcessorE2ETests.WhileLoopHookComponent));

            // Assert
            Assert.That((report.BodyGate, report.Scopes, report.Bailouts.Any(note => note.Contains("skipped or repeated"))),
                Is.EqualTo((false, 0, true)));
        }

        #endregion
    }
}

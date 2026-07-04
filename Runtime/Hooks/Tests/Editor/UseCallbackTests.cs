using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseCallback{T}"/> in a function component.
    /// <list type="bullet">
    /// <item>The deps overload returns the same delegate reference across renders while the dependency array stays
    /// equal, and a new delegate when any dependency changes.</item>
    /// <item>Dependencies are compared by reference identity: a fresh-but-content-equal reference-type dependency
    /// counts as changed and yields a new delegate.</item>
    /// <item>The no-deps overload (<c>UseCallback&lt;T&gt;(T)</c>) is unmemoized: it returns a fresh closure on
    /// every render.</item>
    /// <item>A null callback raises an <see cref="ArgumentNullException"/>.</item>
    /// <item>Each call owns an independent slot keyed by call order; slots memoize and invalidate independently.</item>
    /// <item>The returned delegate is invocable and captures the latest committed state.</item>
    /// <item>A render-phase re-run whose discarded attempt swings a dependency away and back to the committed value
    /// returns the same delegate reference (comparison is against the committed render, not a discarded attempt).</item>
    /// <item>Remount allocates a fresh fiber and slot, so the cached callback does not survive unmount.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each test mounts exactly one of the components, which all share the <c>(1, "hello")</c> initial state and the
    /// <c>s_callbackSetState</c> setter. Per-component captures are exposed via static fields reset together in
    /// <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class UseCallbackTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCallback();
        }

        [Test]
        public void Given_UnchangedDeps_When_ReRendered_Then_ReturnsSameDelegateReference()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));
            var first = s_singleLastCallback;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a callback");

            // Act — Count (the only dep) stays the same while Name changes, triggering a re-render
            s_callbackSetState.Invoke(new CallbackState(1, "world"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(first, s_singleLastCallback, "Unchanged deps reuse the cached delegate reference");
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_ReturnsNewDelegateReference()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));
            var first = s_singleLastCallback;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a callback");

            // Act — Count (the dep) changes, invalidating the cache
            s_callbackSetState.Invoke(new CallbackState(2, "hello"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_singleLastCallback, "A changed dep yields a new delegate reference");
        }

        [Test]
        public void Given_RenderPhaseDepOscillation_When_SettledToCommittedDep_Then_KeepsDelegateReference()
        {
            // Arrange — a render-phase setState normalizes the odd phase to the next even phase in one re-run, so the
            // dep swings to "transient" on the discarded attempt and back to the committed "settled" on settle.
            using var mounted = V.Mount(_root, V.Component(RenderPhaseOscillationCallbackRender, key: "osc"));
            var committed = s_oscLastCallback;
            Assume.That(committed, Is.Not.Null, "Precondition: the mount render produced a committed callback");

            // Act
            s_oscSetPhase.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_oscRenderCount, Is.EqualTo(3), "Precondition: 1 mount render + 2 render-phase attempts (phase 1 -> 2)");

            // Assert
            Assert.AreSame(committed, s_oscLastCallback,
                "The delegate reference stays stable when render-phase state oscillates back to the committed dep");
        }

        [Test]
        public void Given_NoDepsOverload_When_ReRendered_Then_ReturnsFreshClosure()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EmptyDepsCallbackRender, key: "empty"));
            var first = s_emptyDepsLastCallback;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a callback");

            // Act
            s_callbackSetState.Invoke(new CallbackState(2, "world"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_emptyDepsLastCallback, "The no-deps overload returns a fresh closure every render");
        }

        [Test]
        public void Given_RecordDep_When_FreshButContentEqualInstance_Then_ReturnsNewDelegate()
        {
            // Arrange — the dep is a record reconstructed with identical content but a new instance every render
            using var mounted = V.Mount(_root, V.Component(RecordDepCallbackRender, key: "record-dep"));
            var first = s_recordDepLastCallback;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a callback");

            // Act
            s_recordDepSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_recordDepLastCallback,
                "A fresh-but-content-equal reference-type dep counts as changed (identity compare), so the delegate is new");
        }

        [Test]
        public void Given_NullCallback_When_NoDepsOverloadCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert — the no-deps overload guards against a null callback
            Assert.Throws<ArgumentNullException>(
                () => Hooks.UseCallback<Func<string>>(null));
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_OnlyTheInvalidatedSlotGetsNewDelegate()
        {
            // Arrange — slot A depends on Count, slot B depends on Name
            using var mounted = V.Mount(_root, V.Component(DualCallbackRender, key: "dual"));
            var firstA = s_dualLastCallbackA;
            var firstB = s_dualLastCallbackB;

            // Act — change Count only (slot A's dep), leaving Name (slot B's dep) untouched
            s_callbackSetState.Invoke(new CallbackState(2, "hello"));
            mounted.FlushStateForTest();
            Assume.That(s_dualLastCallbackA, Is.Not.SameAs(firstA), "Precondition: slot A's changed dep produced a new delegate");

            // Assert
            Assert.AreSame(firstB, s_dualLastCallbackB, "Slot B's unchanged dep keeps its cached delegate independently");
        }

        [Test]
        public void Given_FirstRender_When_CallbackInvoked_Then_ReturnsCapturedState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));

            // Act
            var result = s_singleLastCallback.Invoke();

            // Assert
            Assert.AreEqual("hello", result, "The returned delegate is invocable and reads the initial captured state");
        }

        [Test]
        public void Given_ChangedDeps_When_CallbackInvoked_Then_ReturnsNewlyCapturedState()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));
            s_callbackSetState.Invoke(new CallbackState(2, "world"));
            mounted.FlushStateForTest();

            // Act
            var result = s_singleLastCallback.Invoke();

            // Assert
            Assert.AreEqual("world", result, "A regenerated delegate captures the latest committed state");
        }

        [Test]
        public void Given_UnmountedComponent_When_Remounted_Then_CacheIsReset()
        {
            // Arrange
            var first = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));
            var firstCallback = s_singleLastCallback;
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(SingleCallbackRender, key: "single"));

            // Assert
            Assert.AreNotSame(firstCallback, s_singleLastCallback,
                "Remount allocates a fresh fiber and slot, so the cached callback does not survive unmount");
        }

        internal sealed record CallbackState(int Count, string Name);

        internal sealed record DepRecord(string Value);

        #region Single callback component (UseState + UseCallback; initial state is (1, "hello"))

        private static CallbackState s_callbackInitial;
        private static Action<CallbackState> s_callbackSetState;
        private static Func<string> s_singleLastCallback;
        private static Func<string> s_emptyDepsLastCallback;
        private static Func<string> s_dualLastCallbackA;
        private static Func<string> s_dualLastCallbackB;
        private static int s_oscRenderCount;
        private static Action<int> s_oscSetPhase;
        private static Func<string> s_oscLastCallback;
        private static Action<int> s_recordDepSetTick;
        private static Func<string> s_recordDepLastCallback;

        private static void ResetCallback()
        {
            s_callbackInitial = new CallbackState(1, "hello");
            s_callbackSetState = null;
            s_singleLastCallback = null;
            s_emptyDepsLastCallback = null;
            s_dualLastCallbackA = null;
            s_dualLastCallbackB = null;
            s_oscRenderCount = 0;
            s_oscSetPhase = null;
            s_oscLastCallback = null;
            s_recordDepSetTick = null;
            s_recordDepLastCallback = null;
        }

        [Component]
        private static VNode SingleCallbackRender()
        {
            var (state, setState) = Hooks.UseState(s_callbackInitial);
            s_callbackSetState = setState;
            s_singleLastCallback = Hooks.UseCallback<Func<string>>(
                () => state.Name,
                state.Count);
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode EmptyDepsCallbackRender()
        {
            var (state, setState) = Hooks.UseState(s_callbackInitial);
            s_callbackSetState = setState;
            s_emptyDepsLastCallback = Hooks.UseCallback<Func<string>>(() => state.Name);
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode DualCallbackRender()
        {
            var (state, setState) = Hooks.UseState(s_callbackInitial);
            s_callbackSetState = setState;
            s_dualLastCallbackA = Hooks.UseCallback<Func<string>>(
                () => $"A:{state.Count}",
                state.Count);
            s_dualLastCallbackB = Hooks.UseCallback<Func<string>>(
                () => $"B:{state.Name}",
                state.Name);
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode RecordDepCallbackRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_recordDepSetTick = setTick;
            // A fresh-but-content-equal record instance every render. Under reference-identity deps semantics this is
            // a CHANGED dep (the reference differs), so the callback must be a fresh reference.
            var dep = new DepRecord("constant");
            s_recordDepLastCallback = Hooks.UseCallback<Func<string>>(() => dep.Value, dep);
            return V.Label(text: $"{tick}:{dep.Value}");
        }

        // A render-phase setState normalizes an odd phase to the next even phase in one re-run, so the callback dep
        // swings to "transient" on the discarded attempt and back to the committed "settled" on the settled attempt.
        [Component]
        private static VNode RenderPhaseOscillationCallbackRender()
        {
            s_oscRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_oscSetPhase = setPhase;
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var dep = phase % 2 == 1 ? "transient" : "settled";
            s_oscLastCallback = Hooks.UseCallback<Func<string>>(() => dep, dep);
            return V.Label(text: dep);
        }

        #endregion
    }
}

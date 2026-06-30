using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseMemo{T}"/> in a function component.
    /// <list type="bullet">
    /// <item>The deps overload returns the same computed value across renders while the dependency array stays
    /// equal, and recomputes a new value when any dependency changes.</item>
    /// <item>Dependencies are compared by reference identity: a fresh-but-content-equal reference-type dependency
    /// counts as changed and recomputes.</item>
    /// <item>The no-deps overload (<c>UseMemo&lt;T&gt;(Func&lt;T&gt;)</c>) is unmemoized: it recomputes on every render.</item>
    /// <item>A null factory raises an <see cref="ArgumentNullException"/>.</item>
    /// <item>Each call owns an independent slot keyed by call order; slots memoize and invalidate independently.</item>
    /// <item>The cached value reflects the dependencies captured when it was last recomputed.</item>
    /// <item>A render-phase re-run whose discarded attempt swings a dependency away and back to the committed value
    /// returns the same cached value (comparison is against the committed render, not a discarded attempt).</item>
    /// <item>Remount allocates a fresh fiber and slot, so the cached value does not survive unmount.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Each test mounts exactly one of the components, which share the <c>(1, "hello")</c> initial state and the
    /// <c>s_memoSetState</c> setter. Per-component captures are exposed via static fields reset together in
    /// <see cref="SetUp"/>. The memoized payload is a reference type (<see cref="Computed"/>) so reference identity
    /// distinguishes a memo hit (same instance) from a recompute (new instance).
    /// </remarks>
    [TestFixture]
    internal sealed class UseMemoTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetMemo();
        }

        [Test]
        public void Given_UnchangedDeps_When_ReRendered_Then_ReturnsSameValueReference()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleMemoRender, key: "single"));
            var first = s_singleLastValue;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a value");

            // Act — Count (the only dep) stays the same while Name changes, triggering a re-render
            s_memoSetState.Invoke(new MemoState(1, "world"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(first, s_singleLastValue, "Unchanged deps reuse the cached value reference");
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_RecomputesNewValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleMemoRender, key: "single"));
            var first = s_singleLastValue;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a value");

            // Act — Count (the dep) changes, invalidating the cache
            s_memoSetState.Invoke(new MemoState(2, "hello"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_singleLastValue, "A changed dep recomputes a new value");
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_RecomputedValueReflectsDep()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SingleMemoRender, key: "single"));

            // Act
            s_memoSetState.Invoke(new MemoState(2, "hello"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_singleLastValue.Count, "The recomputed value reflects the latest committed dep");
        }

        [Test]
        public void Given_NoDepsOverload_When_ReRendered_Then_RecomputesEveryRender()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NoDepsMemoRender, key: "nodeps"));
            var first = s_noDepsLastValue;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a value");

            // Act
            s_memoSetState.Invoke(new MemoState(2, "world"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_noDepsLastValue, "The no-deps overload recomputes on every render");
        }

        [Test]
        public void Given_RecordDep_When_FreshButContentEqualInstance_Then_Recomputes()
        {
            // Arrange — the dep is a record reconstructed with identical content but a new instance every render
            using var mounted = V.Mount(_root, V.Component(RecordDepMemoRender, key: "record-dep"));
            var first = s_recordDepLastValue;
            Assume.That(first, Is.Not.Null, "Precondition: the first render produced a value");

            // Act
            s_recordDepSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(first, s_recordDepLastValue,
                "A fresh-but-content-equal reference-type dep counts as changed (identity compare), so the value recomputes");
        }

        [Test]
        public void Given_NullFactory_When_NoDepsOverloadCalled_Then_ThrowsArgumentNullException()
        {
            // Act + Assert — the no-deps overload guards against a null factory
            Assert.Throws<ArgumentNullException>(() => Hooks.UseMemo<object>(null));
        }

        [Test]
        public void Given_ChangedDeps_When_ReRendered_Then_OnlyTheInvalidatedSlotRecomputes()
        {
            // Arrange — slot A depends on Count, slot B depends on Name
            using var mounted = V.Mount(_root, V.Component(DualMemoRender, key: "dual"));
            var firstA = s_dualLastValueA;
            var firstB = s_dualLastValueB;

            // Act — change Count only (slot A's dep), leaving Name (slot B's dep) untouched
            s_memoSetState.Invoke(new MemoState(2, "hello"));
            mounted.FlushStateForTest();
            Assume.That(s_dualLastValueA, Is.Not.SameAs(firstA), "Precondition: slot A's changed dep recomputed");

            // Assert
            Assert.AreSame(firstB, s_dualLastValueB, "Slot B's unchanged dep keeps its cached value independently");
        }

        [Test]
        public void Given_RenderPhaseDepOscillation_When_SettledToCommittedDep_Then_KeepsValueReference()
        {
            // Arrange — a render-phase setState normalizes the odd phase to the next even phase in one re-run, so the
            // dep swings to "transient" on the discarded attempt and back to the committed "settled" on settle.
            using var mounted = V.Mount(_root, V.Component(RenderPhaseOscillationMemoRender, key: "osc"));
            var committed = s_oscLastValue;
            Assume.That(committed, Is.Not.Null, "Precondition: the mount render produced a committed value");

            // Act
            s_oscSetPhase.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_oscRenderCount, Is.EqualTo(3), "Precondition: 1 mount render + 2 render-phase attempts (phase 1 -> 2)");

            // Assert
            Assert.AreSame(committed, s_oscLastValue,
                "The cached value stays stable when render-phase state oscillates back to the committed dep");
        }

        [Test]
        public void Given_UnmountedComponent_When_Remounted_Then_CacheIsReset()
        {
            // Arrange
            var first = V.Mount(_root, V.Component(SingleMemoRender, key: "single"));
            var firstValue = s_singleLastValue;
            first.Dispose();

            // Act
            using var second = V.Mount(_root, V.Component(SingleMemoRender, key: "single"));

            // Assert
            Assert.AreNotSame(firstValue, s_singleLastValue,
                "Remount allocates a fresh fiber and slot, so the cached value does not survive unmount");
        }

        internal sealed record MemoState(int Count, string Name);

        internal sealed record DepRecord(string Value);

        // Reference-type payload so reference identity distinguishes a memo hit from a recompute.
        internal sealed class Computed
        {
            public Computed(int count) => Count = count;

            public int Count { get; }
        }

        #region Components (UseState + UseMemo; initial state is (1, "hello"))

        private static MemoState s_memoInitial;
        private static Action<MemoState> s_memoSetState;
        private static Computed s_singleLastValue;
        private static Computed s_noDepsLastValue;
        private static Computed s_dualLastValueA;
        private static Computed s_dualLastValueB;
        private static int s_oscRenderCount;
        private static Action<int> s_oscSetPhase;
        private static Computed s_oscLastValue;
        private static Action<int> s_recordDepSetTick;
        private static Computed s_recordDepLastValue;

        private static void ResetMemo()
        {
            s_memoInitial = new MemoState(1, "hello");
            s_memoSetState = null;
            s_singleLastValue = null;
            s_noDepsLastValue = null;
            s_dualLastValueA = null;
            s_dualLastValueB = null;
            s_oscRenderCount = 0;
            s_oscSetPhase = null;
            s_oscLastValue = null;
            s_recordDepSetTick = null;
            s_recordDepLastValue = null;
        }

        [Component]
        private static VNode SingleMemoRender()
        {
            var (state, setState) = Hooks.UseState(s_memoInitial);
            s_memoSetState = setState;
            s_singleLastValue = Hooks.UseMemo(() => new Computed(state.Count), state.Count);
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode NoDepsMemoRender()
        {
            var (state, setState) = Hooks.UseState(s_memoInitial);
            s_memoSetState = setState;
            s_noDepsLastValue = Hooks.UseMemo(() => new Computed(state.Count));
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode DualMemoRender()
        {
            var (state, setState) = Hooks.UseState(s_memoInitial);
            s_memoSetState = setState;
            s_dualLastValueA = Hooks.UseMemo(() => new Computed(state.Count), state.Count);
            s_dualLastValueB = Hooks.UseMemo(() => new Computed(state.Name.Length), state.Name);
            return V.Label(text: state.Name);
        }

        [Component]
        private static VNode RecordDepMemoRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_recordDepSetTick = setTick;
            // A fresh-but-content-equal record instance every render. Under reference-identity deps semantics this is
            // a CHANGED dep (the reference differs), so the value must be recomputed.
            var dep = new DepRecord("constant");
            s_recordDepLastValue = Hooks.UseMemo(() => new Computed(dep.Value.Length), dep);
            return V.Label(text: $"{tick}:{dep.Value}");
        }

        // A render-phase setState normalizes an odd phase to the next even phase in one re-run, so the memo dep
        // swings to "transient" on the discarded attempt and back to the committed "settled" on the settled attempt.
        [Component]
        private static VNode RenderPhaseOscillationMemoRender()
        {
            s_oscRenderCount++;
            var (phase, setPhase) = Hooks.UseState(0);
            s_oscSetPhase = setPhase;
            if (phase % 2 == 1)
            {
                setPhase.Invoke(phase + 1);
            }
            var dep = phase % 2 == 1 ? "transient" : "settled";
            s_oscLastValue = Hooks.UseMemo(() => new Computed(dep.Length), dep);
            return V.Label(text: dep);
        }

        #endregion
    }
}

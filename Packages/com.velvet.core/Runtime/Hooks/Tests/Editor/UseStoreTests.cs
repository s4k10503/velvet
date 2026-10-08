using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseStore"/> in a function component, including its cross-tier
    /// tearing guard across a batch drain wave.
    /// <list type="bullet">
    /// <item>The first render returns the selector applied to the current store snapshot.</item>
    /// <item>When the selected value changes, the component re-renders once and observes the new value.</item>
    /// <item>When a store change leaves the selected value unchanged, no re-render is scheduled.</item>
    /// <item>Change detection uses the supplied <see cref="IEqualityComparer{T}"/> (default Object.is): values equal by the comparer skip the re-render, values that differ trigger it.</item>
    /// <item>Unmount disposes the store subscription, so later store changes no longer reach the component.</item>
    /// <item>Re-mounting establishes a fresh subscription.</item>
    /// <item>A selector that throws on a store emit is not swallowed: it re-renders so the render-phase throw reaches the ErrorBoundary.</item>
    /// <item>Readers rendered in one batch drain pass read the snapshot that pass pinned, even when the store
    /// mutates partway through it; a reader whose pinned read selects a different value than the store now
    /// gives — one already queued, or one mounted after the mutation — renders again in a later pass, so it does
    /// not stay behind the store, and one whose selection the mutation left unchanged does not.</item>
    /// <item>Each drain pass pins afresh: when one reader lands on the immediate tier and another on the delayed
    /// tier and the store mutates between their tier drains — outside a transition or inside one — both commit
    /// the latest snapshot by the end of the delayed drain.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers. The tearing guard
    /// tests additionally mirror <c>AutoBatchingTests</c> for the tier access
    /// (<c>mounted.Root.Reconciler.Context.BatchScheduler</c>, DrainImmediate/DrainDelayed for test); the
    /// UIToolkit scheduler does not advance in EditMode, so the two tier drains are simulated directly and the
    /// store is mutated between them to open the tearing window.
    /// </remarks>
    [TestFixture]
    internal sealed class UseStoreTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetStoreRead();
            ResetSelectorPartial();
            ResetCustomComparer();
            ResetThrowingSelector();
            ResetTearingParity();
            ResetNullTearing();
        }

        [Test]
        public void Given_StoreWithInitialValue_When_FirstRender_Then_ReturnsSelectedValue()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 100);
            s_storeReadStore = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(StoreReadRender, key: "store"));

            // Assert
            Assert.AreEqual(100, s_storeReadLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_SelectedValueChanges_Then_NewValueIsObserved()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 0);
            s_storeReadStore = store;
            using var mounted = V.Mount(_root, V.Component(StoreReadRender, key: "store"));

            // Act
            store.SetValue(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(5, s_storeReadLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_SelectedValueChanges_Then_ComponentReRendersExactlyOnce()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 0);
            s_storeReadStore = store;
            using var mounted = V.Mount(_root, V.Component(StoreReadRender, key: "store"));
            var renderCountBefore = s_storeReadRenderCount;

            // Act
            store.SetValue(5);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore + 1, s_storeReadRenderCount);
        }

        [Test]
        public void Given_MountedComponent_When_StoreChangesButSelectedValueIsEqual_Then_NoRerender()
        {
            // Arrange
            using var store = new TestPairStore(new PairState(1, "a"));
            s_selectorPartialStore = store;
            using var mounted = V.Mount(_root, V.Component(SelectorPartialRender, key: "partial"));
            var renderCountBefore = s_selectorPartialRenderCount;

            // Act — only Text changes; the .Number selector output is unchanged
            store.SetPair(new PairState(1, "b"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_selectorPartialRenderCount,
                "A store change that leaves the selected value equal schedules no re-render");
        }

        [Test]
        public void Given_CustomComparer_When_ValuesEqualByComparer_Then_NoRerender()
        {
            // Arrange
            using var store = new TestPairStore(new PairState(1, "a"));
            s_customComparerStore = store;
            s_customComparerComparer = new NumberEqualityOnly();
            using var mounted = V.Mount(_root, V.Component(CustomComparerRender, key: "custom"));
            var renderCountBefore = s_customComparerRenderCount;

            // Act — Number is unchanged, so the comparer treats the value as equal
            store.SetPair(new PairState(1, "different"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_customComparerRenderCount,
                "Values equal under the supplied comparer skip the re-render");
        }

        [Test]
        public void Given_CustomComparer_When_ValuesDifferByComparer_Then_ComponentReRenders()
        {
            // Arrange
            using var store = new TestPairStore(new PairState(1, "a"));
            s_customComparerStore = store;
            s_customComparerComparer = new NumberEqualityOnly();
            using var mounted = V.Mount(_root, V.Component(CustomComparerRender, key: "custom"));
            var renderCountBefore = s_customComparerRenderCount;

            // Act — Number changes, so the comparer treats the value as different
            store.SetPair(new PairState(2, "a"));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore + 1, s_customComparerRenderCount,
                "Values that differ under the supplied comparer trigger a re-render");
        }

        [Test]
        public void Given_MountedComponent_When_Unmounted_Then_StoreSubscriptionIsDisposed()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 0);
            s_storeReadStore = store;
            var mounted = V.Mount(_root, V.Component(StoreReadRender, key: "store"));

            // Act
            mounted.Dispose();
            store.SetValue(99);

            // Assert — the disposed subscription does not advance the component's observed value
            Assert.AreEqual(0, s_storeReadLastValue, "A store change after unmount does not reach the component");
        }

        [Test]
        public void Given_RemountedComponent_When_StoreChanges_Then_FreshSubscriptionObservesChange()
        {
            // Arrange
            using var store = new TestCounterStore(initial: 0);
            s_storeReadStore = store;
            var first = V.Mount(_root, V.Component(StoreReadRender, key: "store"));
            first.Dispose();
            using var second = V.Mount(_root, V.Component(StoreReadRender, key: "store"));

            // Act
            store.SetValue(42);
            second.FlushStateForTest();

            // Assert
            Assert.AreEqual(42, s_storeReadLastValue, "Re-mounting establishes a fresh subscription that observes changes");
        }

        [Test]
        public void Given_SelectorThatThrowsOnEmit_When_StoreChanges_Then_ExceptionReachesErrorBoundary()
        {
            // Arrange
            using var store = new TestIndexedStore(new IndexedState(0, new[] { 10, 20 }));
            s_throwingSelectorStore = store;
            using var mounted = V.Mount(_root, V.Component(ThrowingSelectorBoundaryRender, key: "boundary"), CaughtErrors.Unlogged);
            Assume.That(s_throwingSelectorFallbackShown, Is.False, "Precondition: the boundary is healthy before the bad emit");

            // Act — move Index out of range so the selector throws on the subscription callback
            store.SetIndexed(new IndexedState(5, new[] { 10, 20 }));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_throwingSelectorFallbackShown, Is.True,
                "The throwing subscription selector re-renders and the render-phase throw is caught by the boundary");
        }

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_StoreMutatesBetweenDrains_Then_TheDelayedDrainCommitsTheMutationToBoth()
        {
            // Arrange — after the immediate drain renders the ancestor, the mutation re-schedules both readers on
            // the immediate tier.
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            store.SetValue(1);

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That((s_ancestorValue, s_descendantValue), Is.EqualTo((1, 1)),
                "The delayed drain commits the mutation's follow-up renders in a fresh wave before its own queue");
        }

        [Test]
        public void Given_AncestorImmediateAndDescendantDelayed_When_AStoreMutationInsideATransitionLandsBetweenDrains_Then_TheDelayedDrainCommitsItToBoth()
        {
            // Arrange — the immediate drain renders the ancestor at 0; the mutation, made inside a transition,
            // re-schedules both readers on the Transition lane, so no immediate work precedes the delayed drain.
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_descendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            FiberWorkLoop.StartTransition(s_ancestorFiber, new HookTransitionSlot(), () => store.SetValue(1));

            // Act
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That((s_ancestorValue, s_descendantValue), Is.EqualTo((1, 1)),
                "The delayed drain pins the store afresh rather than reading the immediate drain's snapshot");
        }

        // GREEN_ON_BASE(characterization): a base drain already ended its pinning when it returned.
        [Test]
        public void Given_ADrainThatHasEnded_When_AReaderRendersTwiceOutsideADrainAcrossAStoreChange_Then_ItReadsTheLiveStore()
        {
            // Arrange — a drain runs and ends, then a flush outside any drain renders the reader at 0
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountAncestorDescendant();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_ancestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            FiberWorkLoop.FlushState(s_ancestorFiber);
            store.SetValue(1);

            // Act
            FiberWorkLoop.FlushState(s_ancestorFiber);

            // Assert
            Assert.That(s_ancestorValue, Is.EqualTo(1),
                "Outside a drain no snapshot is pinned, so a render after the change reads it");
        }

        [Test]
        public void Given_AReaderMountedInADrainPassAfterAStoreMutation_When_TheDrainEnds_Then_ItShowsTheLatestSnapshot()
        {
            // Arrange — the mutator mounts the reader in the same pass, after the notification it missed
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountMidPassMutation();
            s_midPassMutateOnRender = true;
            s_midPassMountLateReader = true;
            s_midPassFirstFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassMutatorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<Label>("mid-pass-late").text, Is.EqualTo("1"),
                "A reader whose first render read a pin older than the store renders again and catches up");
        }

        // GREEN_ON_BASE(characterization): a base reader never rendered again for a mid-pass mutation.
        [Test]
        public void Given_AStoreMutatedPartwayThroughADrainPass_When_ALaterReaderWhoseSelectionItLeavesUnchangedRenders_Then_ItRendersOnce()
        {
            // Arrange — the mutation moves the store from 0 to 1, which the reader's `>= 10` selection ignores
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountMidPassMutation();
            s_midPassMutateOnRender = true;
            s_midPassFirstFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassMutatorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassCoarseFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            var rendersBefore = s_midPassCoarseRenders;

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_midPassCoarseRenders - rendersBefore, Is.EqualTo(1),
                "A pinned read the mutation left selecting the same value asks for no further render");
        }

        // GREEN_ON_BASE(characterization): a base drain pass already pinned the snapshot its first reader read.
        [Test]
        public void Given_AStoreMutatedPartwayThroughADrainPass_When_ALaterReaderInThatPassRenders_Then_ItReadsThePinnedSnapshot()
        {
            // Arrange — three siblings queued in one pass; the middle one mutates the store while it renders
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountMidPassMutation();
            s_midPassReaderRenders.Clear();
            s_midPassMutateOnRender = true;
            s_midPassFirstFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassMutatorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassReaderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the reader's first render after the mutation, still inside the pass that pinned 0
            Assert.That(s_midPassReaderRenders[0], Is.EqualTo(0),
                "A reader rendered after a mid-pass mutation reads the snapshot its pass pinned, not the live store");
        }

        [Test]
        public void Given_AStoreMutatedPartwayThroughADrainPass_When_TheDrainEnds_Then_TheLaterReaderShowsTheLatestSnapshot()
        {
            // Arrange — as above: the reader was already queued, so the mutation's notification coalesced onto
            // the render that then read the pinned snapshot
            using var store = new TestCounterStore(initial: 0);
            s_store = store;
            using var mounted = MountMidPassMutation();
            s_midPassMutateOnRender = true;
            s_midPassFirstFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassMutatorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_midPassReaderFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(_root.Q<Label>("mid-pass-reader").text, Is.EqualTo("1"),
                "A reader that read a pin older than the store renders again and catches up with it");
        }

        [Test]
        public void Given_NullSnapshot_When_StoreMutatesPartwayThroughADrainPass_Then_ALaterReaderObservesThePinnedNull()
        {
            // Arrange — the ancestor pins null, then changes the store while it renders
            using var store = new TestTextStore(null);
            s_textStore = store;
            using var mounted = V.Mount(_root, V.Div(name: "host", children: new VNode[]
            {
                V.Component(TextAncestorRender, key: "ancestor"),
                V.Component(TextDescendantRender, key: "descendant"),
            }));
            s_textAncestorFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_textDescendantFiber.ScheduleRerenderForTest(FiberUpdatePriority.Normal);
            s_textDescendantReads.Clear();
            s_textMutateOnRender = true;

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the descendant's first read after the change, still inside the pass that pinned null
            Assert.AreEqual("<null>", s_textDescendantReads[0] ?? "<null>");
        }

        private sealed record PairState(int Number, string Text);

        private sealed record IndexedState(int Index, IReadOnlyList<int> Items);

        private sealed class TestCounterStore : Store<int>
        {
            public TestCounterStore(int initial) : base(initial) { }
            public void SetValue(int v) => SetState(_ => v);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private sealed class TestPairStore : Store<PairState>
        {
            public TestPairStore(PairState initial) : base(initial) { }
            public void SetPair(PairState next) => SetState(_ => next);
            protected override void ResetCore() => SetState(_ => new PairState(0, ""));
        }

        private sealed class TestIndexedStore : Store<IndexedState>
        {
            public TestIndexedStore(IndexedState initial) : base(initial) { }
            public void SetIndexed(IndexedState next) => SetState(_ => next);
            protected override void ResetCore() => SetState(_ => new IndexedState(0, System.Array.Empty<int>()));
        }

        private sealed class TestTextStore : Store<string>
        {
            public TestTextStore(string initial) : base(initial) { }
            public void SetText(string next) => SetState(_ => next);
            protected override void ResetCore() => SetState(_ => null);
        }

        private sealed class NumberEqualityOnly : IEqualityComparer<int>
        {
            public bool Equals(int x, int y) => x == y;
            public int GetHashCode(int obj) => obj.GetHashCode();
        }

        #region StoreRead component (UseStore identity selector)

        private static Store<int> s_storeReadStore;
        private static int s_storeReadLastValue;
        private static int s_storeReadRenderCount;

        private static void ResetStoreRead()
        {
            s_storeReadStore = null;
            s_storeReadLastValue = 0;
            s_storeReadRenderCount = 0;
        }

        [Component]
        private static VNode StoreReadRender()
        {
            s_storeReadRenderCount++;
            s_storeReadLastValue = Hooks.UseStore(s_storeReadStore, s => s);
            return V.Label(text: s_storeReadLastValue.ToString());
        }

        #endregion

        #region SelectorPartial component (UseStore with .Number selector)

        private static Store<PairState> s_selectorPartialStore;
        private static int s_selectorPartialRenderCount;

        private static void ResetSelectorPartial()
        {
            s_selectorPartialStore = null;
            s_selectorPartialRenderCount = 0;
        }

        [Component]
        private static VNode SelectorPartialRender()
        {
            s_selectorPartialRenderCount++;
            var number = Hooks.UseStore(s_selectorPartialStore, s => s.Number);
            return V.Label(text: number.ToString());
        }

        #endregion

        #region CustomComparer component (UseStore with selector + IEqualityComparer)

        private static Store<PairState> s_customComparerStore;
        private static IEqualityComparer<int> s_customComparerComparer;
        private static int s_customComparerRenderCount;

        private static void ResetCustomComparer()
        {
            s_customComparerStore = null;
            s_customComparerComparer = null;
            s_customComparerRenderCount = 0;
        }

        [Component]
        private static VNode CustomComparerRender()
        {
            s_customComparerRenderCount++;
            var number = Hooks.UseStore(
                s_customComparerStore,
                s => s.Number,
                s_customComparerComparer);
            return V.Label(text: number.ToString());
        }

        #endregion

        #region ThrowingSelector component (UseStore selector that throws on out-of-range index)

        private static Store<IndexedState> s_throwingSelectorStore;
        private static bool s_throwingSelectorFallbackShown;

        private static void ResetThrowingSelector()
        {
            s_throwingSelectorStore = null;
            s_throwingSelectorFallbackShown = false;
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ThrowingSelectorBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_throwingSelectorFallbackShown = true;
                return V.Label(text: "caught");
            });
            return V.Fragment(new VNode[] { V.Component(ThrowingSelectorChildRender, key: "child") });
        }

        [Component]
        private static VNode ThrowingSelectorChildRender()
        {
            var value = Hooks.UseStore(s_throwingSelectorStore, s => s.Items[s.Index]);
            return V.Label(text: value.ToString());
        }

        #endregion

        #region TearingParity components (cross-tier snapshot consistency)

        private static Store<int> s_store;
        private static int s_ancestorValue;
        private static int s_descendantValue;
        private static ComponentFiber s_ancestorFiber;
        private static ComponentFiber s_descendantFiber;

        private static void ResetTearingParity()
        {
            s_store = null;
            s_ancestorValue = 0;
            s_descendantValue = 0;
            s_ancestorFiber = null;
            s_descendantFiber = null;
        }

        private MountedTree MountAncestorDescendant()
        {
            var tree = V.Div(name: "host", children: new VNode[]
            {
                V.Component(AncestorRender, key: "ancestor"),
                V.Component(DescendantRender, key: "descendant"),
            });
            return V.Mount(_root, tree);
        }

        [Component]
        private static VNode AncestorRender()
        {
            s_ancestorFiber = FiberAmbientStack.Current;
            s_ancestorValue = Hooks.UseStore(s_store, s => s);
            return V.Label(text: s_ancestorValue.ToString());
        }

        [Component]
        private static VNode DescendantRender()
        {
            s_descendantFiber = FiberAmbientStack.Current;
            s_descendantValue = Hooks.UseStore(s_store, s => s);
            return V.Label(text: s_descendantValue.ToString());
        }

        private static bool s_midPassMountLateReader;
        private static int s_midPassCoarseRenders;
        private static ComponentFiber s_midPassCoarseFiber;
        private static ComponentFiber s_midPassFirstFiber;
        private static ComponentFiber s_midPassMutatorFiber;
        private static ComponentFiber s_midPassReaderFiber;
        private static bool s_midPassMutateOnRender;
        private static readonly List<int> s_midPassReaderRenders = new();

        private MountedTree MountMidPassMutation()
        {
            s_midPassFirstFiber = null;
            s_midPassMutatorFiber = null;
            s_midPassReaderFiber = null;
            s_midPassMutateOnRender = false;
            s_midPassMountLateReader = false;
            s_midPassCoarseRenders = 0;
            s_midPassCoarseFiber = null;
            s_midPassReaderRenders.Clear();
            return V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(MidPassFirstRender, key: "first"),
                V.Component(MidPassMutatorRender, key: "mutator"),
                V.Component(MidPassReaderRender, key: "reader"),
                V.Component(MidPassCoarseReaderRender, key: "coarse"),
            }));
        }

        [Component(Compiler = false)]
        private static VNode MidPassLateReaderRender()
        {
            var value = Hooks.UseStore(s_store, s => s);
            return V.Label(name: "mid-pass-late", text: value.ToString());
        }

        [Component(Compiler = false)]
        private static VNode MidPassCoarseReaderRender()
        {
            s_midPassCoarseFiber = FiberAmbientStack.Current;
            s_midPassCoarseRenders++;
            var atLeastTen = Hooks.UseStore(s_store, s => s >= 10);
            return V.Label(text: atLeastTen.ToString());
        }

        [Component(Compiler = false)]
        private static VNode MidPassFirstRender()
        {
            s_midPassFirstFiber = FiberAmbientStack.Current;
            var value = Hooks.UseStore(s_store, s => s);
            return V.Label(name: "mid-pass-first", text: value.ToString());
        }

        [Component(Compiler = false)]
        private static VNode MidPassMutatorRender()
        {
            s_midPassMutatorFiber = FiberAmbientStack.Current;
            if (s_midPassMutateOnRender)
            {
                s_midPassMutateOnRender = false;
                ((TestCounterStore)s_store).SetValue(1);
            }
            return s_midPassMountLateReader
                ? V.Component(MidPassLateReaderRender, key: "late")
                : V.Label();
        }

        [Component(Compiler = false)]
        private static VNode MidPassReaderRender()
        {
            s_midPassReaderFiber = FiberAmbientStack.Current;
            var value = Hooks.UseStore(s_store, s => s);
            s_midPassReaderRenders.Add(value);
            return V.Label(name: "mid-pass-reader", text: value.ToString());
        }

        #endregion

        #region NullTearing components (a store whose pinned snapshot is null)

        private static Store<string> s_textStore;
        private static string s_textDescendantValue;
        private static readonly List<string> s_textDescendantReads = new();
        private static bool s_textMutateOnRender;
        private static ComponentFiber s_textAncestorFiber;
        private static ComponentFiber s_textDescendantFiber;

        private static void ResetNullTearing()
        {
            s_textStore = null;
            s_textDescendantValue = null;
            s_textDescendantReads.Clear();
            s_textMutateOnRender = false;
            s_textAncestorFiber = null;
            s_textDescendantFiber = null;
        }

        // Unwoven, like the mid-pass components: on a memo hit a woven body skips what follows its hooks, which
        // here is the mutation and the recorded read.
        [Component(Compiler = false)]
        private static VNode TextAncestorRender()
        {
            s_textAncestorFiber = FiberAmbientStack.Current;
            var text = Hooks.UseStore(s_textStore, s => s);
            if (s_textMutateOnRender)
            {
                s_textMutateOnRender = false;
                ((TestTextStore)s_textStore).SetText("changed");
            }
            return V.Label(text: text ?? "");
        }

        [Component(Compiler = false)]
        private static VNode TextDescendantRender()
        {
            s_textDescendantFiber = FiberAmbientStack.Current;
            s_textDescendantValue = Hooks.UseStore(s_textStore, s => s);
            s_textDescendantReads.Add(s_textDescendantValue);
            return V.Label(text: s_textDescendantValue ?? "");
        }

        #endregion
    }
}

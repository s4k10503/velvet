using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseStore"/> in a function component.
    /// <list type="bullet">
    /// <item>The first render returns the selector applied to the current store snapshot.</item>
    /// <item>When the selected value changes, the component re-renders once and observes the new value.</item>
    /// <item>When a store change leaves the selected value unchanged, no re-render is scheduled.</item>
    /// <item>Change detection uses the supplied <see cref="IEqualityComparer{T}"/> (default Object.is): values equal by the comparer skip the re-render, values that differ trigger it.</item>
    /// <item>Unmount disposes the store subscription, so later store changes no longer reach the component.</item>
    /// <item>Re-mounting establishes a fresh subscription.</item>
    /// <item>A selector that throws on a store emit is not swallowed: it re-renders so the render-phase throw reaches the ErrorBoundary.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
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
            using var mounted = V.Mount(_root, V.Component(ThrowingSelectorBoundaryRender, key: "boundary"));
            Assume.That(s_throwingSelectorFallbackShown, Is.False, "Precondition: the boundary is healthy before the bad emit");

            // Act — move Index out of range so the selector throws on the subscription callback
            store.SetIndexed(new IndexedState(5, new[] { 10, 20 }));
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_throwingSelectorFallbackShown, Is.True,
                "The throwing subscription selector re-renders and the render-phase throw is caught by the boundary");
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
    }
}

using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of the props-receiving <c>V.Component&lt;TProps&gt;</c> overload.
    /// <list type="bullet">
    /// <item>The first render delivers the parent's props to the child, which observes their values exactly once.</item>
    /// <item>When the parent re-renders with different props, the new values propagate and the child re-renders.</item>
    /// <item>When the parent re-renders with shallow-equal props, a memoized child bails the re-render (the opt-in
    /// props-equality gate), comparing each property by identity.</item>
    /// <item>A <c>UseCallback</c> inside the child returns a stable delegate while its dependencies are unchanged and
    /// rebuilds when a dependency changes.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> render-target pattern with static-field exposure; per-test state is
    /// reset together in <see cref="SetUp"/> via <c>ResetState()</c>. The child is declared with
    /// <c>[Component(Memoize = true)]</c> so the props-equality bail is in effect.
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentWithPropsTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetState();
        }

        [Test]
        public void Given_InitialProps_When_FirstRender_Then_ChildObservesPropValue()
        {
            // Arrange
            using var store = new PropsStore(new CounterProps(Value: 42, OnIncrement: null));
            s_parentStore = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));

            // Assert
            Assert.AreEqual(42, s_lastValue, "The first render delivers the parent's prop value to the child");
        }

        [Test]
        public void Given_InitialProps_When_FirstRender_Then_ChildRendersExactlyOnce()
        {
            // Arrange
            using var store = new PropsStore(new CounterProps(Value: 42, OnIncrement: null));
            s_parentStore = store;

            // Act
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));

            // Assert
            Assert.AreEqual(1, s_renderCount, "The child renders exactly once on the first mount");
        }

        [Test]
        public void Given_MountedComponent_When_ParentRerendersWithDifferentProps_Then_ChildObservesNewValue()
        {
            // Arrange
            using var store = new PropsStore(new CounterProps(Value: 1, OnIncrement: null));
            s_parentStore = store;
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));
            Assume.That(s_lastValue, Is.EqualTo(1), "Precondition: the initial prop value reached the child");

            // Act
            store.SetValue(new CounterProps(Value: 99, OnIncrement: null));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(99, s_lastValue, "A parent re-render with a different prop propagates the new value");
        }

        [Test]
        public void Given_MountedComponent_When_ParentRerendersWithDifferentProps_Then_ChildReRendersExactlyOnce()
        {
            // Arrange
            using var store = new PropsStore(new CounterProps(Value: 1, OnIncrement: null));
            s_parentStore = store;
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));
            Assume.That(s_renderCount, Is.EqualTo(1), "Precondition: the child rendered once on mount");

            // Act
            store.SetValue(new CounterProps(Value: 99, OnIncrement: null));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_renderCount, "A parent re-render with a different prop re-renders the child once");
        }

        [Test]
        public void Given_MemoizedChild_When_ParentRerendersWithShallowEqualProps_Then_ChildDoesNotReRender()
        {
            // Arrange
            Action<int> handler = _ => { };
            using var store = new PropsStore(new CounterProps(Value: 5, OnIncrement: handler));
            s_parentStore = store;
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));
            Assume.That(s_renderCount, Is.EqualTo(1), "Precondition: the child rendered once on mount");

            // Act — same Value and same OnIncrement identity yields shallow-equal props
            store.SetValue(new CounterProps(Value: 5, OnIncrement: handler));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_renderCount, "A memoized child bails the parent-driven re-render when props are shallow-equal");
        }

        [Test]
        public void Given_StableDependencies_When_PropsRenderRepeats_Then_UseCallbackReturnsSameDelegate()
        {
            // Arrange
            Action<int> handler = _ => { };
            using var store = new PropsStore(new CounterProps(Value: 5, OnIncrement: handler));
            s_parentStore = store;
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));
            var firstCallback = s_lastCallback;

            // Act — re-issuing shallow-equal props keeps the UseCallback dependencies (Value, OnIncrement) unchanged
            store.SetValue(new CounterProps(Value: 5, OnIncrement: handler));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(firstCallback, s_lastCallback, "UseCallback returns the same delegate while its dependencies are unchanged");
        }

        [Test]
        public void Given_ChangedDependency_When_PropsRenderRepeats_Then_UseCallbackRebuilds()
        {
            // Arrange
            Action<int> handler = _ => { };
            using var store = new PropsStore(new CounterProps(Value: 1, OnIncrement: handler));
            s_parentStore = store;
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "p"));
            var firstCallback = s_lastCallback;

            // Act — changing Value invalidates the UseCallback dependency
            store.SetValue(new CounterProps(Value: 2, OnIncrement: handler));
            mounted.FlushStateForTest();

            // Assert
            Assert.AreNotSame(firstCallback, s_lastCallback, "UseCallback rebuilds when one of its dependencies changes");
        }

        #region Props component (parent UseStore feeds a memoized child)

        private sealed record CounterProps(int Value, Action<int> OnIncrement);

        private sealed class PropsStore : Store<CounterProps>
        {
            public PropsStore(CounterProps initial) : base(initial) { }
            public void SetValue(CounterProps next) => SetState(_ => next);
            protected override void ResetCore() { /* no-op */ }
        }

        private static PropsStore s_parentStore;
        private static int s_lastValue;
        private static int s_renderCount;
        private static Action s_lastCallback;

        private static void ResetState()
        {
            s_parentStore = null;
            s_lastValue = 0;
            s_renderCount = 0;
            s_lastCallback = null;
        }

        [Component]
        private static VNode ParentRender()
        {
            var props = Hooks.UseStore(s_parentStore, s => s);
            return V.Component(ChildRender, props, key: "child");
        }

        [Component(Memoize = true)]
        private static VNode ChildRender(CounterProps p)
        {
            s_renderCount++;
            s_lastValue = p.Value;
            s_lastCallback = Hooks.UseCallback<Action>(
                () => p.OnIncrement?.Invoke(p.Value),
                p.Value,
                p.OnIncrement);
            return V.Label(text: p.Value.ToString());
        }

        #endregion
    }
}

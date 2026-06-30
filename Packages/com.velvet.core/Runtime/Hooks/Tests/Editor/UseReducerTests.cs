using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseReducer{TState,TAction}"/> in a function component.
    /// <list type="bullet">
    /// <item>The first render returns the initial state.</item>
    /// <item>Dispatching an action schedules a re-render that observes the reducer's next state.</item>
    /// <item>When the reducer returns a state equal to the current one, no re-render is scheduled.</item>
    /// <item>The dispatch identity is stable across re-renders.</item>
    /// <item>The reducer is pure with respect to rendering: it is invoked only on dispatch, never on an unrelated re-render.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class UseReducerTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCounterReducer();
        }

        [Test]
        public void Given_InitialState_When_FirstRender_Then_ReturnsInitialState()
        {
            // Arrange
            s_counterReducerInitial = 10;

            // Act
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));

            // Assert
            Assert.AreEqual(10, s_counterReducerLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_IncrementDispatched_Then_StateIncreasesByOne()
        {
            // Arrange
            s_counterReducerInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));

            // Act
            s_counterReducerDispatch.Invoke(CounterAction.Increment);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_counterReducerLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_DecrementDispatched_Then_StateDecreasesByOne()
        {
            // Arrange
            s_counterReducerInitial = 5;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));

            // Act
            s_counterReducerDispatch.Invoke(CounterAction.Decrement);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(4, s_counterReducerLastValue);
        }

        [Test]
        public void Given_MountedComponent_When_IncrementDispatchedTwice_Then_StateAccumulates()
        {
            // Arrange
            s_counterReducerInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));

            // Act
            s_counterReducerDispatch.Invoke(CounterAction.Increment);
            mounted.FlushStateForTest();
            s_counterReducerDispatch.Invoke(CounterAction.Increment);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(2, s_counterReducerLastValue, "Successive dispatches accumulate through the reducer");
        }

        [Test]
        public void Given_ActionLeavingStateUnchanged_When_Dispatched_Then_NoRerender()
        {
            // Arrange
            s_counterReducerInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));
            var renderCountBefore = s_counterReducerRenderCount;

            // Act — NoOp returns the same state value
            s_counterReducerDispatch.Invoke(CounterAction.NoOp);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(renderCountBefore, s_counterReducerRenderCount,
                "A reducer returning an equal state schedules no re-render");
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_DispatchReferenceIsStable()
        {
            // Arrange
            s_counterReducerInitial = 0;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));
            var firstDispatch = s_counterReducerDispatch;

            // Act
            s_counterReducerDispatch.Invoke(CounterAction.Increment);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreSame(firstDispatch, s_counterReducerDispatch, "Dispatch reference is identical after re-render");
        }

        [Test]
        public void Given_MountedComponent_When_ReRenderedWithoutDispatch_Then_ReducerIsNotInvoked()
        {
            // Arrange
            s_counterReducerInitial = 100;
            using var mounted = V.Mount(_root, V.Component(CounterReducerRender, key: "reducer"));

            // Act — force re-renders through an unrelated state hook, never dispatching
            s_counterReducerTriggerRerender();
            mounted.FlushStateForTest();
            s_counterReducerTriggerRerender();
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(0, s_counterReducerCallCount, "The reducer runs only on dispatch, not on unrelated re-renders");
        }

        internal enum CounterAction { Increment, Decrement, NoOp }

        #region CounterReducer component (UseReducer + UseState for tick)

        private static int s_counterReducerInitial;
        private static int s_counterReducerLastValue;
        private static int s_counterReducerRenderCount;
        private static int s_counterReducerCallCount;
        private static Action<CounterAction> s_counterReducerDispatch;
        private static Action s_counterReducerTriggerRerender;

        private static void ResetCounterReducer()
        {
            s_counterReducerInitial = 0;
            s_counterReducerLastValue = 0;
            s_counterReducerRenderCount = 0;
            s_counterReducerCallCount = 0;
            s_counterReducerDispatch = null;
            s_counterReducerTriggerRerender = null;
        }

        [Component]
        public static VNode CounterReducerRender()
        {
            s_counterReducerRenderCount++;
            var (value, dispatch) = Hooks.UseReducer<int, CounterAction>(Reduce, s_counterReducerInitial);
            s_counterReducerLastValue = value;
            s_counterReducerDispatch = dispatch;

            var (tick, setTick) = Hooks.UseState(0);
            s_counterReducerTriggerRerender = () => setTick.Invoke(tick + 1);

            return V.Label(text: value.ToString());
        }

        private static int Reduce(int state, CounterAction action)
        {
            s_counterReducerCallCount++;
            return action switch
            {
                CounterAction.Increment => state + 1,
                CounterAction.Decrement => state - 1,
                _ => state,
            };
        }

        #endregion
    }

    /// <summary>
    /// Specifies the contract of the lazy-initializer overload
    /// <see cref="Hooks.UseReducer{TArg,TState,TAction}"/>.
    /// <list type="bullet">
    /// <item>The initializer runs exactly once on the first render, receiving the initial argument.</item>
    /// <item>The initializer's result becomes the initial reducer state.</item>
    /// <item>The initializer is never invoked again on dispatch-triggered or unrelated re-renders.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UseReducerLazyInitTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetLazyInit();
        }

        [Test]
        public void Given_LazyInitializer_When_FirstRender_Then_InitializerRunsExactlyOnce()
        {
            // Arrange
            s_lazyInitArg = 7;

            // Act
            using var mounted = V.Mount(_root, V.Component(LazyInitRender, key: "lazy"));

            // Assert
            Assert.AreEqual(1, s_lazyInitCallCount, "The initializer runs exactly once on the first render");
        }

        [Test]
        public void Given_LazyInitializer_When_FirstRender_Then_InitializerReceivesInitialArg()
        {
            // Arrange
            s_lazyInitArg = 7;

            // Act
            using var mounted = V.Mount(_root, V.Component(LazyInitRender, key: "lazy"));

            // Assert
            Assert.AreEqual(7, s_lazyInitLastArg, "The initializer receives the initial argument");
        }

        [Test]
        public void Given_LazyInitializer_When_FirstRender_Then_StateComesFromInitializer()
        {
            // Arrange
            s_lazyInitArg = 5;

            // Act
            using var mounted = V.Mount(_root, V.Component(LazyInitRender, key: "lazy"));

            // Assert — init(5) builds [0,1,2,3,4], so the reducer state Count starts at 5
            Assert.AreEqual(5, s_lazyLastCount, "The initial state is produced by the initializer from the argument");
        }

        [Test]
        public void Given_LazyInitializer_When_Dispatched_Then_InitializerDoesNotRunAgain()
        {
            // Arrange
            s_lazyInitArg = 3;
            using var mounted = V.Mount(_root, V.Component(LazyInitRender, key: "lazy"));
            Assume.That(s_lazyInitCallCount, Is.EqualTo(1), "Precondition: the initializer ran once on the first render");

            // Act
            s_lazyDispatch.Invoke(LazyAction.Append);
            mounted.FlushStateForTest();
            s_lazyDispatch.Invoke(LazyAction.Append);
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_lazyInitCallCount, "The initializer does not run again on dispatch-triggered renders");
        }

        [Test]
        public void Given_LazyInitializer_When_UnrelatedHookRerenders_Then_InitializerDoesNotRunAgain()
        {
            // Arrange
            s_lazyInitArg = 2;
            using var mounted = V.Mount(_root, V.Component(LazyInitRender, key: "lazy"));
            Assume.That(s_lazyInitCallCount, Is.EqualTo(1), "Precondition: the initializer ran once on the first render");

            // Act — re-render through an unrelated state hook
            s_lazyTriggerRerender();
            mounted.FlushStateForTest();
            s_lazyTriggerRerender();
            mounted.FlushStateForTest();

            // Assert
            Assert.AreEqual(1, s_lazyInitCallCount, "The initializer does not run again on unrelated re-renders");
        }

        internal enum LazyAction { Append }

        #region LazyInit component

        private static int s_lazyInitArg;
        private static int s_lazyInitCallCount;
        private static int s_lazyInitLastArg;
        private static int s_lazyLastCount;
        private static Action<LazyAction> s_lazyDispatch;
        private static Action s_lazyTriggerRerender;

        private static void ResetLazyInit()
        {
            s_lazyInitArg = 0;
            s_lazyInitCallCount = 0;
            s_lazyInitLastArg = 0;
            s_lazyLastCount = 0;
            s_lazyDispatch = null;
            s_lazyTriggerRerender = null;
        }

        [Component]
        public static VNode LazyInitRender()
        {
            var (state, dispatch) = Hooks.UseReducer<int, List<int>, LazyAction>(
                ReduceAppend, s_lazyInitArg, BuildInitialList);
            s_lazyLastCount = state.Count;
            s_lazyDispatch = dispatch;

            var (tick, setTick) = Hooks.UseState(0);
            s_lazyTriggerRerender = () => setTick.Invoke(tick + 1);

            return V.Label(text: state.Count.ToString());
        }

        private static List<int> BuildInitialList(int size)
        {
            s_lazyInitCallCount++;
            s_lazyInitLastArg = size;
            var list = new List<int>(size);
            for (var i = 0; i < size; i++) list.Add(i);
            return list;
        }

        private static List<int> ReduceAppend(List<int> state, LazyAction action) => action switch
        {
            LazyAction.Append => new List<int>(state) { state.Count },
            _ => state,
        };

        #endregion
    }
}

using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that what a pass does with the fibers under a row it has just inserted into an existing container
    /// reaches them: a Suspense that keeps its primary hidden marks them offscreen.
    /// </summary>
    [TestFixture]
    internal sealed class RecordedRowFiberScopeTests
    {
        private VisualElement _root = null!;
        private static StateUpdater<int> s_setTick;
        private static ComponentFiber s_childFiber;
        private static int s_cleanups;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = default;
            s_childFiber = null;
            s_cleanups = 0;
        }

        [Component]
        private static VNode ChildRender()
        {
            s_childFiber = FiberAmbientStack.Current;
            Hooks.UseLayoutEffect(() => (Action)(() => s_cleanups++), Array.Empty<object>());
            return V.Label(text: "child");
        }

        // Pending for good once tick is not zero.
        [Component]
        private static VNode TickReaderRender(int tick)
        {
            var value = Hooks.Use<int>(_ => tick == 0
                ? VelvetTask.FromResult(0)
                : new VelvetTaskCompletionSource<int>().Task, tick);
            return V.Label(text: "tick:" + value);
        }

        [Component]
        private static VNode SuspendingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var rows = tick == 0
                ? new VNode[] { V.Div(key: "a", name: "a") }
                : new VNode[]
                {
                    V.Div(key: "a", name: "a"),
                    V.Div(key: "new", name: "new", children: new VNode[] { V.Component(ChildRender, key: "child") }),
                };
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(name: "list", children: rows),
                    V.Component(TickReaderRender, tick, key: "tick-reader"),
                });
        }

        // GREEN_ON_BASE(refactor): the base inserts the row at once, so the hide walk reaches the component through
        // the list; what this pins is that it still does where the row waits for the end of the pass.
        [Test]
        public void Given_ARevealedPrimaryWhoseListGainsARowHoldingAComponent_When_AReadSuspendsInThatPass_Then_TheComponentIsOffscreen()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SuspendingHostRender, key: "host"));

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_childFiber?.IsOffscreen, Is.True);
        }
    }
}

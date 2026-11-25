using System;
using System.Text;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the order in which a Suspense's hide detaches the refs of a Portal's rows when a layout cleanup the hide
    /// runs ahead of the Portal takes one of those rows out of the target: the rows the Portal held when the hide began,
    /// in their order, as React's walk over the committed children detaches them.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHiddenPortalRowOrderTests
    {
        private static StateUpdater<int> s_setBlocked;
        private static VisualElement s_target;
        private static VisualElement s_taken;
        private static StringBuilder s_detached;

        [SetUp]
        public void SetUp()
        {
            FiberStrictMode.Enabled = false;
            s_setBlocked = default;
            s_target = new VisualElement();
            s_taken = null;
            s_detached = new StringBuilder();
        }

        // Pending for good once blocked.
        [Component(Compiler = false)]
        private static VNode Reader(int blocked)
        {
            var value = Hooks.Use<int>(_ => blocked > 0
                ? new VelvetTaskCompletionSource<int>().Task
                : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        // Its layout cleanup, which the hide runs ahead of the Portal, takes the Portal's row named a out of the target by
        // hand.
        [Component(Compiler = false)]
        private static VNode Taker()
        {
            Hooks.UseLayoutEffect(() => () => s_taken?.RemoveFromHierarchy(), Array.Empty<object>());
            return V.Div(name: "taker");
        }

        // A component among the Portal's rows: placing it is the hide's first read of the target, ahead of any cleanup.
        [Component(Compiler = false)]
        private static VNode Kid() => V.Label(text: "kid");

        [Component(Compiler = false)]
        private static VNode Host()
        {
            var (blocked, setBlocked) = Hooks.UseState(0);
            s_setBlocked = setBlocked;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[]
                    {
                        V.Component(Reader, blocked, key: "reader"),
                        V.Component(Taker, key: "taker"),
                        V.Portal(s_target, children: new VNode[]
                        {
                            V.Component(Kid, key: "kid"),
                            V.Label(name: "a", refCallback: element =>
                            {
                                s_taken = element;
                                return () => s_detached.Append("a ");
                            }),
                            V.Label(name: "b", refCallback: _ => () => s_detached.Append("b ")),
                        }, key: "portal"),
                    }),
            });
        }

        // GREEN_ON_BASE(characterization): the base visits a hidden Portal's rows from the walk's first read of its target.
        // So the row the cleanup took out still detaches between its neighbours, as React's walk over the committed children does.
        [Test]
        public void Given_ALayoutCleanupAheadOfAPortalThatTakesOneOfItsRowsOut_When_TheBoundaryHidesTheTree_Then_TheRowsRefsDetachInTheirOrder()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));

            // Act
            s_setBlocked.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(s_detached.ToString(), Is.EqualTo("a b "));
        }
    }
}

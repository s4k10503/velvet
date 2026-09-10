using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the lifetime of the record a re-placement leaves on an inline fiber: the slot start its
    /// container still holds while the placement moving it has not run.
    /// <list type="bullet">
    /// <item>A pass that re-placed an inline fiber leaves no such record behind, so a later pass reading
    /// one cannot be handed a slot start from a container arrangement that is over.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The behaviour a stale record produces is pinned by <c>ErrorBoundaryTests</c>' committed-reorder
    /// case, which is where a fallback reads the record. This fixture pins the release itself, because
    /// a release that resets the record without dropping it reads the same there and retains every fiber
    /// it ever recorded.
    /// </remarks>
    [TestFixture]
    internal sealed class InlinePlacementRecordTests
    {
        private VisualElement _root;
        private static Action<int> s_setOrder;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setOrder = null;
        }

        [Test]
        public void Given_APassReplacedAnInlineFiber_When_ItHasFinished_Then_ItLeavesNoPrePlacementRecord()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"));

            // Act
            s_setOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the mark is the number of records outstanding.
            Assert.That(mounted.Root.Reconciler.Context.ComponentRegistry.MarkPrePlacements(), Is.Zero);
        }

        [Component]
        private static VNode PlacedChildRender() => V.Label(name: "placed", text: "placed");

        [Component]
        private static VNode ReorderHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setOrder = setOrder;
            return order == 0
                ? V.Div(children: new VNode[]
                {
                    V.Component(PlacedChildRender, key: "child"),
                    V.Label(name: "ahead", text: "ahead", key: "ahead"),
                })
                : V.Div(children: new VNode[]
                {
                    V.Label(name: "ahead", text: "ahead", key: "ahead"),
                    V.Component(PlacedChildRender, key: "child"),
                });
        }
    }
}

// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a host element belongs to the component whose output emitted it, so a slot whose
    /// component changes takes the element with it — the DOM half of the remount
    /// <c>Documentation~/react-migration.md</c> states for hook state.
    /// <list type="bullet">
    /// <item>Two components at one slot, each rendering a plain container, do not share that container: the
    /// arriving one's body is all its container holds, including where the departing one's body held an
    /// <c>V.AnimatePresence</c> whose committed children the arriving one never declared.</item>
    /// <item>The same holds where the arriving side is written inline rather than as a component, which
    /// leaves the container's new children a flat list of host leaves.</item>
    /// </list>
    /// The other direction — an ordinary re-render patching the element in place rather than rebuilding it —
    /// is <see cref="ReconcilerMemoTests"/>' to pin, and is why the reading here is a term about the
    /// component instance rather than about the slot.
    /// <see cref="ComponentContainerIdentityTests"/> owns the other direction of the same pairing — which
    /// instance a container holds — and <see cref="AnimatePresenceStateRetirementTests"/> owns when a
    /// presence's own bookkeeping is retired.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentSwapElementOwnershipTests
    {
        private VisualElement _root = null!;
        private static SwapStore s_swap;
        private static readonly string[] Rows = { "a", "b", "c" };

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            s_swap?.Dispose();
            s_swap = null;
            _root = null!;
        }

        private readonly record struct SwapState(bool First);

        private sealed class SwapStore : Store<SwapState>
        {
            public SwapStore() : base(new SwapState(true)) { }
            public void ShowSecond() => SetState(_ => new SwapState(false));
            protected override void ResetCore() => SetState(_ => new SwapState(true));
        }

        [Test]
        public void Given_TwoComponentsAtOneSlot_When_TheDepartingOneHeldAPresence_Then_ItsChildrenLeaveWithIt()
        {
            // Arrange — the departing body's rows are the presence's committed children, which only its own
            // boundary can reproduce for a diff; the arriving body declares none of them.
            s_swap = new SwapStore();
            using var mounted = V.Mount(_root, V.Component(SwapHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var rowsBefore = host.ElementAt(0).childCount;

            // Act
            s_swap.ShowSecond();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the row count before rides along, since an arrangement that mounted no rows would
            // satisfy the arriving container's own count while saying nothing about a departure.
            Assert.That(
                (rowsBefore, host.ElementAt(0).name, host.ElementAt(0).childCount),
                Is.EqualTo((3, "second", 1)),
                "The arriving component's body is all its container holds");
        }

        [Test]
        public void Given_AComponentReplacedByAnInlineElement_When_TheNewChildrenAreFlat_Then_ItsChildrenLeaveWithIt()
        {
            // Arrange — the arriving side is a host leaf rather than a component, so the container's new
            // children need no inline expansion and the path selection is what has to route them.
            s_swap = new SwapStore();
            using var mounted = V.Mount(_root, V.Component(InlineArrivalHostRender, key: "host"));
            var host = _root.Q<VisualElement>("host");
            var rowsBefore = host.ElementAt(0).childCount;

            // Act
            s_swap.ShowSecond();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(
                (rowsBefore, host.ElementAt(0).name, host.ElementAt(0).childCount),
                Is.EqualTo((3, "second", 1)),
                "The inline arrival's own children are all its container holds");
        }

        #region Render targets

        [Component]
        private static VNode SwapHostRender()
        {
            var first = Hooks.UseStore(s_swap, state => state.First);
            return V.Div(name: "host", children: new VNode[]
            {
                first ? V.Component(PresenceBodyRender) : V.Component(PlainBodyRender),
            });
        }

        [Component]
        private static VNode PresenceBodyRender()
            => V.Div(name: "first", children: new VNode[]
            {
                V.AnimatePresence(children: V.List(Rows, row => row, row => V.Motion(
                    name: "row-" + row, children: new VNode[] { V.Label(text: row) }))),
            });

        [Component]
        private static VNode InlineArrivalHostRender()
        {
            var first = Hooks.UseStore(s_swap, state => state.First);
            return V.Div(name: "host", children: first
                ? new VNode[] { V.Component(PresenceBodyRender) }
                : new VNode[] { V.Div(name: "second", children: new VNode[] { V.Label(text: "only") }) });
        }

        [Component]
        private static VNode PlainBodyRender()
            => V.Div(name: "second", children: new VNode[] { V.Label(text: "only", name: "only") });

        #endregion
    }
}

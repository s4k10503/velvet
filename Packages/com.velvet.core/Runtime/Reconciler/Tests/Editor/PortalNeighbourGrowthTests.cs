using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a component rendered inside one <c>V.Portal</c> keeps patching its own element
    /// after a neighbouring portal on the same target grows. Several portals sharing one container is
    /// what <c>Documentation~/portals.md</c> states is supported, and the growth moves the second
    /// portal's range without moving what its own child addresses.
    /// </summary>
    internal sealed class PortalNeighbourGrowthTests
    {
        private MountedTree? _mounted;
        private static Action<int>? s_grow;
        private static Action<int>? s_change;

        [SetUp]
        public void SetUp()
        {
            s_grow = null;
            s_change = null;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private static string Names(VisualElement parent) =>
            string.Join("|", parent.Children().Select(child => child.name));

        [Component]
        private static VNode ModalChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_change = setTick;
            return V.Div(name: tick == 0 ? "content" : "changed");
        }

        // The growth is on the portal's own children array, which is what reaches
        // PortalSlotTracker.ShiftSlotStartsAfter -- a component inside the portal growing instead is an
        // inline-mount delta and takes the fiber sibling chain.
        [Component]
        private static VNode SharedTargetHostRender()
        {
            var (count, setCount) = Hooks.UseState(1);
            s_grow = setCount;
            return V.Fragment(children: new VNode?[]
            {
                V.Portal("shared", children: Enumerable.Range(0, count)
                    .Select(index => (VNode?)V.Div(name: index == 0 ? "toast-one" : "toast-two")).ToArray()),
                V.Portal("shared", children: new VNode?[] { V.Component(ModalChildRender, key: "modal") }),
            });
        }

        // GREEN_ON_BASE(characterization): the base already patches the right element, and nothing said
        // so. What holds it up is `ComponentRegistry`'s refresh of `MountSlotStart` from the reconcile
        // site — measured, by deleting that line, which fails this case.
        [Test]
        public void Given_TwoPortalsOnOneTarget_When_TheFirstGrowsAndTheSecondsChildRerenders_Then_ItPatchesItsOwnElement()
        {
            // Arrange
            var target = new VisualElement();
            FiberPortalRegistry.Register("shared", target);
            _mounted = V.Mount(new VisualElement(), V.Component(SharedTargetHostRender, key: "host"));
            _mounted.FlushEffectsForTest();
            Assume.That(Names(target), Is.EqualTo("toast-one|content"), "Precondition: both portals mounted in order");
            s_grow!.Invoke(2);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            Assume.That(Names(target), Is.EqualTo("toast-one|toast-two|content"),
                        "Precondition: the first portal grew and the second's element moved with it");

            // Act — the second portal's own child re-renders on its own state.
            s_change!.Invoke(1);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(Names(target), Is.EqualTo("toast-one|toast-two|changed"));
        }
    }
}

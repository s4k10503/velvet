using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which siblings an inline-mount fiber's committed child-count delta reaches: the ones
    /// mounted on the same target, and no others. Two portals declared side by side are siblings in the
    /// fiber chain whatever targets they name, so the chain alone does not say whose coordinate a delta
    /// is a coordinate into.
    /// </summary>
    internal sealed class PortalSlotShiftScopeTests
    {
        private MountedTree? _mounted;
        private static Action<int>? s_grow;
        private static Action<int>? s_settle;

        [SetUp]
        public void SetUp()
        {
            s_grow = null;
            s_settle = null;
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

        // The growing side is a component, so its extra row is a committed child-count delta on the
        // portal's own target rather than a change to the declaring tree's children.
        [Component]
        private static VNode Growing()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_grow = setRows;
            return V.Fragment(children: Enumerable.Range(0, rows)
                .Select(index => (VNode?)V.Div(name: "g" + index)).ToArray());
        }

        // It holds state so it can re-render on its own after the other side's delta is committed --
        // which is when a coordinate rewritten by that delta is first written at.
        [Component]
        private static VNode Settled()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_settle = setTick;
            return V.Div(name: "s" + tick);
        }

        [Component]
        private static VNode TwoTargets() => V.Fragment(children: new VNode?[]
        {
            V.Portal("scope-a", children: new VNode?[] { V.Component(Growing, key: "grow") }),
            V.Portal("scope-b", children: new VNode?[] { V.Component(Settled, key: "settled") }),
        });

        [Component]
        private static VNode OneTarget() => V.Fragment(children: new VNode?[]
        {
            V.Portal("scope-a", children: new VNode?[]
            {
                V.Component(Growing, key: "grow"),
                V.Component(Settled, key: "settled"),
            }),
        });

        private (VisualElement A, VisualElement B) MountAndGrow(Func<VNode> host)
        {
            var a = new VisualElement();
            var b = new VisualElement();
            // `b` starts occupied so a shift shows against a non-zero base rather than against zero.
            b.Add(new VisualElement { name = "own" });
            FiberPortalRegistry.Register("scope-a", a);
            FiberPortalRegistry.Register("scope-b", b);
            _mounted = V.Mount(new VisualElement(), V.Component(host, key: "host"));
            _mounted.FlushEffectsForTest();
            s_grow!.Invoke(2);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();
            return (a, b);
        }

        [Test]
        public void Given_TwoPortalsOnDifferentTargets_When_OneGrows_Then_TheOthersNextWriteStaysWhereItWas()
        {
            // Arrange
            var (a, b) = MountAndGrow(() => V.Component(TwoTargets, key: "two"));
            Assume.That(Names(a), Is.EqualTo("g0|g1"), "Precondition: the growing side committed its delta");

            Assume.That(Names(b), Is.EqualTo("own|s0"), "Precondition: the settled side is where it mounted");

            // Act — the settled side re-renders on its own, writing at the coordinate it holds.
            s_settle!.Invoke(1);
            _mounted!.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(Names(b), Is.EqualTo("own|s1"));
        }

        // GREEN_ON_BASE(characterization): the same-target direction the base already gets right. It is
        // what a guard written as "skip every sibling" would take away, and the case above would not
        // notice that guard at all.
        [Test]
        public void Given_TwoComponentsOnOneTarget_When_TheFirstGrows_Then_TheSecondMovesWithIt()
        {
            // Arrange & Act — the control for the case above: on one target the delta is a coordinate
            // into the same list, and the second component's row has to follow it.
            var (a, _) = MountAndGrow(() => V.Component(OneTarget, key: "one"));

            // Assert
            Assert.That(Names(a), Is.EqualTo("g0|g1|s0"));
        }
    }
}

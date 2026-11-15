using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the ranges recorded for a Portal declared inside another Portal's children on the same target, once
    /// the inner one's rows change in a pass that patches the outer one too: each records the rows its own children
    /// put on the target, and the ranges do not overlap.
    /// </summary>
    [TestFixture]
    internal sealed class NestedPortalRangeTests
    {
        private static StateUpdater<int> s_setInner;
        private static VisualElement s_target;

        [SetUp]
        public void SetUp()
        {
            s_target = new VisualElement();
            FiberStrictMode.Enabled = false;
        }

        [Component]
        private static VNode Host()
        {
            var (inner, setInner) = Hooks.UseState(1);
            s_setInner = setInner;
            var labels = new VNode[inner];
            for (var i = 0; i < inner; i++) labels[i] = V.Label(text: "b" + i, key: "b" + i);
            return V.Div(children: new VNode[]
            {
                V.Portal(s_target, key: "outer", children: new VNode[]
                {
                    V.Label(text: "a", key: "a"),
                    V.Portal(s_target, key: "inner", children: labels),
                }),
            });
        }

        private static (int Start, int Length)[] RangesAfterGrowingTheInnerTo(int inner)
        {
            using var mounted = V.Mount(new VisualElement(), V.Component(Host, key: "host"));
            s_setInner.Invoke(inner);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            return mounted.Root.Reconciler.Context.PortalState
                .Select(pair => (pair.Value.SlotStart, pair.Value.SlotLength))
                .OrderBy(range => range.SlotStart)
                .ToArray();
        }

        [Test]
        public void Given_APortalInsideAnotherOnOneTarget_When_TheInnersRowsGrowInThePassPatchingTheOuter_Then_TheOuterRecordsOnlyItsOwnRows()
        {
            // Act
            var ranges = RangesAfterGrowingTheInnerTo(3);

            // Assert
            Assert.That(ranges[0].Length, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the inner's own length was recorded in full before the outer stopped counting it.
        [Test]
        public void Given_APortalInsideAnotherOnOneTarget_When_TheInnersRowsGrowInThePassPatchingTheOuter_Then_TheInnerRecordsAllItsRows()
        {
            // Act
            var ranges = RangesAfterGrowingTheInnerTo(3);

            // Assert
            Assert.That(ranges[1].Length, Is.EqualTo(3));
        }

        [Test]
        public void Given_APortalInsideAnotherOnOneTarget_When_TheInnersRowsGrowInThePassPatchingTheOuter_Then_TheInnerStartsWhereTheOuterEnds()
        {
            // Act
            var ranges = RangesAfterGrowingTheInnerTo(3);

            // Assert
            Assert.That(ranges[1].Start - (ranges[0].Start + ranges[0].Length), Is.EqualTo(0));
        }
    }
}

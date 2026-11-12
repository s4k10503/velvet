using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the commit sequence of a tree holding a <c>V.Portal</c> as one <see cref="EventLog"/> string: React 19's
    /// <c>ReactFiberCommitWork</c> walks a portal's children where the portal sits among its siblings, whichever
    /// container they mount into.
    /// </summary>
    [TestFixture]
    internal sealed class PortalCommitOrderEventLogTests
    {
        private sealed record Named(string Name);

        private sealed record ParentProps(VisualElement Target, bool LastRendersNothing);

        private sealed record Dep(string Name, int Value);

        private sealed class Slicing
        {
            internal ComponentFiber? Fiber;
            internal int Version;
        }

        private sealed record SlicedProps(VisualElement Target, Slicing Slicing);

        private const int SlicedRowCount = 4;

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Empty(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode PortalParent(ParentProps props)
        {
            EventLog.UseEffects("P", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Probed, new Named("A"), key: "a"),
                V.Portal(props.Target, new VNode[] { V.Component(Probed, new Named("PC"), key: "pc") }, key: "portal"),
                props.LastRendersNothing
                    ? V.Component(Empty, new Named("N"), key: "n")
                    : V.Component(Probed, new Named("B"), key: "b"),
            });
        }

        [Component]
        private static VNode Leaf(Dep props)
        {
            EventLog.UseEffects(props.Name, new object[] { props.Value });
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode PortalChild()
        {
            var (value, _) = EventLog.UseState("PortalChild", 0);
            return V.Fragment(V.Component(Leaf, new Dep("Leaf", value), key: "leaf"), V.Div());
        }

        [Component]
        private static VNode After()
        {
            var (value, _) = EventLog.UseState("After", 0);
            EventLog.UseEffects("After", new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode PortalThenSibling(ParentProps props)
            => V.Fragment(
                V.Portal(props.Target, new VNode[] { V.Component(PortalChild, key: "child") }, key: "portal"),
                V.Component(After, key: "after"));

        [Component]
        private static VNode SlicedPortalHost(SlicedProps props)
            => V.Fragment(
                V.Portal(props.Target, new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(SlicedRows, props.Slicing, key: "rows") }),
                }, key: "portal"),
                V.Component(After, key: "after"));

        // Keyed elements with a component one level down, so a time-sliced update of this component parks between
        // rows. Its version is read off the holder and the test schedules the Transition lane itself, so no
        // UseTransition slot's pending-clear render follows the slices; and with the compiler off, nothing memoizes
        // the rows against that read.
        [Component(Compiler = false)]
        private static VNode SlicedRows(Slicing slicing)
        {
            slicing.Fiber = FiberAmbientStack.Current;
            EventLog.UseRender("Rows");
            var version = slicing.Version;
            var rows = new VNode[SlicedRowCount];
            for (var i = 0; i < SlicedRowCount; i++)
            {
                rows[i] = V.Div(key: "row" + i,
                    children: new VNode?[] { V.Component(SlicedRow, new Dep("R" + i, version), key: "row") });
            }
            return V.Fragment(rows);
        }

        [Component]
        private static VNode SlicedRow(Dep props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            EventLog.UseEffects(props.Name, new object[] { value });
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode ChildRenderingAComponentThenAnElement()
        {
            EventLog.UseEffects("C1", Array.Empty<object>());
            return V.Fragment(V.Component(Empty, new Named("E"), key: "e"), V.Div(refCallback: EventLog.UseRefCallback("D")));
        }

        [Component]
        private static VNode PortalOfAChildWithAComponentThenAnElement(ParentProps props)
            => V.Fragment(V.Portal(props.Target,
                new VNode[] { V.Component(ChildRenderingAComponentThenAnElement, key: "c1") }, key: "portal"));

        [Component]
        private static VNode VirtualRow(Named props)
        {
            var (value, _) = EventLog.UseState(props.Name, 0);
            EventLog.UseEffects(props.Name, new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode VirtualListInAPortalThenSibling(ParentProps props)
            => V.Fragment(
                V.Portal(props.Target, new VNode[]
                {
                    V.VirtualList(
                        items: new[] { "R" },
                        keySelector: item => item,
                        itemHeight: 50f,
                        renderer: item => V.Component(VirtualRow, new Named(item), key: item),
                        overscan: 0),
                }, key: "portal"),
                V.Component(After, key: "after"));

        private static string MountAndLog(bool lastRendersNothing)
        {
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalParent, new ParentProps(new VisualElement(), lastRendersNothing))));
            mounted.DrainForTest();
            return log.ToString();
        }

        [Test]
        public void Given_APortalBetweenTwoSiblings_When_Mounted_Then_ThePortalsChildCommitsAtThePortalsPositionInEveryPhase()
        {
            // Arrange
            const bool lastRendersNothing = false;

            // Act
            var logged = MountAndLog(lastRendersNothing);

            // Assert
            Assert.That(logged, Is.EqualTo(
                "ins:A ins:PC ins:B ins:P ref:A lay:A ref:PC lay:PC ref:B lay:B ref:P lay:P eff:A eff:PC eff:B eff:P"));
        }

        [Test]
        public void Given_APortalFollowedByAComponentRenderingNothing_When_Mounted_Then_ThePortalsChildCommitsAheadOfThatComponent()
        {
            // Arrange
            const bool lastRendersNothing = true;

            // Act
            var logged = MountAndLog(lastRendersNothing);

            // Assert
            Assert.That(logged, Is.EqualTo(
                "ins:A ins:PC ins:N ins:P ref:A lay:A ref:PC lay:PC lay:N ref:P lay:P eff:A eff:PC eff:N eff:P"));
        }

        [Test]
        public void Given_APortalChildAndASiblingAfterThePortalUpdatedTogether_When_TheyCommit_Then_ThePortalChildsEffectsRunFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalThenSibling, new ParentProps(new VisualElement(), false))));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("PortalChild").Invoke(1);
            log.Setter<int>("After").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ins-x:Leaf ins:Leaf lay-x:Leaf ins-x:After ins:After lay-x:After lay:Leaf lay:After"
                + " eff-x:Leaf eff-x:After eff:Leaf eff:After"));
        }

        [Test]
        public void Given_APortalChildRenderingAComponentAheadOfAnElementWithARef_When_Mounted_Then_TheRefAttachesBetweenTheirLayoutEffects()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = V.Mount(new VisualElement(), log.Provide(
                V.Component(PortalOfAChildWithAComponentThenAnElement, new ParentProps(new VisualElement(), false))));
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:E ins:C1 lay:E ref:D lay:C1 eff:E eff:C1"));
        }

        [Test]
        public void Given_AVirtualListRowInAPortalAndASiblingAfterThePortalUpdatedTogether_When_TheyCommit_Then_TheRowsEffectsRunFirst()
        {
            // Arrange — the row renders from a range update outside any pass, the way a scroll renders one
            var log = new EventLog();
            var target = new VisualElement();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(VirtualListInAPortalThenSibling, new ParentProps(target, false))));
            mounted.DrainForTest();
            var list = mounted.Root.Reconciler!.Context.VirtualListControllers[target.Q<ScrollView>()];
            list.UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("R").Invoke(1);
            log.Setter<int>("After").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ins-x:R ins:R lay-x:R ins-x:After ins:After lay-x:After lay:R lay:After"
                + " eff-x:R eff-x:After eff:R eff:After"));
        }

        // GREEN_ON_BASE(characterization): the base never names a Portal for a component's own render to take back.
        // What this pins is that the render of a Portal's child, which now names its Portal, takes it back.
        [Test]
        public void Given_APortalChildThatRendersOnItsOwn_When_ThatRenderCommits_Then_NoPortalIsLeftInFlight()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalThenSibling, new ParentProps(new VisualElement(), false))));
            mounted.DrainForTest();

            // Act
            log.Setter<int>("PortalChild").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(mounted.Root.Reconciler!.Context.CurrentPortalPlaceholder, Is.Null);
        }

        [Test]
        public void Given_APortalChildsRowReachedByAResumedSliceOfItsUpdate_When_ThatRowAndASiblingAfterThePortalUpdateTogether_Then_TheRowsEffectsRunFirst()
        {
            // Arrange — a transition of a component inside the Portal parks, and its resumed slices reach the rows it
            // left
            var log = new EventLog();
            var slicing = new Slicing();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(SlicedPortalHost, new SlicedProps(new VisualElement(), slicing))));
            mounted.DrainForTest();
            slicing.Version = 1;
            slicing.Fiber!.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            slicing.Fiber.FlushStateWithTinyBudgetForTest();
            var parked = slicing.Fiber.HasPendingReconcileWorkForTest();
            log.Clear();
            slicing.Fiber.DrainTimeSlicedReconcileForTest();
            var survivedTheSlices = slicing.Fiber.IsMounted;
            mounted.DrainForTest();
            var renderedAfterTheSlices = log.ToString().Contains("render:Rows");
            log.Clear();
            var last = "R" + (SlicedRowCount - 1);

            // Act
            log.Setter<int>(last).Invoke(1);
            log.Setter<int>("After").Invoke(1);
            mounted.DrainForTest();

            // Assert — a pass that never parked reaches every row in its first slice, and a render of the component
            // after its slices reaches every row again, so neither says anything here
            var unmet = !parked ? "the transition's pass never parked"
                : !survivedTheSlices ? "the component was unmounted while its slices resumed"
                : renderedAfterTheSlices ? "the component rendered again after its slices"
                : null;
            Assert.That(unmet ?? log.ToString(), Is.EqualTo(
                $"ins-x:{last} ins:{last} lay-x:{last} ins-x:After ins:After lay-x:After lay:{last} lay:After"
                + $" eff-x:{last} eff-x:After eff:{last} eff:After"));
        }
    }
}

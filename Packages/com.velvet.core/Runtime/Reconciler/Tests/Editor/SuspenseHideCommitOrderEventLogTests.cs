using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the commit sequence of a revealed <c>V.Suspense</c> that suspends again as one <see cref="EventLog"/>
    /// string: React 19's <c>disappearLayoutEffects</c> walks the hidden tree in pre-order, running a component's
    /// layout cleanups as it reaches the component and detaching an element's ref as it reaches the element.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseHideCommitOrderEventLogTests
    {
        private enum Shape
        {
            Nested,
            PortalInside,
            EmptyLast,
            EmptyMountedFirst,
            Relocated,
        }

        private sealed record Named(string Name);

        private sealed record HostProps(Shape Shape, VisualElement Target);

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
        private static VNode Outer()
        {
            EventLog.UseEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback("O");
            var refO2 = EventLog.UseRefCallback("O2");
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Component(Probed, new Named("I"), key: "i"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component]
        private static VNode PortalOuter(HostProps props)
        {
            EventLog.UseEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback("O");
            var refO2 = EventLog.UseRefCallback("O2");
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Portal(props.Target, new VNode[] { V.Component(Probed, new Named("PC"), key: "pc") }, key: "portal"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component]
        private static VNode TrailingOuter()
        {
            EventLog.UseEffects("O", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("O"), children: new VNode[]
            {
                V.Component(Probed, new Named("I"), key: "i"),
                V.Component(Empty, new Named("N"), key: "n"),
            });
        }

        [Component]
        private static VNode LeadingOuter()
        {
            var (lead, _) = EventLog.UseState("Lead", false);
            EventLog.UseEffects("O", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("O"), children: new VNode[]
            {
                lead ? V.Component(Empty, new Named("N"), key: "n") : null,
                V.Component(Probed, new Named("I"), key: "i"),
            });
        }

        [Component]
        private static VNode RelocatingOuter()
        {
            EventLog.UseEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback("O");
            var refZ = EventLog.UseRefCallback("Z");
            return V.Div(className: "relative", refCallback: refO, children: new VNode[]
            {
                V.Component(Probed, new Named("A"), key: "a"),
                V.Div(className: "absolute -z-10", refCallback: refZ, key: "z"),
                V.Component(Probed, new Named("B"), key: "b"),
            });
        }

        // Pending for good once blocked, so the boundary keeps its fallback for the rest of the case.
        [Component]
        private static VNode Reader(int blocked)
        {
            var value = Hooks.Use(
                () => blocked > 0 ? new VelvetTaskCompletionSource<int>().Task : VelvetTask.FromResult(0), blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode SuspenseHost(HostProps props)
        {
            var (blocked, _) = EventLog.UseState("Block", 0);
            var reader = V.Component(Reader, blocked, key: "reader");
            return V.Suspense(
                fallback: V.Component(Probed, new Named("F"), key: "f"),
                children: props.Shape switch
                {
                    Shape.Nested => new VNode[] { V.Component(Outer, key: "outer"), reader },
                    Shape.PortalInside => new VNode[] { V.Component(PortalOuter, props, key: "outer"), reader },
                    Shape.EmptyLast => new VNode[]
                    {
                        V.Component(TrailingOuter, key: "outer"), V.Component(Probed, new Named("Z"), key: "z"), reader,
                    },
                    Shape.EmptyMountedFirst => new VNode[] { V.Component(LeadingOuter, key: "outer"), reader },
                    _ => new VNode[] { V.Component(RelocatingOuter, key: "outer"), reader },
                });
        }

        private static MountedTree MountShown(EventLog log, Shape shape)
        {
            var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(SuspenseHost, new HostProps(shape, new VisualElement()))));
            mounted.DrainForTest();
            return mounted;
        }

        private static void Suspend(EventLog log, MountedTree mounted)
        {
            log.Clear();
            log.Setter<int>("Block").Invoke(1);
            mounted.DrainForTest();
        }

        [Test]
        public void Given_ARevealedPrimaryWithNestedComponentsAndRefs_When_ItSuspendsAgain_Then_LayoutCleanupsAndRefDetachesInterleaveInPreOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.Nested);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:I ref-x:I ref-x:O2 ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_ARevealedPrimaryWithAPortalInsideAnElement_When_ItSuspendsAgain_Then_ThePortalsChildIsTakenDownWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalInside);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:PC ref-x:PC ref-x:O2 ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_AComponentRenderingNothingLastInAnElementWithASiblingAfterTheElement_When_ItSuspendsAgain_Then_ItsCleanupRunsBeforeTheSiblings()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.EmptyLast);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:I ref-x:I lay-x:N lay-x:Z ref-x:Z ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_AComponentRenderingNothingMountedInFrontOfAnEarlierOne_When_ItSuspendsAgain_Then_ItsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.EmptyMountedFirst);
            log.Setter<bool>("Lead").Invoke(true);
            mounted.DrainForTest();

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:N lay-x:I ref-x:I ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_ARevealedPrimaryWithAnElementItsZClassPaintsBehindItsSiblings_When_ItSuspendsAgain_Then_ItsRefDetachesAtTheSlotItWasDeclaredAt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.Relocated);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:A ref-x:A ref-x:Z lay-x:B ref-x:B ins:F ref:F lay:F eff:F"));
        }
    }
}

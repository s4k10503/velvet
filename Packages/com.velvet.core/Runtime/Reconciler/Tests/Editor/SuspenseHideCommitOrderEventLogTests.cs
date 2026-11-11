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
            RelocatedFirst,
            PortalChildEmpty,
            PortalChildRelocated,
            PortalIntoOwnElement,
            PortalIntoOwnElementRelocated,
            PortalIntoOwnElementAfterEmpty,
            TopLevelPortalEmpty,
            PortalInPortalRow,
            PortalToMissingTarget,
            PortalChildRenderedAlone,
        }

        private sealed record Named(string Name, string? ClassName = null);

        private sealed class Holder
        {
            internal VisualElement? Element;
        }

        private sealed record HostProps(Shape Shape, VisualElement Target, VisualElement Second, Holder Own);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(className: props.ClassName, refCallback: EventLog.UseRefCallback(props.Name));
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
            var refPZ = EventLog.UseRefCallback("PZ");
            VNode portalChild = props.Shape switch
            {
                Shape.PortalChildEmpty => V.Component(Empty, new Named("PC"), key: "pc"),
                Shape.PortalChildRelocated => V.Div(className: "absolute z-10", refCallback: refPZ, key: "pz"),
                Shape.PortalInPortalRow => V.Component(InnerPortal, props, key: "inner"),
                _ => V.Component(Probed, new Named("PC"), key: "pc"),
            };
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Portal(props.Target, new VNode[] { portalChild }, key: "portal"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component(Memoize = true)]
        private static VNode MemoizedPortalOuter(HostProps props)
        {
            var refO = EventLog.UseRefCallback("O");
            var refO2 = EventLog.UseRefCallback("O2");
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Portal(props.Target, new VNode[] { V.Component(SelfUpdating, key: "c1") }, key: "portal"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component]
        private static VNode SelfUpdating()
        {
            EventLog.UseState("C1", 0);
            EventLog.UseEffects("C1", Array.Empty<object>());
            var refD = EventLog.UseRefCallback("D");
            return V.Fragment(V.Component(Empty, new Named("E"), key: "e"), V.Div(refCallback: refD, key: "d"));
        }

        [Component]
        private static VNode InnerPortal(HostProps props)
            => V.Portal(props.Second, new VNode[] { V.Component(Probed, new Named("PC"), key: "pc") });

        [Component]
        private static VNode PortalIntoOwnOuter(HostProps props)
        {
            var (show, _) = EventLog.UseState("Show", false);
            EventLog.UseEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback("O");
            var refPZ = EventLog.UseRefCallback("PZ");
            var keepTarget = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                props.Own.Element = element;
                return null;
            }, Array.Empty<object>());
            VNode portalChild = props.Shape == Shape.PortalIntoOwnElementRelocated
                ? V.Div(className: "absolute z-10", refCallback: refPZ, key: "pz")
                : V.Component(Probed, new Named("PC"), key: "pc");
            var targetChildren = props.Shape == Shape.PortalIntoOwnElementAfterEmpty
                ? new VNode[] { V.Component(Empty, new Named("TN"), key: "tn") }
                : Array.Empty<VNode>();
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Div(refCallback: keepTarget, key: "target", children: targetChildren),
                V.Component(Probed, new Named("B"), key: "b"),
                show ? V.Portal(props.Own.Element!, new VNode[] { portalChild }, key: "portal") : null,
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
            // The positioned parent is kept apart from O's element: the case is about Z's slot, and the case whose
            // first element is relocated owns what becomes of a component starting there.
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Div(className: "relative", children: new VNode[]
                {
                    V.Component(Probed, new Named("A"), key: "a"),
                    V.Div(className: "absolute -z-10", refCallback: refZ, key: "z"),
                    V.Component(Probed, new Named("B"), key: "b"),
                }),
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
                    Shape.EmptyLast => new VNode[]
                    {
                        V.Component(TrailingOuter, key: "outer"), V.Component(Probed, new Named("Z"), key: "z"), reader,
                    },
                    Shape.EmptyMountedFirst => new VNode[] { V.Component(LeadingOuter, key: "outer"), reader },
                    Shape.Relocated => new VNode[] { V.Component(RelocatingOuter, key: "outer"), reader },
                    Shape.RelocatedFirst => new VNode[]
                    {
                        V.Component(Probed, new Named("G", "absolute z-10"), key: "outer"), reader,
                    },
                    Shape.PortalIntoOwnElement or Shape.PortalIntoOwnElementRelocated or Shape.PortalIntoOwnElementAfterEmpty
                        => new VNode[] { V.Component(PortalIntoOwnOuter, props, key: "outer"), reader },
                    Shape.PortalChildRenderedAlone => new VNode[]
                    {
                        V.Div(children: new VNode[] { V.Component(MemoizedPortalOuter, props, key: "outer") }), reader,
                    },
                    Shape.PortalToMissingTarget => new VNode[]
                    {
                        V.Div(children: new VNode[]
                        {
                            V.Portal("suspense-hide-missing-target", new VNode[] { V.Component(Probed, new Named("PC"), key: "pc") }),
                        }),
                        reader,
                    },
                    Shape.TopLevelPortalEmpty => new VNode[]
                    {
                        V.Portal(props.Target, new VNode[] { V.Component(Empty, new Named("PC"), key: "pc") }, key: "portal"),
                        V.Component(Probed, new Named("B"), key: "b"),
                        reader,
                    },
                    _ => new VNode[] { V.Component(PortalOuter, props, key: "outer"), reader },
                });
        }

        private static MountedTree MountShown(EventLog log, Shape shape)
        {
            var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(SuspenseHost, new HostProps(shape, new VisualElement(), new VisualElement(), new Holder()))));
            mounted.DrainForTest();
            return mounted;
        }

        private static MountedTree MountShownWithItsPortal(EventLog log, Shape shape)
        {
            var mounted = MountShown(log, shape);
            log.Setter<bool>("Show").Invoke(true);
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
        [Test]
        public void Given_ARevealedPrimaryWhoseFirstElementItsZClassRelocates_When_ItSuspendsAgain_Then_ItsComponentIsTakenDownAheadOfItsRef()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.RelocatedFirst);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:G ref-x:G ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalWhoseChildRendersNothingInsideAnElement_When_ItSuspendsAgain_Then_TheChildIsTakenDownWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalChildEmpty);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:O ref-x:O lay-x:PC ref-x:O2 ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalWhoseElementItsZClassRelocates_When_ItSuspendsAgain_Then_ThatElementsRefDetachesWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalChildRelocated);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:O ref-x:O ref-x:PZ ref-x:O2 ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalIntoAnEarlierElementOfTheSamePrimary_When_ItSuspendsAgain_Then_ItsChildIsTakenDownWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShownWithItsPortal(log, Shape.PortalIntoOwnElement);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:B ref-x:B lay-x:PC ref-x:PC ins:F ref:F lay:F eff:F"));
        }
        [Test]
        public void Given_APortalIntoAnEarlierElementWhoseRowItsZClassRelocates_When_ItSuspendsAgain_Then_ThatRowsRefDetachesWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShownWithItsPortal(log, Shape.PortalIntoOwnElementRelocated);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:O ref-x:O lay-x:B ref-x:B ref-x:PZ ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalIntoAnElementWhoseOwnChildRendersNothing_When_ItSuspendsAgain_Then_ThatChildIsTakenDownInItsElement()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShownWithItsPortal(log, Shape.PortalIntoOwnElementAfterEmpty);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:O ref-x:O lay-x:TN lay-x:B ref-x:B lay-x:PC ref-x:PC ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APrimaryWhosePortalChildRendersNothing_When_ItSuspendsAgain_Then_TheChildIsTakenDownWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.TopLevelPortalEmpty);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:PC lay-x:B ref-x:B ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalInsideAnotherPortalsChildren_When_ItSuspendsAgain_Then_ItsChildIsTakenDownWhereTheInnerPortalSits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalInPortalRow);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:O ref-x:O lay-x:PC ref-x:PC ref-x:O2 ins:F ref:F lay:F eff:F"));
        }

        // GREEN_ON_BASE(characterization): the base takes a hidden Portal with no target down without reading rows.
        // What this pins is that the walk, which reads a Portal's rows where it sits, reads none there.
        [Test]
        public void Given_APortalWhoseRegisteredTargetIsMissing_When_ItsPrimarySuspendsAgain_Then_OnlyTheFallbackCommits()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalToMissingTarget);

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins:F ref:F lay:F eff:F"));
        }

        [Test]
        public void Given_APortalChildThatRenderedAloneUnderAComponentThatBails_When_ItSuspendsAgain_Then_ItsChildRenderingNothingIsTakenDownWhereItSits()
        {
            // Arrange — the Portal's child re-renders on its own, under a memoized component inside an element
            var log = new EventLog();
            using var mounted = MountShown(log, Shape.PortalChildRenderedAlone);
            log.Setter<int>("C1").Invoke(1);
            mounted.DrainForTest();

            // Act
            Suspend(log, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "ref-x:O lay-x:C1 lay-x:E ref-x:D ref-x:O2 ins:F ref:F lay:F eff:F"));
        }
    }
}

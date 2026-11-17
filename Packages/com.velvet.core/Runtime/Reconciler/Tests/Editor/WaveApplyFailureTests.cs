using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that a component's host writes throw where one drain applies them — an element's
    /// panel-attach callback, here — as React's captureCommitPhaseError sends an error its commit meets: to the nearest
    /// boundary above the component whose render recorded the write, which renders its fallback in a follow-up commit.
    /// Two such errors under one boundary are both reported; an error whose component a later component's render
    /// error has already replaced with the boundary's fallback is not. The guards hold that each error reaches the
    /// boundary of its own component, whichever of the drain's components it came from, including where a later
    /// component's Portal ends the drain's pass early and where the write is a <c>z-*</c> placement.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs an attach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class WaveApplyFailureTests
    {
        private HeadlessEditorPanelHost _host;
        private static VisualElement s_portalTarget;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_portalTarget = new VisualElement();
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-attach callback that throws.
        internal sealed class AttachThrower : VisualElement
        {
            public AttachThrower()
            {
                RegisterCallback<AttachToPanelEvent>(_ => throw new InvalidOperationException("attach:" + name));
            }
        }

        private sealed record Named(string Name);

        private sealed record Holds(VNode Child);

        [Component]
        private static VNode MountsAThrower(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            return V.Div(children: new VNode[]
            {
                shown ? V.Custom<AttachThrower>(name: props.Name, key: "thrower") : null,
            });
        }

        // The element is relocated (FiberZLayerCoordinator), so its arrival in its parent waits for the pass's drain.
        [Component]
        private static VNode MountsAStackedThrower(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            return V.Div(className: "relative", children: new VNode[]
            {
                shown ? V.Custom<AttachThrower>(className: "absolute z-10", name: props.Name, key: "thrower") : null,
            });
        }

        [Component]
        private static VNode MountsADiv(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            return V.Div(children: new VNode[] { shown ? V.Div(name: props.Name, key: "div") : null });
        }

        [Component]
        private static VNode ThrowsInRender(Named props)
        {
            var (throws, _) = EventLog.UseState(props.Name, false);
            if (throws) throw new InvalidOperationException("render:" + props.Name);
            return V.Div();
        }

        [Component]
        private static VNode Modal()
        {
            EventLog.UseRender("Modal");
            return V.Div();
        }

        [Component]
        private static VNode ShowsAPortal(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            return V.Div(children: new VNode[]
            {
                shown ? V.Portal(s_portalTarget, new VNode[] { V.Component(Modal, key: "modal") }, key: "portal") : null,
            });
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode BothThrowAsTheyAttach()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Component(MountsAThrower, new Named("A"), key: "a"),
                V.Component(MountsAThrower, new Named("C"), key: "c"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode AttachThrowThenRenderThrow()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Component(MountsAThrower, new Named("A"), key: "a"),
                V.Component(ThrowsInRender, new Named("C"), key: "c"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "B1")]
        private static VNode FirstBoundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-1"));
            return props.Child;
        }

        [Component(IsErrorBoundary = true, DisplayName = "B2")]
        private static VNode SecondBoundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-2"));
            return props.Child;
        }

        [Component]
        private static VNode TwoBoundaries(Holds props)
            => V.Fragment(
                V.Component(FirstBoundary, new Holds(V.Component(MountsAThrower, new Named("R1"), key: "r1")), key: "b1"),
                V.Component(SecondBoundary, props, key: "b2"));

        [Component]
        private static VNode QuietThenThrowing()
            => V.Fragment(
                V.Component(FirstBoundary, new Holds(V.Component(MountsADiv, new Named("R1"), key: "r1")), key: "b1"),
                V.Component(SecondBoundary, new Holds(V.Component(MountsAThrower, new Named("R2"), key: "r2")), key: "b2"));

        [Component]
        private static VNode StackedThrowerThenPortal()
            => V.Fragment(
                V.Component(FirstBoundary, new Holds(V.Component(MountsAStackedThrower, new Named("R1"), key: "r1")),
                    key: "b1"),
                V.Component(SecondBoundary, new Holds(V.Component(ShowsAPortal, new Named("R2"), key: "r2")), key: "b2"));

        private MountedTree Mount(EventLog log, VNode tree)
            => V.Mount(_host.Root, log.Provide(tree), log.ReportingErrors());

        [Test]
        public void Given_TwoComponentsUnderOneBoundaryWhoseNewElementsThrowAsTheyAttach_When_OneDrainRendersBoth_Then_TheBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(BothThrowAsTheyAttach));
            log.Clear();

            // Act
            log.Setter<bool>("A").Invoke(true);
            log.Setter<bool>("C").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:attach:A caught:B:attach:C"));
        }

        [Test]
        public void Given_AComponentWhoseNewElementThrowsAsItAttachesAndALaterOneWhoseRenderThrows_When_OneDrainRendersBoth_Then_TheBoundaryReportsOnlyTheRenderError()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(AttachThrowThenRenderThrow));
            log.Clear();

            // Act
            log.Setter<bool>("A").Invoke(true);
            log.Setter<bool>("C").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B:render:C"));
        }

        // GREEN_ON_BASE(characterization): the base catches the error in the pass of the component that threw it.
        // Taking the root of every op from the last of ReconcilePass.Segments in ChildListWrites.Apply reddens it.
        [Test]
        public void Given_TwoComponentsUnderBoundariesOfTheirOwnAndTheFirstsNewElementThrowsAsItAttaches_When_OneDrainRendersBoth_Then_TheFirstsBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoBoundaries,
                new Holds(V.Component(MountsADiv, new Named("R2"), key: "r2"))));
            log.Clear();

            // Act
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<bool>("R2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B1:attach:R1"));
        }

        // GREEN_ON_BASE(characterization): the base catches the error in the pass of the component that threw it.
        // Taking the root of every op from the first of ReconcilePass.Segments in ChildListWrites.Apply reddens it.
        [Test]
        public void Given_TwoComponentsUnderBoundariesOfTheirOwnAndTheSecondsNewElementThrowsAsItAttaches_When_OneDrainRendersBoth_Then_TheSecondsBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(QuietThenThrowing));
            log.Clear();

            // Act
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<bool>("R2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B2:attach:R2"));
        }

        // GREEN_ON_BASE(characterization): the base catches the error in the first component's pass, apart from the Portal's.
        // Making Reconciler.FinishPass throw what the wave's pass met into the render whose Portal ended it reddens it.
        [Test]
        public void Given_AComponentWhoseNewElementThrowsAsItAttachesAndALaterOneUnderAnotherBoundaryThatShowsAPortal_When_OneDrainRendersBoth_Then_TheFirstsBoundaryReportsItAndThePortalsChildRenders()
        {
            // Arrange — the later component's Portal ends the drain's pass inside that component's render
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoBoundaries,
                new Holds(V.Component(ShowsAPortal, new Named("R2"), key: "r2"))));
            log.Clear();

            // Act
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<bool>("R2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Modal caught:B1:attach:R1"));
        }

        // GREEN_ON_BASE(characterization): the base drains the z-* placement in the first component's own pass.
        // Taking out the catch in ChildReconciler.PlaceRelocated, which lets the throw end the drain that mounts the
        // Portal queued behind it, reddens it.
        [Test]
        public void Given_AComponentWhoseZPlacementThrowsAsItAttachesAndALaterOneThatShowsAPortal_When_OneDrainRendersBoth_Then_ThePortalsChildRendersAndTheFirstsBoundaryReportsIt()
        {
            // Arrange — the placement and the Portal's mount are queued in the one pass the Portal ends
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(StackedThrowerThenPortal));
            log.Clear();

            // Act
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<bool>("R2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Modal caught:B1:attach:R1"));
        }
    }
}

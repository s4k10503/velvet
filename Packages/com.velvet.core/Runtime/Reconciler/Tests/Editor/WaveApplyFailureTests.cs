using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where an error goes that a new element's panel-attach callback throws as a wave applies its writes: to the
    /// nearest boundary at or above the component that rendered the element the write adds, passing over the components
    /// the wave mounts, as React captures a placement's error from above the topmost fiber new in the commit; the
    /// boundary renders its fallback in a follow-up commit. A boundary that renders the element itself catches it, and
    /// one inside the walk of the component whose update the wave renders does as well, while a boundary that mounts
    /// with the element does not, so a mount's error goes to the root, which takes the tree down. A <c>z-*</c>
    /// placement's error goes the same way, and a Portal's mount files its child's error under the component that
    /// rendered the Portal, whose own mount does not pass it over, and goes on to the Portals queued behind it. The
    /// errors of two writes, or of two <c>z-*</c> placements, are both reported, as is a Portal child's beside a
    /// write's. The error of an element the wave takes off the panel again is not reported, as React commits no host of
    /// a render it discarded: here, one whose component a later component's render error replaced with the boundary's
    /// fallback, and one the boundary's own catch replaced with its fallback; a Portal under a tree that was never on a
    /// panel is still reported. A removal a later drain records is filed with that drain's run, not one an earlier drain
    /// left behind.
    /// </summary>
    /// <remarks>
    /// The tree mounts on a real panel, which is what runs an attach callback.
    /// </remarks>
    [TestFixture]
    internal sealed class WaveApplyFailureTests
    {
        private HeadlessEditorPanelHost _host;
        private VisualElement _mountPoint;
        private static VisualElement s_portalTarget;
        private static VisualElement s_secondTarget;
        private static readonly List<string> s_reports = new();
        private static readonly List<AttachThrower> s_built = new();

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            _mountPoint = new VisualElement();
            _host.Root.Add(_mountPoint);
            s_portalTarget = new VisualElement();
            s_secondTarget = new VisualElement();
            s_reports.Clear();
            s_built.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _host?.Dispose();
            _host = null;
        }

        // Stands in for a V.Custom<T> whose element registers a panel-attach callback that throws. The message says
        // whether the element attached as a relocated z-* element, so a case about a z-* placement fails where the
        // arrangement stops making one. Each one built is kept, so a case whose reading leaves an error out can tell
        // that its render built the element, whether or not the element ever attaches.
        internal sealed class AttachThrower : VisualElement
        {
            public AttachThrower()
            {
                s_built.Add(this);
                RegisterCallback<AttachToPanelEvent>(_ => throw new InvalidOperationException(
                    "attach:" + name + (FiberZLayerCoordinator.IsRelocatedReal(this) ? ":z" : "")));
            }
        }

        private static bool Built(string name) => s_built.Exists(thrower => thrower.name == name);

        private sealed record Named(string Name);

        private sealed record Holds(VNode Child);

        [Component]
        private static VNode MountsAThrower(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            EventLog.UseRender(props.Name);
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
            EventLog.UseRender(props.Name);
            return V.Div(className: "relative", children: new VNode[]
            {
                shown ? V.Custom<AttachThrower>(className: "absolute z-10", name: props.Name, key: "thrower") : null,
            });
        }

        [Component]
        private static VNode MountsADiv(Named props)
        {
            var (shown, _) = EventLog.UseState(props.Name, false);
            EventLog.UseRender(props.Name);
            return V.Div(children: new VNode[] { shown ? V.Div(name: props.Name, key: "div") : null });
        }

        [Component]
        private static VNode ThrowsInRender(Named props)
        {
            var (throws, _) = EventLog.UseState(props.Name, false);
            EventLog.UseRender(props.Name);
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
            EventLog.UseRender(props.Name);
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

        [Component]
        private static VNode MountsTwoThrowers()
        {
            var (shown, _) = EventLog.UseState("Both", false);
            EventLog.UseRender("Both");
            return V.Div(children: new VNode[]
            {
                shown ? V.Custom<AttachThrower>(name: "X", key: "x") : null,
                shown ? V.Custom<AttachThrower>(name: "Y", key: "y") : null,
            });
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode TwoThrowersInOneComponent()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(MountsTwoThrowers, key: "both");
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
            => V.Mount(_mountPoint, log.Provide(tree), log.ReportingErrors());

        [Component(Compiler = false)]
        private static VNode LaysOut()
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:S");
                return () => log.Add("lay-x:S");
            }, Array.Empty<object>());
            return V.Div();
        }

        // The root takes the error by rendering nothing, which takes down what the mount committed.
        [Test]
        public void Given_ATreeWhoseElementThrowsAsItAttaches_When_ItMounts_Then_TheMountsLayoutEffectsRunAndAreCleanedUpBeforeTheRootReportsTheError()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = Mount(log, V.Fragment(
                V.Component(LaysOut, key: "lays-out"),
                V.Custom<AttachThrower>(name: "M", key: "thrower")));

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:S lay-x:S uncaught:attach:M"));
        }

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
            Assert.That(log.ToString(), Is.EqualTo("render:A render:C caught:B:attach:A caught:B:attach:C"));
        }

        [Test]
        public void Given_AComponentWhoseTwoNewElementsThrowAsTheyAttach_When_ADrainRendersIt_Then_ItsBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoThrowersInOneComponent));
            log.Clear();

            // Act
            log.Setter<bool>("Both").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Both caught:B:attach:X caught:B:attach:Y"));
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

            // Assert — where A's render never built its element there was nothing to leave out, so that reads as a sentinel
            Assert.That(Built("A") ? log.ToString() : "A's element was never built",
                Is.EqualTo("render:A render:C caught:B:render:C"));
        }

        [Component]
        private static VNode ThrowsAsItRenders() => throw new InvalidOperationException("render:Q");

        // Adds its new element to a Div it already holds ahead of the child whose render throws, so the walk records that
        // insert before its own catch swaps the fallback in over both.
        [Component(IsErrorBoundary = true, DisplayName = "Own")]
        private static VNode CatchesOverItsOwnNewElement()
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Div(children: new VNode[] { shown ? V.Custom<AttachThrower>(name: "X", key: "thrower") : null }),
                shown ? V.Component(ThrowsAsItRenders, key: "render-thrower") : null);
        }

        [Test]
        public void Given_ABoundaryThatAddsANewElementThrowingAsItAttachesAheadOfAChildWhoseRenderThrows_When_ADrainRendersIt_Then_ItReportsOnlyTheRenderError()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(CatchesOverItsOwnNewElement));
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — where the render never built the element there was nothing to leave out, so that reads as a sentinel
            Assert.That(Built("X") ? log.ToString() : "X was never built", Is.EqualTo("caught:Own:render:Q"));
        }

        // GREEN_ON_BASE(characterization): the base catches the error in the pass of the component that threw it.
        // Filing a failed insert in ChildListWrites.FailureOwner under the root of the last run in ReconcilePass.Segments,
        // rather than under the component that rendered the element, reddens it.
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
            Assert.That(log.ToString(), Is.EqualTo("render:R1 render:R2 caught:B1:attach:R1"));
        }

        // GREEN_ON_BASE(characterization): the base catches the error in the pass of the component that threw it.
        // Filing a failed insert in ChildListWrites.FailureOwner under the root of the first run in
        // ReconcilePass.Segments, rather than under the component that rendered the element, reddens it.
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
            Assert.That(log.ToString(), Is.EqualTo("render:R1 render:R2 caught:B2:attach:R2"));
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
            Assert.That(log.ToString(), Is.EqualTo("render:R1 render:R2 render:Modal caught:B1:attach:R1"));
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
            Assert.That(log.ToString(), Is.EqualTo("render:R1 render:R2 render:Modal caught:B1:attach:R1:z"));
        }

        #region Where the search for the boundary starts

        [Component(IsErrorBoundary = true, DisplayName = "B3")]
        private static VNode ThirdBoundary(Holds props)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback-3"));
            return props.Child;
        }

        [Component]
        private static VNode ThrowerWhenOpen(bool open)
            => open ? V.Custom<AttachThrower>(name: "W", key: "thrower") : null;

        [Component]
        private static VNode OpensAThrowerUnderABoundary()
        {
            var (open, _) = EventLog.UseState("Open", false);
            return V.Div(children: new VNode[]
            {
                V.Component(FirstBoundary, new Holds(V.Component(ThrowerWhenOpen, open, key: "widget")), key: "b1"),
            });
        }

        [Test]
        public void Given_ABoundaryInsideTheWalkOfTheDrainedComponentAroundTheOneWhoseNewElementThrowsAsItAttaches_When_TheDrainRendersIt_Then_ThatBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(OpensAThrowerUnderABoundary));
            log.Clear();

            // Act
            log.Setter<bool>("Open").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B1:attach:W"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "P")]
        private static VNode RendersItsOwnThrower()
        {
            var (shown, _) = EventLog.UseState("P", false);
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Div(children: new VNode[] { shown ? V.Custom<AttachThrower>(name: "P", key: "thrower") : null });
        }

        [Test]
        public void Given_ABoundaryWhoseOwnNewElementThrowsAsItAttaches_When_ADrainRendersIt_Then_ThatBoundaryReportsIt()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(RendersItsOwnThrower));
            log.Clear();

            // Act
            log.Setter<bool>("P").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:P:attach:P"));
        }

        [Component]
        private static VNode Header() => V.Div(name: "header");

        // GREEN_ON_BASE(characterization): the base hands the mount's error to the root, which unmounts the tree.
        // Starting a placement's search at the component it is filed under in FiberWave.RouteFailures, rather than
        // above the components the wave mounted, reddens it: B1 then catches and the header stays.
        [Test]
        public void Given_ATreeWhoseBoundaryHoldsAComponentWhoseElementThrowsAsItAttaches_When_ItMounts_Then_TheRootReportsItAndTakesTheTreeDown()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = Mount(log, V.Fragment(
                V.Component(Header, key: "header"),
                V.Component(FirstBoundary, new Holds(V.Component(ThrowerWhenOpen, true, key: "widget")), key: "b1")));

            // Assert — a header the error left mounted reads as such rather than as the report
            Assert.That(_mountPoint.Q("header") == null ? log.ToString() : "the header stayed",
                Is.EqualTo("uncaught:attach:W"));
        }

        [Component]
        private static VNode StackedThrower()
            => V.Div(className: "relative", children: new VNode[]
            {
                V.Custom<AttachThrower>(className: "absolute z-10", name: "Z", key: "thrower"),
            });

        // GREEN_ON_BASE(characterization): the base hands the mount's z-* placement error to the root.
        // Passing `placement: false` for a z-* placement in ChildReconciler.PlaceRelocated reddens it: B1 then catches.
        [Test]
        public void Given_ATreeWhoseBoundaryHoldsAComponentWhoseZPlacedElementThrowsAsItAttaches_When_ItMounts_Then_TheRootReportsIt()
        {
            // Arrange
            var log = new EventLog();

            // Act
            using var mounted = Mount(log,
                V.Component(FirstBoundary, new Holds(V.Component(StackedThrower, key: "stacked")), key: "b1"));

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("uncaught:attach:Z:z"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode AddsAThrowerAheadOfARenderError()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(
                V.Div(children: new VNode[] { V.Custom<AttachThrower>(name: "M", key: "thrower") }),
                V.Component(ThrowsAsItRenders, key: "render-thrower"));
        }

        [Test]
        public void Given_ARootFiberThatAddsANewElementThrowingAsItAttachesAheadOfAChildWhoseRenderThrows_When_FiberRendererMountsIt_Then_ItReportsOnlyTheRenderError()
        {
            // Arrange
            var root = FiberRenderer.CreateRoot(AddsAThrowerAheadOfARenderError, isErrorBoundary: true);

            // Act
            FiberRenderer.Mount(root, _mountPoint, onCaughtError: (error, _) => s_reports.Add("caught:" + error.Message));
            var reports = string.Join(" ", s_reports);
            FiberRenderer.Dispose(root);

            // Assert — where the render never built the element there was nothing to leave out, so that reads as a sentinel
            Assert.That(Built("M") ? reports : "M was never built", Is.EqualTo("caught:render:Q"));
        }

        [Component]
        private static VNode MountsTwoStackedThrowers()
        {
            var (shown, _) = EventLog.UseState("Stacked", false);
            EventLog.UseRender("Stacked");
            return V.Div(className: "relative", children: new VNode[]
            {
                shown ? V.Custom<AttachThrower>(className: "absolute z-10", name: "X", key: "x") : null,
                shown ? V.Custom<AttachThrower>(className: "absolute z-10", name: "Y", key: "y") : null,
            });
        }

        [Component(IsErrorBoundary = true, DisplayName = "B")]
        private static VNode TwoStackedThrowersInOneComponent()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(MountsTwoStackedThrowers, key: "both");
        }

        [Test]
        public void Given_AComponentWhoseTwoNewZPlacedElementsThrowAsTheyAttach_When_ADrainRendersIt_Then_ItsBoundaryReportsBothErrors()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoStackedThrowersInOneComponent));
            log.Clear();

            // Act
            log.Setter<bool>("Stacked").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:Stacked caught:B:attach:X:z caught:B:attach:Y:z"));
        }

        [Component]
        private static VNode PortalOfAThrower(bool shown) => V.Div(children: new VNode[]
        {
            shown
                ? V.Portal(s_portalTarget, new VNode[] { V.Custom<AttachThrower>(name: "Portal", key: "thrower") },
                    key: "portal")
                : null,
        });

        // Shows its Portal through a boundary it renders inline, so the component that renders the Portal is not the one
        // whose update the drain renders.
        [Component]
        private static VNode ShowsAThrowingPortalUnderABoundary()
        {
            var (shown, _) = EventLog.UseState("R2", false);
            EventLog.UseRender("R2");
            return V.Component(ThirdBoundary, new Holds(V.Component(PortalOfAThrower, shown, key: "shower")), key: "b3");
        }

        [Test]
        public void Given_AComponentWhoseNewElementThrowsAsItAttachesAndALaterOneWhosePortalsChildDoes_When_OneDrainRendersBoth_Then_EachIsReportedByTheBoundaryAroundTheComponentThatRenderedIt()
        {
            // Arrange — the Portal's target is on the panel, so its child attaches as the Portal mounts
            _host.Root.Add(s_portalTarget);
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoBoundaries,
                new Holds(V.Component(ShowsAThrowingPortalUnderABoundary, key: "r2"))));
            log.Clear();

            // Act
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<bool>("R2").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:R1 render:R2 caught:B1:attach:R1 caught:B3:attach:Portal"));
        }

        [Component]
        private static VNode ShowsTwoPortals()
        {
            var (shown, _) = EventLog.UseState("Portals", false);
            return V.Fragment(
                V.Component(FirstBoundary, new Holds(shown
                    ? V.Portal(s_portalTarget, new VNode[] { V.Custom<AttachThrower>(name: "First", key: "thrower") },
                        key: "first")
                    : null), key: "b1"),
                V.Component(SecondBoundary, new Holds(shown
                    ? V.Portal(s_secondTarget, new VNode[] { V.Label(text: "ok") }, key: "second")
                    : null), key: "b2"));
        }

        [Test]
        public void Given_TwoPortalsUnderBoundariesOfTheirOwnAndTheFirstsChildThrowsAsItAttaches_When_OneDrainShowsBoth_Then_TheSecondMountsAndTheFirstsBoundaryReportsIt()
        {
            // Arrange — both targets are on the panel
            _host.Root.Add(s_portalTarget);
            _host.Root.Add(s_secondTarget);
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ShowsTwoPortals));
            log.Clear();

            // Act
            log.Setter<bool>("Portals").Invoke(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — a second Portal the throw left unmounted reads as such rather than as the report
            Assert.That(s_secondTarget.Q<Label>()?.text == "ok" ? log.ToString() : "the second Portal never mounted",
                Is.EqualTo("caught:B1:attach:First"));
        }

        [Component]
        private static VNode ShowsAThrowingPortal()
            => V.Div(children: new VNode[]
            {
                V.Portal(s_portalTarget, new VNode[] { V.Custom<AttachThrower>(name: "Portal", key: "thrower") },
                    key: "portal"),
            });

        [Test]
        public void Given_ATreeOffThePanelWhoseBoundaryHoldsAPortalIntoATargetOnIt_When_ItMountsAndThePortalsChildThrowsAsItAttaches_Then_TheBoundaryReportsIt()
        {
            // Arrange — the tree mounts into an element off the panel, and the Portal's target is on it
            _host.Root.Add(s_portalTarget);
            var page = new VisualElement();
            var log = new EventLog();

            // Act
            using var mounted = V.Mount(page,
                log.Provide(V.Component(FirstBoundary, new Holds(V.Component(ShowsAThrowingPortal, key: "shower")),
                    key: "b1")),
                log.ReportingErrors());

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("caught:B1:attach:Portal"));
        }

        // Throws from its first DetachFromPanelEvent alone, so the removal that takes it down again at the end of a case
        // does not throw.
        internal sealed class DetachThrower : VisualElement
        {
            private bool _thrown;

            public DetachThrower()
            {
                RegisterCallback<DetachFromPanelEvent>(_ =>
                {
                    if (_thrown) return;
                    _thrown = true;
                    throw new InvalidOperationException("detach:" + name);
                });
            }
        }

        [Component]
        private static VNode RemovesADetachThrower(Named props)
        {
            var (kept, _) = EventLog.UseState(props.Name, true);
            var (rows, _) = EventLog.UseState(props.Name + "Rows", 0);
            EventLog.UseRender(props.Name);
            return V.Div(children: new VNode[]
            {
                kept ? V.Custom<DetachThrower>(name: props.Name, key: "thrower") : null,
                rows > 0 ? V.Div(key: "row") : null,
            });
        }

        [Component]
        private static VNode QuietThenRemoving()
            => V.Fragment(
                V.Component(FirstBoundary, new Holds(V.Component(MountsADiv, new Named("R1"), key: "r1")), key: "b1"),
                V.Component(SecondBoundary, new Holds(V.Component(RemovesADetachThrower, new Named("R2"), key: "r2")),
                    key: "b2"));

        // GREEN_ON_BASE(characterization): the base catches the error in the pass of the component whose removal threw it.
        // Leaving ReconcilePass.Segments uncleared at the end of ChildListWrites.Apply reddens it: the first drain's runs
        // then stay ahead of the second's, and the removal is filed under R1's.
        [Test]
        public void Given_TwoComponentsUnderBoundariesOfTheirOwnThatOneDrainRenders_When_ALaterDrainOfTheSecondRemovesAnElementThatThrowsAsItDetaches_Then_TheSecondsBoundaryReportsIt()
        {
            // Arrange — one drain renders both, each adding a row
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(QuietThenRemoving));
            log.Setter<bool>("R1").Invoke(true);
            log.Setter<int>("R2Rows").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            log.Setter<bool>("R2").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:R2 caught:B2:detach:R2"));
        }

        #endregion
    }
}

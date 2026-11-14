using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a <c>V.Suspense</c> hiding the children it showed commits, as one <see cref="EventLog"/> string per
    /// case, each the sequence react-reconciler 19.1 produces for the same tree: revealing takes the fallback down in
    /// the mutation walk (<c>commitDeletionEffectsOnFiber</c>) and sets the children's layout effects and refs up again
    /// in post-order (<c>recursivelyTraverseReappearLayoutEffects</c>); their passive effects stay connected
    /// throughout, as <c>commitPassiveUnmountOnFiber</c> leaves them connected for an Offscreen whose parent is a
    /// Suspense. A removal is read against 19.2, in the order <see cref="SuspenseHideCommitOrderEventLogTests"/> reads
    /// off its deletion walk, which detaches none of the refs the hide detached. The fallback's probes register no
    /// passive effect, for the reason <see cref="DeletionCommitOrderEventLogTests"/> gives.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class SuspenseRevealCommitOrderEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public SuspenseRevealCommitOrderEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        private enum Shape
        {
            Nested,
            Counter,
            Portal,
        }

        private sealed record Named(string Name);

        private sealed class Gate
        {
            internal VelvetTaskCompletionSource<int>? Source;
        }

        private sealed record HostProps(Shape Shape, Gate Gate, VisualElement Target);

        private sealed record ReaderProps(int Blocked, Gate Gate);

        private sealed record PortalProps(VisualElement Target, bool PassiveProbes);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>(props.Name));
        }

        [Component]
        private static VNode CleanedUpInTheWalk(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>(props.Name));
        }

        [Component]
        private static VNode Outer()
        {
            EventLog.UseEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback<VisualElement>("O");
            var refO2 = EventLog.UseRefCallback<VisualElement>("O2");
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Component(Probed, new Named("I"), key: "i"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component]
        private static VNode PortalOuter(PortalProps props)
        {
            if (props.PassiveProbes) EventLog.UseEffects("O", Array.Empty<object>());
            else EventLog.UseInsertionAndLayoutEffects("O", Array.Empty<object>());
            var refO = EventLog.UseRefCallback<VisualElement>("O");
            var refO2 = EventLog.UseRefCallback<VisualElement>("O2");
            VNode child = props.PassiveProbes
                ? V.Component(Probed, new Named("PC"), key: "pc")
                : V.Component(CleanedUpInTheWalk, new Named("PC"), key: "pc");
            return V.Div(refCallback: refO, children: new VNode[]
            {
                V.Portal(props.Target, new[] { child }, key: "portal"),
                V.Div(refCallback: refO2, key: "o2"),
            });
        }

        [Component]
        private static VNode Counter()
        {
            var (count, _) = EventLog.UseState("Count", 0);
            EventLog.UseEffects("C", Array.Empty<object>());
            EventLog.UseCommit("C", count);
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>("C"));
        }

        [Component]
        private static VNode Reader(ReaderProps props)
        {
            var value = Hooks.Use(() => props.Blocked > 0
                ? (props.Gate.Source = new VelvetTaskCompletionSource<int>()).Task
                : VelvetTask.FromResult(0), props.Blocked);
            return V.Label(text: "reader:" + value);
        }

        [Component]
        private static VNode SuspenseHost(HostProps props)
        {
            var (blocked, _) = EventLog.UseState("Block", 0);
            var reader = V.Component(Reader, new ReaderProps(blocked, props.Gate), key: "reader");
            return V.Suspense(
                fallback: V.Component(CleanedUpInTheWalk, new Named("F"), key: "f"),
                children: props.Shape switch
                {
                    Shape.Counter => new VNode[] { V.Div(children: new VNode[] { V.Component(Counter, key: "c") }), reader },
                    Shape.Portal => new VNode[]
                    {
                        V.Component(PortalOuter, new PortalProps(props.Target, PassiveProbes: true), key: "outer"), reader,
                    },
                    _ => new VNode[] { V.Component(Outer, key: "outer"), reader },
                });
        }

        [Component]
        private static VNode RemovableSuspense(HostProps props)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            var (blocked, _) = EventLog.UseState("Block", 0);
            VNode primary = props.Shape == Shape.Portal
                ? V.Component(PortalOuter, new PortalProps(props.Target, PassiveProbes: false), key: "outer")
                : V.Component(CleanedUpInTheWalk, new Named("H"), key: "h");
            return V.Fragment(shown
                ? V.Suspense(
                    fallback: V.Component(CleanedUpInTheWalk, new Named("F"), key: "f"),
                    children: new[] { primary, V.Component(Reader, new ReaderProps(blocked, props.Gate), key: "reader") })
                : null);
        }

        private MountedEventLogTree MountHidden(EventLog log, Func<HostProps, VNode> host, Shape shape, Gate gate)
        {
            var props = new HostProps(shape, gate, _renderer.CreatePortalTarget());
            var mounted = _renderer.Mount(log.Provide(host(props)), new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();
            log.Setter<int>("Block").Invoke(1);
            mounted.DrainForTest();
            log.Clear();
            return mounted;
        }

        private static void Resolve(Gate gate, MountedEventLogTree mounted)
        {
            gate.Source!.TrySetResult(5);
            mounted.DrainForTest();
        }

        // GREEN_ON_BASE(characterization): the base brings a revealed primary's layout effects and refs back in post-order.
        // Drop the Ctx.AttachRefsHiddenUnder call SuspenseOffscreenCommit.Run makes for a revealed element and `ref:I`,
        // the ref one element down, is missing from the reveal.
        [Test]
        public void Given_APrimaryWithNestedComponentsAndRefsHiddenBehindAFallback_When_TheReadResolves_Then_TheFallbackIsTakenDownAndTheirLayoutEffectsAndRefsComeBackInPostOrder()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = MountHidden(log, props => V.Component(SuspenseHost, props), Shape.Nested, gate);

            // Act
            Resolve(gate, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ref-x:F ref:I lay:I ref:O2 ref:O lay:O"));
        }

        // GREEN_ON_BASE(characterization): the base keeps a hidden component's passive effect and state through the hide.
        // Have FiberEffects.HideLayoutEffects run the cleanups of fiber.Effects as well and `eff-x:C` follows `lay-x:C`.
        [Test]
        public void Given_AStatefulComponentInsideAnElementOfARevealedPrimary_When_ItIsHiddenRevealedAndUpdated_Then_ItsLayoutEffectAndRefCycleWhileItsPassiveEffectAndStateStay()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            var props = new HostProps(Shape.Counter, gate, _renderer.CreatePortalTarget());
            using var mounted = _renderer.Mount(log.Provide(V.Component(SuspenseHost, props)),
                new MountOptions { OnCaughtError = (_, _) => { } });
            mounted.DrainForTest();
            log.Setter<int>("Count").Invoke(1);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Block").Invoke(1);
            mounted.DrainForTest();
            Resolve(gate, mounted);
            log.Setter<int>("Count").Invoke(count => count + 1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "lay-x:C ref-x:C ins:F ref:F lay:F ins-x:F lay-x:F ref-x:F ref:C lay:C commit:C=1 commit:C=2"));
        }

        // GREEN_ON_BASE(characterization): the base detaches no ref of the hidden tree a second time when it removes it.
        // Have ReconcilerContext.ForgetRefDetachedWhileHidden run the callback it held and the cleanup that returns, and
        // `ref:H ref-x:H` follows `ins-x:H`.
        [Test]
        public void Given_ASuspenseHidingAComponentWithARef_When_TheSuspenseIsRemoved_Then_OnlyItsInsertionCleanupRunsAndNoRefDetachesAgain()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountHidden(log, props => V.Component(RemovableSuspense, props), Shape.Nested, new Gate());

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:H ins-x:F lay-x:F ref-x:F"));
        }

        // GREEN_ON_BASE(characterization): the base sets a revealed Portal child's layout effect up where the Portal sits.
        // Have FiberEffects.ShowLayoutEffects queue no layout effect of a fiber with an OwningPortalPlaceholder and
        // `lay:PC` drops out.
        [Test]
        public void Given_APrimaryHoldingAPortalHiddenBehindAFallback_When_TheReadResolves_Then_ThePortalsChildComesBackWhereThePortalSits()
        {
            // Arrange
            var log = new EventLog();
            var gate = new Gate();
            using var mounted = MountHidden(log, props => V.Component(SuspenseHost, props), Shape.Portal, gate);

            // Act
            Resolve(gate, mounted);

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ref-x:F ref:PC lay:PC ref:O2 ref:O lay:O"));
        }

        // GREEN_ON_BASE(characterization): the base runs the Portal child's insertion cleanup where the Portal sits.
        // Have PreOrderTeardown.VisitPortalRows return for a Portal with a target and `ins-x:PC` moves behind `ref-x:F`.
        [Test]
        public void Given_ASuspenseHidingAPortal_When_TheSuspenseIsRemoved_Then_OnlyTheInsertionCleanupsInsideThePortalRunAheadOfTheFallbacks()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountHidden(log, props => V.Component(RemovableSuspense, props), Shape.Portal, new Gate());

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:O ins-x:PC ins-x:F lay-x:F ref-x:F"));
        }
    }
}

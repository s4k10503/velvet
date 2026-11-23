using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where a container's deletion of components runs its take-downs: where the pass applies the writes it
    /// recorded (<see cref="DeletionCommit.Teardown"/>), which is after the renders of the walk ahead of the layout
    /// commit unless a flush of the pass's writes reaches it sooner, as react-reconciler 19.1 runs a deleted fiber's
    /// destroys in the mutation phase of the commit rather than while it renders. A cleanup's error is captured for the
    /// boundary above the deletion, which renders its fallback in a follow-up commit (<c>captureCommitPhaseError</c>)
    /// and does not stop the render under way. The rows of a container that holds no component are removed in the walk.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class DeletionTeardownAtApplyTests
    {
        private readonly EventLogRenderer _renderer;

        public DeletionTeardownAtApplyTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Renders again whenever its flag changes, which a row after the deleted one does within the same pass.
        [Component]
        private static VNode Watcher(bool shown)
        {
            EventLog.UseRender("W");
            return V.Div(name: shown ? "watching" : "watched");
        }

        // The deleted component and the watcher sit in elements of their own, so the watcher's render comes in a walk of
        // a container after the one that decides the deletion, and no row between them is patched at once.
        [Component]
        private static VNode DeletionBesideAWatcher()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Div(key: "a", children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        [Component]
        private static VNode ThrowsInItsCleanup()
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() => () =>
            {
                log.Add("lay-x:T");
                throw new InvalidOperationException("T");
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component]
        private static VNode Fallback()
        {
            EventLog.UseRender("Fb");
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode Guard(bool shown)
        {
            Hooks.UseFallback(_ => V.Component(Fallback));
            return V.Div(children: new VNode[] { shown ? V.Component(ThrowsInItsCleanup, key: "t") : null });
        }

        [Component]
        private static VNode GuardBesideAWatcher()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Div(children: new VNode[]
            {
                V.Component(Guard, shown, key: "g"),
                V.Div(key: "b", children: new VNode[] { V.Component(Watcher, shown, key: "w") }),
            });
        }

        [Component]
        private static VNode ThrowsInItsCleanupAs(Named props)
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() => () =>
            {
                log.Add("lay-x:" + props.Name);
                throw new InvalidOperationException(props.Name);
            }, Array.Empty<object>());
            return V.Div();
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardOfTwo(bool shown)
        {
            Hooks.UseFallback(_ => V.Component(Fallback));
            return V.Div(children: new VNode[]
            {
                shown ? V.Component(ThrowsInItsCleanupAs, new Named("T1"), key: "t1") : null,
                shown ? V.Component(ThrowsInItsCleanupAs, new Named("T2"), key: "t2") : null,
            });
        }

        [Component]
        private static VNode GuardOfTwoHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Component(GuardOfTwo, shown, key: "g");
        }

        [Test]
        public void Given_TwoDeletedComponentsWhoseLayoutCleanupsThrow_When_TheParentStopsRenderingThem_Then_BothRunAndBothAreReported()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(GuardOfTwoHost)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay-x:T1 lay-x:T2 render:Fb caught:Guard:T1 caught:Guard:T2"));
        }

        [Component]
        private static VNode Held() => V.Div(name: "held");

        [Test]
        public void Given_ADeletedComponentAndARowAfterItThatRendersAgain_When_TheParentStopsRenderingIt_Then_ItsCleanupsRunAfterThatRender()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DeletionBesideAWatcher)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W ins-x:D lay-x:D"));
        }

        [Test]
        public void Given_ADeletedComponentWhoseLayoutCleanupThrowsBelowABoundary_When_TheParentStopsRenderingIt_Then_ARowAfterItRendersAndTheFallbackFollows()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(GuardBesideAWatcher)), log.ReportingErrors());
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:W lay-x:T render:Fb caught:Guard:T"));
        }

        [Component]
        private static VNode RingedRowBesideAComponent()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Div(className: "ring-2", name: "card", key: "c") : null,
                V.Component(Held, key: "h"));
        }

        // GREEN_ON_BASE(refactor): the base removes the band with the row in the walk; this pins that a removal run in the
        // apply removes it too.
        [Test]
        public void Given_ARingedRowBesideAComponent_When_TheRowIsDeleted_Then_NoBandIsLeftBesideTheComponent()
        {
            // Arrange
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(RingedRowBesideAComponent)));
            mounted.DrainForTest();
            var hosted = RingOverlayIn(root) != null;

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — a band that was never hosted reads as its own value, which the expected one is not
            Assert.That(hosted ? (RingOverlayIn(root) == null ? "gone" : "stays") : "never hosted", Is.EqualTo("gone"));
        }

        private static VisualElement RingOverlayIn(VisualElement host)
        {
            for (var i = 0; i < host.childCount; i++)
            {
                if (host[i].ClassListContains(RingOverlay.MarkerClass)) return host[i];
            }
            return null;
        }
    }
}

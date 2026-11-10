using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a Suspense does when a transition's render suspends the primary it shows. The pass is given up
    /// whole: the primary stays on screen, the transition stays pending, and the update renders again when the
    /// resource it waits on settles. A boundary that shows no primary yet, and a render that is not a
    /// transition's, show the fallback.
    /// </summary>
    [TestFixture]
    internal sealed class SuspenseTransitionTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_sources = new List<VelvetTaskCompletionSource<int>>();
            s_setKey = default;
            s_setTick = default;
            s_setShown = default;
            s_start = default;
            s_hostFiber = null;
            s_otherSource = null;
        }

        private string Texts() => _root.DisplayedLabelTexts("|");

        private static void FlushTwice(MountedTree mounted)
        {
            mounted.FlushStateForTest();
            mounted.FlushStateForTest();
        }

        [Test]
        public void Given_ARevealedPrimary_When_ATransitionSuspendsIt_Then_ThePrimaryStaysShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));

            // Act
            FlushTwice(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("value:0"));
        }

        [Test]
        public void Given_ARevealedPrimary_When_ATransitionSuspendsIt_Then_TheTransitionIsStillPending()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));

            // Act
            FlushTwice(mounted);

            // Assert
            Assert.That(s_hostFiber.IsTransitionPending, Is.True);
        }

        [Test]
        public void Given_ATransitionThatSuspendedARevealedPrimary_When_TheResourceResolves_Then_TheNewContentIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));
            FlushTwice(mounted);
            var whileSuspended = Texts();

            // Act
            s_sources[0].TrySetResult(7);
            FlushTwice(mounted);

            // Assert — the reading while suspended is folded in, since the content after the resolve reads the same either way
            Assert.That((whileSuspended, Texts()), Is.EqualTo(("value:0", "value:7")));
        }

        [Test]
        public void Given_ATransitionThatSuspendedARevealedPrimary_When_AnotherTransitionSupersedesIt_Then_ThePrimaryStaysShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));
            FlushTwice(mounted);
            s_start.Invoke(() => s_setKey.Invoke(2));

            // Act
            FlushTwice(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("value:0"));
        }

        [Test]
        public void Given_ATransitionThatSuspendedARevealedPrimary_When_AnotherTransitionSupersedesItAndTheFirstResolves_Then_ThePrimaryStaysShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));
            FlushTwice(mounted);
            s_start.Invoke(() => s_setKey.Invoke(2));
            FlushTwice(mounted);

            // Act
            s_sources[0].TrySetResult(7);
            FlushTwice(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("value:0"));
        }

        [Test]
        public void Given_ATransitionThatSuspendedARevealedPrimary_When_TheResourceFaults_Then_TheErrorBoundaryShowsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(
                _root, V.Component(ErrorBoundaryRender, key: "errors"),
                new MountOptions { OnCaughtError = (_, _) => { } });
            s_start.Invoke(() => s_setKey.Invoke(1));
            FlushTwice(mounted);
            var whileSuspended = Texts();

            // Act
            s_sources[0].TrySetException(new InvalidOperationException("boom"));
            FlushTwice(mounted);

            // Assert — the reading while suspended is folded in, since the fallback after the fault reads the same either way
            Assert.That((whileSuspended, Texts()), Is.EqualTo(("value:0", "errors-fallback")));
        }

        [Test]
        public void Given_APendingTransitionSuspendingARevealedPrimary_When_AnUrgentUpdateRendersAndTheResourceResolves_Then_TheUrgentUpdateAndTheTransitionBothShow()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TickHostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));
            s_setTick.Invoke(1);
            FlushTwice(mounted);
            mounted.GetSchedulerForTest().DrainDelayedForTest();
            var whileSuspended = Texts();

            // Act
            s_sources[0].TrySetResult(7);
            FlushTwice(mounted);

            // Assert — the reading while suspended is folded in, since the content after the resolve reads the same either way
            Assert.That((whileSuspended, Texts()), Is.EqualTo(("tick:1|value:0", "tick:1|value:7")));
        }

        [Test]
        public void Given_AHostUnderAnOuterBoundary_When_ATransitionSuspendsItsRevealedPrimary_Then_NeitherFallbackIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OuterBoundaryRender, key: "outer"));
            s_start.Invoke(() => s_setKey.Invoke(1));

            // Act
            FlushTwice(mounted);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("value:0"));
        }

        [Test]
        public void Given_AComponentBelowABoundary_When_ItsOwnTransitionSuspendsIt_Then_TheBoundaryKeepsItsPrimary()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryAboveOwnTransitionRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));

            // Act
            FlushTwice(mounted);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("value:0"));
        }

        // GREEN_ON_BASE(characterization): the base already reveals it; deleting `&& ReferenceEquals(current.SuspendedOn, fiber)` from IsWaitedOnByAGivenUpTransitionPass reddens it.
        [Test]
        public void Given_ATransitionGivenUpOnOneBoundarysRead_When_AnotherBoundarysReadResolves_Then_ThatBoundaryShowsItsContent()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoBoundaryHostRender, key: "host"));
            s_start.Invoke(() => s_setKey.Invoke(1));
            FlushTwice(mounted);
            var givenUp = s_hostFiber.SuspendedInTransition;

            // Act
            s_otherSource.TrySetResult(5);
            FlushTwice(mounted);

            // Assert — a host whose transition was not given up reads as no content, which the expected label is not in
            var shown = givenUp ? Texts() : "not given up";
            Assert.That(shown, Does.Contain("other:5"));
        }

        // GREEN_ON_BASE(characterization): the base already keeps the update; deleting `_baseQueue = _beforeBaseQueue;` from UndoCommit reddens it.
        [Test]
        public void Given_ATransitionWhoseLaterSliceSuspendsARevealedPrimary_When_TheResourceResolves_Then_TheNewContentIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SlicedHostRender, key: "host"));
            mounted.FlushEffectsForTest();
            s_start.Invoke(() => s_setKey.Invoke(1));
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_hostFiber.FlushStateWithTinyBudgetForTest();
            var readInTheFirstSlice = s_sources.Count != 0;
            for (var slices = 0; slices < 100 && s_hostFiber.HasPendingReconcileWorkForTest() && s_sources.Count == 0; slices++)
            {
                FiberWorkLoop.ContinueReconcile(s_hostFiber);
            }
            var keptWhileSuspended = !readInTheFirstSlice && s_sources.Count == 1
                && Texts().Contains("value:0") && !Texts().Contains("loading");

            // Act
            if (s_sources.Count != 0) s_sources[0].TrySetResult(7);
            mounted.FlushStateForTest();
            s_hostFiber.DrainTimeSlicedReconcileForTest();
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — a pass that did not suspend in a later slice with the primary kept reads as no content
            var shown = keptWhileSuspended ? Texts() : "not suspended in a later slice";
            Assert.That(shown, Does.Contain("value:7"));
        }

        // GREEN_ON_BASE(characterization): the merge base already shows the fallback when an urgent update
        // suspends a revealed primary; what this pins is that only a transition's render declines.
        [Test]
        public void Given_ARevealedPrimary_When_AnUrgentUpdateSuspendsIt_Then_TheFallbackIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            s_setKey.Invoke(1);

            // Act
            FlushTwice(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("loading"));
        }

        // GREEN_ON_BASE(characterization): the merge base already shows the fallback for a boundary a transition
        // mounts that suspends; what this pins is that only a revealed primary is kept.
        [Test]
        public void Given_ANoBoundaryYet_When_ATransitionMountsOneThatSuspends_Then_TheFallbackIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NewBoundaryHostRender, key: "host"));
            s_start.Invoke(() => s_setShown.Invoke(1));

            // Act
            FlushTwice(mounted);

            // Assert
            Assert.That(Texts(), Is.EqualTo("host|loading"));
        }

        private static List<VelvetTaskCompletionSource<int>> s_sources;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setKey;
        private static StateUpdater<int> s_setShown;
        private static TransitionStarter s_start;
        private static ComponentFiber s_hostFiber;

        [Component]
        private static VNode HostRender()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (key, setKey) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setKey = setKey;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(ReaderRender, key, key: "reader") }),
            });
        }

        private static VelvetTaskCompletionSource<int> s_otherSource;

        // The first boundary's reader is the one a transition suspends; the second's is pending from the mount.
        [Component]
        private static VNode TwoBoundaryHostRender()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (key, setKey) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setKey = setKey;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(ReaderRender, key, key: "reader") }),
                V.Suspense(
                    fallback: V.Label(text: "other-loading"),
                    children: new VNode[] { V.Component(OtherReaderRender, key: "other") }),
            });
        }

        [Component]
        private static VNode OtherReaderRender()
        {
            var value = Hooks.Use<int>(_ =>
            {
                s_otherSource ??= new VelvetTaskCompletionSource<int>();
                return s_otherSource.Task;
            }, "other");
            return V.Label(text: "other:" + value);
        }

        // Rows ahead of the boundary, each written from the key, for the first slice to park among; the case reads
        // whether it did.
        private const int SlicedRows = 30;

        [Component]
        private static VNode SlicedHostRender()
        {
            s_hostFiber = FiberAmbientStack.Current;
            var (_, start) = Hooks.UseTransition();
            var (key, setKey) = Hooks.UseState(0);
            s_setKey = setKey;
            s_start = start;
            var children = new VNode[SlicedRows + 1];
            for (var i = 0; i < SlicedRows; i++) children[i] = V.Label(text: "row-" + i + ":" + key);
            children[SlicedRows] = V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(ReaderRender, key, key: "reader") });
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode TickHostRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setKey = setKey;
            s_setTick = setTick;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Label(text: "tick:" + tick),
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(ReaderRender, key, key: "reader") }),
            });
        }

        [Component]
        private static VNode OuterBoundaryRender()
            => V.Suspense(
                fallback: V.Label(text: "outer-loading"),
                children: new VNode[] { V.Component(HostRender, key: "host") });

        [Component]
        private static VNode BoundaryAboveOwnTransitionRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { V.Component(OwnTransitionReaderRender, key: "own") });

        [Component]
        private static VNode OwnTransitionReaderRender()
        {
            var (key, setKey) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setKey = setKey;
            s_start = start;
            return V.Component(ReaderRender, key, key: "reader");
        }

        [Component]
        private static VNode NewBoundaryHostRender()
        {
            var (shown, setShown) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setShown = setShown;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.Label(text: "host"),
                shown == 1
                    ? V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[] { V.Component(ReaderRender, 1, key: "reader") })
                    : null,
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ErrorBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "errors-fallback"));
            return V.Component(HostRender, key: "host");
        }

        [Component]
        private static VNode ReaderRender(int key)
        {
            var value = Hooks.Use<int>(_ =>
            {
                if (key == 0) return VelvetTask.FromResult(0);
                var source = new VelvetTaskCompletionSource<int>();
                s_sources.Add(source);
                return source.Task;
            }, key);
            return V.Label(text: "value:" + value);
        }
    }
}

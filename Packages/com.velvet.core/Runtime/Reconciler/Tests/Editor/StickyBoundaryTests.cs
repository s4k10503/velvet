using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an error boundary that has caught keeps its fallback until it remounts, as React's does.
    /// <list type="bullet">
    /// <item>A later render of it — its parent's, with nothing throwing any more — renders the fallback
    /// again, handed the error it caught and the props of that render, and not its children.</item>
    /// <item>Given a new key, it remounts and renders its children.</item>
    /// <item>An error its fallback's content throws on such a render is caught by it once, as React's boundary
    /// catches it, so a fallback rendered for that error shows; one that throws again goes to the boundary
    /// above, whether its parent's render or its own update renders it.</item>
    /// <item>A catch in a Suspense primary that then suspends is discarded with that render: the boundary
    /// reports nothing for it and renders its children on the retry.</item>
    /// <item>A factory that gives no fallback on such a render passes the error to the boundary above, as it does
    /// at the catch.</item>
    /// <item>With the StrictMode double render on, rendering its fallback again reports no impure render.</item>
    /// <item>What its body renders on those renders goes back to the pool.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StickyBoundaryTests
    {
        private static bool s_throws;
        private static bool s_fallbackContentThrows;
        private static Action<int> s_setTick;
        private static Action<int> s_setKey;
        private static Action<int> s_setOwnTick;
        private static int s_innerFactoryRuns;
        private static bool s_noFallback;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throws = false;
            s_fallbackContentThrows = false;
            s_setTick = null;
            s_setKey = null;
            s_setOwnTick = null;
            s_innerFactoryRuns = 0;
            s_noFallback = false;
            s_pending = null;
            FiberStrictMode.Enabled = false;
        }

        [TearDown]
        public void TearDown()
        {
            FiberStrictMode.Enabled = false;
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryThatCaught_When_ItsParentRendersAgainWithNothingThrowing_Then_ItShowsItsFallbackWithTheCaughtErrorAndTheNewProps()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;

            // Act
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback:2:Sticky boundary throw"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base renders the children of a boundary given a new key too.
        // A boundary keeps its fallback until it remounts now, and a new key remounting it is what this pins.
        [Test]
        public void Given_ABoundaryThatCaught_When_ItsParentGivesItANewKey_Then_ItRendersItsChildren()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            var caught = Texts();
            s_throws = false;

            // Act
            s_setKey.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the catch is folded in, since a boundary that never caught renders its children as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("fallback:0:Sticky boundary throw|child"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsParentsRenderMakesTheFallbacksContentThrow_Then_TheBoundaryAboveCatchesIt()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(OuterHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_fallbackContentThrows = true;
            s_innerFactoryRuns = 0;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the inner factory's runs are read with it: as React's boundary does, it catches the content's
            // error once, rendering its fallback again for it, and only the retry's throw goes above
            Assert.That(Texts() + ", inner factory ran " + s_innerFactoryRuns, Is.EqualTo("outer-fallback, inner factory ran 2"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsOwnUpdateMakesTheFallbacksContentThrow_Then_TheBoundaryAboveCatchesIt()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(OuterHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_fallbackContentThrows = true;
            s_innerFactoryRuns = 0;

            // Act
            s_setOwnTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the inner factory's runs are read with it: as React's boundary does, it catches the content's
            // error once, rendering its fallback again for it, and only the retry's throw goes above
            Assert.That(Texts() + ", inner factory ran " + s_innerFactoryRuns, Is.EqualTo("outer-fallback, inner factory ran 2"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsFallbacksContentThrowsAndTheFallbackForThatErrorRenders_Then_TheBoundaryKeepsThatFallback()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(RecoveringHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_fallbackContentThrows = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("content-error-fallback"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryThatCaughtOnItsOwnUpdate_When_ItsNextOwnUpdateMakesItsFallbacksContentThrowOnce_Then_ItShowsTheFallbackForThatError()
        {
            // Arrange — the first catch is the one the boundary's own reconcile takes
            using var mounted = V.Mount(_root, V.Component(OwnRecoveringHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_setOwnTick.Invoke(1);
            mounted.FlushStateForTest();
            var afterOwnUpdate = Texts();
            s_throws = false;
            s_fallbackContentThrows = true;

            // Act
            s_setOwnTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert — the fallback after the first own update is read with it, since a boundary that never caught
            // there shows its children and catches the content's error as a first catch
            Assert.That((afterOwnUpdate, Texts()), Is.EqualTo(("inner-fallback", "content-error-fallback")));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base renders the boundary's children again, the shared node among
        // them. What this pins is that the node the kept fallback shares with the body stays out of the pool.
        [Test]
        public void Given_AFallbackBuiltFromANodeTheBodyAlsoReturns_When_TheNextRenderDropsAPropTheFirstGaveIt_Then_TheElementLosesThatProp()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(SharedNodeHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert — the text is read with the tooltip, since a render that never reached the element leaves both
            var button = _root.Q<Button>();
            Assert.That((button?.text, string.IsNullOrEmpty(button?.tooltip)), Is.EqualTo(("shared:2", true)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryInASuspensePrimaryThatSuspends_When_ItCaughtInThatRenderAndTheResourceResolves_Then_ItRendersItsChildren()
        {
            // Arrange — the boundary is memoized, so the retry reaches it only as a render it was asked for
            s_throws = true;
            s_pending = new VelvetTaskCompletionSource<string>();
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;

            // Act
            s_pending.TrySetResult("value");
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("child,loaded:value"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base renders a boundary's children again on any later render,
        // so this reads the same there. What this pins is that a catch the suspended render took inside an element
        // of the primary does not keep the boundary on its fallback once the primary is revealed.
        [Test]
        public void Given_ABoundaryInsideAnElementOfAShownPrimary_When_AnUpdateThatSuspendsThePrimaryCaughtThereAndTheResourceResolves_Then_ItRendersItsChildren()
        {
            // Arrange — the update makes the child throw and mounts a reader that waits, so the catch is in a
            // render the primary discards
            using var mounted = V.Mount(_root, V.Component(ElementSuspenseHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_pending = new VelvetTaskCompletionSource<string>();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            s_throws = false;

            // Act
            s_pending.TrySetResult("value");
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("child,loaded:value"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryInASuspensePrimaryThatSuspends_When_ItCaughtInThatRender_Then_NothingIsReported()
        {
            // Arrange
            s_throws = true;
            s_pending = new VelvetTaskCompletionSource<string>();
            var reports = 0;

            // Act
            using var mounted = V.Mount(_root, V.Component(SuspenseHostRender, key: "host"),
                new MountOptions((_, _) => reports++));
            mounted.FlushEffectsForTest();

            // Assert — the Suspense fallback is read with it, since a primary that never suspended reports its catch
            Assert.That(Texts() + ", reports " + reports, Is.EqualTo("loading, reports 0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryShowingItsFallback_When_ItsFactoryGivesNoFallbackOnALaterRender_Then_TheBoundaryAboveCatchesTheError()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(DecliningHostRender, key: "host"), CaughtErrors.Unlogged);
            var caught = Texts();
            s_throws = false;
            s_noFallback = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the first catch is folded in, since a boundary that never caught renders its children
            Assert.That(caught + "|" + Texts(), Is.EqualTo("inner-fallback|outer-fallback"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base double-renders a boundary's body, which matches its commit.
        // A boundary showing its fallback commits the fallback instead, and no impure report for that is what this pins.
        [Test]
        public void Given_StrictModeAndABoundaryThatCaught_When_ItsParentRendersAgain_Then_NoImpureRenderIsReported()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);
            FiberStrictMode.Enabled = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — an error log fails the case as well
            LogAssert.NoUnexpectedReceived();
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base commits and later retires what this body renders.
        // A boundary showing its fallback discards it at each render instead, and handing it back is what this pins.
        [Test]
        public void Given_ABoundaryShowingItsFallbackWhoseBodyRentsPooledNodes_When_ItRendersAgainAndIsDisposed_Then_ThePoolCountsNothingRentedOut()
        {
            // Arrange
            s_throws = true;
            var before = Rented();
            var mounted = V.Mount(_root, V.Component(PooledHostRender, key: "host"), CaughtErrors.Unlogged);
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            var after = Rented();
            Assert.That(
                (after.Props - before.Props, after.EventArrays - before.EventArrays, after.NodeArrays - before.NodeArrays),
                Is.EqualTo((0, 0, 0)));
        }

        // Read by reflection for the reason MemoCacheCollisionTests reads the same sets.
        private static (int Props, int EventArrays, int NodeArrays) Rented()
            => VNodePoolTestAccess.RentedOutCountsForTest();

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PooledBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id + tick, onClick: () => { }))),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PooledHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(PooledBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false)]
        private static VNode ThrowerRender()
        {
            if (s_throws) throw new InvalidOperationException("Sticky boundary throw");
            return V.Label(text: "child");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode BoundaryRender(int tick)
        {
            Hooks.UseFallback(ex => V.Label(text: "fallback:" + tick + ":" + ex.Message));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            var (key, setKey) = Hooks.UseState(0);
            s_setTick = setTick;
            s_setKey = setKey;
            return V.Div(children: new VNode[] { V.Component(BoundaryRender, tick, key: "boundary-" + key) });
        }

        [Component(Compiler = false)]
        private static VNode FallbackContentRender()
        {
            if (s_fallbackContentThrows) throw new InvalidOperationException("Fallback content throw");
            return V.Label(text: "inner-fallback");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InnerBoundaryRender(int tick)
        {
            var (_, setOwnTick) = Hooks.UseState(0);
            s_setOwnTick = setOwnTick;
            Hooks.UseFallback(_ =>
            {
                s_innerFactoryRuns++;
                return V.Component(FallbackContentRender, key: "content");
            });
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "outer-fallback"));
            return V.Component(InnerBoundaryRender, tick, key: "inner");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode DecliningInnerRender(int tick)
        {
            Hooks.UseFallback(_ => s_noFallback ? null : V.Label(text: "inner-fallback"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode DecliningOuterRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "outer-fallback"));
            return V.Component(DecliningInnerRender, tick, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode DecliningHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(DecliningOuterRender, tick, key: "outer") });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RecoveringBoundaryRender(int tick)
        {
            Hooks.UseFallback(ex => ex.Message == "Fallback content throw"
                ? V.Label(text: "content-error-fallback")
                : V.Component(FallbackContentRender, key: "content"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnRecoveringBoundaryRender(int tick)
        {
            var (_, setOwnTick) = Hooks.UseState(0);
            s_setOwnTick = setOwnTick;
            Hooks.UseFallback(ex => ex.Message == "Fallback content throw"
                ? V.Label(text: "content-error-fallback")
                : V.Component(FallbackContentRender, key: "content"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode OwnRecoveringHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(OwnRecoveringBoundaryRender, tick, key: "boundary") });
        }

        // Its fallback is a node its body returns as well, built in the same render, and its first render after the
        // catch gives that node a tooltip the next one drops.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SharedNodeBoundaryRender(int tick)
        {
            var shared = V.Button(text: "shared:" + tick, tooltip: tick == 1 ? "first" : null, onClick: () => { });
            Hooks.UseFallback(_ => shared);
            return V.Fragment(new VNode[] { shared, V.Component(ThrowerRender, key: "thrower") });
        }

        [Component(Compiler = false)]
        private static VNode SharedNodeHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(SharedNodeBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false)]
        private static VNode RecoveringHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(RecoveringBoundaryRender, tick, key: "boundary") });
        }

        private static VelvetTaskCompletionSource<string> s_pending;

        [Component(Compiler = false, Memoize = true, IsErrorBoundary = true)]
        private static VNode SuspendedBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode PendingReaderRender() => V.Label(text: "loaded:" + Hooks.Use(_ => s_pending.Task, resourceKey: 0));

        [Component(Compiler = false)]
        private static VNode SuspenseHostRender()
            => V.Div(children: new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[]
                {
                    V.Component(SuspendedBoundaryRender, key: "boundary"),
                    V.Component(PendingReaderRender, key: "reader"),
                }),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode ElementBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        // The boundary sits inside a host element of the primary, which that element's own reconcile expands. The
        // reader that waits is one the update mounts.
        [Component(Compiler = false)]
        private static VNode ElementSuspenseHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Suspense(V.Label(text: "loading"), new VNode[]
                {
                    V.Div(children: new VNode[] { V.Component(ElementBoundaryRender, tick, key: "boundary") }),
                    tick == 0 ? V.Label(text: "loaded:first") : V.Component(PendingReaderRender, key: "reader-" + tick),
                }),
            });
        }

        [Component(Compiler = false)]
        private static VNode OuterHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(OuterBoundaryRender, tick, key: "outer") });
        }
    }
}

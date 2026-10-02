using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the error-boundary contract for function components. A boundary is a component declared with
    /// <c>[Component(IsErrorBoundary = true)]</c> that registers a fallback factory via <c>Hooks.UseFallback</c>.
    /// <list type="bullet">
    /// <item>A render exception with no enclosing boundary is logged and not swallowed.</item>
    /// <item>A boundary that renders without error shows its normal subtree and never invokes its fallback.</item>
    /// <item>When a descendant's render throws, the exception propagates up to the nearest enclosing boundary,
    /// which invokes its fallback factory with the thrown exception; non-boundary components in between are
    /// transparent to the propagation.</item>
    /// <item>A boundary over multiple children aborts the in-progress render when the first child throws and
    /// shows its fallback.</item>
    /// <item>A boundary that caught keeps its fallback until it remounts: a boundary given a new key renders
    /// its children again, every one of them mounting, and its fallback factory does not run again.</item>
    /// <item>A catch rewrites the boundary's own slot range rather than the first slots of its
    /// container: with a sibling on each side, both stay and the subtree that threw goes.</item>
    /// <item>Where the pass that catches is itself re-placing the boundary, the rest of that pass commits and
    /// the fallback takes the rows it gives the boundary; where such a pass has already committed and a later
    /// render of the thrower throws, the rows rewritten are the ones it placed. An element callback's error
    /// raised while that pass creates an element is caught the same way.</item>
    /// <item>A catch inside its parent's render leaves the rest of that render to commit: a sibling behind the
    /// boundary, one inside a child that re-rendered in that render, and the fallback a Suspense behind the
    /// boundary is showing keep their state and their effect.</item>
    /// <item>A component that render drops is cleaned up, whether the parent itself, a child that re-rendered,
    /// with its own children, or an element that child owns dropped it, and it comes back as a new instance
    /// when rendered again. One whose own cleanup threw into the boundary that caught it is disposed.</item>
    /// <item>A catch leaves no fiber on the reconciler's fiber stack once the pass ends.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers. Parent-child fiber
    /// relations form naturally through <c>V.Component</c> nesting; a re-render is driven by a child-side
    /// <c>setTick</c> setter so the throw happens on an update rather than the initial mount.
    /// </remarks>
    [TestFixture]
    internal sealed class ErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetTracking();
            ResetBoundary();
            ResetMultiChild();
            ResetEffectBoundary();
            ResetBrokenFallback();
            ResetPlacement();
            ResetMoved();
            ResetUnreached();
        }

        #region No boundary

        [Test]
        public void Given_NoErrorBoundary_When_RenderThrows_Then_LogsException()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TrackingRender, key: "track"));
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test render error");
            s_trackingShouldThrow = true;

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect verifies the unguarded render exception is logged
        }

        #endregion

        #region Boundary's own normal mount

        [Test]
        public void Given_BoundaryComponent_When_MountedWithoutError_Then_ShowsNormalTree()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryRender, key: "boundary"));

            // Assert — the boundary is inline-mounted (no wrapper VE), so its normal Label sits directly under root
            Assert.That(((Label)_root.ElementAt(0)).text, Is.EqualTo("ok"),
                "A boundary that renders cleanly shows its normal subtree");
        }

        [Test]
        public void Given_BoundaryComponent_When_MountedWithoutError_Then_FallbackIsNotShown()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryRender, key: "boundary"));

            // Assert
            Assert.That(s_boundaryFallbackShown, Is.False, "A clean mount never invokes the fallback factory");
        }

        #endregion

        #region Propagation to an enclosing boundary

        [Test]
        public void Given_ChildWithNoBoundary_When_ChildRenderThrows_Then_ParentBoundaryShowsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingTrackingRender, key: "wrapper"), CaughtErrors.Unlogged);
            Assume.That(s_boundaryFallbackShown, Is.False, "Precondition: children succeed on the initial mount");
            s_trackingShouldThrow = true;

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_boundaryFallbackShown, Is.True,
                "A throwing child propagates to the parent boundary, firing its fallback factory");
        }

        [Test]
        public void Given_ChildWithNoBoundary_When_ChildRenderThrows_Then_BoundaryReceivesThrownException()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingTrackingRender, key: "wrapper"), CaughtErrors.Unlogged);
            s_trackingShouldThrow = true;

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_boundaryLastCaughtMessage, Is.EqualTo("Test render error"),
                "The fallback factory receives the exact exception thrown by the descendant");
        }

        [Test]
        public void Given_ThreeComponentChain_When_GrandchildRenderThrows_Then_GrandparentBoundaryShowsFallback()
        {
            // Arrange — boundary -> non-boundary Middle -> throwing Tracking
            using var mounted = V.Mount(_root, V.Component(GrandparentBoundaryRender, key: "grandparent"), CaughtErrors.Unlogged);
            Assume.That(s_boundaryFallbackShown, Is.False, "Precondition: the chain mounts cleanly");
            s_trackingShouldThrow = true;

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_boundaryFallbackShown, Is.True,
                "Propagation passes transparently through the non-boundary Middle to reach the grandparent boundary");
        }

        #endregion

        #region Multi-child boundary abort and recovery

        [Test]
        public void Given_MultiChildBoundary_When_FirstChildThrows_Then_FallbackShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MultiChildBoundaryRender, key: "multi"), CaughtErrors.Unlogged);
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 3;

            // Act
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_multiFallbackShown, Is.True,
                "A first child that throws aborts the multi-child render and fires the boundary's fallback factory");
        }

        // GREEN_ON_BASE(characterization): the merge base renders the children of a boundary given a new key too.
        // A boundary keeps its fallback until it remounts now, and a new key remounting it is what this pins.
        [Test]
        public void Given_MultiChildBoundaryAfterFallback_When_ItIsGivenANewKey_Then_AllChildrenMount()
        {
            // Arrange — drive the boundary into its fallback first
            using var mounted = V.Mount(_root, V.Component(MultiChildHostRender, key: "multi-host"), CaughtErrors.Unlogged);
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 2;
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = s_multiFallbackShown;
            s_multiNormalRenderCount = 0;
            s_multiFirstChildShouldThrow = false;

            // Act
            s_multiSetGeneration.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the catch is folded in, since a boundary that never caught mounts its children as well
            Assert.That((fellBack, s_multiNormalRenderCount >= s_multiChildCount), Is.EqualTo((true, true)),
                "A boundary given a new key remounts, and every child mounts");
        }

        // GREEN_ON_BASE(characterization): the merge base runs no fallback factory for a boundary given a new key.
        // A boundary keeps its fallback until it remounts now, and a remount not showing it is what this pins.
        [Test]
        public void Given_MultiChildBoundaryAfterFallback_When_ItIsGivenANewKey_Then_FallbackDoesNotRestart()
        {
            // Arrange — drive the boundary into its fallback first
            using var mounted = V.Mount(_root, V.Component(MultiChildHostRender, key: "multi-host"), CaughtErrors.Unlogged);
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 2;
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = s_multiFallbackShown;
            s_multiFallbackShown = false;
            s_multiFirstChildShouldThrow = false;

            // Act
            s_multiSetGeneration.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the catch is folded in, since a boundary that never caught runs no factory either
            Assert.That((fellBack, s_multiFallbackShown), Is.EqualTo((true, false)),
                "A remounted boundary does not run its fallback factory");
        }

        [Test]
        public void Given_MultiChildBoundaryAfterFallback_When_ItRendersAgainWithNoChildThrowing_Then_ItKeepsItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MultiChildHostRender, key: "multi-host"), CaughtErrors.Unlogged);
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 2;
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();
            s_multiNormalRenderCount = 0;
            s_multiFirstChildShouldThrow = false;

            // Act — the boundary's own update, which React answers from the error state it keeps
            s_multiSetTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)) + ", children rendered "
                    + s_multiNormalRenderCount,
                Is.EqualTo("error, children rendered 0"));
        }

        #endregion

        #region Tracking component (conditional exception, re-renders via child-side setTick)

        private static bool s_trackingShouldThrow;
        private static Action<int> s_trackingSetTick;

        private static void ResetTracking()
        {
            s_trackingShouldThrow = false;
            s_trackingSetTick = null;
        }

        [Component]
        private static VNode TrackingRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_trackingSetTick = setTick;
            if (s_trackingShouldThrow) throw new InvalidOperationException("Test render error");
            return V.Label(text: "ok");
        }

        #endregion

        #region Boundary components (boundary + Hooks.UseFallback, fallback observed)

        private static bool s_boundaryFallbackShown;
        private static string s_boundaryLastCaughtMessage;

        private static void ResetBoundary()
        {
            s_boundaryFallbackShown = false;
            s_boundaryLastCaughtMessage = null;
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_boundaryFallbackShown = true;
                s_boundaryLastCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Label(text: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingTrackingRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_boundaryFallbackShown = true;
                s_boundaryLastCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(TrackingRender, key: "tracking");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode GrandparentBoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_boundaryFallbackShown = true;
                s_boundaryLastCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(MiddleRender, key: "middle");
        }

        [Component]
        private static VNode MiddleRender()
            => V.Component(TrackingRender, key: "tracking");

        #endregion

        #region MultiChildBoundary (V.Fragment children + abort reset)

        private static bool s_multiFirstChildShouldThrow;
        private static int s_multiChildCount = 1;
        private static int s_multiNormalRenderCount;
        private static bool s_multiFallbackShown;
        private static Action<int> s_multiSetTick;

        private static void ResetMultiChild()
        {
            s_multiFirstChildShouldThrow = false;
            s_multiChildCount = 1;
            s_multiNormalRenderCount = 0;
            s_multiFallbackShown = false;
            s_multiSetTick = null;
            s_multiSetGeneration = null;
        }

        private static Action<int> s_multiSetGeneration;

        // Keys the boundary by a generation, so a new generation remounts it.
        [Component]
        private static VNode MultiChildHostRender()
        {
            var (generation, setGeneration) = Hooks.UseState(0);
            s_multiSetGeneration = setGeneration;
            return V.Component(MultiChildBoundaryRender, key: "multi-" + generation);
        }

        [Component(IsErrorBoundary = true)]
        private static VNode MultiChildBoundaryRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_multiSetTick = setTick;
            Hooks.UseFallback(_ =>
            {
                s_multiFallbackShown = true;
                return V.Label(text: "error");
            });

            var children = new VNode[s_multiChildCount];
            for (var i = 0; i < s_multiChildCount; i++)
            {
                children[i] = i == 0 && s_multiFirstChildShouldThrow
                    ? V.Component(MultiThrowingChildRender, key: $"child-{i}")
                    : V.Component(MultiNormalChildRender, key: $"child-{i}");
            }
            return V.Fragment(children);
        }

        [Component]
        private static VNode MultiThrowingChildRender()
            => throw new InvalidOperationException("Child render error");

        [Component]
        private static VNode MultiNormalChildRender()
        {
            s_multiNormalRenderCount++;
            return V.Label(text: "child-ok");
        }

        #endregion

        #region Effect-phase error propagation

        private static bool s_effectBoundaryFallbackShown;
        private static string s_effectBoundaryCaughtMessage;
        private static int s_effectCleanupRunCount;
        private static Action<int> s_effectCleanupChildSetTick;

        private static void ResetEffectBoundary()
        {
            s_effectBoundaryFallbackShown = false;
            s_effectBoundaryCaughtMessage = null;
            s_effectCleanupRunCount = 0;
            s_effectCleanupChildSetTick = null;
        }

        [Test]
        public void Given_ChildLayoutEffectThrows_When_Mounted_Then_EnclosingBoundaryShowsFallback()
        {
            // An exception thrown by an effect setup propagates to the nearest Error Boundary,
            // the same as a render-phase throw — not merely logged. Without that routing the boundary never fires.
            // Act
            using var mounted = V.Mount(_root, V.Component(EffectBoundaryWrappingChildRender, key: "effect-boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_effectBoundaryFallbackShown, Is.True,
                "A throwing layout effect propagates to the enclosing boundary, firing its fallback factory");
        }

        [Test]
        public void Given_ChildLayoutEffectThrows_When_Mounted_Then_BoundaryReceivesThrownException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(EffectBoundaryWrappingChildRender, key: "effect-boundary-msg"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_effectBoundaryCaughtMessage, Is.EqualTo("Test effect error"),
                "The fallback factory receives the exact exception thrown by the descendant's effect");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode EffectBoundaryWrappingChildRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_effectBoundaryFallbackShown = true;
                s_effectBoundaryCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(EffectThrowingChildRender, key: "effect-child");
        }

        [Component]
        private static VNode EffectThrowingChildRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() => throw new InvalidOperationException("Test effect error")), Array.Empty<object>());
            return V.Label(text: "ok");
        }

        [Test]
        public void Given_ChildEffectCleanupThrows_When_DepsChange_Then_CleanupRunsExactlyOnce()
        {
            // A cleanup throw routes to the boundary, whose fallback synchronously unmounts the child. The
            // cleanup must run only once (it is detached before invocation), not a second time from the nested
            // unmount's own cleanup pass over the same slot.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EffectCleanupBoundaryRender, key: "cleanup-boundary"), CaughtErrors.Unlogged);
            Assume.That(s_effectCleanupRunCount, Is.EqualTo(0), "Precondition: setup ran, no cleanup yet");

            // Act — a deps change runs the prior cleanup, which throws.
            s_effectCleanupChildSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_effectCleanupRunCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_ChildEffectCleanupThrows_When_DepsChange_Then_EnclosingBoundaryShowsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(EffectCleanupBoundaryRender, key: "cleanup-boundary-fb"), CaughtErrors.Unlogged);

            // Act
            s_effectCleanupChildSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_effectBoundaryFallbackShown, Is.True,
                "A throwing effect cleanup propagates to the enclosing boundary");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode EffectCleanupBoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_effectBoundaryFallbackShown = true;
                s_effectBoundaryCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(EffectCleanupThrowingChildRender, key: "cleanup-child");
        }

        [Component]
        private static VNode EffectCleanupThrowingChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_effectCleanupChildSetTick = setTick;
            // deps = [tick]: changing tick runs this effect's prior cleanup, which throws.
            Hooks.UseLayoutEffect(() => (Action)(() =>
            {
                s_effectCleanupRunCount++;
                throw new InvalidOperationException("Test cleanup error");
            }), new object[] { tick });
            return V.Label(text: "ok");
        }

        #endregion

        #region A boundary's own fallback content throws (self re-catch guard)

        private static int s_brokenFallbackContentRenderCount;
        private static int s_outerBoundaryForCascadeFallbackRenderCount;

        private static void ResetBrokenFallback()
        {
            s_brokenFallbackContentRenderCount = 0;
            s_outerBoundaryForCascadeFallbackRenderCount = 0;
        }

        [Test]
        public void Given_ABoundarysOwnFallbackContentThrows_When_TheOriginalExceptionTriggersIt_Then_TheFallbackContentRendersExactlyOnce()
        {
            // A component nested inside the fallback VNode throws when rendered. Its exception routes back to
            // this SAME boundary through the ordinary per-fiber render catch (the boundary is the nested
            // fiber's parent). Without a re-entrant guard, the boundary would attempt to show its own (still
            // broken) fallback again, recursing without bound. The guard makes it decline immediately instead.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWithBrokenFallbackRender, key: "broken-fallback-boundary"), CaughtErrors.Unlogged);
            Assume.That(s_brokenFallbackContentRenderCount, Is.EqualTo(0), "Precondition: nothing has thrown yet");
            s_trackingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test fallback content error");
            // The original exception is not treated as caught just because the fallback rendered (see the
            // next test) — it also surfaces here since this boundary has no ancestor to escalate to.
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test render error");

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_brokenFallbackContentRenderCount, Is.EqualTo(1),
                "The broken fallback content renders exactly once, not recursively");
        }

        [Test]
        public void Given_ABoundarysOwnFallbackContentThrows_When_NoAncestorBoundaryExists_Then_TheOriginalExceptionIsStillLogged()
        {
            // TryShowFallback reports the ORIGINAL exception as caught only when the fallback both
            // Reconciles without a raw throw AND its own content did not fail (fiber.FallbackContentFailed
            // stays false) — a Reconcile call returning normally is not enough on its own, since the
            // fallback's failure can be absorbed elsewhere (logged, or shown by a farther boundary) without
            // a raw throw reaching this call site. With no ancestor boundary here, an uncaught original
            // exception falls through to Debug.LogException like any other uncaught exception.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWithBrokenFallbackRender, key: "broken-fallback-boundary-logged"), CaughtErrors.Unlogged);
            s_trackingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test fallback content error");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test render error");

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — LogAssert.Expect (registered above) verifies the original exception is logged
            // rather than silently treated as caught
        }

        [Test]
        public void Given_ABoundarysOwnFallbackContentThrows_When_AnAncestorBoundaryExists_Then_TheAncestorShowsItsFallbackExactlyOnce()
        {
            // With an ancestor boundary present, the inner boundary's failed fallback attempt now
            // correctly reports failure (see the two tests above), so propagation continues past it —
            // the ancestor gets the chance to show a working fallback instead of the error being lost.
            // Exactly once, not twice: the cascaded fallback-content exception's own propagation already
            // reaches and resolves this same ancestor before the original exception's propagation would
            // otherwise redundantly retry it (PropagateException stops once the original exception's own
            // throwing fiber is disposed, since that means its whole context was already replaced).
            // Arrange
            using var mounted = V.Mount(_root,
                V.Component(OuterBoundaryWrappingBrokenInnerBoundaryRender, key: "outer-wrapping-broken-inner"), CaughtErrors.Unlogged);
            s_trackingShouldThrow = true;

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_outerBoundaryForCascadeFallbackRenderCount, Is.EqualTo(1),
                "The ancestor boundary's fallback factory runs exactly once for the whole cascade, not once per exception");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWithBrokenFallbackRender()
        {
            Hooks.UseFallback(_ => V.Component(BrokenFallbackContentRender, key: "broken-fallback-content"));
            return V.Component(TrackingRender, key: "tracking");
        }

        [Component]
        private static VNode BrokenFallbackContentRender()
        {
            s_brokenFallbackContentRenderCount++;
            throw new InvalidOperationException("Test fallback content error");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode OuterBoundaryWrappingBrokenInnerBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_outerBoundaryForCascadeFallbackRenderCount++;
                return V.Label(text: "outer-fallback-shown");
            });
            return V.Component(BoundaryWithBrokenFallbackRender, key: "inner");
        }

        #endregion

        #region Where in its container a boundary's fallback lands

        private static bool s_placementShouldThrow;
        private static Action<int> s_placementSetTick;

        private static void ResetPlacement()
        {
            s_placementShouldThrow = false;
            s_placementSetTick = null;
        }

        [Test]
        public void Given_ABoundaryWithSiblingsOnBothSides_When_ItCatches_Then_OnlyItsOwnSlotsAreRewritten()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PlacementHostRender, key: "host"), CaughtErrors.Unlogged);
            s_placementShouldThrow = true;

            // Act
            s_placementSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the container's rows by name and in order. The fallback is present on the broken
            // tree too, sitting where the leading sibling was and beside the subtree that threw, so a
            // lookup for the fallback alone reads the same on both.
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("placement-ahead,placement-fallback,placement-behind"));
        }

        [Component]
        private static VNode PlacementThrowerRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_placementSetTick = setTick;
            if (s_placementShouldThrow) throw new InvalidOperationException("Placement boundary throw");
            return V.Label(name: "placement-thrower", text: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode PlacementBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "placement-fallback", text: "caught"));
            return V.Div(name: "placement-thrown", children: new VNode[]
            {
                V.Component(PlacementThrowerRender, key: "thrower"),
            });
        }

        [Component]
        private static VNode PlacementHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(name: "placement-ahead", text: "ahead"),
                V.Component(PlacementBoundaryRender, key: "boundary"),
                V.Label(name: "placement-behind", text: "behind"),
            });

        #endregion

        #region A catch on either side of the expansion that re-places the boundary

        private static bool s_movedShouldThrow;
        private static bool s_movedThrowsInCallback;
        private static Action<int> s_movedSetOrder;
        private static Action<int> s_movedSetLeading;
        private static Action<int> s_movedSetGrow;
        private static Action<int> s_movedSetTick;

        private static void ResetMoved()
        {
            s_movedShouldThrow = false;
            s_movedThrowsInCallback = false;
            s_movedSetOrder = null;
            s_movedSetLeading = null;
            s_movedSetGrow = null;
            s_movedSetTick = null;
        }

        [Test]
        public void Given_APassReorderingTheBoundaryBehindItsSibling_When_ItCatches_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;

            // Act
            s_movedSetOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-ahead,moved-fallback"));
        }

        [Test]
        public void Given_APassDroppingRowsAheadOfTheBoundary_When_ItCatches_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShrinkHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;

            // Act
            s_movedSetLeading.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-lead0,moved-fallback"));
        }

        [Test]
        public void Given_APassAddingRowsAheadOfTheBoundary_When_ItCatches_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrowHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;

            // Act
            s_movedSetGrow.Invoke(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-lead0,moved-lead1,moved-lead2,moved-fallback"));
        }

        [Test]
        public void Given_APassReorderingTheBoundaryBehindItsSibling_When_ItCatchesAnElementCallbackError_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;
            s_movedThrowsInCallback = true;
            // The callback fails while the pass creates the element, so the boundary catches it in the walk as it
            // catches a render error, and the pass commits around the fallback.

            // Act
            s_movedSetOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-ahead,moved-fallback"));
        }

        [Test]
        public void Given_APassDroppingRowsAheadOfTheBoundary_When_ItCatchesAnElementCallbackError_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ShrinkHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;
            s_movedThrowsInCallback = true;
            // The callback fails while the pass creates the element, so the boundary catches it in the walk as it
            // catches a render error, and the pass commits around the fallback.

            // Act
            s_movedSetLeading.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-lead0,moved-fallback"));
        }

        [Test]
        public void Given_APassAddingRowsAheadOfTheBoundary_When_ItCatchesAnElementCallbackError_Then_TheFallbackTakesTheRowsThatPassGaveIt()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrowHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedShouldThrow = true;
            s_movedThrowsInCallback = true;
            // The callback fails while the pass creates the element, so the boundary catches it in the walk as it
            // catches a render error, and the pass commits around the fallback.

            // Act
            s_movedSetGrow.Invoke(3);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-lead0,moved-lead1,moved-lead2,moved-fallback"));
        }

        [Test]
        public void Given_APassAlreadyReorderedTheBoundaryBehindItsSibling_When_ALaterRenderThrows_Then_TheRowsItStillHoldsAreTheOnesRewritten()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ReorderHostRender, key: "host"), CaughtErrors.Unlogged);
            s_movedSetOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Act — the thrower's own re-render, so no expansion re-places the boundary this time.
            s_movedShouldThrow = true;
            s_movedSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                string.Join(",", _root.ElementAt(0).Children().Select(child => child.name)),
                Is.EqualTo("moved-ahead,moved-fallback"));
        }

        [Component]
        private static VNode MovedThrowerRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_movedSetTick = setTick;
            if (s_movedShouldThrow && s_movedThrowsInCallback)
            {
                return V.ScrollView(name: "moved-thrown", onCreated: _ => throw new InvalidOperationException("Moved boundary throw"));
            }
            if (s_movedShouldThrow) throw new InvalidOperationException("Moved boundary throw");
            return V.Label(name: "moved-thrown", text: "ok");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode MovedBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "moved-fallback", text: "caught"));
            return V.Component(MovedThrowerRender, key: "thrower");
        }

        [Component]
        private static VNode ReorderHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_movedSetOrder = setOrder;
            return order == 0
                ? V.Div(children: new VNode[]
                {
                    V.Component(MovedBoundaryRender, key: "boundary"),
                    V.Label(name: "moved-ahead", text: "ahead", key: "ahead"),
                })
                : V.Div(children: new VNode[]
                {
                    V.Label(name: "moved-ahead", text: "ahead", key: "ahead"),
                    V.Component(MovedBoundaryRender, key: "boundary"),
                });
        }

        [Component]
        private static VNode GrowHostRender()
        {
            var (leading, setGrow) = Hooks.UseState(1);
            s_movedSetGrow = setGrow;
            return LeadingRowsBefore(leading);
        }

        [Component]
        private static VNode ShrinkHostRender()
        {
            var (leading, setLeading) = Hooks.UseState(3);
            s_movedSetLeading = setLeading;
            return LeadingRowsBefore(leading);
        }

        private static VNode LeadingRowsBefore(int leading)
        {
            var children = new VNode[leading + 1];
            for (var i = 0; i < leading; i++)
            {
                children[i] = V.Label(name: "moved-lead" + i, text: "lead", key: "lead" + i);
            }
            children[leading] = V.Component(MovedBoundaryRender, key: "boundary");
            return V.Div(children: children);
        }

        #endregion

        #region A catch inside its parent's render

        private static bool s_unreachedShouldThrow;
        private static Action<int> s_unreachedSetHostTick;
        private static Action<int> s_unreachedSetCount;
        private static int s_unreachedCleanups;

        private static void ResetUnreached()
        {
            s_unreachedShouldThrow = false;
            s_unreachedSetHostTick = null;
            s_unreachedSetCount = null;
            s_unreachedCleanups = 0;
            s_droppedCleanups = 0;
            s_droppedSetValue = null;
            s_throwingCleanupRuns = 0;
            s_throwingSetValue = null;
            s_throwingFiber = null;
        }

        [Test]
        public void Given_ASiblingBehindACatchingBoundary_When_ItCatchesInTheParentsRender_Then_TheSiblingKeepsItsStateAndEffect()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UnreachedHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_unreachedSetCount.Invoke(1);
            mounted.FlushStateForTest();
            s_unreachedShouldThrow = true;
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = _root.FindLabelByText("unreached-fallback") != null;
            var cleanupsAfterCatch = s_unreachedCleanups;
            s_unreachedShouldThrow = false;

            // Act
            s_unreachedSetHostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert — the fallback term says the boundary caught.
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", cleanups " + cleanupsAfterCatch + ", "
                    + string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("fell back, cleanups 0, unreached-fallback,count:1"));
        }

        [Test]
        public void Given_ABoundaryCatchingAChildsRender_When_ThePassEnds_Then_NoFiberIsLeftOnTheStack()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UnreachedHostRender, key: "host"), CaughtErrors.Unlogged);
            s_unreachedShouldThrow = true;

            // Act
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback term says the catch happened.
            Assert.That(
                (_root.FindLabelByText("unreached-fallback") != null ? "fell back" : "never fell back")
                    + ", stack depth " + mounted.Root.Reconciler.Context.FiberStack.Depth,
                Is.EqualTo("fell back, stack depth 0"));
        }

        [Component(Compiler = false)]
        private static VNode UnreachedThrowerRender()
        {
            if (s_unreachedShouldThrow) throw new InvalidOperationException("Unreached sibling throw");
            return V.Label(text: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode UnreachedBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "unreached-fallback"));
            return V.Component(UnreachedThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode UnreachedCounterRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_unreachedSetCount = setCount;
            Hooks.UseEffect(() => () => s_unreachedCleanups++, Array.Empty<object>());
            return V.Label(text: "count:" + count);
        }

        private static int s_droppedCleanups;
        private static Action<int> s_droppedSetValue;

        // GREEN_ON_BASE(characterization): the merge base sweeps the dropped child with the orphans of the walk it stops.
        // The catch taken in the walk leaves that walk to complete, and the child going in its sweep is what this pins.
        [Test]
        public void Given_AChildThatDropsItsOwnChildAheadOfACatch_When_TheChildRendersItAgain_Then_ItMountsAfresh()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(DroppingHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_droppedSetValue.Invoke(5);
            mounted.FlushStateForTest();
            s_unreachedShouldThrow = true;
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = _root.FindLabelByText("unreached-fallback") != null;
            var cleanupsAfterCatch = s_droppedCleanups;
            s_unreachedShouldThrow = false;

            // Act
            s_unreachedSetHostTick.Invoke(0);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the fallback term says the boundary caught; the value says which instance came back: a
            // new one starts at 0, one left registered holds 5. The great-grandchild is the dropped child's own
            // child, so both cleanups count. Only the grandchild's label is read.
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", cleanups " + cleanupsAfterCatch + ", "
                    + string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)
                        .Where(text => text.StartsWith("grandchild"))),
                Is.EqualTo("fell back, cleanups 2, grandchild:0"));
        }

        [Test]
        public void Given_ASiblingBehindACatchInsideAChildThatReRendered_When_ItCatchesInThatRender_Then_TheSiblingKeepsItsStateAndEffect()
        {
            // Arrange — the same sibling as the case above it, two components further in: the child holding the
            // boundary and the sibling, and the one above it, both render in the pass the catch is taken in.
            using var mounted = V.Mount(_root, V.Component(NestingHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_unreachedSetCount.Invoke(1);
            mounted.FlushStateForTest();
            s_unreachedShouldThrow = true;
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = _root.FindLabelByText("unreached-fallback") != null;
            var cleanupsAfterCatch = s_unreachedCleanups;
            s_unreachedShouldThrow = false;

            // Act
            s_unreachedSetHostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", cleanups " + cleanupsAfterCatch + ", "
                    + string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("fell back, cleanups 0, unreached-fallback,count:1"));
        }

        // GREEN_ON_BASE(characterization): the merge base sweeps this component with the orphans of the walk it stops.
        // The catch taken in the walk leaves that walk to complete, and its sweep is what this pins.
        [Test]
        public void Given_AChildWhoseOwnElementDropsAComponentBehindACatch_When_ItCatchesInThatRender_Then_TheDroppedComponentIsCleanedUp()
        {
            // Arrange — the boundary and the dropped component sit in an element the child renders, so the
            // walk that stops is that element's, and the child that owns it re-rendered in the enclosing one.
            using var mounted = V.Mount(_root, V.Component(WrappingHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_unreachedShouldThrow = true;

            // Act
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (_root.FindLabelByText("unreached-fallback") != null ? "fell back" : "never fell back")
                    + ", cleanups " + s_droppedCleanups,
                Is.EqualTo("fell back, cleanups 2"));
        }

        [Component(Compiler = false)]
        private static VNode WrappingChildRender(int tick)
            => V.Div(children: new VNode[]
            {
                V.Component(UnreachedBoundaryRender, key: "boundary"),
                tick == 0 ? V.Component(DroppedGrandchildRender, key: "grandchild") : null,
            });

        [Component(Compiler = false)]
        private static VNode WrappingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[] { V.Component(WrappingChildRender, tick, key: "child") });
        }

        [Component(Compiler = false)]
        private static VNode DroppedGrandchildRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_droppedSetValue = setValue;
            Hooks.UseEffect(() => () => s_droppedCleanups++, Array.Empty<object>());
            return V.Fragment(new VNode[]
            {
                V.Label(text: "grandchild:" + value),
                V.Component(DroppedGreatGrandchildRender, key: "great"),
            });
        }

        [Test]
        public void Given_AParentWhoseRenderDropsAChildBesideACatch_When_ItRendersTheChildAgain_Then_TheChildMountsAfresh()
        {
            // Arrange — the parent's render drops the child ahead of the boundary, and the catch leaves that
            // render to commit.
            using var mounted = V.Mount(_root, V.Component(DiscardedDropHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_unreachedSetCount.Invoke(1);
            mounted.FlushStateForTest();
            s_unreachedShouldThrow = true;
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = _root.FindLabelByText("unreached-fallback") != null;
            var cleanupsAfterCatch = s_unreachedCleanups;
            s_unreachedShouldThrow = false;

            // Act
            s_unreachedSetHostTick.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", cleanups " + cleanupsAfterCatch + ", "
                    + string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("fell back, cleanups 1, count:0,unreached-fallback"));
        }

        private static VelvetTaskCompletionSource<string> s_pendingResource;
        private static Action<int> s_shownFallbackSetValue;
        private static int s_shownFallbackCleanups;

        // GREEN_ON_BASE(characterization): the merge base keeps this fallback by stopping the pass short of it.
        // The catch taken in the walk keeps it by rendering the Suspense again, still pending, which this pins.
        [Test]
        public void Given_ASuspenseShowingItsFallbackBehindACatch_When_ItCatchesInThatRender_Then_TheFallbackKeepsItsStateAndEffect()
        {
            // Arrange — the Suspense comes after the boundary inside a child that re-renders, and its reader is
            // still pending when that render reaches it.
            s_pendingResource = new VelvetTaskCompletionSource<string>();
            s_shownFallbackCleanups = 0;
            using var mounted = V.Mount(_root, V.Component(SuspenseAfterCatchHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_shownFallbackSetValue.Invoke(5);
            mounted.FlushStateForTest();
            s_unreachedShouldThrow = true;
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            var fellBack = _root.FindLabelByText("unreached-fallback") != null;
            var cleanupsAfterCatch = s_shownFallbackCleanups;
            s_unreachedShouldThrow = false;

            // Act
            s_unreachedSetHostTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", cleanups " + cleanupsAfterCatch + ", "
                    + string.Join(",", _root.Query<Label>().ToList().Select(label => label.text)
                        .Where(text => text.StartsWith("waiting"))),
                Is.EqualTo("fell back, cleanups 0, waiting:5"));
        }

        [Component(Compiler = false)]
        private static VNode PendingReaderRender()
        {
            var text = Hooks.Use(_ => s_pendingResource.Task);
            return V.Label(text: "loaded:" + text);
        }

        [Component(Compiler = false)]
        private static VNode ShownFallbackRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_shownFallbackSetValue = setValue;
            Hooks.UseEffect(() => () => s_shownFallbackCleanups++, Array.Empty<object>());
            return V.Label(text: "waiting:" + value);
        }

        [Component(Compiler = false)]
        private static VNode SuspenseAfterCatchChildRender(int tick)
            => V.Fragment(new VNode[]
            {
                V.Component(UnreachedBoundaryRender, key: "boundary"),
                V.Suspense(
                    fallback: V.Component(ShownFallbackRender, key: "waiting"),
                    children: new VNode[] { V.Component(PendingReaderRender, key: "reader") }),
            });

        [Component(Compiler = false)]
        private static VNode SuspenseAfterCatchHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[] { V.Component(SuspenseAfterCatchChildRender, tick, key: "child") });
        }

        [Component(Compiler = false)]
        private static VNode DiscardedDropHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[]
            {
                tick == 0 ? V.Component(UnreachedCounterRender, key: "counter") : null,
                V.Component(UnreachedBoundaryRender, key: "boundary"),
            });
        }

        [Component(Compiler = false)]
        private static VNode DroppedGreatGrandchildRender()
        {
            Hooks.UseEffect(() => () => s_droppedCleanups++, Array.Empty<object>());
            return V.Label(text: "great");
        }

        [Component(Compiler = false)]
        private static VNode DroppingChildRender(int tick)
            => tick == 0 ? V.Component(DroppedGrandchildRender, key: "grandchild") : V.Label(text: "no-grandchild");

        [Component(Compiler = false)]
        private static VNode DroppingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(DroppingChildRender, tick, key: "child"),
                V.Component(UnreachedBoundaryRender, key: "boundary"),
                V.Component(UnreachedCounterRender, key: "counter"),
            });
        }

        [Component(Compiler = false)]
        private static VNode NestingChildRender(int tick)
            => V.Fragment(new VNode[]
            {
                V.Component(UnreachedBoundaryRender, key: "boundary"),
                V.Component(UnreachedCounterRender, key: "counter"),
            });

        [Component(Compiler = false)]
        private static VNode NestingOuterRender(int tick) => V.Component(NestingChildRender, tick, key: "child");

        [Component(Compiler = false)]
        private static VNode NestingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[] { V.Component(NestingOuterRender, tick, key: "outer") });
        }

        private static int s_throwingCleanupRuns;
        private static Action<int> s_throwingSetValue;

        // GREEN_ON_BASE(characterization): the merge base already disposes an orphan whose cleanup raised the abort.
        // That abort comes after a walk that reached every fiber, so nothing is kept back from the sweep.
        [Test]
        public void Given_AnOrphanWhoseCleanupThrowsIntoABoundary_When_TheRenderDropsIt_Then_ItIsDisposed()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CleanupThrowHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_throwingSetValue.Invoke(5);
            mounted.FlushStateForTest();
            var orphan = s_throwingFiber;

            // Act
            s_unreachedSetHostTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the fallback term says the boundary caught the cleanup's error
            Assert.That(
                (_root.FindLabelByText("cleanup-fallback") != null ? "fell back" : "never fell back")
                    + ", cleanup ran " + s_throwingCleanupRuns + ", disposed " + orphan?.IsDisposed,
                Is.EqualTo("fell back, cleanup ran 1, disposed True"));
        }

        private static ComponentFiber s_throwingFiber;

        [Component(Compiler = false)]
        private static VNode ThrowingCleanupRender()
        {
            s_throwingFiber = FiberAmbientStack.Current;
            var (value, setValue) = Hooks.UseState(0);
            s_throwingSetValue = setValue;
            Hooks.UseEffect(() => () =>
            {
                s_throwingCleanupRuns++;
                throw new InvalidOperationException("Cleanup throw");
            }, Array.Empty<object>());
            return V.Label(text: "throwing:" + value);
        }

        // The boundary is the orphan's own parent, so the fallback it shows replaces a tree that no longer
        // holds the orphan, and only the sweep of the walk that dropped it can dispose it.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CleanupBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "cleanup-fallback"));
            return tick == 1 ? V.Label(text: "dropped") : V.Component(ThrowingCleanupRender, key: "throwing");
        }

        [Component(Compiler = false)]
        private static VNode CleanupThrowHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[] { V.Component(CleanupBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false)]
        private static VNode UnreachedHostRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_unreachedSetHostTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(UnreachedBoundaryRender, key: "boundary"),
                V.Component(UnreachedCounterRender, key: "counter"),
            });
        }

        #endregion
    }

    /// <summary>
    /// Specifies the contract of a function-component Error Boundary, declared as
    /// <c>[Component(IsErrorBoundary = true)]</c> with a fallback factory registered inside Render via
    /// <see cref="Hooks.UseFallback(System.Func{System.Exception, VNode})"/>.
    /// <list type="bullet">
    /// <item>A render exception propagates only to ancestor boundaries; the throwing fiber's own enclosing
    /// boundary is the nearest ancestor that opted in via <c>IsErrorBoundary = true</c>.</item>
    /// <item>When an ancestor boundary catches a child exception, its registered fallback factory runs and
    /// receives the caught exception, and the fallback VNode replaces the boundary's subtree.</item>
    /// <item>A boundary never catches an exception thrown by its own Render; that exception bubbles to an
    /// enclosing boundary instead, so the boundary's own fallback factory does not run.</item>
    /// <item>A boundary that does not register a fallback factory produces no fallback and lets the exception
    /// bubble to an enclosing boundary.</item>
    /// <item>When no enclosing boundary catches the exception, it is logged as an unhandled exception.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The fixture's
    /// static observation fields are reset together in <see cref="SetUp"/> via <see cref="ResetBoundaryState"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ExplicitErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetBoundaryState();
        }

        [Test]
        public void Given_NoEnclosingBoundary_When_ComponentRenderThrows_Then_LogsException()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "Exception: boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowingParentRender, key: "parent"));

            // Assert — LogAssert.Expect verifies the unhandled exception was logged
        }

        [Test]
        public void Given_BoundaryWrappingThrowingChild_When_ChildRenderThrows_Then_FallbackFactoryRuns()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "The factory registered via Hooks.UseFallback at the boundary fires on a child exception");
        }

        [Test]
        public void Given_BoundaryWrappingThrowingChild_When_ChildRenderThrows_Then_FactoryReceivesCaughtException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"), CaughtErrors.Unlogged);
            Assume.That(s_fallbackShown, Is.True, "Precondition: the boundary caught the child exception");

            // Assert
            Assert.That(s_lastCaughtMessage, Is.EqualTo("boom-child"),
                "The fallback factory receives the exact exception thrown by the child render");
        }

        [Test]
        public void Given_BoundaryWhoseOwnRenderThrows_When_Mounted_Then_LogsException()
        {
            // Arrange — without an enclosing boundary, the un-self-caught exception is logged
            LogAssert.Expect(LogType.Exception, "Exception: self-boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"), CaughtErrors.Unlogged);

            // Assert — LogAssert.Expect verifies the own-Render exception was not self-caught but logged
        }

        [Test]
        public void Given_BoundaryWhoseOwnRenderThrows_When_Mounted_Then_OwnFallbackFactoryDoesNotRun()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "Exception: self-boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_fallbackShown, Is.False,
                "A boundary does not catch an exception thrown by its own Render");
        }

        [Test]
        public void Given_BoundaryWithoutFallback_When_ChildRenderThrows_Then_ExceptionBubblesAndIsLogged()
        {
            // Arrange — the boundary opts in but registers no fallback factory, so the exception bubbles
            // past it to an enclosing boundary; with none present it is logged as unhandled
            LogAssert.Expect(LogType.Exception, "Exception: boom-child");

            // Act
            using var mounted = V.Mount(_root, V.Component(NoFallbackBoundaryRender, key: "no-fallback"), CaughtErrors.Unlogged);

            // Assert — LogAssert.Expect verifies the un-caught child exception was logged
        }

        #region Boundary observation state

        private static bool s_fallbackShown;
        private static string s_lastCaughtMessage;

        private static void ResetBoundaryState()
        {
            s_fallbackShown = false;
            s_lastCaughtMessage = null;
        }

        #endregion

        #region ThrowingParent component (no boundary; its own Render throws)

        [Component]
        private static VNode ThrowingParentRender() => throw new Exception("boom");

        #endregion

        #region BoundaryWrappingThrower component (boundary + Hooks.UseFallback wrapping a throwing child)

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryWrappingThrowerRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_fallbackShown = true;
                s_lastCaughtMessage = ex.Message;
                return V.Label(text: "caught");
            });
            return V.Fragment(new VNode[] { V.Component(ThrowingChildRender, key: "throwing-child") });
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new Exception("boom-child");

        #endregion

        #region SelfThrowingBoundary component (boundary whose own Render throws)

        [Component(IsErrorBoundary = true)]
        private static VNode SelfThrowingBoundaryRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_fallbackShown = true;
                return V.Label(text: "should-not-self-catch");
            });
            throw new Exception("self-boom");
        }

        #endregion

        #region NoFallbackBoundary component (boundary opt-in but no Hooks.UseFallback call)

        [Component(IsErrorBoundary = true)]
        private static VNode NoFallbackBoundaryRender()
            => V.Fragment(new VNode[] { V.Component(ThrowingChildRender, key: "throwing-child") });

        #endregion
    }

    /// <summary>
    /// Specifies that error-boundary identity resolves through <see cref="ComponentMethodRegistry"/> even when the
    /// boundary component is declared inside a closed generic class.
    /// <list type="bullet">
    /// <item>A boundary declared in a closed generic class catches a descendant's render exception, because the
    /// registry rebuilds the open-form lookup key when the live type name carries a type-argument suffix.</item>
    /// <item>A boundary declared in a type nested inside a closed generic class likewise catches, because the
    /// registry walks the declaring chain to rebuild the open form when the live type name is null.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The throw is
    /// driven by a child-side <c>setTick</c> setter so it fires on an update.
    /// </remarks>
    [TestFixture]
    internal sealed class GenericClassErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_fallbackShown = false;
            s_throwOnNextRender = false;
            s_setTick = null;
        }

        [Test]
        public void Given_BoundaryInClosedGenericClass_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GenericBoundary<int>.Render, key: "boundary"), CaughtErrors.Unlogged);
            Assume.That(s_fallbackShown, Is.False, "Precondition: the initial mount renders the child without fallback");
            Assume.That(s_setTick, Is.Not.Null, "Precondition: the child wired its setter on the initial mount");
            s_throwOnNextRender = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "A boundary in a closed generic class resolves through the registry's open-form key and catches");
        }

        [Test]
        public void Given_BoundaryInNestedTypeOfClosedGeneric_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange — the live type name of a type nested in a closed generic is null, so the registry must
            // walk the declaring chain to rebuild the open-form key.
            using var mounted = V.Mount(_root, V.Component(GenericOuter<int>.NestedBoundary.Render, key: "boundary"), CaughtErrors.Unlogged);
            Assume.That(s_fallbackShown, Is.False, "Precondition: the initial mount renders the child without fallback");
            Assume.That(s_setTick, Is.Not.Null, "Precondition: the child wired its setter on the initial mount");
            s_throwOnNextRender = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "A boundary nested inside a closed generic resolves through the chain-walking open-form key and catches");
        }

        private static bool s_fallbackShown;
        private static bool s_throwOnNextRender;
        private static Action<int> s_setTick;

        private static class GenericBoundary<T>
        {
            [Component(IsErrorBoundary = true)]
            public static VNode Render()
            {
                Hooks.UseFallback(_ =>
                {
                    s_fallbackShown = true;
                    return V.Label(text: "error");
                });
                return V.Component(ChildRender, key: "child");
            }
        }

        private static class GenericOuter<T>
        {
            public static class NestedBoundary
            {
                [Component(IsErrorBoundary = true)]
                public static VNode Render()
                {
                    Hooks.UseFallback(_ =>
                    {
                        s_fallbackShown = true;
                        return V.Label(text: "error");
                    });
                    return V.Component(NestedChildRender, key: "child");
                }
            }
        }

        [Component]
        private static VNode ChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throwOnNextRender) throw new InvalidOperationException("Generic boundary throw");
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode NestedChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throwOnNextRender) throw new InvalidOperationException("Nested generic boundary throw");
            return V.Label(text: "ok");
        }
    }
}

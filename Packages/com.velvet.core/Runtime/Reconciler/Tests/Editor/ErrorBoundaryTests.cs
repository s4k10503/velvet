using System;
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
    /// <item>A boundary recovers: once a later render produces no throwing child, the abort state resets, all
    /// children mount, and the fallback factory does not restart.</item>
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
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingTrackingRender, key: "wrapper"));
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
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingTrackingRender, key: "wrapper"));
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
            using var mounted = V.Mount(_root, V.Component(GrandparentBoundaryRender, key: "grandparent"));
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
            using var mounted = V.Mount(_root, V.Component(MultiChildBoundaryRender, key: "multi"));
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 3;

            // Act
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_multiFallbackShown, Is.True,
                "A first child that throws aborts the multi-child render and fires the boundary's fallback factory");
        }

        [Test]
        public void Given_MultiChildBoundaryAfterFallback_When_AllChildrenSucceed_Then_AllChildrenMount()
        {
            // Arrange — drive the boundary into its fallback first
            using var mounted = V.Mount(_root, V.Component(MultiChildBoundaryRender, key: "multi"));
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 2;
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_multiFallbackShown, Is.True, "Precondition: the first pass shows the fallback");
            s_multiFallbackShown = false;
            s_multiNormalRenderCount = 0;
            s_multiFirstChildShouldThrow = false;

            // Act
            s_multiSetTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_multiNormalRenderCount, Is.GreaterThanOrEqualTo(s_multiChildCount),
                "Once all children succeed the abort state resets and every child mounts");
        }

        [Test]
        public void Given_MultiChildBoundaryAfterFallback_When_AllChildrenSucceed_Then_FallbackDoesNotRestart()
        {
            // Arrange — drive the boundary into its fallback first
            using var mounted = V.Mount(_root, V.Component(MultiChildBoundaryRender, key: "multi"));
            s_multiFirstChildShouldThrow = true;
            s_multiChildCount = 2;
            s_multiSetTick.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(s_multiFallbackShown, Is.True, "Precondition: the first pass shows the fallback");
            s_multiFallbackShown = false;
            s_multiFirstChildShouldThrow = false;

            // Act
            s_multiSetTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_multiFallbackShown, Is.False,
                "After recovery the fallback factory does not re-run");
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
            using var mounted = V.Mount(_root, V.Component(EffectBoundaryWrappingChildRender, key: "effect-boundary"));

            // Assert
            Assert.That(s_effectBoundaryFallbackShown, Is.True,
                "A throwing layout effect propagates to the enclosing boundary, firing its fallback factory");
        }

        [Test]
        public void Given_ChildLayoutEffectThrows_When_Mounted_Then_BoundaryReceivesThrownException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(EffectBoundaryWrappingChildRender, key: "effect-boundary-msg"));

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
            using var mounted = V.Mount(_root, V.Component(EffectCleanupBoundaryRender, key: "cleanup-boundary"));
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
            using var mounted = V.Mount(_root, V.Component(EffectCleanupBoundaryRender, key: "cleanup-boundary-fb"));

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

        private static void ResetBrokenFallback()
        {
            s_brokenFallbackContentRenderCount = 0;
        }

        [Test]
        public void Given_ABoundarysOwnFallbackContentThrows_When_TheOriginalExceptionTriggersIt_Then_TheFallbackContentRendersExactlyOnce()
        {
            // A component nested inside the fallback VNode throws when rendered. Its exception routes back to
            // this SAME boundary through the ordinary per-fiber render catch (the boundary is the nested
            // fiber's parent). Without a re-entrant guard, the boundary would attempt to show its own (still
            // broken) fallback again, recursing without bound. The guard makes it decline immediately instead.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWithBrokenFallbackRender, key: "broken-fallback-boundary"));
            Assume.That(s_brokenFallbackContentRenderCount, Is.EqualTo(0), "Precondition: nothing has thrown yet");
            s_trackingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test fallback content error");

            // Act
            s_trackingSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_brokenFallbackContentRenderCount, Is.EqualTo(1),
                "The broken fallback content renders exactly once, not recursively");
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

        #endregion
    }
}

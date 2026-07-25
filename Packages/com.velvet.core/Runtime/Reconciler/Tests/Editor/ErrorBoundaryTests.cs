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
            using var mounted = V.Mount(_root, V.Component(BoundaryWithBrokenFallbackRender, key: "broken-fallback-boundary"));
            Assume.That(s_brokenFallbackContentRenderCount, Is.EqualTo(0), "Precondition: nothing has thrown yet");
            s_trackingShouldThrow = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test fallback content error");
            // The original exception is no longer silently treated as caught (see the next test) — it
            // also surfaces here since this boundary has no ancestor to escalate to.
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
            // Before this fix, TryShowFallback reported the ORIGINAL exception as successfully caught
            // whenever its Reconcile call returned without a raw throw — true whether the fallback
            // content rendered cleanly or failed and was absorbed elsewhere (logged, or shown by a
            // farther boundary). With no ancestor boundary here, that meant the original exception was
            // silently dropped instead of falling through to Debug.LogException like any other uncaught
            // exception. It must now surface.
            // Arrange
            using var mounted = V.Mount(_root, V.Component(BoundaryWithBrokenFallbackRender, key: "broken-fallback-boundary-logged"));
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
                V.Component(OuterBoundaryWrappingBrokenInnerBoundaryRender, key: "outer-wrapping-broken-inner"));
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
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"));

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "The factory registered via Hooks.UseFallback at the boundary fires on a child exception");
        }

        [Test]
        public void Given_BoundaryWrappingThrowingChild_When_ChildRenderThrows_Then_FactoryReceivesCaughtException()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryWrappingThrowerRender, key: "boundary"));
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
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"));

            // Assert — LogAssert.Expect verifies the own-Render exception was not self-caught but logged
        }

        [Test]
        public void Given_BoundaryWhoseOwnRenderThrows_When_Mounted_Then_OwnFallbackFactoryDoesNotRun()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "Exception: self-boom");

            // Act
            using var mounted = V.Mount(_root, V.Component(SelfThrowingBoundaryRender, key: "self-throw"));

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
            using var mounted = V.Mount(_root, V.Component(NoFallbackBoundaryRender, key: "no-fallback"));

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
            using var mounted = V.Mount(_root, V.Component(GenericBoundary<int>.Render, key: "boundary"));
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
            using var mounted = V.Mount(_root, V.Component(GenericOuter<int>.NestedBoundary.Render, key: "boundary"));
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

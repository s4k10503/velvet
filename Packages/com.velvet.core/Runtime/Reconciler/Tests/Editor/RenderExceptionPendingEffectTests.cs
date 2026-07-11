using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a passive effect committed by an earlier, successful render survives a later render
    /// of the same fiber throwing. The render-exception handler blanket-cleared PendingEffects — but
    /// unlike the layout/insertion lists (rebuilt every render), PendingEffects intentionally
    /// persists across renders until the deferred frame-boundary flush runs it. Because the settled
    /// deps were already promoted at commit time, a wiped mount effect (stable deps) was never
    /// re-staged by any later successful render either: it silently never ran, with no error, while
    /// the component kept rendering normally. The handler must truncate back to the committed
    /// baseline instead of clearing, exactly as the render-phase retry path already does.
    /// </summary>
    [TestFixture]
    internal sealed class RenderExceptionPendingEffectTests
    {
        private readonly record struct ThrowState(bool Throw);

        private sealed class ThrowStore : Store<ThrowState>
        {
            public ThrowStore() : base(new ThrowState(false)) { }
            public void Set(bool value) => SetState(_ => new ThrowState(value));
            protected override void ResetCore() => SetState(_ => new ThrowState(false));
        }

        private static ThrowStore s_store;
        private static bool s_mountEffectRan;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_mountEffectRan = false;
        }

        [Component]
        private static VNode EffectThenThrow()
        {
            var shouldThrow = Hooks.UseStore(s_store, s => s.Throw);
            Hooks.UseEffect(() =>
            {
                s_mountEffectRan = true;
                return (Action)null;
            }, Array.Empty<object>());
            if (shouldThrow)
            {
                throw new InvalidOperationException("boom");
            }
            return V.Div(name: "etr");
        }

        [Test]
        public void Given_ACommittedMountEffectNotYetFlushed_When_ALaterRenderThrows_Then_TheEffectStillRuns()
        {
            // Arrange — mount commits the effect into the pending list; the deferred flush has not
            // run yet when a second render of the same fiber throws (no error boundary above).
            using var store = new ThrowStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(EffectThenThrow, key: "etr"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            store.Set(true);
            scheduler.DrainImmediateForTest();

            // Act — the deferred passive-effect flush finally runs.
            mounted.FlushEffectsForTest();

            // Assert — the already-committed mount effect was not discarded by the failing render.
            Assert.That(s_mountEffectRan, Is.True);
        }
    }
}

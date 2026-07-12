using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Shared harness for the Motion enter/orchestration fixtures that need a REAL (simulated) panel: their
    /// scheduled swap-to-animate and deferred inline-style clears run through
    /// <c>schedule.Execute().ExecuteLater(ms)</c>, which only fires once a panel ticks its scheduler against its
    /// own clock — the batchmode EditMode PlayerLoop never does. Owns the simulated panel and a fresh Reconciler
    /// per test, plus the deterministic frame-advance helpers every such fixture needs, so each one only has to
    /// declare its own component tree and assertions.
    /// </summary>
    /// <remarks>
    /// Lifecycle methods are <c>virtual</c>; a subclass that needs its own additional per-test reset overrides
    /// and calls <c>base</c> (see <see cref="MotionStandaloneEnterTests"/>), mirroring
    /// <c>Velvet.TestUtilities.ReconcilerScope</c>'s <c>ReconcilerTestFixture</c> base. A separate, in-asmdef
    /// base (rather than extending that one, or the Component-test assembly's own internal simulated-panel
    /// base) because neither exposes a panel-backed <c>Root</c> a Motion-enter fixture can attach to.
    /// </remarks>
    internal abstract class MotionSimulatedPanelTestsBase
    {
        private EditorPanelSimulator _sim;
        protected Reconciler _reconciler;

        [SetUp]
        public virtual void SetUp()
        {
            // Simulated time (and the per-frame step) are process-static and not auto-reset between tests;
            // reset both so this fixture's frame accounting starts from a known clock regardless of what a
            // sibling simulator-based fixture left behind.
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _reconciler = new Reconciler();
        }

        [TearDown]
        public virtual void TearDown()
        {
            _reconciler?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        protected VisualElement Root => _sim.rootVisualElement;

        // One frame (a real-frame-sized scheduler tick).
        protected void Tick() => _sim.FrameUpdateMs(16);

        // Advances well past the given duration so any scheduled callback due by then fires; the +0.2s margin
        // absorbs the scheduler's internal grace period without coupling to its exact value.
        protected void AdvancePast(float seconds)
        {
            var steps = (int)((seconds + 0.2f) * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Tick();
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Base for EditMode fixtures that need a REAL panel whose scheduler clock can be advanced deterministically
    /// — the only way to test time-driven UI Toolkit behaviour (transition completion, scheduled callbacks)
    /// without a live PlayMode loop. It wraps Unity's official <see cref="EditorPanelSimulator"/>, which overrides
    /// the panel clock with a simulated time and exposes <c>FrameUpdate</c> to tick the scheduler synchronously
    /// (<c>Panel.TickSchedulingUpdaters</c> + <c>UpdateForRepaint</c>).
    ///
    /// Why a real (simulated) panel and not a detached root: a VisualElement's
    /// <c>schedule.Execute().ExecuteLater(ms)</c> only fires when its panel ticks the scheduler against the panel
    /// clock. A detached root has no panel and never ticks, so a Velvet AnimatePresence exit never completes
    /// (its completion timer never fires). Driving a simulated panel makes those timers fire on demand, exactly
    /// once per <see cref="Frame"/>, deterministically and headless.
    /// </summary>
    internal abstract class SimulatedPanelTestBase
    {
        protected EditorPanelSimulator _sim;

        [SetUp]
        public void SimBaseSetUp()
        {
            // Simulated time AND the per-frame step are process-static and not auto-reset between tests; reset
            // both so each test's frame accounting starts from a known clock and default step, regardless of what
            // a sibling simulator fixture left behind.
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            OnSetUp();
        }

        [TearDown]
        public void SimBaseTearDown()
        {
            OnTearDown();
            _sim?.Dispose();
            _sim = null;
        }

        // Per-fixture hooks (e.g. resetting static setters), run after the panel exists / before it is disposed.
        protected virtual void OnSetUp() { }
        protected virtual void OnTearDown() { }

        // The panel root to mount onto and query.
        protected VisualElement Root => _sim.rootVisualElement;

        // Advances the simulated clock by ms and ticks the panel scheduler once: Velvet's coalesced re-render
        // drains plus any AnimatePresence enter/exit timers whose delay has now elapsed — one rendered frame.
        protected void Frame(long ms) => _sim.FrameUpdateMs(ms);

        // Ticks the scheduler without advancing the clock (commits a pending re-render / runs due next-frame work).
        protected void Settle() => _sim.FrameUpdate(0.0);
    }
}

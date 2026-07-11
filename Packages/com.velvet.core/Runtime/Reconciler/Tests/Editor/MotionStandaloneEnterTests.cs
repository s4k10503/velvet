using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins Framer parity for a standalone <c>V.Motion(variants:, initial:, animate:)</c> mounted with NO
    /// AnimatePresence: in Framer Motion, <c>initial</c>/<c>animate</c> drive the mount enter on any
    /// <c>motion.*</c> component — AnimatePresence is only required to defer an unmount so <c>exit</c> can play
    /// against it. The standalone enter plays the same variant enter the AnimatePresence expansion drives (compare
    /// <c>MotionVariantPropagationTests.Given_AMotionWithInitial_When_MountedUnderAnimatePresence_...</c> and
    /// <c>AnimatePresenceAnimationTests</c>'s "Variant initial" region): the element mounts carrying
    /// <c>variants[initial]</c>, then the scheduled swap to <c>variants[animate]</c> — its persistent resting
    /// state — fires on the next tick and stays afterwards.
    /// </summary>
    /// <remarks>
    /// Needs a REAL (simulated) panel: the swap-to-animate is driven by
    /// <c>schedule.Execute().ExecuteLater(ms)</c>, which only fires once a panel ticks its scheduler against its
    /// clock, and the batchmode EditMode PlayerLoop never does. <see cref="EditorPanelSimulator"/> ticks it
    /// deterministically instead (see <see cref="Tick"/> / <see cref="AdvancePast"/>). This duplicates the small
    /// setup Component.Editor's own simulated-panel base wraps, rather than sharing it, because that base is
    /// internal to a sibling test assembly this fixture's asmdef has no visibility into (Motion enter is a
    /// Reconciler-owned concern, so this fixture lives alongside the Reconciler test suite instead).
    /// </remarks>
    [TestFixture]
    internal sealed class MotionStandaloneEnterTests
    {
        private const float DurationSec = 0.1f;

        private static readonly Dictionary<string, string> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        private EditorPanelSimulator _sim;
        private Reconciler _reconciler;

        [SetUp]
        public void SetUp()
        {
            // Simulated time (and the per-frame step) are process-static and not auto-reset between tests;
            // reset both so this fixture's frame accounting starts from a known clock regardless of what a
            // sibling simulator-based fixture left behind.
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _reconciler = new Reconciler();
            s_bump = null;
        }

        [TearDown]
        public void TearDown()
        {
            _reconciler?.Dispose();
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        // One frame (a real-frame-sized scheduler tick).
        private void Tick() => _sim.FrameUpdateMs(16);

        // Advances well past the given duration so the scheduled swap-to-animate and its completion timer both
        // fire; the +0.2s margin absorbs the scheduler's internal grace period without coupling to its exact value.
        private void AdvancePast(float seconds)
        {
            var steps = (int)((seconds + 0.2f) * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Tick();
        }

        [Test]
        public void Given_AStandaloneMotionWithInitial_When_Mounted_Then_ItStartsAtTheInitialVariant()
        {
            // Arrange / Act — no AnimatePresence anywhere: initial/animate must still drive the mount enter.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — starts at variants[initial]=opacity-0; variants[animate]=opacity-100 is stripped during
            // the from-frame (swapped back in, and kept on completion — see the next two tests).
            var element = Root.Q<VisualElement>("m");
            Assert.That((element.ClassListContains("opacity-0"), element.ClassListContains("opacity-100")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AStandaloneMotionWithoutInitial_When_Mounted_Then_ItStartsAtTheAnimateVariantWithNoTweenScheduled()
        {
            // Arrange / Act — no `initial` declared, so there is no starting pose to enter FROM.
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Motion(name: "m", variants: s_fade, animate: "visible",
                    transition: new StyleTransitionConfig { DurationSec = DurationSec }),
            });

            // Assert — rests directly at variants[animate], and (unlike the `initial` case above) no transition
            // was scheduled: no inline transition-duration was ever applied.
            var element = Root.Q<VisualElement>("m");
            Assert.That(
                (element.ClassListContains("opacity-100"), element.style.transitionDuration.keyword),
                Is.EqualTo((true, StyleKeyword.Null)));
        }

        private static Action<int> s_bump;

        [Component]
        private static VNode StandaloneHostRender()
        {
            var (_, bump) = Hooks.UseState(0);
            s_bump = bump;
            return V.Motion(name: "m", variants: s_fade, initial: "hidden", animate: "visible",
                transition: new StyleTransitionConfig { DurationSec = DurationSec });
        }

        [Test]
        public void Given_AStandaloneMotionThatFinishedEntering_When_AnUnrelatedStateChangeReRenders_Then_ItKeepsTheAnimateVariant()
        {
            // Arrange — mount under a component (so a self-contained state update can re-render it), and let
            // the enter complete: it rests at variants[animate], persistently.
            using var mounted = V.Mount(Root, V.Component(StandaloneHostRender, key: "host"));
            AdvancePast(DurationSec);
            var element = Root.Q<VisualElement>("m");
            Assume.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)),
                "Precondition: the enter completed and rests at variants[animate]");

            // Act — an UNRELATED state change re-renders the same Motion node through PatchMotion (not
            // CreateElement again), which resolves the applied classes from Animate/ambient only.
            s_bump.Invoke(1);
            Tick();

            // Assert — the patch never replays `initial`: the element keeps resting at variants[animate].
            Assert.That(
                (element.ClassListContains("opacity-100"), element.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }
    }
}

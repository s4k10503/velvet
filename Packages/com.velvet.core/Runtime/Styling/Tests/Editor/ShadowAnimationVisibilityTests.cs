using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.UIElements.TestFramework;
using UnityEditor.UIElements.TestFramework;

namespace Velvet.Tests
{
    /// <summary>
    /// The drop shadow is painted as a baked quad in the caster's own generateVisualContent and does NOT honor
    /// UI Toolkit opacity, so a fading enter/exit — whether a CSS-transition-style tween or a spring-driven
    /// Motion animation writing its opacity as a per-frame inline style tick — would show the full-strength
    /// shadow through the still-translucent target as a dark box. To match a CSS box-shadow the scheduler
    /// instead CO-FADES every descendant shadow with its element: it registers the animation as a driver and
    /// samples the caster's opacity each frame into the binding's <see cref="DropShadowBinding.ShadowOpacity"/>
    /// multiplier (the paint scales the shadow alpha by it) — the tick does not care what is driving the opacity,
    /// so the tween and spring paths share the same wiring. Overlapping drivers compose multiplicatively, and
    /// the shadow returns to full only when the last driver ends.
    /// </summary>
    /// <remarks>
    /// The tween-path coverage is panel-free — <c>PlayEnter</c> applies the from-state and registers the driver
    /// at the from-value synchronously (only the per-frame tick is deferred, and the EditMode PlayerLoop does not
    /// tick it), so those cases assert the synchronous values and the pure driver composition. The spring-path
    /// coverage needs a real (simulated) panel instead: its co-fade tick samples <c>resolvedStyle.opacity</c>,
    /// which the panel-free setup cannot resolve — exactly why a missing co-fade wire on the spring path was
    /// invisible there. GWT, one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class ShadowAnimationVisibilityTests
    {
        private static readonly ShadowSpec Spec =
            new(new Color(0f, 0f, 0f, 0.3f), blur: 20f, offsetY: 4f, spread: 0f);

        // Attaches a shadow paint to a child of target and returns the child's binding.
        private static DropShadowBinding AttachShadowChild(VisualElement target)
        {
            var child = new VisualElement();
            target.Add(child);
            return DropShadowSilhouette.Attach(child, Spec, classNames: System.Array.Empty<string>(), skewXDeg: 0f);
        }

        private EditorPanelSimulator _sim;
        private StyleAnimationScheduler _scheduler;

        [SetUp]
        public void SetUp()
        {
            PanelSimulator.ResetCurrentTime();
            _sim = new EditorPanelSimulator { panelSize = new Vector2(800, 600) };
            _sim.ResetTimePerSimulatedFrameToDefault();
            _scheduler = new StyleAnimationScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            _sim?.Dispose();
            _sim = null;
        }

        private VisualElement Root => _sim.rootVisualElement;

        private void Tick() => _sim.FrameUpdateMs(16);

        // Strictly inside the range an enter or an exit travels, which excludes BOTH a spring still parked at
        // its from-value and one already settled at its to-value. "Not yet at full" would accept the first of
        // those, and an enter's from-value is 0 for the caster and the shadow alike — so a spring that never
        // advanced satisfies it on both terms at once.
        private static bool IsMidFlight(float value) => value > 0f && value < 1f;

        [Test]
        public void Given_ShadowedElement_When_EnterStarts_Then_DescendantShadowStartsTransparent()
        {
            // Arrange: an element carrying a shadow-painted child, at rest (ShadowOpacity 1).
            var scheduler = new StyleAnimationScheduler();
            var target = new VisualElement();
            var binding = AttachShadowChild(target);
            var atRest = binding.ShadowOpacity;

            // Act: an enter animation starts on the target.
            scheduler.PlayEnter(target, StyleTransition.FadeSlideUp);

            // Assert: the shadow starts at the enter from-value (invisible) so it fades IN with the element
            // rather than being hidden then popping in once the enter completes. Paired with the resting
            // value, because a paint that attached already faded would satisfy the from-value alone.
            Assert.That((atRest, binding.ShadowOpacity), Is.EqualTo((1f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_CoFadingDuringEnter_When_EnterCancelled_Then_DescendantShadowRestoredToFull()
        {
            // Arrange: a shadowed element whose enter is co-fading its shadow (ShadowOpacity driven to 0).
            var scheduler = new StyleAnimationScheduler();
            var target = new VisualElement();
            var binding = AttachShadowChild(target);
            scheduler.PlayEnter(target, StyleTransition.FadeSlideUp);
            var whileCoFading = binding.ShadowOpacity;

            // Act: the enter is cancelled (e.g. the element is interrupted / re-keyed before it settled).
            scheduler.CancelEnter(target);

            // Assert: the driver is dropped so a cancelled animation never leaves the shadow stuck faded — it
            // returns to full strength, matching the element snapping back to its resting opaque state. A
            // shadow no enter ever drove down is already at full, so the co-faded value has to be pinned too.
            Assert.That((whileCoFading, binding.ShadowOpacity), Is.EqualTo((0f, 1f)).Within(1e-4f));
        }

        [Test]
        public void Given_TwoOverlappingDrivers_When_OneReleases_Then_ShadowStaysPartiallyFaded()
        {
            // Arrange: a shadow covered by two overlapping fades — e.g. an enclosing screen-enter and a list-item
            // fade — each contributing a 0.5 factor, so ShadowOpacity is their product (0.25).
            var element = new VisualElement();
            var binding = DropShadowSilhouette.Attach(element, Spec, System.Array.Empty<string>(), 0f);
            var outer = new object();
            var inner = new object();
            DropShadowSilhouette.SetCoFade(binding, element, outer, 0.5f);
            DropShadowSilhouette.SetCoFade(binding, element, inner, 0.5f);
            var whileBothDrive = binding.ShadowOpacity;

            // Act: the inner animation completes first and drops its contribution.
            DropShadowSilhouette.EndCoFade(binding, element, inner);

            // Assert: the shadow stays driven by the still-running outer fade (back to that fade's 0.5), not
            // snapped to full — otherwise the shadow would show through the outer target. The product term is
            // what separates that from a binding that only ever tracked the last driver set on it.
            Assert.That((whileBothDrive, binding.ShadowOpacity), Is.EqualTo((0.25f, 0.5f)).Within(1e-4f));
        }

        [Test]
        public void Given_AShadowedSpringEnter_When_APanelTickRunsMidFlight_Then_TheDescendantShadowIsCoFading()
        {
            // Arrange — a spring-driven enter (opacity 0 -> 100) on a target carrying a shadow-painted child.
            var target = new VisualElement();
            Root.Add(target);
            var binding = AttachShadowChild(target);
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                EnterFromClass = "opacity-0",
                EnterToClass = "opacity-100",
            };

            // Act — a few ticks start the spring and let it climb without fully settling.
            _scheduler.PlayEnter(target, config);
            Tick();
            Tick();
            Tick();

            // Assert — an un-cofaded shadow would sit stuck at its resting full strength; the co-fade tick must
            // have already pulled it down alongside the still-translucent caster. The shadow trails the caster
            // by one frame (the co-fade tick samples before the spring steps), so the two are bracketed
            // independently by IsMidFlight rather than compared against each other.
            Assert.That(
                (IsMidFlight(target.resolvedStyle.opacity), IsMidFlight(binding.ShadowOpacity)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AShadowedSpringExit_When_APanelTickRunsMidFlight_Then_TheDescendantShadowIsCoFading()
        {
            // Arrange — a spring-driven exit (opacity 100 -> 0) on a target carrying a shadow-painted child.
            var target = new VisualElement();
            Root.Add(target);
            var binding = AttachShadowChild(target);
            target.AddToClassList("opacity-100");
            var config = new StyleTransitionConfig
            {
                Type = TransitionType.Spring,
                Stiffness = 200f,
                Damping = 26f,
                ExitFromClass = "opacity-100",
                ExitToClass = "opacity-0",
            };

            // Act
            _scheduler.PlayExit(target, config, onComplete: null);
            Tick();
            Tick();
            Tick();

            // Assert — the shadow must be following the caster's fade down, not sitting untouched at full.
            // Bracketed the same way as the enter case, and for the same reason.
            Assert.That(
                (IsMidFlight(target.resolvedStyle.opacity), IsMidFlight(binding.ShadowOpacity)),
                Is.EqualTo((true, true)));
        }
    }
}

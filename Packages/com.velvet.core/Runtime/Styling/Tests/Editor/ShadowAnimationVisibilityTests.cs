using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The drop shadow is painted as a baked quad in the caster's own generateVisualContent and does NOT honor
    /// UI Toolkit opacity, so a FadeSlideUp / Fade enter would show the full-strength shadow through the
    /// still-translucent target as a dark box. To match a CSS box-shadow the scheduler instead CO-FADES every
    /// descendant shadow with its element: it registers the animation as a driver and samples the caster's
    /// opacity each frame into the binding's <see cref="DropShadowBinding.ShadowOpacity"/> multiplier (the paint
    /// scales the shadow alpha by it). Overlapping drivers compose multiplicatively, and the shadow returns to
    /// full only when the last driver ends. Panel-free — <c>PlayEnter</c> applies the from-state and registers
    /// the driver at the from-value synchronously (only the per-frame tick is deferred, and the EditMode
    /// PlayerLoop does not tick it), so these assert the synchronous values and the pure driver composition.
    /// GWT, one assert per case.
    /// </summary>
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

        [Test]
        public void Given_ShadowedElement_When_EnterStarts_Then_DescendantShadowStartsTransparent()
        {
            // Arrange: an element carrying a shadow-painted child, at rest (ShadowOpacity 1).
            var scheduler = new StyleAnimationScheduler();
            var target = new VisualElement();
            var binding = AttachShadowChild(target);
            Assume.That(binding.ShadowOpacity, Is.EqualTo(1f).Within(1e-4f));

            // Act: an enter animation starts on the target.
            scheduler.PlayEnter(target, StyleTransition.FadeSlideUp);

            // Assert: the shadow starts at the enter from-value (invisible) so it fades IN with the element
            // rather than being hidden then popping in once the enter completes.
            Assert.That(binding.ShadowOpacity, Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Given_CoFadingDuringEnter_When_EnterCancelled_Then_DescendantShadowRestoredToFull()
        {
            // Arrange: a shadowed element whose enter is co-fading its shadow (ShadowOpacity driven to 0).
            var scheduler = new StyleAnimationScheduler();
            var target = new VisualElement();
            var binding = AttachShadowChild(target);
            scheduler.PlayEnter(target, StyleTransition.FadeSlideUp);
            Assume.That(binding.ShadowOpacity, Is.EqualTo(0f).Within(1e-4f));

            // Act: the enter is cancelled (e.g. the element is interrupted / re-keyed before it settled).
            scheduler.CancelEnter(target);

            // Assert: the driver is dropped so a cancelled animation never leaves the shadow stuck faded — it
            // returns to full strength, matching the element snapping back to its resting opaque state.
            Assert.That(binding.ShadowOpacity, Is.EqualTo(1f).Within(1e-4f));
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
            Assume.That(binding.ShadowOpacity, Is.EqualTo(0.25f).Within(1e-4f));

            // Act: the inner animation completes first and drops its contribution.
            DropShadowSilhouette.EndCoFade(binding, element, inner);

            // Assert: the shadow stays driven by the still-running outer fade (back to that fade's 0.5), not
            // snapped to full — otherwise the opacity-blind shadow would show through the outer target.
            Assert.That(binding.ShadowOpacity, Is.EqualTo(0.5f).Within(1e-4f));
        }
    }
}

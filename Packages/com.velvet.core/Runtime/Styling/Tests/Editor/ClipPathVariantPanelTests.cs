using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// End-to-end coverage for clip-path STATE VARIANTS (<c>hover:clip-path-[…]</c>). The stencil wrapper is
    /// created up-front (WantsClipWrapper) and persists; the shape is none at rest and lights up when the
    /// variant's state turns on (the manipulator toggles the payload, StyleVariantPayload re-resolves the mask
    /// from the live class list). The per-binding bake cache makes a return to a previously-baked shape
    /// re-tessellation-free. Driven on a laid-out <see cref="UnityEditor.EditorWindow"/> panel; hover via a
    /// simulated PointerOver/Out through the manipulator's callback registry. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ClipPathVariantPanelTests : PanelTestBase
    {
        private const string HoverTriangle = "w-[100px] h-[100px] hover:clip-path-[polygon(50%_0%,100%_100%,0%_100%)]";

        // This fixture lays out at a 400x400 box (smaller than the base default).
        protected override Rect WindowSize => new Rect(0, 0, 400, 400);

        // Mounts the hover-clip card, forces a layout pass (so the box size is known for the bake), and returns
        // the inner element + its clip binding. The card is wrapped, so it is the wrapper's child.
        private (VisualElement card, ClipPathBinding binding) MountHoverClip() => MountClip(HoverTriangle);

        private (VisualElement card, ClipPathBinding binding) MountClip(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            ForcePanelUpdate(card.panel);
            _mounted.Root.Reconciler.Context.ClipPathBindings.TryGetValue(card, out var binding);
            return (card, binding);
        }

        private static void Hover(VisualElement el)
        {
            using var e = PointerOverEvent.GetPooled();
            el.SimulateEvent(e);
        }

        private static void Unhover(VisualElement el)
        {
            using var e = PointerOutEvent.GetPooled();
            el.SimulateEvent(e);
        }

        [Test]
        public void Given_HoverClipVariant_When_Mounted_Then_WrapperExistsWithNoMaskAtRest()
        {
            var (_, binding) = MountHoverClip();

            Assume.That(binding, Is.Not.Null, "Precondition: a variant clip wraps the element up-front");
            // At rest (not hovered) the variant clip is inactive — no shape resolved.
            Assert.That(binding.Spec, Is.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_Hovered_Then_MaskIsBaked()
        {
            var (card, binding) = MountHoverClip();

            Hover(card);

            Assert.That(binding.Image, Is.Not.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_Unhovered_Then_MaskIsCleared()
        {
            var (card, binding) = MountHoverClip();
            Hover(card);
            Assume.That(binding.Image, Is.Not.Null, "Precondition: hover baked a mask");

            Unhover(card);

            Assert.That(binding.Image, Is.Null);
        }

        [Test]
        public void Given_HoverClipVariant_When_ReHovered_Then_BakeIsReusedFromCache()
        {
            var (card, binding) = MountHoverClip();
            Hover(card);
            var first = binding.Image;
            Assume.That(first, Is.Not.Null, "Precondition: first hover baked a mask");
            Unhover(card);

            Hover(card);

            // The same VectorImage instance is reused (per-binding cache hit) — no re-tessellation on re-hover.
            Assert.That(binding.Image, Is.SameAs(first));
        }

        [Test]
        public void Given_HoverClipVariant_When_AtRest_Then_WrapperOverflowIsVisible()
        {
            // No mask at rest ⇒ no clipping at all: the wrapper must not rectangle-clip the unclipped element.
            var (_, binding) = MountHoverClip();

            Assert.That(binding.Wrapper.style.overflow.value, Is.EqualTo(Overflow.Visible));
        }

        [Test]
        public void Given_HoverClipVariant_When_Hovered_Then_WrapperOverflowIsHidden()
        {
            // Active mask ⇒ stencil-clip: overflow hidden is half of the UIR mask combination.
            var (card, binding) = MountHoverClip();

            Hover(card);

            Assert.That(binding.Wrapper.style.overflow.value, Is.EqualTo(Overflow.Hidden));
        }

        [Test]
        public void Given_BaseClipPlusHoverNone_When_Hovered_Then_MaskIsCleared()
        {
            // A base clip with hover:clip-path-none must CLEAR the mask on hover (the none payload overrides
            // the base in the live cascade) and restore it on hover-out.
            var (card, binding) = MountClip("w-[100px] h-[100px] clip-path-[circle(50%)] hover:clip-path-none");
            Assume.That(binding.Image, Is.Not.Null, "Precondition: the base clip baked a mask at rest");

            Hover(card);

            Assert.That(binding.Image, Is.Null);
        }
    }
}

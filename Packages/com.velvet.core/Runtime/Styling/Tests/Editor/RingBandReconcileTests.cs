using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Which bands a reconciled element's ring binding draws, how wide, and in what colour: the ring Tailwind
    /// composes into box-shadow, the ring-offset fill over it, the outline, and the inset layers. Read off the
    /// binding, which needs no panel. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class RingBandReconcileTests
    {
        private static VisualElement Mount(ReconcilerScope scope, string className)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, name: "card") });
            return scope.Root.Q<VisualElement>("card");
        }

        // How wide band paints: 0 where it is hidden.
        private static float? Shown(VisualElement band, float width)
            => band == null ? null : band.style.display.value == DisplayStyle.None ? 0f : width;

        [Test]
        public void Given_ARingAndAnOutline_When_Reconciled_Then_EachBandCarriesItsOwnWidth()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var binding = RingOverlay.TryGet(Mount(scope, "ring-4 outline-2"));

            // Assert
            Assert.That(
                (Shown(binding?.RingElement, binding?.Spec.RingSpread ?? 0f), Shown(binding?.OutlineElement, binding?.Spec.Outline.Width ?? 0f)),
                Is.EqualTo(((float?)4f, (float?)2f)));
        }

        [Test]
        public void Given_ARingOffsetNamingNoColour_When_Reconciled_Then_TheOffsetIsFilledWhite()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var binding = RingOverlay.TryGet(Mount(scope, "ring-2 ring-offset-2 ring-[#ff0000]"));

            // Assert — --tw-ring-offset-color's initial value is #fff.
            Assert.That((Shown(binding?.OffsetElement, binding?.Spec.OffsetSpread ?? 0f), binding?.Spec.OffsetColor),
                Is.EqualTo(((float?)2f, (Color?)Color.white)));
        }

        [Test]
        public void Given_ARingOffsetColour_When_Reconciled_Then_TheFillTakesIt()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            StyleColorValueParser.TryParseColor("#00ff00".AsSpan(), out var green);

            // Act
            var binding = RingOverlay.TryGet(Mount(scope, "ring-2 ring-offset-2 ring-offset-[#00ff00]"));

            // Assert
            Assert.That(binding?.Spec.OffsetColor, Is.EqualTo((Color?)green));
        }

        [Test]
        public void Given_AShadowBesideARingOffset_When_Reconciled_Then_OnlyTheOffsetFillIsDrawn()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var binding = RingOverlay.TryGet(Mount(scope, "shadow-lg ring-offset-2"));

            // Assert — the box-shadow a shadow-* writes carries the offset fill and no ring.
            Assert.That(
                (Shown(binding?.OffsetElement, binding?.Spec.OffsetSpread ?? 0f), Shown(binding?.RingElement, binding?.Spec.RingSpread ?? 0f)),
                Is.EqualTo(((float?)2f, (float?)0f)));
        }

        // The inside bands: painted in the element's own content rather than on the overlay beside it.

        [Test]
        public void Given_AnInsetRing_When_Reconciled_Then_TheElementPaintsItAndTheOverlayDoesNot()
        {
            // Arrange
            using var scope = new ReconcilerScope();

            // Act
            var card = Mount(scope, "ring-2 ring-inset");
            var binding = RingOverlay.TryGet(card);

            // Assert
            Assert.That(
                (binding?.RingElement.style.display.value,
                    Array.IndexOf(card.generateVisualContent?.GetInvocationList() ?? Array.Empty<Delegate>(), binding?.OnGenerateInside) >= 0),
                Is.EqualTo(((DisplayStyle?)DisplayStyle.None, true)));
        }

        [Test]
        public void Given_AnInsetRingOnAButton_When_Reconciled_Then_ItPaintsBeforeTheButtonsText()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Button(className: "inset-ring-2", name: "btn", text: "ok") });
            var button = scope.Root.Q<Button>("btn");
            var callbacks = button.generateVisualContent.GetInvocationList();

            // Act
            var inside = Array.IndexOf(callbacks, RingOverlay.TryGet(button)?.OnGenerateInside);
            var text = Array.FindIndex(callbacks, c => ReferenceEquals(c.Target, button));

            // Assert — the text's own callback is the control: it is there to come after.
            Assert.That((inside >= 0, text > inside), Is.EqualTo((true, true)));
        }
    }
}

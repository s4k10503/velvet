using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the settling a dashed or dotted outline queues for the sibling overlay a clipping element paints it on:
    /// a settle puts the overlay up, a settle queued before the binding was torn down does not, and a settle that
    /// ran leaves the next paint free to queue another.
    /// </summary>
    internal sealed class BorderStyleOverlaySettleTests : PanelTestBase
    {
        private static void Settle(VisualElement element, BorderStyleBinding binding)
            => typeof(BorderStyleSilhouette).GetMethod("SettleOverlay", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { element, binding });

        // A clipping element on the panel with a dashed outline attached.
        private (VisualElement Element, BorderStyleBinding Binding) AttachToClippingElement()
        {
            var element = new VisualElement { style = { width = 40f, height = 40f, overflow = Overflow.Hidden } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            return (element, BorderStyleSilhouette.Attach(element, new BorderStyleSpec(BorderLineStyle.Dashed, "border-dashed")));
        }

        // GREEN_ON_BASE(characterization): the control for the detached case beside it, which the base already passes.
        [Test]
        public void Given_AClippingDashedElement_When_ASettleRuns_Then_TheOverlayIsUp()
        {
            // Arrange
            var (element, binding) = AttachToClippingElement();

            // Act
            Settle(element, binding);

            // Assert
            Assert.That(FaceBorderOverlay.Has(element), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base puts back no overlay for a binding detached before its settle.
        [Test]
        public void Given_AClippingDashedElementDetachedWithASettleQueued_When_TheSettleRuns_Then_NoOverlayIsPutBack()
        {
            // Arrange
            var (element, binding) = AttachToClippingElement();
            BorderStyleSilhouette.Detach(element, binding);

            // Act
            Settle(element, binding);

            // Assert
            Assert.That(FaceBorderOverlay.Has(element), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base clears the pending mark once its settle runs.
        [Test]
        public void Given_ASettleQueued_When_ItRuns_Then_TheBindingNoLongerMarksOnePending()
        {
            // Arrange
            var element = new VisualElement();
            var binding = new BorderStyleBinding { OverlayPending = true };

            // Act
            Settle(element, binding);

            // Assert
            Assert.That(binding.OverlayPending, Is.False);
        }
    }
}

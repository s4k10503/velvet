using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a clipping element carrying both a dashed border and a dashed divider: the divider settles on whether it
    /// holds an overlay itself, and detaching it leaves the border's overlay up.
    /// </summary>
    internal sealed class FaceBorderOverlayDividerSharingTests : PanelTestBase
    {
        private readonly List<Action> _cleanups = new();

        public override void TearDown()
        {
            foreach (var cleanup in _cleanups)
            {
                cleanup();
            }
            _cleanups.Clear();
            base.TearDown();
        }

        private (VisualElement Element, BorderStyleBinding Border, DivideDashChildBinding Divider) MountWithBoth()
        {
            var element = new VisualElement { style = { width = 40f, height = 40f, overflow = Overflow.Hidden } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            var border = BorderStyleSilhouette.Attach(element, new BorderStyleSpec(BorderLineStyle.Dashed, "border-dashed"));
            var divider = DivideDashPainter.Attach(element, DivideEdge.Bottom, BorderLineStyle.Dashed);
            _cleanups.Add(() =>
            {
                BorderStyleSilhouette.Detach(element, border);
                DivideDashPainter.Detach(element, divider);
            });
            return (element, border, divider);
        }

        private static void Settle(Type owner, VisualElement element, object binding)
            => owner.GetMethod("SettleOverlay", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new[] { element, binding });

        [Test]
        public void Given_ABorderOverlayUp_When_ADividerIsPaintedOnTheClippingElement_Then_TheDividerQueuesItsOwnSettle()
        {
            // Arrange
            var (element, border, divider) = MountWithBoth();
            Settle(typeof(BorderStyleSilhouette), element, border);

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(divider.OverlayPending, Is.True);
        }

        [Test]
        public void Given_BothOverlaysSettled_When_TheDividerIsDetached_Then_TheBorderOverlayStays()
        {
            // Arrange
            var (element, border, divider) = MountWithBoth();
            Settle(typeof(BorderStyleSilhouette), element, border);
            Settle(typeof(DivideDashPainter), element, divider);

            // Act
            DivideDashPainter.Detach(element, divider);

            // Assert
            Assert.That(FaceBorderOverlay.Has(element), Is.True);
        }
    }
}

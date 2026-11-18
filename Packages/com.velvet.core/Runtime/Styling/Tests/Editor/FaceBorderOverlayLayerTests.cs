using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies an overlay shared by two layers of one clipping element (<see cref="FaceBorderOverlay"/>): a dashed
    /// border and a dashed divider each keep their own paint, ask it in the order a CSS border and its divider band
    /// stack, and settle on whether they themselves hold the overlay.
    /// </summary>
    internal sealed class FaceBorderOverlayLayerTests : PanelTestBase
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

        private VisualElement MountClipping()
        {
            var element = new VisualElement { style = { width = 40f, height = 40f, overflow = Overflow.Hidden } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            _cleanups.Add(() =>
            {
                FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Face);
                FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Divider);
            });
            return element;
        }

        private static void SettleBorder(VisualElement element, BorderStyleBinding binding)
            => typeof(BorderStyleSilhouette).GetMethod("SettleOverlay", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { element, binding });

        private static void SettleDivider(VisualElement element, DivideDashChildBinding binding)
            => typeof(DivideDashPainter).GetMethod("SettleOverlay", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { element, binding });

        private (BorderStyleBinding Border, DivideDashChildBinding Divider) AttachBoth(VisualElement element)
        {
            var border = BorderStyleSilhouette.Attach(element, new BorderStyleSpec(BorderLineStyle.Dashed, "border-dashed"));
            var divider = DivideDashPainter.Attach(element, DivideEdge.Bottom, BorderLineStyle.Dashed);
            _cleanups.Add(() =>
            {
                BorderStyleSilhouette.Detach(element, border);
                DivideDashPainter.Detach(element, divider);
            });
            return (border, divider);
        }

        [Test]
        public void Given_ADividerOverlayUp_When_TheBorderOnTheSameElementIsDetached_Then_TheDividerOverlayStays()
        {
            // Arrange
            var element = MountClipping();
            var (border, divider) = AttachBoth(element);
            SettleBorder(element, border);
            SettleDivider(element, divider);

            // Act
            BorderStyleSilhouette.Detach(element, border);

            // Assert
            Assert.That(FaceBorderOverlay.Has(element, FaceBorderOverlay.Layer.Divider), Is.True);
        }

        [Test]
        public void Given_BothLayersOnOneElement_When_ThePanelPaints_Then_TheFacePaintIsAsked()
        {
            // Arrange
            var element = MountClipping();
            var asked = 0;
            FaceBorderOverlay.Ensure(element, (_, _, _) => asked++, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Divider);

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(asked, Is.GreaterThan(0));
        }

        [Test]
        public void Given_BothLayersOnOneElement_When_ThePanelPaints_Then_TheDividerPaintIsAsked()
        {
            // Arrange
            var element = MountClipping();
            var asked = 0;
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Ensure(element, (_, _, _) => asked++, FaceBorderOverlay.Layer.Divider);

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(asked, Is.GreaterThan(0));
        }

        [Test]
        public void Given_BothLayersEnsuredDividerFirst_When_ThePanelPaints_Then_TheFaceIsAskedBeforeTheDivider()
        {
            // Arrange
            var element = MountClipping();
            var order = new List<string>();
            FaceBorderOverlay.Ensure(element, (_, _, _) => order.Add("divider"), FaceBorderOverlay.Layer.Divider);
            FaceBorderOverlay.Ensure(element, (_, _, _) => order.Add("face"), FaceBorderOverlay.Layer.Face);
            order.Clear();

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(order.IndexOf("face"), Is.LessThan(order.IndexOf("divider")));
        }

        [Test]
        public void Given_BothLayersOnOneElement_When_OneIsRemoved_Then_TheOtherStillPaints()
        {
            // Arrange
            var element = MountClipping();
            var asked = 0;
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Ensure(element, (_, _, _) => asked++, FaceBorderOverlay.Layer.Divider);
            FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Face);

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(asked, Is.GreaterThan(0));
        }

        [Test]
        public void Given_BothLayersOnOneElement_When_BothAreRemoved_Then_TheElementHoldsNoOverlay()
        {
            // Arrange
            var element = MountClipping();
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Divider);
            FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Face);

            // Act
            FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Divider);

            // Assert
            Assert.That(FaceBorderOverlay.HasOverlay(element), Is.False);
        }

        [Test]
        public void Given_ARemovedLayer_When_AnotherLayerIsEnsuredAndThePanelPaints_Then_TheRemovedPaintIsNotAsked()
        {
            // Arrange
            var element = MountClipping();
            var removed = 0;
            FaceBorderOverlay.Ensure(element, (_, _, _) => removed++, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Remove(element, FaceBorderOverlay.Layer.Face);
            FaceBorderOverlay.Ensure(element, (_, _, _) => { }, FaceBorderOverlay.Layer.Divider);

            // Act
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(removed, Is.Zero);
        }
    }
}

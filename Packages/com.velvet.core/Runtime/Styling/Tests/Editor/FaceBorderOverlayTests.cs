using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the sibling a clipping element's border is painted on (<see cref="FaceBorderOverlay"/>): which paint it
    /// asks and for which box, where it sits among its host's children, and the transform it takes from its element.
    /// </summary>
    internal sealed class FaceBorderOverlayTests : PanelTestBase
    {
        private readonly List<VisualElement> _overlaid = new();

        public override void TearDown()
        {
            foreach (var element in _overlaid)
            {
                FaceBorderOverlay.Remove(element);
            }
            _overlaid.Clear();
            base.TearDown();
        }

        // A width x 40 element in host, laid out.
        private static VisualElement Mount(VisualElement host, float width)
        {
            var element = new VisualElement { style = { width = width, height = 40f } };
            host.Add(element);
            ForcePanelUpdate(element.panel);
            return element;
        }

        private void Overlay(VisualElement element, Action<MeshGenerationContext, float, float> paint)
        {
            FaceBorderOverlay.Ensure(element, paint);
            _overlaid.Add(element);
        }

        private static VisualElement OverlayIn(VisualElement host)
        {
            foreach (var child in host.Children())
            {
                if (child.ClassListContains(FaceBorderOverlay.MarkerClass))
                {
                    return child;
                }
            }
            return null;
        }

        // GREEN_ON_BASE(characterization): the base paints an overlay with the paint it was last ensured with.
        [Test]
        public void Given_AnOverlay_When_ItIsEnsuredAgainWithAnotherPaint_Then_ThatPaintIsAsked()
        {
            // Arrange
            var element = Mount(_window.rootVisualElement, 40f);
            var replacing = 0;
            Overlay(element, (_, _, _) => { });
            ForcePanelUpdate(element.panel);

            // Act
            Overlay(element, (_, _, _) => replacing++);
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(replacing, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base asks no paint for an element laid out with no width.
        [Test]
        public void Given_AnOverlaidElementOfNoWidth_When_ThePanelPaints_Then_ItsPaintIsNotAsked()
        {
            // Arrange — beside it an element 40 wide, whose paint is asked, so a panel that paints no overlay cannot pass.
            var empty = Mount(_window.rootVisualElement, 0f);
            var control = Mount(_window.rootVisualElement, 40f);
            var emptyPaints = 0;
            var controlPaints = 0;
            Overlay(empty, (_, _, _) => emptyPaints++);
            Overlay(control, (_, _, _) => controlPaints++);

            // Act
            ForcePanelUpdate(empty.panel);

            // Assert
            Assert.That(controlPaints > 0 ? emptyPaints : -1, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base marks a settle pending for a clipping element with no overlay.
        [Test]
        public void Given_AClippingElementWithNoOverlay_When_ItIsAskedWhetherItClips_Then_ASettleIsMarkedPending()
        {
            // Arrange
            var element = new VisualElement { style = { width = 40f, height = 40f, overflow = Overflow.Hidden } };
            _window.rootVisualElement.Add(element);
            ForcePanelUpdate(element.panel);
            var pending = false;

            // Act
            FaceBorderOverlay.ClipsAndSettle(element, ref pending, 0, (_, _) => { });

            // Assert
            Assert.That(pending, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads the element's index after taking its overlay out.
        [Test]
        public void Given_AnOverlaidElement_When_ItMovesPastItsOverlay_Then_TheOverlayIsPutDirectlyAfterIt()
        {
            // Arrange — the element, its overlay and two more children; moving the element two places on leaves the
            // overlay ahead of it, which the element's attach puts back.
            var host = new VisualElement();
            _window.rootVisualElement.Add(host);
            var element = Mount(host, 40f);
            host.Add(new VisualElement());
            host.Add(new VisualElement());
            Overlay(element, (_, _, _) => { });

            // Act
            host.Insert(2, element);

            // Assert
            Assert.That(host.IndexOf(OverlayIn(host)), Is.EqualTo(host.IndexOf(element) + 1));
        }

        // GREEN_ON_BASE(characterization): the base repaints a widened element's overlay in the update that lays it out.
        // A `MUTANT_SURVIVES` over Place's own repaint rests on this.
        [Test]
        public void Given_AnOverlaidElement_When_ItIsWidened_Then_TheSameUpdatePaintsItAtTheNewWidth()
        {
            // Arrange
            var element = Mount(_window.rootVisualElement, 40f);
            var widths = new List<float>();
            Overlay(element, (_, w, _) => widths.Add(w));
            ForcePanelUpdate(element.panel);

            // Act
            element.style.width = 60f;
            ForcePanelUpdate(element.panel);

            // Assert
            Assert.That(widths.Count > 0 ? widths[widths.Count - 1] : float.NaN, Is.EqualTo(60f));
        }

        // GREEN_ON_BASE(characterization): the base gives an overlay its element's translation as it places it.
        [Test]
        public void Given_ATranslatedElement_When_ItIsOverlaid_Then_TheOverlayIsTranslatedAsItIs()
        {
            // Arrange
            var element = Mount(_window.rootVisualElement, 40f);
            element.style.translate = new Translate(5f, 7f);
            ForcePanelUpdate(element.panel);

            // Act
            Overlay(element, (_, _, _) => { });

            // Assert
            Assert.That(OverlayIn(_window.rootVisualElement).style.translate.value.x.value, Is.EqualTo(5f));
        }

        // GREEN_ON_BASE(characterization): the base gives an overlay its element's rotation as it places it.
        [Test]
        public void Given_ARotatedElement_When_ItIsOverlaid_Then_TheOverlayIsRotatedAsItIs()
        {
            // Arrange
            var element = Mount(_window.rootVisualElement, 40f);
            element.style.rotate = new Rotate(30f);
            ForcePanelUpdate(element.panel);

            // Act
            Overlay(element, (_, _, _) => { });

            // Assert
            Assert.That(OverlayIn(_window.rootVisualElement).style.rotate.value.angle.ToDegrees(),
                Is.EqualTo(30f).Within(1e-3f));
        }
    }
}

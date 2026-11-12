using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the layer bookkeeping StyleArbitraryValueResolver keeps per element: a parsed pair value is a value
    /// rather than a rejection, an element nothing was applied to has no resting layer, and a driver's write lands
    /// inline.
    /// </summary>
    internal sealed class ArbitraryStyleLayerTests
    {
        // GREEN_ON_BASE(characterization): the base parses a two-length transform origin as a value it takes.
        [Test]
        public void Given_ATransformOriginPair_When_Parsed_Then_ItIsNoRejectedValue()
        {
            // Act
            var ok = StyleArbitraryValueResolver.TryParse("origin-[12px_80%]", out var style);

            // Assert — null where the class did not parse, which leaves no layer to read.
            Assert.That(ok ? style.Rejected : (bool?)null, Is.EqualTo(false));
        }

        // GREEN_ON_BASE(characterization): the base finds no resting layer on an element no layer was applied to.
        [Test]
        public void Given_AnElementNoLayerWasAppliedTo_When_ItsRestingLayerIsRead_Then_ThereIsNone()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            var found = StyleArbitraryValueResolver.TryRestingLayer(element, ArbitraryProperty.TranslateX,
                new HashSet<ArbitraryProperty>(), out _, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base writes a driver's colour inline on an element with no layers.
        [Test]
        public void Given_AnElementWithNoLayers_When_APlayDrivesItsBackground_Then_TheDrivenColourIsWritten()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            StyleArbitraryValueResolver.ApplyDriven(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.blue));

            // Assert
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        // GREEN_ON_BASE(characterization): the base finds no resting layer for a property the element's layers do not write.
        [Test]
        public void Given_AnElementWithALayerOfAnotherProperty_When_ItsRestingLayerIsRead_Then_ThereIsNone()
        {
            // Arrange — the background layer gives the element a layer map, which the case above has none of.
            var element = new VisualElement();
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.red));

            // Act
            var found = StyleArbitraryValueResolver.TryRestingLayer(element, ArbitraryProperty.TranslateX,
                new HashSet<ArbitraryProperty>(), out _, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base holds no gap over a percentage width it cannot measure.
        [Test]
        public void Given_APercentageWidthWithNoParentToMeasureItAgainst_When_AGapAsksToGrowIt_Then_NothingIsHeld()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.TryParse("w-[50%]", out var width);
            StyleArbitraryValueResolver.Apply(element, width);

            // Act
            var held = StyleArbitraryValueResolver.HoldSizeGap(element, HeldSlot.Width, 8f, mine: false);

            // Assert
            Assert.That(held, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base shows a reversed play over an important layer newer than the play.
        [Test]
        public void Given_APlayUnderAnImportantLayerThatArrivedAfterIt_When_ThePlayReverses_Then_ThePlaysValueIsShown()
        {
            // Arrange — the important layer outranks the drive it found already writing.
            var element = new VisualElement();
            StyleArbitraryValueResolver.ApplyDriven(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.blue));
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.red),
                StyleLayerPriority.ImportantOf(StyleLayerPriority.Base));
            var outranked = element.style.backgroundColor.value == Color.red;

            // Act
            StyleArbitraryValueResolver.Redrive(element);

            // Assert — the control is the red the important layer showed before the reversal.
            Assert.That(outranked ? element.style.backgroundColor.value : Color.clear, Is.EqualTo(Color.blue));
        }
    }
}

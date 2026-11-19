using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a line box leaves after a pool reset and what it measures for a text the generator gives no height,
    /// without a panel.
    /// </summary>
    internal sealed class TextLineBoxResetTests
    {
        // GREEN_ON_BASE(characterization): the base starts a reset line box with no text laid out in several lines.
        [Test]
        public void Given_AResetLineBox_When_ItIsAskedWhetherItsTextSpansLines_Then_ItDoesNot()
        {
            // Arrange — no text has been shown since the reset, so the text asked about is the one it last saw.
            var box = new TextLineBox(new Label(), centred: false);

            // Act
            box.Reset();

            // Assert
            Assert.That(box.SpansLines(null), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base leaves a measure of no height at no height.
        [Test]
        public void Given_LineBoxesOfAnElementWithALineHeight_When_TheGeneratorMeasuresNoHeight_Then_TheHeightStaysNone()
        {
            // Arrange — a 20px line box around a 14px line.
            var lines = new TextLineBox.Lines(lineHeight: 20f, singleLine: 14f, advance: 16f, byGenerator: false);

            // Act
            var fit = lines.Fit(new Vector2(30f, 0f));

            // Assert
            Assert.That(fit.y, Is.EqualTo(0f));
        }
    }
}

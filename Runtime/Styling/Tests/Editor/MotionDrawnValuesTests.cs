using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that what a play draws carries the colours its channels draw, so a play interrupting it starts each
    /// colour from there.
    /// </summary>
    internal sealed class MotionDrawnValuesTests
    {
        // GREEN_ON_BASE(characterization): the base reports the colour a colour channel draws.
        [Test]
        public void Given_AColourChannel_When_WhatItDrawsIsRead_Then_ItsColourIsTheOneDrawn()
        {
            // Arrange
            var colors = new List<(ArbitraryProperty, Color)> { (ArbitraryProperty.BorderTopColor, Color.red) };

            // Act
            var drawn = MotionDrawnValues.Of<(ArbitraryProperty, Color), (ArbitraryProperty, LengthUnit, float, float)>(
                new MotionDrawnValues.Channels(null, null, null, null, null), colors, c => c, null, l => l);

            // Assert
            Assert.That(drawn.ColorOf(ArbitraryProperty.BorderTopColor), Is.EqualTo((Color?)Color.red));
        }
    }
}

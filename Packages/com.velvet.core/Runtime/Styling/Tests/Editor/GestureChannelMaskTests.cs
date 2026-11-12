using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which of a play's channels a gesture state's classes take over (<see cref="GestureChannelMask.MaskChannels"/>),
    /// without a panel.
    /// </summary>
    internal sealed class GestureChannelMaskTests
    {
        // GREEN_ON_BASE(characterization): the base leaves alone a channel the gesture state's classes do not name.
        [Test]
        public void Given_ABezierPlayWritingOpacity_When_AGestureStateNamingOnlyScaleMasksIt_Then_TheOpacityStays()
        {
            // Arrange
            var element = new VisualElement();
            var state = new BezierTweenState
            {
                X1 = 0f, Y1 = 0f, X2 = 1f, Y2 = 1f, DurationSec = 1f, ElapsedSec = 0.5f,
                Opacity = new BezierTweenChannel(0f, 1f),
            };
            element.style.opacity = 0.5f;

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Scale));

            // Assert
            Assert.That(element.style.opacity.value, Is.EqualTo(0.5f));
        }
    }
}

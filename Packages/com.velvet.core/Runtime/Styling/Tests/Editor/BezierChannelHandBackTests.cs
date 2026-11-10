using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a bezier play giving up its scale to a play that names it, and taking its rotate back from a gesture
    /// state that let it go, without a panel.
    /// </summary>
    [TestFixture]
    internal sealed class BezierChannelHandBackTests
    {
        private static BezierTweenState LinearHalfway()
            => new() { X1 = 0f, Y1 = 0f, X2 = 1f, Y2 = 1f, DurationSec = 1f, ElapsedSec = 0.5f };

        // GREEN_ON_BASE(characterization): the base clears the inline scale a bezier play hands back.
        [Test]
        public void Given_ABezierPlayWritingScale_When_ItReleasesScale_Then_TheInlineScaleIsCleared()
        {
            // Arrange
            var element = new VisualElement();
            var state = LinearHalfway();
            state.Scale = new BezierTweenChannel(1f, 2f);
            element.style.scale = new Scale(new Vector2(1.5f, 1.5f));

            // Act
            BezierTweenDriver.ReleaseChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Scale));

            // Assert
            Assert.That(element.style.scale.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base restarts a bezier rotate a gesture hands back.
        [Test]
        public void Given_ABezierRotateAGestureHeld_When_TheGestureHandsRotateBack_Then_TheChannelWritesAgain()
        {
            // Arrange
            var element = new VisualElement();
            var state = LinearHalfway();
            state.Rotate = new BezierTweenChannel(0f, 90f) { Masked = true };

            // Act
            BezierTweenDriver.ReturnChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Rotate),
                MotionSlotContext.ReadImportant(element, null, null));

            // Assert
            Assert.That(state.Rotate.Masked, Is.False);
        }
    }
}

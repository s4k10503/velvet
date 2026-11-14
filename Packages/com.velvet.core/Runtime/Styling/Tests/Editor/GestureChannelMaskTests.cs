using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins which of a play's channels a gesture state's classes take over (<see cref="GestureChannelMask.MaskChannels"/>),
    /// and a play marked as an exit's (<see cref="GestureChannelMask.MarkExit"/>), without a panel.
    /// </summary>
    internal sealed class GestureChannelMaskTests
    {
        private static BezierTweenState LinearHalfway()
            => new() { X1 = 0f, Y1 = 0f, X2 = 1f, Y2 = 1f, DurationSec = 1f, ElapsedSec = 0.5f };

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

        // GREEN_ON_BASE(characterization): the base masks a colour channel the gesture state's classes name.
        [Test]
        public void Given_ABezierPlayDrivingTheBackground_When_AGestureStateNamingTheBackgroundMasksIt_Then_TheColourChannelIsMasked()
        {
            // Arrange
            var element = new VisualElement();
            var colour = new BezierColorChannel(ArbitraryProperty.BackgroundColor, Color.red, Color.blue);
            var state = LinearHalfway();
            state.Colors = new List<BezierColorChannel> { colour };

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.BackgroundColor));

            // Assert
            Assert.That(colour.Progress.Masked, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base masks a length channel the gesture state's classes name.
        [Test]
        public void Given_ABezierPlayDrivingTheWidth_When_AGestureStateNamingTheWidthMasksIt_Then_TheLengthChannelIsMasked()
        {
            // Arrange
            var element = new VisualElement();
            var width = new BezierLengthChannel(ArbitraryProperty.Width, 10f, 20f, LengthUnit.Pixel);
            var state = LinearHalfway();
            state.Lengths = new List<BezierLengthChannel> { width };

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Width));

            // Assert
            Assert.That(width.Value.Masked, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base ends the drive of a colour channel it masks.
        // Nothing then writes the play's colour back over the element's own layer.
        [Test]
        public void Given_ABezierPlayDrivingTheBackgroundOverTheElementsOwn_When_AGestureStateNamingTheBackgroundMasksIt_Then_TheElementsOwnBackgroundIsShown()
        {
            // Arrange — a red layer of the element's own, and blue the play drove over it last.
            var element = new VisualElement();
            StyleArbitraryValueResolver.Apply(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.red));
            var state = LinearHalfway();
            state.Colors = new List<BezierColorChannel> { new(ArbitraryProperty.BackgroundColor, Color.red, Color.blue) };
            StyleArbitraryValueResolver.ApplyDriven(element, new ArbitraryStyle(ArbitraryProperty.BackgroundColor, Color.blue));

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.BackgroundColor));

            // Assert
            Assert.That(element.style.backgroundColor.value, Is.EqualTo(Color.red));
        }

        // GREEN_ON_BASE(characterization): the base marks a play an exit plays as an exit's.
        [Test]
        public void Given_ABezierPlay_When_AnExitMarksIt_Then_ItIsAnExitsPlay()
        {
            // Arrange
            var state = LinearHalfway();

            // Act
            GestureChannelMask.MarkExit(state, true);

            // Assert
            Assert.That(state.Exit, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base clears the inline scale of a channel the gesture state's classes name.
        [Test]
        public void Given_ABezierPlayWritingScale_When_AGestureStateNamingScaleMasksIt_Then_TheInlineScaleIsCleared()
        {
            // Arrange
            var element = new VisualElement();
            var state = LinearHalfway();
            state.Scale = new BezierTweenChannel(1f, 2f);
            element.style.scale = new Scale(new Vector2(1.5f, 1.5f));

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Scale));

            // Assert
            Assert.That(element.style.scale.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base clears the inline rotate of a channel the gesture state's classes name.
        [Test]
        public void Given_ABezierPlayWritingRotate_When_AGestureStateNamingRotateMasksIt_Then_TheInlineRotateIsCleared()
        {
            // Arrange
            var element = new VisualElement();
            var state = LinearHalfway();
            state.Rotate = new BezierTweenChannel(0f, 90f);
            element.style.rotate = new Rotate(Angle.Degrees(45f));

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Rotate));

            // Assert
            Assert.That(element.style.rotate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base clears the inline translate of a channel the gesture state's classes name.
        [Test]
        public void Given_ABezierPlayWritingTranslate_When_AGestureStateNamingTranslateMasksIt_Then_TheInlineTranslateIsCleared()
        {
            // Arrange
            var element = new VisualElement();
            var state = LinearHalfway();
            (state.TranslateX, state.TranslateY) = (new BezierTweenChannel(0f, 20f), new BezierTweenChannel(0f, 10f));
            element.style.translate = new Translate(new Length(10f), new Length(5f));

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Translate));

            // Assert
            Assert.That(element.style.translate.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        // GREEN_ON_BASE(characterization): the base hands back a narrowing whose only channel a gesture state takes.
        [Test]
        public void Given_ABezierOpacityNarrowingTheElementsOpacityTransition_When_AGestureStateTakesTheOpacity_Then_NoTransitionPropertyIsLeftInline()
        {
            // Arrange — the play takes opacity out of the transition transition-opacity gives the element, which writes
            // the element's transition-property inline.
            var element = new VisualElement();
            element.AddToClassList("transition-opacity");
            var state = LinearHalfway();
            state.Opacity = new BezierTweenChannel(0f, 1f);
            GestureChannelMask.ResyncNarrowing(element, state);
            var narrowed = element.style.transitionProperty.keyword == StyleKeyword.Undefined;

            // Act
            GestureChannelMask.MaskChannels(element, state, StyleLonghandSet.Of(StyleLonghand.Opacity));

            // Assert — Undefined where the arrangement wrote no list, which the expected keyword is not.
            Assert.That(narrowed ? element.style.transitionProperty.keyword : StyleKeyword.Undefined, Is.EqualTo(StyleKeyword.Null));
        }
    }
}

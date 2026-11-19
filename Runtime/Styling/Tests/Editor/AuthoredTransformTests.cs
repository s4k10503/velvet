using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class AuthoredTransformTests
    {
        [Test]
        public void Given_ASkewOnAnElementWithNoOtherWriter_When_Engaged_Then_TheSlotsCarryTheShear()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            AuthoredTransform.Engage(element, 12f, 6f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element), Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, 1f, 12f, 6f)).Within(1e-4f));
        }

        [Test]
        public void Given_AnAuthoredRotateAndScale_When_ASkewIsEngaged_Then_TheSlotsCarryTheComposite()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.SetRotate(element, new Rotate(new Angle(30f, AngleUnit.Degree)));
            AuthoredTransform.SetScale(element, new Scale(new Vector2(2f, 0.5f)));

            // Act
            AuthoredTransform.Engage(element, -12f, 6f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element), Is.EqualTo(SkewCompositeMath.Composite(30f, 2f, 0.5f, -12f, 6f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_ARotateIsWritten_Then_TheSlotsCarryTheNewComposite()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.Engage(element, 12f, 0f);

            // Act
            AuthoredTransform.SetRotate(element, new Rotate(new Angle(45f, AngleUnit.Degree)));

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element), Is.EqualTo(SkewCompositeMath.Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElementWithANegativeScale_When_Engaged_Then_TheSlotsCarryTheReflection()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.SetScale(element, new Scale(new Vector2(1f, -1f)));

            // Act
            AuthoredTransform.Engage(element, 12f, 6f);

            // Assert
            Assert.That(SkewCompositeMath.Drawn(element), Is.EqualTo(SkewCompositeMath.Composite(0f, 1f, -1f, 12f, 6f)).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_TheRotateIsRead_Then_ItIsWhatWasWritten()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.SetRotate(element, new Rotate(new Angle(30f, AngleUnit.Degree)));
            AuthoredTransform.Engage(element, 12f, 6f);

            // Act
            var read = AuthoredTransform.Rotate(element);

            // Assert
            Assert.That(read.value.angle.ToDegrees(), Is.EqualTo(30f).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElement_When_Released_Then_TheSlotsHoldWhatWasWritten()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.SetRotate(element, new Rotate(new Angle(30f, AngleUnit.Degree)));
            AuthoredTransform.SetScale(element, new Scale(new Vector2(2f, 0.5f)));
            AuthoredTransform.Engage(element, 12f, 6f);

            // Act
            AuthoredTransform.Release(element);

            // Assert
            Assert.That(
                new[]
                {
                    element.style.rotate.value.angle.ToDegrees(), element.style.scale.value.value.x,
                    element.style.scale.value.value.y,
                },
                Is.EqualTo(new[] { 30f, 2f, 0.5f }).Within(1e-4f));
        }

        [Test]
        public void Given_ASkewedElementWithNoWriter_When_Released_Then_TheSlotsAreCleared()
        {
            // Arrange
            var element = new VisualElement();
            AuthoredTransform.Engage(element, 12f, 6f);

            // Act
            AuthoredTransform.Release(element);

            // Assert
            Assert.That((element.style.rotate.keyword, element.style.scale.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Null)));
        }

        // GREEN_ON_BASE(characterization): the base takes off the element the restyle callback that engaging registered.
        [Test]
        public void Given_ASkewedElement_When_Released_Then_ItHoldsNoCallbackEngagingRegistered()
        {
            // Arrange
            var element = new VisualElement();
            var before = CallbackRegistryProbe.BubbleUpCallbackCount(element);
            AuthoredTransform.Engage(element, 12f, 6f);
            var engaged = CallbackRegistryProbe.BubbleUpCallbackCount(element);

            // Act
            AuthoredTransform.Release(element);

            // Assert — -1 where engaging registered nothing, which no count is.
            Assert.That(engaged > before ? CallbackRegistryProbe.BubbleUpCallbackCount(element) : -1, Is.EqualTo(before));
        }

        [Test]
        public void Given_AnElementWithNoSkew_When_ARotateIsWritten_Then_TheSlotHoldsIt()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            AuthoredTransform.SetRotate(element, new Rotate(new Angle(30f, AngleUnit.Degree)));

            // Assert
            Assert.That(element.style.rotate.value.angle.ToDegrees(), Is.EqualTo(30f).Within(1e-4f));
        }
    }
}

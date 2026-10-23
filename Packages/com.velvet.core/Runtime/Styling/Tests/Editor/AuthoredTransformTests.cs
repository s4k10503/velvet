using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    internal sealed class AuthoredTransformTests
    {
        // The matrix the two inline slots draw: the top-left block of the rotation, its columns scaled.
        private static float[] Drawn(VisualElement element)
        {
            var rotate = element.style.rotate.value;
            var carried = Matrix4x4.Rotate(Quaternion.AngleAxis(rotate.angle.ToDegrees(), rotate.axis));
            var scale = element.style.scale.value.value;
            return new[] { scale.x * carried.m00, scale.y * carried.m01, scale.x * carried.m10, scale.y * carried.m11 };
        }

        // rotate(theta) * scale(sx, sy) * skewX(x) skewY(y)
        private static float[] Composite(float rotateDeg, float scaleX, float scaleY, float xDeg, float yDeg)
        {
            var tx = Mathf.Tan(xDeg * Mathf.Deg2Rad);
            var ty = Mathf.Tan(yDeg * Mathf.Deg2Rad);
            var cos = Mathf.Cos(rotateDeg * Mathf.Deg2Rad);
            var sin = Mathf.Sin(rotateDeg * Mathf.Deg2Rad);
            var p00 = cos * scaleX;
            var p01 = -sin * scaleY;
            var p10 = sin * scaleX;
            var p11 = cos * scaleY;
            var k = 1f + (tx * ty);
            return new[] { (p00 * k) + (p01 * ty), (p00 * tx) + p01, (p10 * k) + (p11 * ty), (p10 * tx) + p11 };
        }

        [Test]
        public void Given_ASkewOnAnElementWithNoOtherWriter_When_Engaged_Then_TheSlotsCarryTheShear()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            AuthoredTransform.Engage(element, 12f, 6f);

            // Assert
            Assert.That(Drawn(element), Is.EqualTo(Composite(0f, 1f, 1f, 12f, 6f)).Within(1e-4f));
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
            Assert.That(Drawn(element), Is.EqualTo(Composite(30f, 2f, 0.5f, -12f, 6f)).Within(1e-4f));
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
            Assert.That(Drawn(element), Is.EqualTo(Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-4f));
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
            Assert.That(Drawn(element), Is.EqualTo(Composite(0f, 1f, -1f, 12f, 6f)).Within(1e-4f));
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

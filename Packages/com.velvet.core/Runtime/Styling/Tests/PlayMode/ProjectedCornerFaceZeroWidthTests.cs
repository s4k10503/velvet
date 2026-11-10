using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies, by GPU pixel readback, that a projected corner face on a box laid out with no width paints nothing,
    /// as a box with no area paints nothing in CSS, beside a box with width whose face paints.
    /// </summary>
    internal sealed class ProjectedCornerFaceZeroWidthTests
    {
        private const int Size = 120;

        private RenderTexturePanelHost _host;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private VisualElement Box(float left, float width)
        {
            var box = new VisualElement();
            box.style.position = Position.Absolute;
            box.style.left = left;
            box.style.top = 30f;
            box.style.width = width;
            box.style.height = 40f;
            box.style.backgroundColor = Color.red;
            box.style.borderTopWidth = 4f;
            box.style.borderBottomWidth = 4f;
            box.style.borderTopColor = Color.blue;
            box.style.borderBottomColor = Color.blue;
            _host.Root.Add(box);
            return box;
        }

        private int PaintedPixels(int fromX, int toX)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(fromX, 0, toX - fromX, Size));
            var painted = 0;
            foreach (var pixel in pixels)
            {
                if (pixel.a > 0) painted++;
            }
            return painted;
        }

        // GREEN_ON_BASE(characterization): the base paints no face on a box with no width; measured the same with
        // Paint's `w > 0f` written `w >= 0f`, the case its declaration names.
        [UnityTest]
        public IEnumerator Given_AProjectedFaceOnABoxWithNoWidth_When_Rendered_Then_NoPixelIsPainted()
        {
            // Arrange — the control, 20 wide, stands in the left half and the box with no width in the right.
            _host = new RenderTexturePanelHost("ZeroWidthFace", Size, Size, clear: true);
            yield return null;
            var control = Box(10f, 20f);
            var empty = Box(80f, 0f);
            for (var i = 0; i < 3; i++) yield return null;
            var radii = CornerRadii.Circular(10f, 10f, 10f, 10f);

            // Act
            ProjectedCornerFace.Draw(control, radii, 0f);
            ProjectedCornerFace.Draw(empty, radii, 0f);
            for (var i = 0; i < 5; i++) yield return null;

            // Assert — -1 where the control painted nothing, which zero never equals.
            Assert.That(PaintedPixels(0, Size / 2) > 0 ? PaintedPixels(Size / 2, Size) : -1, Is.Zero);
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins, by real GPU pixel readback, that a skew written as a uniform scale and one 3D rotation
    /// (<see cref="SkewTransformFactor"/>) is drawn by UI Toolkit as the shear it stands for.
    /// </summary>
    [Timeout(600000)]
    internal sealed class SkewTransformPlaybackTests
    {
        private const int Width = 360;
        private const int Height = 290;
        private const float BoxSize = 100f;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private TargetFrameRateScope _frameRateScope;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _frameRateScope.Dispose();
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private static bool IsBlue(Color32 p) => p.b > 140 && p.r < 90 && p.g < 90;

        // Readback rows run bottom-up while worldBound runs top-down, so the row index is mirrored.
        private static Color32 At(Color32[] pixels, int col, int row)
            => pixels[((Height - 1 - row) * Width) + col];

        // The blue box's measured shape: the rows it spans, and the first blue column two rows in from its top
        // and two rows in from its bottom.
        private static (int Rows, int TopLeft, int BottomLeft) Measure(Color32[] pixels)
        {
            var first = -1;
            var last = -1;
            for (var row = 0; row < Height; row++)
            {
                for (var col = 0; col < Width; col++)
                {
                    if (IsBlue(At(pixels, col, row)))
                    {
                        first = first < 0 ? row : first;
                        last = row;
                        break;
                    }
                }
            }
            return (last - first + 1, LeftmostBlue(pixels, first + 2), LeftmostBlue(pixels, last - 2));
        }

        private static int LeftmostBlue(Color32[] pixels, int row)
        {
            for (var col = 0; col < Width; col++)
            {
                if (IsBlue(At(pixels, col, row)))
                {
                    return col;
                }
            }
            return -1;
        }

        private IEnumerator Mount(float xDeg, float yDeg, string extraClasses = "")
        {
            _host = new RenderTexturePanelHost("Skew", Width, Height);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root,
                V.Div(name: "box", className: "w-[100px] h-[100px] mt-[80px] ml-[130px] bg-[#0000ff] " + extraClasses));
            var box = _host.Root.Q<VisualElement>("box");
            if (SkewTransformFactor.TryFactor(xDeg, yDeg, out var rotation, out var scale))
            {
                rotation.ToAngleAxis(out var angle, out var axis);
                box.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree), axis);
                box.style.scale = new Scale(new Vector3(scale, scale, 1f));
            }
            return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        [UnityTest]
        public IEnumerator Given_ABoxCarryingTheFactoredSkew_When_UIToolkitDrawsIt_Then_ItsTopAndBottomEdgesAreShearedSideways()
        {
            // Arrange — skewX(30deg) carries a point t * y sideways with t = tan 30, so rows 2 and 97 of a box
            // 100 tall sit 95 * t apart and the box keeps its height.
            yield return Mount(30f, 0f);

            // Act
            var measured = Measure(RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height)));

            // Assert — the expected values are declared (100 rows; the edge offset between the two probed rows),
            // and the margin is the antialiased edge's width.
            var shear = measured.TopLeft - measured.BottomLeft;
            Assert.That(new float[] { measured.Rows, shear },
                Is.EqualTo(new[] { BoxSize, -(BoxSize - 5f) * Mathf.Tan(30f * Mathf.Deg2Rad) }).Within(3f));
        }

        [UnityTest]
        public IEnumerator Given_AClippingBoxCarryingTheFactoredSkew_When_UIToolkitDrawsIt_Then_ItsShearedEdgesAreNotCutAtTheLayoutBox()
        {
            // Arrange — the same box and angle as above, with a hidden overflow, which cuts what the element's own
            // content paints outside its padding box.
            yield return Mount(30f, 0f, "overflow-hidden");

            // Act
            var measured = Measure(RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height)));

            // Assert
            var shear = measured.TopLeft - measured.BottomLeft;
            Assert.That(new float[] { measured.Rows, shear },
                Is.EqualTo(new[] { BoxSize, -(BoxSize - 5f) * Mathf.Tan(30f * Mathf.Deg2Rad) }).Within(3f));
        }

        [UnityTest]
        public IEnumerator Given_ABoxCarryingTheFactoredSkew_When_APointerPicksNearItsTopEdge_Then_TheBoundingRectangleOfTheShearedBoxIsPicked()
        {
            // Arrange — skewX(30deg) carries the top edge left by 50 * tan 30 = 28.9 px about the centre of the
            // 100 x 100 box at (130, 80): 15 px left of its left edge near the top is painted, and 15 px left of its
            // right edge there is not.
            yield return Mount(30f, 0f);
            var panel = _host.Root.panel;

            // Act
            var paintedCorner = panel.Pick(new Vector2(130f - 15f, 85f));
            var unpaintedCorner = panel.Pick(new Vector2(230f - 15f, 85f));

            // Assert — both resolve to the box: the pick reaches the sheared box's bounding rectangle rather than
            // the sheared shape.
            var box = _host.Root.Q<VisualElement>("box");
            Assert.That((ReferenceEquals(paintedCorner, box), ReferenceEquals(unpaintedCorner, box)),
                Is.EqualTo((true, true)));
        }
    }
}

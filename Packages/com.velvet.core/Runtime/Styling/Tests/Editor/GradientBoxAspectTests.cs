using NUnit.Framework;
using UnityEngine;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a gradient is laid out over the box's physical proportions, as CSS lays it out: a
    /// diagonal angle's gradient line runs the length the box gives it, a corner direction keeps its 50%
    /// line through the two other corners, a conic sweeps physical angles, and a radial is the
    /// farthest-corner ellipse. The bake cases take the aspect directly and read texels, so a texel's
    /// coordinates are the gradient parameter's inputs; the shader cases read the same four shapes off the
    /// sheared-silhouette bake of a 128 x 64 box. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientBoxAspectTests
    {
        private static GradientSpec Extract(string shape)
        {
            StyleGradientClass.TryExtract(new[] { shape, "from-[#000000]", "to-[#ffffff]" }, out var spec);
            return spec;
        }

        // The red channel of a baked texel; black to white makes it the gradient parameter.
        private static float BakedRed(string shape, float aspect, int x, int y)
        {
            var tex = GradientBackground.Bake(Extract(shape), aspect);
            var red = tex.GetPixel(x, y).r;
            Object.DestroyImmediate(tex);
            return red;
        }

        // The red channel of the silhouette bake of a 128 x 64 box without corner radii, at a pixel measured
        // from the box's top-left. Null when the bake could not run. The texture carries a 2px margin
        // around the box and reads bottom-up.
        private static float? SilhouetteRed(string shape, float boxX, float boxY)
        {
            var tex = GradientSilhouetteBaker.Bake(Extract(shape), 128f, 64f, 0f, 0f, Vector4.zero);
            if (tex == null)
            {
                return null;
            }
            var red = tex.GetPixel(2 + Mathf.FloorToInt(boxX), tex.height - 1 - (2 + Mathf.FloorToInt(boxY))).r;
            Object.DestroyImmediate(tex);
            return red;
        }

        #region Bake

        [Test]
        public void Given_A45DegreeGradient_When_BakedForAWideBox_Then_TheTopLeftCornerIsAThirdAlong()
        {
            // Act — in a 2:1 box the gradient line is 2.12 box heights long and the corner sits 0.33 along it;
            // laid out as a square it would be 0.5.
            var red = BakedRed("bg-linear-45", 2f, 0, 127);

            // Assert
            Assert.That(red, Is.EqualTo(1f / 3f).Within(0.02f));
        }

        [Test]
        public void Given_ToTopRight_When_BakedForAWideBox_Then_TheLeftEdgeMiddleIsAQuarterAlong()
        {
            // Act — the corner direction of a 2:1 box is steeper than 45deg, so the left edge's middle is
            // a quarter along where the 45deg line puts it a sixth along. A line through the other two
            // corners puts it there.
            var red = BakedRed("bg-gradient-to-tr", 2f, 0, 64);

            // Assert
            Assert.That(red, Is.EqualTo(0.25f).Within(0.02f));
        }

        [Test]
        public void Given_AConic_When_BakedForAWideBox_Then_TheTopRightCornerIsAtItsPhysicalAngle()
        {
            // Act — from the centre of a 2:1 box the top-right corner lies 63.4° clockwise from up, not 45°.
            var red = BakedRed("bg-conic", 2f, 127, 127);

            // Assert
            Assert.That(red, Is.EqualTo(63.43f / 360f).Within(0.01f));
        }

        [Test]
        public void Given_ARadialAtTheTopMiddle_When_Baked_Then_TheTopRightCornerIsOnTheFarthestCornerEllipse()
        {
            // Act — the ellipse through the bottom corners has semi-axes of sqrt 2 times the farthest side
            // (0.71 of the width, 1.41 of the height): the top-right corner is at 1 / sqrt 2 of the way out.
            var red = BakedRed("bg-radial-[at_top]", 1f, 127, 127);

            // Assert
            Assert.That(red, Is.EqualTo(0.7071f).Within(0.02f));
        }

        #endregion

        #region Which gradients depend on the box

        [Test]
        public void Given_ADiagonalAngle_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-linear-45"));

            // Assert
            Assert.That(depends, Is.True);
        }

        [Test]
        public void Given_AnAngleAlongAnAxis_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-linear-90"));

            // Assert
            Assert.That(depends, Is.False);
        }

        [Test]
        public void Given_ANegativeAngleAlongAnAxis_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act — -bg-linear-90 is 270deg, to the left.
            var depends = GradientBackground.DependsOnAspect(Extract("-bg-linear-90"));

            // Assert
            Assert.That(depends, Is.False);
        }

        [Test]
        public void Given_ACornerDirection_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-gradient-to-br"));

            // Assert
            Assert.That(depends, Is.False);
        }

        [Test]
        public void Given_AConic_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-conic"));

            // Assert
            Assert.That(depends, Is.True);
        }

        [Test]
        public void Given_ARadial_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial"));

            // Assert
            Assert.That(depends, Is.False);
        }

        #endregion

        #region Aspect key

        [Test]
        public void Given_ABoxTwiceAsWideAsTall_When_Keyed_Then_ItIsOneOctaveOfSteps()
        {
            // Act
            var key = GradientBackground.AspectKey(200f, 100f);

            // Assert
            Assert.That(key, Is.EqualTo(32));
        }

        [Test]
        public void Given_ABoxTwiceAsTallAsWide_When_Keyed_Then_ItIsOneOctaveOfStepsTheOtherWay()
        {
            // Act
            var key = GradientBackground.AspectKey(100f, 200f);

            // Assert
            Assert.That(key, Is.EqualTo(-32));
        }

        [Test]
        public void Given_ABoxWithNoSizeYet_When_Keyed_Then_ItIsASquare()
        {
            // Act
            var key = GradientBackground.AspectKey(float.NaN, 100f);

            // Assert
            Assert.That(key, Is.Zero);
        }

        [Test]
        public void Given_AnExtremelyWideBox_When_Keyed_Then_TheKeyIsHeldToFourOctaves()
        {
            // Act
            var key = GradientBackground.AspectKey(1000000f, 1f);

            // Assert
            Assert.That(key, Is.EqualTo(128));
        }

        #endregion

        #region Silhouette shader

        [Test]
        public void Given_A45DegreeGradient_When_SilhouetteBakedForAWideBox_Then_TheTopLeftIsAThirdAlong()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — a point just inside the corner, on the same perpendicular to the line as the corner.
            var red = SilhouetteRed("bg-linear-45", 10f, 10f);

            // Assert — NaN when the bake did not run.
            Assert.That(red ?? float.NaN, Is.EqualTo(1f / 3f).Within(0.03f));
        }

        [Test]
        public void Given_ToTopRight_When_SilhouetteBakedForAWideBox_Then_TheLeftEdgeMiddleIsAQuarterAlong()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act
            var red = SilhouetteRed("bg-gradient-to-tr", 2f, 32f);

            // Assert
            Assert.That(red ?? float.NaN, Is.EqualTo(0.256f).Within(0.03f));
        }

        [Test]
        public void Given_AConic_When_SilhouetteBakedForAWideBox_Then_TheTopRightIsAtItsPhysicalAngle()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — 56.5px right of and 25.5px above the centre: 65.7° clockwise from up.
            var red = SilhouetteRed("bg-conic", 120f, 6f);

            // Assert
            Assert.That(red ?? float.NaN, Is.EqualTo(0.1825f).Within(0.03f));
        }

        [Test]
        public void Given_ARadialAtTheTopMiddle_When_SilhouetteBaked_Then_TheTopRightIsOnTheFarthestCornerEllipse()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — (120.5, 6.5) of the 128 x 64 box.
            var red = SilhouetteRed("bg-radial-[at_top]", 120f, 6f);

            // Assert
            Assert.That(red ?? float.NaN, Is.EqualTo(0.628f).Within(0.03f));
        }

        [Test]
        public void Given_AStopFadingToTransparent_When_SilhouetteBaked_Then_TheMidpointKeepsTheStopsColour()
        {
            TestGraphics.IgnoreIfHeadless("a GPU silhouette bake (Graphics.Blit + ReadPixels)");

            // Act — half transparent red across the middle of the box.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000,transparent]" }, out var spec);
            var tex = GradientSilhouetteBaker.Bake(spec, 128f, 64f, 0f, 0f, Vector4.zero);
            var red = float.NaN;
            if (tex != null)
            {
                red = tex.GetPixel(2 + 64, tex.height / 2).r;
                Object.DestroyImmediate(tex);
            }

            // Assert
            Assert.That(red, Is.GreaterThan(0.95f));
        }

        #endregion
    }
}

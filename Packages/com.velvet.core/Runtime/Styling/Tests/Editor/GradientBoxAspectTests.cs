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

        [Test]
        public void Given_ACircleToTheClosestSide_When_BakedForAWideBox_Then_ItsRadiusIsHalfTheHeight()
        {
            // Act — texel 80 sits 0.13 of the width right of the centre, which is 0.26 heights in a 2:1 box;
            // the circle reaches 0.5 heights, so the texel is 0.52 of the way out. An ellipse would put it at 0.26.
            var red = BakedRed("bg-radial-[circle_closest-side]", 2f, 80, 64);

            // Assert
            Assert.That(red, Is.EqualTo(0.52f).Within(0.02f));
        }

        [Test]
        public void Given_ACircleOfFixedRadius_When_BakedForAKnownWidth_Then_TheRadiusIsInPixels()
        {
            // Act — a 200 x 100 box: texel 80 is 26px right of the centre, the circle's radius is 100px.
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_100px]", "from-[#000000]", "to-[#ffffff]" }, out var spec);
            var tex = GradientBackground.Bake(spec, 2f, 200f);
            var red = tex.GetPixel(80, 64).r;
            Object.DestroyImmediate(tex);

            // Assert
            Assert.That(red, Is.EqualTo(0.26f).Within(0.02f));
        }

        [Test]
        public void Given_ACircleToTheClosestSide_When_RadiiAreRead_Then_BothAreTheNearestSide()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_closest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((50, 50)));
        }

        [Test]
        public void Given_ACircleToTheFarthestSide_When_RadiiAreRead_Then_BothAreTheFarthestSide()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_farthest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 100)));
        }

        [Test]
        public void Given_ACircleToTheClosestCorner_When_RadiiAreRead_Then_BothAreTheCornerDistance()
        {
            // Act — from the centre of 200 x 100 every corner is 112px away.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_closest-corner]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((112, 112)));
        }

        [Test]
        public void Given_ACircleToTheFarthestCornerOffCentre_When_RadiiAreRead_Then_BothAreTheFarthestCornerDistance()
        {
            // Act — at the top left the farthest corner is the bottom right, 224px away.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_at_top_left]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((224, 224)));
        }

        [Test]
        public void Given_AnEllipseToTheClosestSide_When_RadiiAreRead_Then_ItHasTheNearestHalfWidthAndHeight()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_closest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 50)));
        }

        [Test]
        public void Given_AnEllipseToTheFarthestSideOffCentre_When_RadiiAreRead_Then_ItHasTheFarthestWidthAndHeight()
        {
            // Act — at the top left the farthest sides are the right and bottom.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_farthest-side_at_top_left]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((200, 100)));
        }

        [Test]
        public void Given_AnEllipseToTheClosestCorner_When_RadiiAreRead_Then_ItIsTheClosestSideEllipseThroughTheCorner()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_closest-corner]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((141, 71)));
        }

        [Test]
        public void Given_ADefaultRadial_When_RadiiAreRead_Then_ItIsTheFarthestCornerEllipse()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((141, 71)));
        }

        [Test]
        public void Given_AnEllipseOfAPercentageAndALength_When_RadiiAreRead_Then_ThePercentageIsOfTheBox()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_50%_20px]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 20)));
        }

        [Test]
        public void Given_ACircleOfFixedRadius_When_RadiiAreRead_Then_BothAreThatRadius()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_30px]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((30, 30)));
        }

        [Test]
        public void Given_ABoxOfAThousandPixels_When_Keyed_Then_ItsWidthIsInOctaveSteps()
        {
            // Act
            var key = GradientBackground.WidthKey(1000f);

            // Assert
            Assert.That(key, Is.EqualTo(319));
        }

        [Test]
        public void Given_ABoxWithNoWidthYet_When_Keyed_Then_ItsWidthKeyIsZero()
        {
            // Act
            var key = GradientBackground.WidthKey(float.NaN);

            // Assert
            Assert.That(key, Is.Zero);
        }

        [Test]
        public void Given_ACircle_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[circle]"));

            // Assert
            Assert.That(depends, Is.True);
        }

        [Test]
        public void Given_AnEllipseToTheClosestSide_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[ellipse_closest-side]"));

            // Assert
            Assert.That(depends, Is.False);
        }

        [Test]
        public void Given_AnEllipseOfPercentages_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[ellipse_50%_30%]"));

            // Assert
            Assert.That(depends, Is.False);
        }

        [Test]
        public void Given_AnEllipseOfLengths_When_Asked_Then_ItDependsOnTheBoxSize()
        {
            // Act
            var absolute = GradientBackground.NeedsAbsoluteSize(Extract("bg-radial-[ellipse_40px_20px]"));

            // Assert
            Assert.That(absolute, Is.True);
        }

        [Test]
        public void Given_ACircleToTheClosestSide_When_Asked_Then_ItDoesNotDependOnTheBoxSize()
        {
            // Act
            var absolute = GradientBackground.NeedsAbsoluteSize(Extract("bg-radial-[circle_closest-side]"));

            // Assert
            Assert.That(absolute, Is.False);
        }

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

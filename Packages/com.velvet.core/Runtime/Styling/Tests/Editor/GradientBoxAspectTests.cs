using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a gradient is laid out over the box's physical proportions, as CSS lays it out: a
    /// diagonal angle's gradient line runs the length the box gives it, a corner direction keeps its 50%
    /// line through the two other corners, a conic sweeps physical angles, and a radial is the
    /// farthest-corner ellipse. The bake cases take the aspect directly and read texels, so a texel's
    /// coordinates are the gradient parameter's inputs. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientBoxAspectTests
    {
        // A utility class is named in sRGB here, so a texel's red is the gradient parameter; a bracket is already.
        private static GradientSpec Extract(string shape)
        {
            var named = shape.Contains("[") || shape.Contains("/") ? shape : shape + "/srgb";
            StyleGradientClass.TryExtract(new[] { named, "from-[#000000]", "to-[#ffffff]" }, out var spec);
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

        #region Bake

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_A45DegreeGradient_When_BakedForAWideBox_Then_TheTopLeftCornerIsAThirdAlong()
        {
            // Act — in a 2:1 box the gradient line is 2.12 box heights long and the corner sits 0.33 along it;
            // laid out as a square it would be 0.5.
            var red = BakedRed("bg-linear-45", 2f, 0, 127);

            // Assert
            Assert.That(red, Is.EqualTo(1f / 3f).Within(0.02f));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
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

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AConic_When_BakedForAWideBox_Then_TheTopRightCornerIsAtItsPhysicalAngle()
        {
            // Act — from the centre of a 2:1 box the top-right corner lies 63.4° clockwise from up, not 45°.
            var red = BakedRed("bg-conic", 2f, 127, 127);

            // Assert
            Assert.That(red, Is.EqualTo(63.43f / 360f).Within(0.01f));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
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

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheClosestSide_When_BakedForAWideBox_Then_ItsRadiusIsHalfTheHeight()
        {
            // Act — texel 80 sits 0.13 of the width right of the centre, which is 0.26 heights in a 2:1 box;
            // the circle reaches 0.5 heights, so the texel is 0.52 of the way out. An ellipse would put it at 0.26.
            var red = BakedRed("bg-radial-[circle_closest-side]", 2f, 80, 64);

            // Assert
            Assert.That(red, Is.EqualTo(0.52f).Within(0.02f));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
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

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheClosestSide_When_RadiiAreRead_Then_BothAreTheNearestSide()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_closest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((50, 50)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheFarthestSide_When_RadiiAreRead_Then_BothAreTheFarthestSide()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_farthest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 100)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheClosestCorner_When_RadiiAreRead_Then_BothAreTheCornerDistance()
        {
            // Act — from the centre of 200 x 100 every corner is 112px away.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_closest-corner]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((112, 112)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheFarthestCornerOffCentre_When_RadiiAreRead_Then_BothAreTheFarthestCornerDistance()
        {
            // Act — at the top left the farthest corner is the bottom right, 224px away.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_at_top_left]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((224, 224)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseToTheClosestSide_When_RadiiAreRead_Then_ItHasTheNearestHalfWidthAndHeight()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_closest-side]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 50)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseToTheFarthestSideOffCentre_When_RadiiAreRead_Then_ItHasTheFarthestWidthAndHeight()
        {
            // Act — at the top left the farthest sides are the right and bottom.
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_farthest-side_at_top_left]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((200, 100)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseToTheClosestCorner_When_RadiiAreRead_Then_ItIsTheClosestSideEllipseThroughTheCorner()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_closest-corner]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((141, 71)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADefaultRadial_When_RadiiAreRead_Then_ItIsTheFarthestCornerEllipse()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((141, 71)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseOfAPercentageAndALength_When_RadiiAreRead_Then_ThePercentageIsOfTheBox()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[ellipse_50%_20px]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((100, 20)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleOfFixedRadius_When_RadiiAreRead_Then_BothAreThatRadius()
        {
            // Act
            var radii = GradientBackground.RadialRadii(Extract("bg-radial-[circle_30px]"), 200f, 100f);

            // Assert
            Assert.That((Mathf.RoundToInt(radii.x), Mathf.RoundToInt(radii.y)), Is.EqualTo((30, 30)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircle_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[circle]"));

            // Assert
            Assert.That(depends, Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseToTheClosestSide_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[ellipse_closest-side]"));

            // Assert
            Assert.That(depends, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseOfPercentages_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-radial-[ellipse_50%_30%]"));

            // Assert
            Assert.That(depends, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnEllipseOfLengths_When_Asked_Then_ItDependsOnTheBoxSize()
        {
            // Act
            var absolute = GradientBackground.NeedsAbsoluteSize(Extract("bg-radial-[ellipse_40px_20px]"));

            // Assert
            Assert.That(absolute, Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACircleToTheClosestSide_When_Asked_Then_ItDoesNotDependOnTheBoxSize()
        {
            // Act
            var absolute = GradientBackground.NeedsAbsoluteSize(Extract("bg-radial-[circle_closest-side]"));

            // Assert
            Assert.That(absolute, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AClosestSideEllipseAndA200PercentStop_When_BakedAtACorner_Then_TheColourKeepsRunningPastTheRadius()
        {
            // Act — the corner is 1.41 radii out, so 70% of the way to a stop at 200%. A parameter held at 1
            // would stop at half.
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[ellipse_closest-side,#000000,#ffffff_200%]" }, out var spec);
            var tex = GradientBackground.Bake(spec, 1f);
            var past = tex.GetPixel(0, 127).r;
            Object.DestroyImmediate(tex);

            // Assert
            Assert.That(past, Is.EqualTo(0.7071f).Within(0.02f));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADiscWithAHardEdge_When_BakedPastIt_Then_TheLaterColourPaintsOutside()
        {
            // Act — red up to the radius and blue from it: the corner lies outside the circle.
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_closest-side,#ff0000_100%,#0000ff_100%]" }, out var spec);
            var tex = GradientBackground.Bake(spec, 1f);
            var corner = tex.GetPixel(0, 127);
            Object.DestroyImmediate(tex);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(corner), Is.EqualTo("0000FFFF"));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADiscWithAHardEdge_When_BakedInsideIt_Then_TheEarlierColourPaintsAtTheCentre()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_closest-side,#ff0000_100%,#0000ff_100%]" }, out var spec);
            var tex = GradientBackground.Bake(spec, 1f);
            var centre = tex.GetPixel(64, 64);
            Object.DestroyImmediate(tex);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(centre), Is.EqualTo("FF0000FF"));
        }

        #region Texture shape and sharpness

        private static (int, int) SizeOfBake(string shape, int resolution)
        {
            var tex = GradientBackground.Bake(Extract(shape), 1f, 0f, resolution);
            var size = (tex.width, tex.height);
            Object.DestroyImmediate(tex);
            return size;
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AGradientToTheRight_When_Baked_Then_ItIsOneRow()
        {
            // Act
            var size = SizeOfBake("bg-gradient-to-r", 0);

            // Assert
            Assert.That(size, Is.EqualTo((128, 1)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AGradientToTheBottom_When_Baked_Then_ItIsOneColumn()
        {
            // Act
            var size = SizeOfBake("bg-gradient-to-b", 0);

            // Assert
            Assert.That(size, Is.EqualTo((1, 128)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADiagonalGradient_When_Baked_Then_ItIsSquare()
        {
            // Act
            var size = SizeOfBake("bg-linear-45", 0);

            // Assert
            Assert.That(size, Is.EqualTo((128, 128)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AGradientToTheRight_When_BakedAtAResolution_Then_TheRowIsThatLong()
        {
            // Act
            var size = SizeOfBake("bg-gradient-to-r", 1024);

            // Assert
            Assert.That(size, Is.EqualTo((1024, 1)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADiagonalGradient_When_BakedAtAResolution_Then_TheSquareIsThatLong()
        {
            // Act
            var size = SizeOfBake("bg-linear-45", 512);

            // Assert
            Assert.That(size, Is.EqualTo((512, 512)));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AHardStopBakedForALongRow_When_Read_Then_TheEdgeFallsBetweenTwoNeighbouringTexels()
        {
            // Arrange — red up to 50%, blue from it, in a row of 1024 texels.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000_50%,#0000ff_50%]" }, out var spec);

            // Act
            var tex = GradientBackground.Bake(spec, 1f, 0f, 1024);
            var edge = (ColorUtility.ToHtmlStringRGBA(tex.GetPixel(511, 0)), ColorUtility.ToHtmlStringRGBA(tex.GetPixel(512, 0)));
            Object.DestroyImmediate(tex);

            // Assert
            Assert.That(edge, Is.EqualTo(("FF0000FF", "0000FFFF")));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AHardStop_When_Asked_Then_ItHasASharpFeature()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000_50%,#0000ff_50%]" }, out var spec);

            // Assert
            Assert.That(GradientBackground.HasSharpFeature(spec), Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ASmoothGradient_When_Asked_Then_ItHasNoSharpFeature()
        {
            // Act
            var sharp = GradientBackground.HasSharpFeature(Extract("bg-gradient-to-r"));

            // Assert
            Assert.That(sharp, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AStopInPixels_When_Asked_Then_ItHasASharpFeature()
        {
            // Act — where the stop falls is not known until the size is.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000_0px,#0000ff_100px]" }, out var spec);

            // Assert
            Assert.That(GradientBackground.HasSharpFeature(spec), Is.True);
        }

        #endregion

        #region Which gradients depend on the box

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ADiagonalAngle_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-linear-45"));

            // Assert
            Assert.That(depends, Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnAngleAlongAnAxis_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-linear-90"));

            // Assert
            Assert.That(depends, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ANegativeAngleAlongAnAxis_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act — -bg-linear-90 is 270deg, to the left.
            var depends = GradientBackground.DependsOnAspect(Extract("-bg-linear-90"));

            // Assert
            Assert.That(depends, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ACornerDirection_When_Asked_Then_ItDoesNotDependOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-gradient-to-br"));

            // Assert
            Assert.That(depends, Is.False);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AConic_When_Asked_Then_ItDependsOnTheAspect()
        {
            // Act
            var depends = GradientBackground.DependsOnAspect(Extract("bg-conic"));

            // Assert
            Assert.That(depends, Is.True);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
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

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ABoxTwiceAsWideAsTall_When_Keyed_Then_ItIsOneOctaveOfSteps()
        {
            // Act
            var key = GradientBackground.AspectKey(200f, 100f);

            // Assert
            Assert.That(key, Is.EqualTo(32));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ABoxTwiceAsTallAsWide_When_Keyed_Then_ItIsOneOctaveOfStepsTheOtherWay()
        {
            // Act
            var key = GradientBackground.AspectKey(100f, 200f);

            // Assert
            Assert.That(key, Is.EqualTo(-32));
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_ABoxWithNoSizeYet_When_Keyed_Then_ItIsASquare()
        {
            // Act
            var key = GradientBackground.AspectKey(float.NaN, 100f);

            // Assert
            Assert.That(key, Is.Zero);
        }

        // GREEN_ON_BASE(refactor): the base already runs this case; the fixture lost its silhouette-bake helpers and this bake case reads none of them.
        [Test]
        public void Given_AnExtremelyWideBox_When_Keyed_Then_TheKeyIsHeldToFourOctaves()
        {
            // Act
            var key = GradientBackground.AspectKey(1000000f, 1f);

            // Assert
            Assert.That(key, Is.EqualTo(128));
        }

        #endregion
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the CSS argument list a gradient shape's brackets may carry —
    /// <c>bg-linear-[90deg,#0f172a_0%,#ffffff_50%,#0f172a_100%]</c> and its radial / conic siblings — as
    /// <see cref="StyleGradientClass"/> parses it and <see cref="GradientBackground"/> bakes it: from 2 up to
    /// <c>GradientSpec.MaxStops</c> stops, CSS Images colour-stop fix-up, the leading line argument each
    /// shape reads, and a malformed list leaving the class inert. The bake cases sample a left-to-right
    /// gradient, where a texture column's fraction of the width is the gradient parameter. GWT, one assert
    /// per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientStopListTests
    {
        private const string HighlightList =
            "bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]";

        private static Color SampleAcross(string[] classNames, float fraction)
        {
            StyleGradientClass.TryExtract(classNames, out var spec);
            var tex = GradientBackground.Bake(spec, 1f);
            var pixel = tex.GetPixel(Mathf.RoundToInt(fraction * (tex.width - 1)), tex.height / 2);
            Object.DestroyImmediate(tex);
            return pixel;
        }

        #region Bake

        [Test]
        public void Given_AFiveStopHighlightList_When_Baked_Then_TheMiddleIsTheHighlight()
        {
            // Act — sample t≈0.5, where the white stop sits between two base stops 5% either side.
            var middle = SampleAcross(new[] { HighlightList }, 0.5f);

            // Assert
            Assert.That(Mathf.Min(middle.r, middle.g, middle.b), Is.GreaterThan(0.9f));
        }

        [Test]
        public void Given_AFiveStopHighlightList_When_Baked_Then_SixtyPercentIsTheBase()
        {
            // Act — sample t≈0.6, past the fourth stop: a model holding only three stops ends on the highlight.
            var past = SampleAcross(new[] { HighlightList }, 0.6f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(past), Is.EqualTo("0F172AFF"));
        }

        [Test]
        public void Given_UnpositionedMiddleStop_When_Baked_Then_ItSitsHalfwayBetweenItsNeighbours()
        {
            // Act — red, green, blue with no positions: green is placed at 50%.
            var middle = SampleAcross(new[] { "bg-linear-[to_right,#ff0000,#00ff00,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.g > 0.95f && middle.r < 0.05f && middle.b < 0.05f, Is.True);
        }

        [Test]
        public void Given_AnUnpositionedStopBetweenPositionedOnes_When_Baked_Then_ItSitsHalfwayBetweenThem()
        {
            // Act — green has no position and sits between red at 20% and blue at 80%, so it is placed at 50%.
            var middle = SampleAcross(
                new[] { "bg-linear-[to_right,#ff0000_0%,#ff0000_20%,#00ff00,#0000ff_80%]" }, 0.5f);

            // Assert
            Assert.That(middle.g > 0.95f && middle.r < 0.05f && middle.b < 0.05f, Is.True);
        }

        [Test]
        public void Given_ASegmentStartingHalfway_When_Baked_Then_ItInterpolatesOverItsOwnSpan()
        {
            // Act — black at 50% to white at 100%: t≈0.75 is halfway along that segment.
            var threeQuarters = SampleAcross(new[] { "bg-linear-[to_right,#000000_50%,#ffffff_100%]" }, 0.75f);

            // Assert
            Assert.That(threeQuarters.r, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void Given_ALastStopBeforeTheEnd_When_Baked_Then_ItsColourHoldsToTheEnd()
        {
            // Act
            var past = SampleAcross(new[] { "bg-linear-[to_right,#ff0000,#0000ff_50%]" }, 0.75f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(past), Is.EqualTo("0000FFFF"));
        }

        [Test]
        public void Given_AHardStopAtTheEnd_When_Baked_Then_TheRightEdgeKeepsTheEarlierColour()
        {
            // Act — blue and green both at 100%: CSS paints the box blue and the green only past its end.
            var right = SampleAcross(new[] { "bg-linear-[to_right,#ff0000,#0000ff_100%,#00ff00_100%]" }, 1f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(right), Is.EqualTo("0000FFFF"));
        }

        [Test]
        public void Given_TwoStopsAtTheStart_When_Baked_Then_TheLeftEdgeIsTheLaterColour()
        {
            // Act — red and blue both at 0%: the red is only before the box, so the whole left edge is blue.
            var left = SampleAcross(new[] { "bg-linear-[to_right,#ff0000_0%,#0000ff_0%]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("0000FFFF"));
        }

        [Test]
        public void Given_AHardStopInTheMiddle_When_Baked_Then_TheLaterColourStartsAtIt()
        {
            // Act — red until 50%, blue from 50%: t≈0.6 is past the edge.
            var past = SampleAcross(new[] { "bg-linear-[to_right,#ff0000_50%,#0000ff_50%]" }, 0.6f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(past), Is.EqualTo("0000FFFF"));
        }

        [Test]
        public void Given_FromAndViaBothAtZero_When_Baked_Then_TheLeftEdgeIsTheViaColour()
        {
            // Act — from and via both sit at the left edge, and the later stop starts there.
            var left = SampleAcross(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "via-[#00ff00]", "via-0%", "to-[#0000ff]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("00FF00FF"));
        }

        // GREEN_ON_BASE(refactor): a to- colour alone still fades from its own transparent version.
        [Test]
        public void Given_AToColourAlone_When_Baked_Then_TheLeftEdgeIsItsTransparentVersion()
        {
            // Act
            var left = SampleAcross(new[] { "bg-linear-to-r", "to-[#0000ff]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("0000FF00"));
        }

        [Test]
        public void Given_AStopPositionedBehindAnEarlierOne_When_Baked_Then_ItIsRaisedToTheEarlierPosition()
        {
            // Arrange — blue at 20% follows red at 50%, so blue is raised to 50%: just past 50% the blue→white
            // segment has barely begun. Left at 20%, that segment would already be 40% of the way to white.
            var classNames = new[] { "bg-linear-[to_right,#ff0000_0%,#ff0000_50%,#0000ff_20%,#ffffff_100%]" };

            // Act
            var pastHalf = SampleAcross(classNames, 0.52f);

            // Assert
            Assert.That(pastHalf.b > 0.9f && pastHalf.r < 0.1f, Is.True);
        }

        [Test]
        public void Given_ATwoPositionStop_When_Baked_Then_TheColourHoldsBetweenItsTwoPositions()
        {
            // Act — red from 0% to 40%, then blue from 60%: t≈0.3 is still flat red.
            var inside = SampleAcross(new[] { "bg-linear-[to_right,#ff0000_0%_40%,#0000ff_60%_100%]" }, 0.3f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(inside), Is.EqualTo("FF0000FF"));
        }

        [Test]
        public void Given_AnRgbFunctionStop_When_Baked_Then_ItsCommasDoNotSplitTheList()
        {
            // Act
            var left = SampleAcross(new[] { "bg-linear-[to_right,rgb(255,0,0),#0000ff]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("FF0000FF"));
        }

        [Test]
        public void Given_APaletteNameStop_When_Baked_Then_ItResolvesThroughThePalette()
        {
            // Act — slate-900 is a palette name, not a CSS colour name.
            var left = SampleAcross(new[] { "bg-linear-[to_right,slate-900,#ffffff]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("0F172AFF"));
        }

        [Test]
        public void Given_AStopListBesideFromAndToUtilities_When_Baked_Then_TheirStopsFollowTheList()
        {
            // Arrange — red at 0%, blue at 50%, then the utilities' green (0%, raised to 50%) and white (100%).
            var classNames = new[] { "bg-linear-[to_right,#ff0000_0%,#0000ff_50%]", "from-[#00ff00]", "to-[#ffffff]" };

            // Act — t≈0.75 is halfway along the green→white segment.
            var threeQuarters = SampleAcross(classNames, 0.75f);

            // Assert
            Assert.That(threeQuarters.r, Is.EqualTo(0.5f).Within(0.03f));
        }

        [Test]
        public void Given_AListWithUnpositionedStopsBesideUtilities_When_Baked_Then_OneFixUpPlacesThemAll()
        {
            // Arrange — red and blue give no position, and the utilities' green sits at 0%. Fixed up with
            // the green, the blue is placed between red at 0% and green at 0%; fixed up before it, the blue
            // would sit at 100% and the red would still be the left edge.
            var classNames = new[] { "bg-linear-[to_right,#ff0000,#0000ff]", "from-[#00ff00]", "to-[#ffffff]" };

            // Act
            var left = SampleAcross(classNames, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("00FF00FF"));
        }

        [Test]
        public void Given_AListAndUtilitiesTotallingSixteenStops_When_Extracted_Then_TheGradientResolves()
        {
            // Arrange — fourteen listed stops, a from- and a to-.
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 14));

            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right," + stops + "]", "from-[#00ff00]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AListAndUtilitiesTotallingSeventeenStops_When_Extracted_Then_TheClassIsInert()
        {
            // Arrange — fifteen listed stops, a from- and a to-: more than the skew shader holds.
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 15));

            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right," + stops + "]", "from-[#00ff00]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AFromPositionPastTheViaDefault_When_Baked_Then_ViaIsRaisedToIt()
        {
            // Arrange — from at 60% with via at its 50% default: via is raised to 60%, so just past 60% the
            // via→to segment has barely begun. Left at 50%, it would already be a fifth of the way to blue.
            var classNames = new[] { "bg-linear-to-r", "from-[#ff0000]", "from-60%", "via-[#00ff00]", "to-[#0000ff]" };

            // Act
            var pastFrom = SampleAcross(classNames, 0.61f);

            // Assert
            Assert.That(pastFrom.g, Is.GreaterThan(0.95f));
        }

        [Test]
        public void Given_AStopFadingToTransparent_When_Baked_Then_TheMidpointKeepsTheStopsColour()
        {
            // Act — half transparent red: the colour is weighted by alpha, so it stays red.
            var middle = SampleAcross(new[] { "bg-linear-[to_right,#ff0000,transparent]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.GreaterThan(0.98f));
        }

        [Test]
        public void Given_AStopFadingToTransparentInOklab_When_Baked_Then_TheMidpointKeepsTheStopsColour()
        {
            // Act — the same fade interpolated in OKLab, where an unweighted lerp would darken the red.
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_oklab,#ff0000,transparent]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.GreaterThan(0.98f));
        }

        #endregion

        #region Line argument

        [Test]
        public void Given_ALinearListWithNoLineArgument_When_Extracted_Then_ItRunsToBottom()
        {
            // Act — CSS's default direction for linear-gradient() is to bottom, 180deg.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(180f));
        }

        [Test]
        public void Given_ALineEndingInAnInterpolationSpace_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right_in_oklab,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.AngleDeg, spec.Interp), Is.EqualTo((90f, GradientInterp.Oklab)));
        }

        [Test]
        public void Given_ALineLedByAnInterpolationSpace_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[in_oklch_45deg,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.AngleDeg, spec.Interp), Is.EqualTo((45f, GradientInterp.Oklab)));
        }

        [Test]
        public void Given_ALineOfAnInterpolationSpaceAlone_When_Extracted_Then_ItRunsToBottomInThatSpace()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[in_oklab,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.AngleDeg, spec.Interp), Is.EqualTo((180f, GradientInterp.Oklab)));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AnUnknownInterpolationSpaceAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left_in_hsl,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AnInWithNoSpaceAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[in,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AnInterpolationSpaceInsideTheLineAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — in_{space} may lead or end the line argument, not split it.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_in_oklab_left,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_ToRightLine_When_Extracted_Then_TheAngleIs90()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_ACornerLineWithTheHorizontalSideFirst_When_Extracted_Then_TheAngleIsTheCorner()
        {
            // Act — to left bottom is the bottom-left corner, as bg-gradient-to-bl.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_left_bottom,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(225f));
        }

        [Test]
        public void Given_ACornerLineWithTheVerticalSideFirst_When_Extracted_Then_TheAngleIsTheCorner()
        {
            // Act — to top right is the top-right corner, as bg-gradient-to-tr.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_top_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_ALineNamingTwoVerticalSidesAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — to top bottom names no direction, so the list is malformed.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_top_bottom,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_AnAngleLine_When_Extracted_Then_TheAngleIsParsed()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[45deg,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        [Test]
        public void Given_StopListsWithAModifier_When_Extracted_Then_NoneResolves()
        {
            // Arrange — Tailwind drops a bracketed shape that carries a modifier. The from- and to- stops are
            // there so that a radial bracket read as a position, as it was before stop lists, would draw.
            var utilities = new[] { "from-[#ff0000]", "to-[#0000ff]" };

            // Act
            var resolved = (
                StyleGradientClass.TryExtract(utilities.Prepend("bg-linear-[to_right,#00ff00,#0000ff]/oklch").ToArray(), out _),
                StyleGradientClass.TryExtract(utilities.Prepend("bg-radial-[at_top_left,#00ff00,#0000ff]/oklch").ToArray(), out _),
                StyleGradientClass.TryExtract(utilities.Prepend("bg-conic-[from_90deg,#00ff00,#0000ff]/oklch").ToArray(), out _));

            // Assert
            Assert.That(resolved, Is.EqualTo((false, false, false)));
        }

        [Test]
        public void Given_AnAngleInTurns_When_Extracted_Then_ItIsInDegrees()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[0.25turn,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Given_AnAngleInGradians_When_Extracted_Then_ItIsInDegrees()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[100grad,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Given_AnAngleInRadians_When_Extracted_Then_ItIsInDegrees()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[1.5707964rad,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Given_AConicStartInTurns_When_Extracted_Then_ItIsInDegrees()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-[from_0.25turn,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f).Within(1e-3f));
        }

        [Test]
        public void Given_ABareZeroAngle_When_Extracted_Then_ItIsZeroDegrees()
        {
            // Act — CSS allows a unitless zero as an angle.
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[0,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((ok, spec.AngleDeg), Is.EqualTo((true, 0f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_ABareNumberAngleAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — CSS takes no unitless angle but zero, so 45 makes the list malformed.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[45,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AConicPositionNamingAnUnknownToken_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — bogus is no position token, so the list is malformed.
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-45", "from-[#ff0000]", "bg-conic-[at_top_bogus,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AConicPositionOfThreeTokens_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — a position takes at most two tokens here.
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-45", "from-[#ff0000]", "bg-conic-[at_left_top_50%,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        [Test]
        public void Given_ARadialPositionNamingAnUnknownToken_When_Extracted_Then_TheListIsNotRead()
        {
            // Act — bogus makes the bracket no stop list, so it keeps its position reading and the utilities'
            // green is the first stop, not the list's red.
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_top_bogus,#ff0000,#0000ff]", "from-[#00ff00]", "to-[#ffffff]" }, out var spec);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(spec.Stops[0].Color), Is.EqualTo("00FF00FF"));
        }

        [Test]
        public void Given_ToTopRightInAList_When_Extracted_Then_ItIsACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_top_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.True);
        }

        [Test]
        public void Given_ToRightInAList_When_Extracted_Then_ItIsNotACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.False);
        }

        [Test]
        public void Given_BgGradientToTr_When_Extracted_Then_ItIsACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-tr", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.True);
        }

        [Test]
        public void Given_BgLinearToTr_When_Extracted_Then_ItIsACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-tr", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.True);
        }

        [Test]
        public void Given_BgLinear45_When_Extracted_Then_ItIsNotACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-45", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.False);
        }

        [Test]
        public void Given_BgGradientToR_When_Extracted_Then_ItIsNotACorner()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.ToCorner, Is.False);
        }

        [Test]
        public void Given_ARadialListWithAPosition_When_Extracted_Then_ItIsRadialAtThatPosition()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[at_top_left,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Type, spec.CenterX, spec.CenterY), Is.EqualTo((GradientType.Radial, 0f, 0f)));
        }

        [Test]
        public void Given_ARadialListOfStopsAlone_When_Extracted_Then_ItIsRadial()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Type, Is.EqualTo(GradientType.Radial));
        }

        // GREEN_ON_BASE(characterization): the base draws from- and to- for a radial bracket naming a shape keyword.
        [Test]
        public void Given_ARadialBracketLedByAShapeKeyword_When_Extracted_Then_TheUtilityStopsStillDraw()
        {
            // Act — circle is not a line argument the stop list reads, so the bracket keeps its position
            // reading and the from-/to- utilities supply the stops.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_at_center,#ff0000,#0000ff]", "from-[#ff0000]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads a centre from a radial bracket its stop list rejects.
        [Test]
        public void Given_ARadialBracketThatIsNoStopList_When_Extracted_Then_ItsPositionTokensStillPlaceTheCentre()
        {
            // Act — notacolor makes this no stop list. Read as a position, top places the centre's y, and
            // the token "left,#ff0000,notacolor" is ignored, as it always was.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_top_left,#ff0000,notacolor]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That((ok, spec.CenterX, spec.CenterY), Is.EqualTo((true, 0.5f, 0f)));
        }

        [Test]
        public void Given_AListPastedWithASpaceAfterEachComma_When_Baked_Then_ItReadsAsWithout()
        {
            // Act — each comma is followed by _, as a CSS list with a space after each comma becomes.
            var left = SampleAcross(new[] { "bg-linear-[to_right,_#ff0000_0%,_#0000ff_100%_]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("FF0000FF"));
        }

        [Test]
        public void Given_AConicListWithStartAndPosition_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-[from_90deg_at_25%_75%,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Type, spec.AngleDeg, spec.CenterX, spec.CenterY),
                Is.EqualTo((GradientType.Conic, 90f, 0.25f, 0.75f)));
        }

        [Test]
        public void Given_AConicListWithAStartAlone_When_Extracted_Then_TheStartIsRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-[from_45deg,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        [Test]
        public void Given_AConicListWithAPositionAlone_When_Extracted_Then_ThePositionIsRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-[at_left,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Type, spec.CenterX), Is.EqualTo((GradientType.Conic, 0f)));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AConicListWithAnUnreadableStartAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-45", "from-[#ff0000]", "bg-conic-[from_bogus,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AConicListWithAnUnreadableTailAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — bogus is neither an at_ position nor an in_ interpolation space.
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-45", "from-[#ff0000]", "bg-conic-[from_90deg_bogus,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        // GREEN_ON_BASE(refactor): a bracket holding no comma keeps the position grammar it had before.
        [Test]
        public void Given_ARadialPositionBracketWithoutStops_When_Extracted_Then_TheUtilityStopsDrawAtThatPosition()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_top_left]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That((ok, spec.CenterX, spec.CenterY), Is.EqualTo((true, 0f, 0f)));
        }

        #endregion

        #region Stop count and malformed lists

        [Test]
        public void Given_SixteenStops_When_Extracted_Then_TheGradientResolves()
        {
            // Arrange
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 16));

            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right," + stops + "]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_SeventeenStopsAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Arrange
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 17));

            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left," + stops + "]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AListWithAnUnknownColourAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#00ff00,notacolor,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AListOfOneStopAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#00ff00]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AStopWithAnUnreadablePositionAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#00ff00_bogus,#0000ff,#ff0000]" },
                out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AStopWithAnUnreadableSecondPositionAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#00ff00_10%_bogus,#0000ff,#ff0000]" },
                out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AStopWithThreePositionsAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — a colour stop takes at most two positions.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#00ff00_10%_20%_30%,#0000ff,#ff0000]" },
                out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        #endregion

        #region Cache key

        [Test]
        public void Given_TheSameFiveStopListTwice_When_Compared_Then_TheyAreEqualAndNotTheEmptySpec()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { HighlightList }, out var a);
            StyleGradientClass.TryExtract(new[] { HighlightList }, out var b);

            // Act
            var reading = (a.Equals(b), a.Equals(default(GradientSpec)));

            // Assert
            Assert.That(reading, Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ACornerAndTheAngleOfThatCorner_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange — to top right and 45deg run the same in a square box and differ in a box that is not square.
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-tr", "from-[#ff0000]", "to-[#0000ff]" }, out var corner);
            StyleGradientClass.TryExtract(new[] { "bg-linear-45", "from-[#ff0000]", "to-[#0000ff]" }, out var angle);

            // Act
            var equal = corner.Equals(angle);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_FiveStopListsDifferingInTheFourthColour_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { HighlightList }, out var a);
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#ff0000_55%,#0f172a_100%]" }, out var b);

            // Act
            var equal = a.Equals(b);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_FiveStopListsDifferingInTheSecondPosition_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { HighlightList }, out var a);
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[90deg,#0f172a_0%,#0f172a_40%,#ffffff_50%,#0f172a_55%,#0f172a_100%]" }, out var b);

            // Act
            var equal = a.Equals(b);

            // Assert
            Assert.That(equal, Is.False);
        }

        #endregion
    }
}

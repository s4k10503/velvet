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
        public void Given_AListAndUtilitiesTotallingTheCap_When_Extracted_Then_TheGradientResolves()
        {
            // Arrange — sixty-two listed stops, a from- and a to-: sixty-four in all.
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 62));

            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right," + stops + "]", "from-[#00ff00]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AListAndUtilitiesPastTheCap_When_Extracted_Then_TheClassIsInert()
        {
            // Arrange — sixty-three listed stops, a from- and a to-: sixty-five in all, more than the skew shader holds.
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 63));

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
            Assert.That((spec.AngleDeg, spec.Interp), Is.EqualTo((45f, GradientInterp.Oklch)));
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
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left_in_nonsense,#00ff00,#0000ff]" }, out var spec);

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
        public void Given_ARadialPositionNamingAnUnknownToken_When_Extracted_Then_TheClassIsInert()
        {
            // Act — bogus is no position token, which makes the whole gradient invalid in CSS.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_at_bogus,#ff0000,#0000ff]", "from-[#00ff00]", "to-[#ffffff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
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

        [Test]
        public void Given_ARadialListLedByACircle_When_Extracted_Then_ItIsACircleAtTheCentre()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_at_center,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.Radial.Extent, spec.CenterX), Is.EqualTo((true, RadialExtent.FarthestCorner, 0.5f)));
        }

        [Test]
        public void Given_AnEllipseWithAnExtentKeyword_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_closest-side,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.Radial.Extent), Is.EqualTo((false, RadialExtent.ClosestSide)));
        }

        [Test]
        public void Given_AnExtentKeywordBeforeTheShape_When_Extracted_Then_BothAreRead()
        {
            // Act — CSS takes the shape and the size in either order.
            StyleGradientClass.TryExtract(new[] { "bg-radial-[closest-corner_circle,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.Radial.Extent), Is.EqualTo((true, RadialExtent.ClosestCorner)));
        }

        [Test]
        public void Given_ACircleWithALength_When_Extracted_Then_ItsRadiusIsThatLength()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[circle_40px_at_top_left,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.Radial.Extent, spec.Radial.X, spec.CenterX),
                Is.EqualTo((true, RadialExtent.Explicit, 40f, 0f)));
        }

        [Test]
        public void Given_ALengthAlone_When_Extracted_Then_ItIsACircleOfThatRadius()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[40px,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.Radial.X), Is.EqualTo((true, 40f)));
        }

        [Test]
        public void Given_AnEllipseWithAPercentageAndALength_When_Extracted_Then_BothRadiiAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[ellipse_50%_20px,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.X, spec.Radial.XPercent, spec.Radial.Y, spec.Radial.YPercent),
                Is.EqualTo((0.5f, true, 20f, false)));
        }

        [Test]
        public void Given_AnEllipseWithOneLength_When_Extracted_Then_TheClassIsInert()
        {
            // Act — an ellipse takes two radii.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[ellipse_40px,#ff0000,#0000ff]", "from-[#00ff00]", "to-[#ffffff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ACircleWithAPercentage_When_Extracted_Then_TheClassIsInert()
        {
            // Act — a circle's radius is a length, not a percentage.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_50%,#ff0000,#0000ff]", "from-[#00ff00]", "to-[#ffffff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ARadialBracketNamingACircleWithoutStops_When_Extracted_Then_TheUtilityStopsDrawOnACircle()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-radial-[circle_at_top]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Radial.Circle, spec.CenterY), Is.EqualTo((true, 0f)));
        }

        [Test]
        public void Given_ASlashInsideTheBrackets_When_TheModifierIsFound_Then_ThereIsNone()
        {
            // Act
            var slash = StyleGradientClass.ModifierSlash("bg-radial-[at_top_a/b]");

            // Assert
            Assert.That(slash, Is.EqualTo(-1));
        }

        [Test]
        public void Given_ASlashAfterTheBrackets_When_TheModifierIsFound_Then_ItIsThatSlash()
        {
            // Act
            var slash = StyleGradientClass.ModifierSlash("bg-linear-[to_right,red,blue]/oklch");

            // Assert
            Assert.That(slash, Is.EqualTo(29));
        }

        [Test]
        public void Given_ASlashAfterABarePlainActivator_When_TheModifierIsFound_Then_ItIsThatSlash()
        {
            // Act
            var slash = StyleGradientClass.ModifierSlash("bg-radial/oklab");

            // Assert
            Assert.That(slash, Is.EqualTo(9));
        }

        [Test]
        public void Given_ARadialCentreOutsideTheBox_When_Extracted_Then_ItIsKept()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[at_150%_-20%]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.CenterX, spec.CenterY), Is.EqualTo((1.5f, -0.2f)));
        }

        [Test]
        public void Given_AConicCentreOutsideTheBox_When_Extracted_Then_ItIsKept()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-[at_-20%_50%,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.CenterX, Is.EqualTo(-0.2f));
        }

        [Test]
        public void Given_ABracketedPercentageInAPosition_When_Extracted_Then_TheListIsRejected()
        {
            // Act — only a bare percentage is a position token, so the centre never silently stays at the middle.
            var ok = StyleGradientClass.TryExtract(new[] { "bg-conic-[at_[25%],#ff0000,#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_AViaColourAlone_When_Extracted_Then_ItIsBetweenTwoTransparentEnds()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "via-[#00ff00]" }, out var spec);

            // Assert
            Assert.That((ok, spec.Stops?.Length ?? 0), Is.EqualTo((true, 3)));
        }

        [Test]
        public void Given_AViaColourAlone_When_Extracted_Then_BothEndsAreTransparent()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "via-[#00ff00]" }, out var spec);

            // Assert
            Assert.That((spec.Stops[0].Color.a, spec.Stops[spec.Stops.Length - 1].Color.a), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public void Given_AListOfSixteenStopsBesideFromViaAndTo_When_Extracted_Then_AllNineteenStopsResolve()
        {
            // Arrange
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 16));

            // Act
            StyleGradientClass.TryExtract(new[]
            {
                "bg-linear-[to_right," + stops + "]", "from-[#00ff00]", "via-[#0000ff]", "to-[#ffffff]",
            }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length ?? 0, Is.EqualTo(19));
        }

        [Test]
        public void Given_AViaColourAloneBesideAList_When_Extracted_Then_ItsStopsFollowTheList()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000,#0000ff]", "via-[#00ff00]" }, out var spec);

            // Assert
            Assert.That(spec.Stops?.Length ?? 0, Is.EqualTo(5));
        }

        [Test]
        public void Given_AViaColourAlone_When_Baked_Then_TheStartIsItsTransparentVersion()
        {
            // Act
            var left = SampleAcross(new[] { "bg-linear-to-r", "via-[#00ff00]" }, 0f);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("00FF0000"));
        }

        [Test]
        public void Given_StopPositionsBeyondTheBox_When_Baked_Then_TheLineRunsOnPastIt()
        {
            // Act — black at -50% and white at 150%: the box shows the middle of that line, so its left edge
            // is a quarter of the way along it.
            var left = SampleAcross(new[] { "bg-linear-[to_right,#000000_-50%,#ffffff_150%]" }, 0f);

            // Assert
            Assert.That(left.r, Is.EqualTo(0.25f).Within(0.01f));
        }

        [Test]
        public void Given_FromAndToPositionsBeyondTheBox_When_Baked_Then_TheLineRunsOnPastIt()
        {
            // Act — in sRGB, so the sample is the position along the line rather than an OKLab lightness.
            var left = SampleAcross(
                new[] { "bg-linear-to-r/srgb", "from-[#000000]", "from-[-50%]", "to-[#ffffff]", "to-[150%]" }, 0f);

            // Assert
            Assert.That(left.r, Is.EqualTo(0.25f).Within(0.01f));
        }

        [Test]
        public void Given_ABareNegativePositionAfterAValidOne_When_Extracted_Then_TheValidPositionStands()
        {
            // Arrange — Tailwind's bare position is a non-negative integer percentage; a negative one is no utility.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-60%", "from--50%", "to-[#0000ff]" }, out var spec);

            // Act
            var position = spec.Stops[0].Position;

            // Assert
            Assert.That(position, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void Given_ABareFractionalPositionAfterAValidOne_When_Extracted_Then_TheValidPositionStands()
        {
            // Arrange — Tailwind's bare position takes whole percentages only.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-60%", "from-12.5%", "to-[#0000ff]" }, out var spec);

            // Act
            var position = spec.Stops[0].Position;

            // Assert
            Assert.That(position, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void Given_ABracketedPixelFromPosition_When_Extracted_Then_TheStopSitsAtThatLength()
        {
            // Arrange — Tailwind's -position utilities take a length as well as a percentage.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[20px]", "to-[#0000ff]" }, out var spec);

            // Act
            var px = spec.Stops[0].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(20f).Within(1e-4f));
        }

        [Test]
        public void Given_ABracketedPixelViaPosition_When_Extracted_Then_TheStopSitsAtThatLength()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "via-[#00ff00]", "via-[30px]", "to-[#0000ff]" }, out var spec);

            // Act
            var px = spec.Stops[1].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(30f).Within(1e-4f));
        }

        [Test]
        public void Given_ABracketedPixelToPosition_When_Extracted_Then_TheStopSitsAtThatLength()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "to-[#0000ff]", "to-[40px]" }, out var spec);

            // Act
            var px = spec.Stops[1].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(40f).Within(1e-4f));
        }

        // GREEN_ON_BASE(characterization): the base never sets a pixel position, so none is left to drop.
        [Test]
        public void Given_APercentageAfterABracketedPixelPosition_When_Extracted_Then_ThePixelLengthIsDropped()
        {
            // Arrange — the later utility wins, so the stop is positioned by the percentage alone.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[20px]", "from-60%", "to-[#0000ff]" }, out var spec);

            // Act
            var px = spec.Stops[0].PositionPx;

            // Assert
            Assert.That(float.IsNaN(px), Is.True);
        }

        [Test]
        public void Given_ARemFromPosition_When_Extracted_Then_TheStopSitsAtSixteenPixelsARem()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[2rem]", "to-[#0000ff]" }, out var spec);

            // Act
            var px = spec.Stops[0].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(32f).Within(1e-4f));
        }

        [Test]
        public void Given_ANegativePixelFromPosition_When_Extracted_Then_TheStopSitsBeforeTheStart()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[-10px]", "to-[#0000ff]" }, out var spec);

            // Act
            var px = spec.Stops[0].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(-10f).Within(1e-4f));
        }

        [Test]
        public void Given_ARemListPosition_When_Extracted_Then_TheStopSitsAtSixteenPixelsARem()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right,#000000_0px,#ffffff_2rem]" }, out var spec);

            // Act
            var px = spec.Stops[1].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(32f).Within(1e-4f));
        }

        [Test]
        public void Given_ANegativePixelListPosition_When_Extracted_Then_TheStopSitsBeforeTheStart()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right,#000000_-20px,#ffffff_100px]" }, out var spec);

            // Act
            var px = spec.Stops[0].PositionPx;

            // Assert
            Assert.That(px, Is.EqualTo(-20f).Within(1e-4f));
        }

        [Test]
        public void Given_ANegativePixelStop_When_BakedForAKnownWidth_Then_TheLineRunsOnBeforeTheBox()
        {
            // Act — black at -100px and white at 100px on a 200px line: the left edge is half way along it.
            var texel = PixelAt(new[] { "bg-linear-[to_right,#000000_-100px,#ffffff_100px]" }, 2f, 200f, 0, 64);

            // Assert
            Assert.That(texel.r, Is.EqualTo(0.5f).Within(0.02f));
        }

        // GREEN_ON_BASE(characterization): the base already reads a bracketed position of any sign or fraction.
        [Test]
        public void Given_ABracketedFractionalPosition_When_Extracted_Then_ItIsTheStopsPosition()
        {
            // Arrange
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "from-[12.5%]", "to-[#0000ff]" }, out var spec);

            // Act
            var position = spec.Stops[0].Position;

            // Assert
            Assert.That(position, Is.EqualTo(0.125f).Within(1e-4f));
        }

        [Test]
        public void Given_ListsWhosePositionsDifferBeyondTheBox_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,#ffffff_150%]" }, out var a);
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,#ffffff_200%]" }, out var b);

            // Act
            var equal = a.Equals(b);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_ARadialListWithAnUnreadableStop_When_Extracted_Then_TheClassIsInert()
        {
            // Act — notacolor makes the list invalid, and CSS drops an invalid gradient whole.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_top_left,#ff0000,notacolor]", "from-[#ff0000]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_ARadialBracketThatReadsAsNothing_When_Extracted_Then_TheClassIsInert()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-radial-[bogus]", "from-[#ff0000]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
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
        public void Given_SixtyFourStops_When_Extracted_Then_TheGradientResolves()
        {
            // Arrange
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 64));

            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right," + stops + "]" }, out _);

            // Assert
            Assert.That(ok, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_SixtyFiveStopsAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Arrange
            var stops = string.Join(",", Enumerable.Repeat("#ff0000", 65));

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

        #region Positions, hints and angles

        private static Color PixelAt(string[] classNames, float aspect, float widthPx, int x, int y)
        {
            StyleGradientClass.TryExtract(classNames, out var spec);
            var tex = GradientBackground.Bake(spec, aspect, widthPx);
            var pixel = tex.GetPixel(x, y);
            Object.DestroyImmediate(tex);
            return pixel;
        }

        [Test]
        public void Given_AConicWithStopsAtAngles_When_Baked_Then_TheAngleIsAFractionOfTheTurn()
        {
            // Act — straight right of the centre is 90°, halfway to a stop at 180°.
            var right = PixelAt(new[] { "bg-conic-[#000000_0deg,#ffffff_180deg]" }, 1f, 0f, 127, 64);

            // Assert
            Assert.That(right.r, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void Given_AConicWithAStopAtAngles_When_BakedPastIt_Then_TheLastColourHolds()
        {
            // Act — 270° is past the last stop at 180°.
            var left = PixelAt(new[] { "bg-conic-[#000000_0deg,#ffffff_180deg]" }, 1f, 0f, 0, 64);

            // Assert
            Assert.That(ColorUtility.ToHtmlStringRGBA(left), Is.EqualTo("FFFFFFFF"));
        }

        [Test]
        public void Given_AConicStopInTurns_When_Extracted_Then_ItIsAFractionOfTheTurn()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-[#000000,#ffffff_0.25turn,#ff0000]" }, out var spec);

            // Assert
            Assert.That(spec.Stops[1].Position, Is.EqualTo(0.25f).Within(1e-4f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_ALinearStopAtAnAngleAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — only a conic positions its stops by angle.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#000000_0deg,#ffffff_90deg]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_AColourHintBetweenTwoStops_When_Extracted_Then_ItIsOnTheStopBeforeAndIsNoStop()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,25%,#ffffff]" }, out var spec);

            // Assert
            Assert.That((spec.Stops.Length, spec.Stops[0].Hint), Is.EqualTo((2, 0.25f)));
        }

        [Test]
        public void Given_AColourHint_When_Baked_Then_TheMidMixIsAtTheHint()
        {
            // Act — texel 32 is at t = 0.252: the hint at 25% puts the half-way mix there.
            var hint = SampleAcross(new[] { "bg-linear-[to_right,#000000,25%,#ffffff]" }, 0.252f);

            // Assert
            Assert.That(hint.r, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void Given_AColourHintAtTheFirstStop_When_Baked_Then_TheLaterColourStartsAtOnce()
        {
            // Act
            var early = SampleAcross(new[] { "bg-linear-[to_right,#000000_0%,0%,#ffffff]" }, 0.1f);

            // Assert
            Assert.That(early.r, Is.EqualTo(1f).Within(0.01f));
        }

        [Test]
        public void Given_AColourHintAtTheNextStop_When_Baked_Then_TheEarlierColourHoldsToIt()
        {
            // Act
            var late = SampleAcross(new[] { "bg-linear-[to_right,#000000,100%,#ffffff]" }, 0.9f);

            // Assert
            Assert.That(late.r, Is.EqualTo(0f).Within(0.01f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AColourHintFirstInTheList_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — a hint sits between two stops.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[25%,#000000,#ffffff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AColourHintLastInTheList_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#000000,#ffffff,25%]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_TwoColourHintsInARow_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left,#000000,25%,50%,#ffffff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_AHintBehindTheStopBefore_When_Extracted_Then_ItIsRaisedToIt()
        {
            // Act — the hint at 10% follows a stop at 40%.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_40%,10%,#ffffff]" }, out var spec);

            // Assert
            Assert.That(spec.Stops[0].Hint, Is.EqualTo(0.4f));
        }

        [Test]
        public void Given_StopsInPixels_When_BakedForAKnownWidth_Then_TheyAreFractionsOfTheLine()
        {
            // Act — a 200px line: 100px is half way, so texel 32 (t = 0.252) is half way to it.
            var texel = PixelAt(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_100px]" }, 2f, 200f, 32, 64);

            // Assert
            Assert.That(texel.r, Is.EqualTo(0.504f).Within(0.02f));
        }

        [Test]
        public void Given_APixelStopBesideAPercentStop_When_Baked_Then_TheyAreFixedUpTogether()
        {
            // Act — white at 50px (25% of 200px) follows red at 50%, so it is raised to 50% and texel 76
            // (t = 0.598) is a fifth of the way from white to blue. Left at 25% it would be nearly half way.
            var texel = PixelAt(
                new[] { "bg-linear-[to_right,#000000_0%,#ff0000_50%,#ffffff_50px,#0000ff]" }, 2f, 200f, 76, 64);

            // Assert
            Assert.That(texel.r, Is.EqualTo(0.8f).Within(0.03f));
        }

        [Test]
        public void Given_AHintInPixels_When_BakedForAKnownWidth_Then_TheMidMixIsAtIt()
        {
            // Act — 50px of a 200px line is 25%; texel 32 sits at 0.252.
            var texel = PixelAt(new[] { "bg-linear-[to_right,#000000,50px,#ffffff]" }, 2f, 200f, 32, 64);

            // Assert
            Assert.That(texel.r, Is.EqualTo(0.5f).Within(0.02f));
        }

        [Test]
        public void Given_RadialStopsInPixels_When_Baked_Then_TheyAreFractionsOfTheRay()
        {
            // Act — a circle of 100px radius, a stop at 50px: texel 80 is 26px right of the centre.
            var texel = PixelAt(new[] { "bg-radial-[circle_100px,#000000_0px,#ffffff_50px]" }, 2f, 200f, 80, 64);

            // Assert
            Assert.That(texel.r, Is.EqualTo(0.52f).Within(0.02f));
        }

        [Test]
        public void Given_AStopInPixels_When_Asked_Then_TheGradientDependsOnTheBoxSize()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000_0px,#ffffff_100px]" }, out var spec);

            // Assert
            Assert.That(GradientBackground.NeedsAbsoluteSize(spec), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AConicStopInPixels_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — a conic has no length to measure a pixel by.
            StyleGradientClass.TryExtract(
                new[] { "bg-conic-45", "from-[#ff0000]", "bg-conic-[#000000_0px,#ffffff_100px]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        [Test]
        public void Given_ListsDifferingInAHint_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,25%,#ffffff]" }, out var a);
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#000000,75%,#ffffff]" }, out var b);

            // Act
            var equal = a.Equals(b);

            // Assert
            Assert.That(equal, Is.False);
        }

        #endregion

        #region Interpolation spaces

        [Test]
        public void Given_BgLinearToR_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act — Tailwind's bg-linear-to-r is linear-gradient(to right in oklab, …).
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_BgLinearAngle_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-45", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_BgGradientToTheLegacyAlias_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act — the v3 spelling reads as bg-linear-to-r.
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_BgRadial_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_BgConic_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_BgConicWithAStart_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-conic-45", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_ANegativeBgLinearAngle_When_Extracted_Then_TheSpaceIsOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "-bg-linear-30", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_AnExplicitSrgbModifier_When_Extracted_Then_TheSpaceIsSrgb()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/srgb", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Srgb));
        }

        [Test]
        public void Given_AnAngleInABracket_When_Extracted_Then_TheSpaceIsSrgb()
        {
            // Act — an arbitrary value names no interpolation, so CSS reads legacy colours in sRGB.
            StyleGradientClass.TryExtract(new[] { "bg-linear-[45deg]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Srgb));
        }

        [Test]
        public void Given_ARadialPositionInABracket_When_Extracted_Then_TheSpaceIsSrgb()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-radial-[at_top]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Srgb));
        }

        [Test]
        public void Given_AStopListWithNoSpace_When_Extracted_Then_TheSpaceIsSrgb()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-[to_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Srgb));
        }

        [Test]
        public void Given_BlackToWhiteWithNoModifier_When_Baked_Then_TheMidpointIsTheOklabMean()
        {
            // Act — half way in OKLab lightness is 0.39 in sRGB; half way in sRGB would be 0.50.
            var middle = SampleAcross(new[] { "bg-linear-to-r", "from-[#000000]", "to-[#ffffff]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.EqualTo(0.393f).Within(0.02f));
        }

        [Test]
        public void Given_AnOklchModifier_When_Extracted_Then_TheSpaceIsOklchNotOklab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/oklch", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklch));
        }

        [Test]
        public void Given_ALinearLightModifier_When_Extracted_Then_TheSpaceIsSrgbLinear()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/srgb-linear", "from-[#000000]", "to-[#ffffff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.SrgbLinear));
        }

        [Test]
        public void Given_AHslModifier_When_Extracted_Then_TheSpaceIsHsl()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/hsl", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Hsl));
        }

        [Test]
        public void Given_ALabModifier_When_Extracted_Then_TheSpaceIsLab()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/lab", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Lab));
        }

        [Test]
        public void Given_ALchModifier_When_Extracted_Then_TheSpaceIsLch()
        {
            // Act
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/lch", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Lch));
        }

        [Test]
        public void Given_AHueMethodModifier_When_Extracted_Then_ItIsOklchWithThatMethod()
        {
            // Act — Tailwind reads /longer as in oklch longer hue.
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/longer", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.Interp, spec.Hue), Is.EqualTo((GradientInterp.Oklch, HueMethod.Longer)));
        }

        [Test]
        public void Given_ASpaceAndHueMethodInAList_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[to_right_in_hsl_longer_hue,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.AngleDeg, spec.Interp, spec.Hue), Is.EqualTo((90f, GradientInterp.Hsl, HueMethod.Longer)));
        }

        [Test]
        public void Given_ASpaceAndHueMethodLeadingAList_When_Extracted_Then_BothAreRead()
        {
            // Act
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-[in_oklch_decreasing_hue_to_right,#ff0000,#0000ff]" }, out var spec);

            // Assert
            Assert.That((spec.AngleDeg, spec.Interp, spec.Hue), Is.EqualTo((90f, GradientInterp.Oklch, HueMethod.Decreasing)));
        }

        // GREEN_ON_BASE(characterization): the base reads no comma in a linear or conic bracket, so this one is inert there too.
        [Test]
        public void Given_AHueMethodOnARectangularSpaceAfterAPlainActivator_When_Extracted_Then_ThePlainActivatorStillWins()
        {
            // Act — a hue method belongs to a polar space.
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "bg-linear-[to_left_in_oklab_longer_hue,#00ff00,#0000ff]" }, out var spec);

            // Assert
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_ListsOfAPolarSpaceDifferingInTheHueMethod_When_Compared_Then_TheyAreNotEqual()
        {
            // Arrange
            StyleGradientClass.TryExtract(new[] { "bg-linear-[in_hsl,#ff0000,#0000ff]" }, out var shorter);
            StyleGradientClass.TryExtract(new[] { "bg-linear-[in_hsl_longer_hue,#ff0000,#0000ff]" }, out var longer);

            // Act
            var equal = shorter.Equals(longer);

            // Assert
            Assert.That(equal, Is.False);
        }

        [Test]
        public void Given_ListsOfARectangularSpaceDifferingInTheHueMethod_When_Compared_Then_TheyAreEqual()
        {
            // Arrange — the method only reaches a polar space, so a modifier naming one on oklab changes nothing.
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/oklab", "from-[#ff0000]", "to-[#0000ff]" }, out var a);
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/oklab", "from-[#ff0000]", "to-[#0000ff]" }, out var b);

            // Act
            var equal = a.Equals(b);

            // Assert
            Assert.That(equal, Is.True);
        }

        [Test]
        public void Given_RedToBlueInOklch_When_Baked_Then_TheMidpointFollowsTheHueArc()
        {
            // Act — clipped to the gamut, the arc through magenta stays bright red.
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_oklch,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.EqualTo(0.726f).Within(0.03f));
        }

        [Test]
        public void Given_RedToBlueInLch_When_Baked_Then_TheMidpointFollowsTheHueArc()
        {
            // Act
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_lch,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.EqualTo(0.958f).Within(0.03f));
        }

        [Test]
        public void Given_RedToBlueInLab_When_Baked_Then_TheMidpointIsTheMeanInLab()
        {
            // Act
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_lab,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.EqualTo(0.754f).Within(0.03f));
        }

        [Test]
        public void Given_BlackToWhiteInLinearLight_When_Baked_Then_TheMidpointIsBrighterThanInSrgb()
        {
            // Act
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_srgb-linear,#000000,#ffffff]" }, 0.5f);

            // Assert
            Assert.That(middle.r, Is.EqualTo(0.738f).Within(0.02f));
        }

        [Test]
        public void Given_RedToBlueInHsl_When_Baked_Then_TheShorterArcPassesThroughMagenta()
        {
            // Act
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_hsl,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.r - middle.g, Is.GreaterThan(0.9f));
        }

        [Test]
        public void Given_RedToBlueInHslTheLongWay_When_Baked_Then_TheMidpointIsGreen()
        {
            // Act
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_hsl_longer_hue,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.g, Is.GreaterThan(0.95f));
        }

        [Test]
        public void Given_RedToBlueInHslByIncreasingHue_When_Baked_Then_TheMidpointIsGreen()
        {
            // Act — red is hue 0 and blue 240: increasing goes through yellow and green.
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_hsl_increasing_hue,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.g, Is.GreaterThan(0.95f));
        }

        [Test]
        public void Given_RedToBlueInHslByDecreasingHue_When_Baked_Then_TheMidpointIsMagenta()
        {
            // Act — decreasing goes from 360 down to 240, through magenta.
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_hsl_decreasing_hue,#ff0000,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(middle.g, Is.LessThan(0.05f));
        }

        [Test]
        public void Given_GreyToBlueInHsl_When_Baked_Then_TheGreyTakesTheBluesHue()
        {
            // Act — grey has no hue, so the mix is a desaturated blue, not a trip through magenta.
            var middle = SampleAcross(new[] { "bg-linear-[to_right_in_hsl,#808080,#0000ff]" }, 0.5f);

            // Assert
            Assert.That(Mathf.Abs(middle.r - middle.g), Is.LessThan(0.02f));
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

using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the gradient utilities. USS has no <c>linear-gradient</c>, so Velvet bakes the
    /// <c>bg-gradient-to-*</c> direction + <c>from-/via-/to-</c> stops into a texture (<see cref="GradientBackground"/>)
    /// set as the element's background-image — UI Toolkit clips it to the border-radius. A lone <c>from-*</c>
    /// with no <c>bg-gradient-to-*</c> is inert. Stop colors come from <c>from-{palette}</c>
    /// or <c>from-[#hex]</c>. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class GradientClassParityTests
    {
        #region Parse

        [Test]
        public void Given_BgGradientToR_When_Extracted_Then_DirectionIsRight()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True, "Precondition: recognized as a gradient");
            Assert.That(spec.AngleDeg, Is.EqualTo(90f)); // bg-gradient-to-r → CSS 90°
        }

        [Test]
        public void Given_FromArbitraryHex_When_Extracted_Then_FromColorParsed()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.From, Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void Given_PaletteStops_When_Extracted_Then_ToColorResolves()
        {
            // Act — named palette stops (from-white / to-black) resolve through VelvetPalette.
            var ok = StyleGradientClass.TryExtract(new[] { "bg-gradient-to-b", "from-white", "to-black" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.To, Is.EqualTo(Color.black));
        }

        [Test]
        public void Given_ViaStop_When_Extracted_Then_HasViaIsTrue()
        {
            // Act
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-r", "from-[#ff0000]", "via-[#00ff00]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.HasVia, Is.True);
        }

        [Test]
        public void Given_LoneFromWithoutDirection_When_Extracted_Then_Inert()
        {
            // Act — a from-* stop with no bg-gradient-to-* activator is inert.
            var ok = StyleGradientClass.TryExtract(new[] { "from-[#ff0000]", "to-[#0000ff]" }, out _);

            // Assert
            Assert.That(ok, Is.False);
        }

        [Test]
        public void Given_TwoDirections_When_Extracted_Then_LastWins()
        {
            // Act — cascade: the later direction class wins.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-r", "bg-gradient-to-l", "from-[#ff0000]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.AngleDeg, Is.EqualTo(270f)); // bg-gradient-to-l → CSS 270°
        }

        [Test]
        public void Given_NoToStop_When_Extracted_Then_ToDefaultsTransparent()
        {
            // Act — from with no to: to defaults to the transparent version of from (the default behavior).
            var ok = StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.To.a, Is.EqualTo(0f));
        }

        [Test]
        public void Given_NoGradientClasses_When_Gated_Then_HasGradientIsFalse()
        {
            // Act
            var has = StyleGradientClass.HasGradientClass(new[] { "bg-[#ffffff]", "rounded-[12px]" });

            // Assert
            Assert.That(has, Is.False);
        }

        [Test]
        public void Given_SpecsWithColorsQuantizingEqual_When_Compared_Then_HashCodesMatch()
        {
            // Arrange — two specs whose stop colors differ by less than Color's == epsilon but quantize to
            // the same 8-bit value (the cache's texture precision). They must compare Equal AND hash equal,
            // or the Dictionary<GradientSpec,…> cache would bucket equal keys apart and double-bake.
            var a = new GradientSpec(GradientType.Linear, 90f, 0.5f, 0.5f, GradientInterp.Srgb,
                new Color(1f, 0f, 0f), new Color(0f, 0f, 1f), false, default, 0f, 0.5f, 1f);
            var b = new GradientSpec(GradientType.Linear, 90f, 0.5f, 0.5f, GradientInterp.Srgb,
                new Color(0.999f, 0.0008f, 0f), new Color(0f, 0.0008f, 1f), false, default, 0f, 0.5f, 1f);
            Assume.That(a.Equals(b), Is.True, "Precondition: colors quantize equal so the specs are equal");

            // Act + Assert — Equals/GetHashCode contract holds (equal objects have equal hash codes).
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        #endregion

        #region Bake

        [Test]
        public void Given_HorizontalRedToBlue_When_Baked_Then_LeftEdgeIsFrom()
        {
            // Arrange — a left→right red→blue gradient.
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Act — bake and sample the left edge (t=0 → from).
            var tex = GradientBackground.Bake(spec);
            var left = tex.GetPixel(0, tex.height / 2);
            Object.DestroyImmediate(tex);

            // Assert — the left edge is the from color (red).
            Assert.That(left.r > 0.9f && left.b < 0.1f, Is.True);
        }

        [Test]
        public void Given_VerticalRedToBlue_When_Baked_Then_TopEdgeIsFrom()
        {
            // Arrange — a top→bottom red→blue gradient (to-b: from at top).
            StyleGradientClass.TryExtract(new[] { "bg-gradient-to-b", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Act — sample the TOP texture row (GetPixel y = height-1 is the top, which UITK draws at the top).
            var tex = GradientBackground.Bake(spec);
            var top = tex.GetPixel(tex.width / 2, tex.height - 1);
            Object.DestroyImmediate(tex);

            // Assert — the top edge is the from color (red).
            Assert.That(top.r > 0.9f && top.b < 0.1f, Is.True);
        }

        #endregion

        #region Arbitrary angle + stop positions (CSS Images L3)

        [Test]
        public void Given_BgLinearAngle_When_Extracted_Then_TheAngleIsParsed()
        {
            // Act — bg-linear-45 sets an arbitrary 45° axis (CSS linear-gradient(45deg)).
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-45", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.AngleDeg, Is.EqualTo(45f));
        }

        [Test]
        public void Given_NegativeBgLinearArbitrary_When_Extracted_Then_TheAngleIsNegative()
        {
            // Act — -bg-linear-[30deg] is a negative arbitrary angle.
            var ok = StyleGradientClass.TryExtract(
                new[] { "-bg-linear-[30deg]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.AngleDeg, Is.EqualTo(-30f));
        }

        [Test]
        public void Given_BgLinearToAlias_When_Extracted_Then_MapsToTheDirectionAngle()
        {
            // Act — bg-linear-to-r is the alias of bg-gradient-to-r (→ CSS 90°).
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.AngleDeg, Is.EqualTo(90f));
        }

        [Test]
        public void Given_FromPercent_When_Extracted_Then_TheStopPositionIsSet()
        {
            // Act — from-25% is a POSITION; from-[#hex] is the colour (independent utilities).
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-b", "from-[#ff0000]", "from-25%", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.FromPos, Is.EqualTo(0.25f));
        }

        [Test]
        public void Given_FromPercent_When_Extracted_Then_TheFromColorStillResolves()
        {
            // Act — the percentage sets only the position; the colour token must still resolve.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-b", "from-[#ff0000]", "from-25%", "to-[#0000ff]" }, out var spec);

            // Assert
            Assume.That(ok, Is.True);
            Assert.That(spec.From, Is.EqualTo(new Color(1f, 0f, 0f, 1f)));
        }

        [Test]
        public void Given_BgLinear90_When_Baked_Then_LeftEdgeIsFrom()
        {
            // Arrange — bg-linear-90 is equivalent to bg-gradient-to-r (the magic-corner axis), so `from` is
            // at the left edge.
            StyleGradientClass.TryExtract(new[] { "bg-linear-90", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Act
            var tex = GradientBackground.Bake(spec);
            var left = tex.GetPixel(0, tex.height / 2);
            Object.DestroyImmediate(tex);

            // Assert — the left edge is the from color (red), confirming 90° == to-right.
            Assert.That(left.r > 0.9f && left.b < 0.1f, Is.True);
        }

        [Test]
        public void Given_FromPositionAtHalf_When_Baked_Then_TheUpperQuarterIsStillFlatFrom()
        {
            // Arrange — to-b red→blue with from-50%: everything before t=0.5 is flat `from` (red). At the box
            // upper quarter (t≈0.25) the colour would be a red/blue blend WITHOUT stop-position support, so
            // this is RED without the fix.
            StyleGradientClass.TryExtract(
                new[] { "bg-gradient-to-b", "from-[#ff0000]", "from-50%", "to-[#0000ff]" }, out var spec);

            // Act — sample the box upper quarter (texture row 0.75·H → t≈0.25; bake flips top=from).
            var tex = GradientBackground.Bake(spec);
            var upper = tex.GetPixel(tex.width / 2, Mathf.RoundToInt(tex.height * 0.75f));
            Object.DestroyImmediate(tex);

            // Assert — still flat red (no blue yet), because the gradient does not start until 50%.
            Assert.That(upper.b, Is.LessThan(0.05f));
        }

        #endregion

        #region Radial / conic / OKLab (CSS Images L3/L4, Color 4)

        [Test]
        public void Given_BgRadial_When_Extracted_Then_TypeIsRadial()
        {
            var ok = StyleGradientClass.TryExtract(new[] { "bg-radial", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);
            Assume.That(ok, Is.True);
            Assert.That(spec.Type, Is.EqualTo(GradientType.Radial));
        }

        [Test]
        public void Given_BgRadialAtTopLeft_When_Extracted_Then_CenterIsTopLeft()
        {
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_top_left]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);
            Assume.That(ok, Is.True);
            Assert.That(spec.CenterX == 0f && spec.CenterY == 0f, Is.True);
        }

        [Test]
        public void Given_BgConicWithStart_When_Extracted_Then_TypeAndStartAngle()
        {
            var ok = StyleGradientClass.TryExtract(new[] { "bg-conic-45", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);
            Assume.That(ok, Is.True);
            Assert.That(spec.Type == GradientType.Conic && spec.AngleDeg == 45f, Is.True);
        }

        [Test]
        public void Given_OklchModifier_When_Extracted_Then_InterpIsOklab()
        {
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r/oklch", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);
            Assume.That(ok, Is.True);
            Assert.That(spec.Interp, Is.EqualTo(GradientInterp.Oklab));
        }

        [Test]
        public void Given_UnknownInterpModifier_When_Gated_Then_NotAGradient()
        {
            // An unknown /modifier makes the activator unrecognized (no valid utility would emit it).
            var has = StyleGradientClass.HasGradientClass(new[] { "bg-linear-to-r/bogus", "from-[#ff0000]" });
            Assert.That(has, Is.False);
        }

        [Test]
        public void Given_BgRadial_When_Baked_Then_TheCenterIsFrom()
        {
            // Arrange — a radial red→blue from the box centre.
            StyleGradientClass.TryExtract(new[] { "bg-radial", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Act — sample the centre pixel (t≈0 → from).
            var tex = GradientBackground.Bake(spec);
            var center = tex.GetPixel(tex.width / 2, tex.height / 2);
            Object.DestroyImmediate(tex);

            // Assert — the centre is the from color (red).
            Assert.That(center.r > 0.9f && center.b < 0.1f, Is.True);
        }

        [Test]
        public void Given_BgConic_When_Baked_Then_RightOfCenterIsNearerFrom()
        {
            // Arrange — a conic red→blue starting at the top (0°), sweeping clockwise.
            StyleGradientClass.TryExtract(new[] { "bg-conic", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            // Act — sample right of centre (90° clockwise → t≈0.25, near the `from` end of the sweep).
            var tex = GradientBackground.Bake(spec);
            var right = tex.GetPixel(Mathf.RoundToInt(tex.width * 0.78f), tex.height / 2);
            Object.DestroyImmediate(tex);

            // Assert — it is redder than blue (early in the sweep), confirming the clockwise conic direction.
            Assert.That(right.r > right.b, Is.True);
        }

        [Test]
        public void Given_OklabInterp_When_Baked_Then_TheMidpointDiffersFromSrgb()
        {
            // Arrange — the same red→blue stops, baked in sRGB vs OKLab.
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r", "from-[#ff0000]", "to-[#0000ff]" }, out var srgb);
            StyleGradientClass.TryExtract(new[] { "bg-linear-to-r/oklch", "from-[#ff0000]", "to-[#0000ff]" }, out var oklab);

            // Act — sample the gradient midpoint (t=0.5) of each.
            var sTex = GradientBackground.Bake(srgb);
            var oTex = GradientBackground.Bake(oklab);
            var sMid = sTex.GetPixel(sTex.width / 2, sTex.height / 2);
            var oMid = oTex.GetPixel(oTex.width / 2, oTex.height / 2);
            Object.DestroyImmediate(sTex);
            Object.DestroyImmediate(oTex);

            // Assert — OKLab interpolation yields a perceptibly different midpoint than the sRGB channel lerp
            // (would be IDENTICAL if /oklch were ignored).
            var delta = Mathf.Max(Mathf.Abs(sMid.r - oMid.r), Mathf.Abs(sMid.g - oMid.g), Mathf.Abs(sMid.b - oMid.b));
            Assert.That(delta, Is.GreaterThan(0.02f));
        }

        [Test]
        public void Given_BgRadialAtPercentCenter_When_Extracted_Then_YIsNotLostWhenXIsHalf()
        {
            // at_50%_75%: x=50%, y=75%. The earlier `centerX == 0.5f` disambiguation lost the y value
            // whenever x resolved to exactly 50% (it routed the 2nd percentage back to x). This pins y.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-radial-[at_50%_75%]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            Assume.That(ok, Is.True);
            Assert.That(spec.CenterY, Is.EqualTo(0.75f));
        }

        [Test]
        public void Given_BgConicNegativeStart_When_Extracted_Then_TheStartAngleIsNegative()
        {
            // The [from_-90deg] arbitrary form, exercising negative-angle parsing + the C# sign-correcting wrap.
            var ok = StyleGradientClass.TryExtract(
                new[] { "bg-conic-[from_-90deg]", "from-[#ff0000]", "to-[#0000ff]" }, out var spec);

            Assume.That(ok, Is.True);
            Assert.That(spec.AngleDeg, Is.EqualTo(-90f));
        }

        [Test]
        public void Given_OklabInterpWithVia_When_Baked_Then_TheFromViaSegmentAlsoDiffersFromSrgb()
        {
            // The from→via SEGMENT of a 3-stop gradient must also interpolate in OKLab (not just from→to).
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r", "from-[#ff0000]", "via-[#00ff00]", "to-[#0000ff]" }, out var srgb);
            StyleGradientClass.TryExtract(
                new[] { "bg-linear-to-r/oklch", "from-[#ff0000]", "via-[#00ff00]", "to-[#0000ff]" }, out var oklab);

            // Act — sample t≈0.25 (mid of the from→via segment, via at 0.5).
            var sTex = GradientBackground.Bake(srgb);
            var oTex = GradientBackground.Bake(oklab);
            var x = Mathf.RoundToInt(sTex.width * 0.25f);
            var sMid = sTex.GetPixel(x, sTex.height / 2);
            var oMid = oTex.GetPixel(x, oTex.height / 2);
            Object.DestroyImmediate(sTex);
            Object.DestroyImmediate(oTex);

            // Assert — OKLab shifts the from→via midpoint too.
            var delta = Mathf.Max(Mathf.Abs(sMid.r - oMid.r), Mathf.Abs(sMid.g - oMid.g), Mathf.Abs(sMid.b - oMid.b));
            Assert.That(delta, Is.GreaterThan(0.02f));
        }

        #endregion
    }
}

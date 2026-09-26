using System;
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
    /// Pins, by real GPU pixel readback, what <c>rounded-full</c> paints where UI Toolkit paints the face, and
    /// the corner radius a shadow is baked with.
    /// </summary>
    /// <remarks>
    /// <c>--radius-full</c> is larger than the boxes these cases mount, so each layer that reads it decides for
    /// itself what a radius the box cannot carry becomes. <c>Documentation~/styling-variants.md</c> states the
    /// deviation the first two cases pin. <see cref="VelvetStyleUtilities"/> is attached because
    /// <c>rounded-full</c>, <c>rounded-lg</c> and <c>rounded-3xl</c> are plain USS rules: without the sheet
    /// the box stays square-cornered, which reddens every case here.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RoundedFullSilhouettePlaybackTests
    {
        private const int Width = 360;
        private const int Height = 290;

        // Wider than it is tall by roughly ten to one: on a square box a 50% radius and a pill agree.
        private const string WideGeometry = "w-[330px] h-[34px] mt-[20px] ml-[15px]";

        private const string CasterGeometry = "w-[160px] h-[40px] mt-[20px] ml-[40px] bg-[#ffffff]";

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
            DisposePanel();
            yield return null;
        }

        private void DisposePanel()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private Color32[] ReadFrame()
            => RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Width, Height));

        // Readback rows run bottom-up while worldBound runs top-down, so the row index is mirrored.
        private static Color32 At(Color32[] pixels, int col, int row)
            => pixels[((Height - 1 - row) * Width) + col];

        private static bool IsBlue(Color32 p) => p.b > 140 && p.r < 90 && p.g < 90;

        private bool[] CaptureBlueMask()
        {
            var pixels = ReadFrame();
            var mask = new bool[pixels.Length];
            for (var i = 0; i < pixels.Length; i++)
            {
                mask[i] = IsBlue(pixels[i]);
            }
            return mask;
        }

        private static int CountSet(bool[] mask)
        {
            var count = 0;
            foreach (var value in mask)
            {
                if (value)
                {
                    count++;
                }
            }
            return count;
        }

        private static bool MasksEqual(bool[] left, bool[] right)
        {
            if (left.Length != right.Length)
            {
                return false;
            }
            for (var i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }
            return true;
        }

        private IEnumerator MountBox(string name, string className, Action<VisualElement> configure = null)
        {
            DisposePanel();
            _host = new RenderTexturePanelHost(name, Width, Height);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: className));
            configure?.Invoke(Box);
            return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        private VisualElement Box => _host.Root.Q<VisualElement>("box");

        // GREEN_ON_BASE(characterization): UI Toolkit paints this face and this change leaves it alone.
        // The case holds the deviation styling-variants.md states, so a change that makes rounded-full
        // a pill here reddens it and the guide has to move with it.
        [UnityTest]
        public IEnumerator Given_AWideRoundedFullBox_When_UIToolkitPaintsTheFace_Then_ItsBluePixelMaskMatchesAHalfPercentRadius()
        {
            // Arrange — square corners, the control: it is the only term that says the box rendered at all
            // and that the rounding removed anything. Comparing whole thresholded frames distinguishes masks
            // that hold the same number of blue pixels in different places.
            yield return MountBox("Square", $"{WideGeometry} bg-[#0000ff]");
            var square = CaptureBlueMask();

            yield return MountBox("HalfPercent", $"{WideGeometry} bg-[#0000ff]", box =>
            {
                box.style.borderTopLeftRadius = new StyleLength(Length.Percent(50));
                box.style.borderTopRightRadius = new StyleLength(Length.Percent(50));
                box.style.borderBottomRightRadius = new StyleLength(Length.Percent(50));
                box.style.borderBottomLeftRadius = new StyleLength(Length.Percent(50));
            });
            var halfPercent = CaptureBlueMask();

            // Act
            yield return MountBox("Full", $"{WideGeometry} bg-[#0000ff] rounded-full");
            var full = CaptureBlueMask();

            // Assert
            Assert.That((MasksEqual(full, halfPercent), CountSet(square) > CountSet(full)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): UI Toolkit paints both faces and this change leaves them alone.
        // The case holds the advice styling-variants.md gives, a named radius for a pill that
        // UI Toolkit paints, so a change to either face reddens it and the guide moves with it.
        [UnityTest]
        public IEnumerator Given_AWideBox_When_UIToolkitPaintsAHalfHeightRadiusAndRoundedFull_Then_OnlyTheNamedRadiusKeepsAFlatTopEdge()
        {
            // Arrange — square corners, the control: the strip lies on the box's top edge and the instrument
            // reads paint there.
            yield return MountBox("SquareEdge", $"{WideGeometry} bg-[#0000ff]");
            var square = CountTopEdgeStrip(Box.worldBound);

            yield return MountBox("NamedEdge", $"{WideGeometry} bg-[#0000ff] rounded-[17px]");
            var named = CountTopEdgeStrip(Box.worldBound);

            // Act
            yield return MountBox("FullEdge", $"{WideGeometry} bg-[#0000ff] rounded-full");
            var full = CountTopEdgeStrip(Box.worldBound);

            // Assert
            Assert.That((square > 0, named == square, full), Is.EqualTo((true, true, 0)));
        }

        // Blue pixels in a strip on the top edge, four fifths of the way along and two rows deep: inside a
        // pill's flat run on the wide box, and outside a 50% radius's boundary there.
        private int CountTopEdgeStrip(Rect box)
        {
            var pixels = ReadFrame();
            var left = Mathf.RoundToInt(box.xMin + (box.width * 0.8f));
            var top = Mathf.RoundToInt(box.yMin);
            var n = 0;
            for (var row = top; row < top + 2; row++)
            {
                for (var col = left; col < left + 8; col++)
                {
                    if (IsBlue(At(pixels, col, row)))
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        [UnityTest]
        public IEnumerator Given_ARoundedFullBoxWearingAShadow_When_Painted_Then_ItsHaloReachesTheScreen()
        {
            // Arrange — a shadow-free mount says how much red sits beside the box with no shadow at all, and
            // the same shadow behind a radius the box can carry says the shadow layer paints on this panel.
            yield return MountBox("NoShadow", $"{CasterGeometry} rounded-full");
            var bare = HaloMean(Box.worldBound, RedOf);

            yield return MountBox("ShadowLg", $"{CasterGeometry} rounded-lg shadow-[0px_0px_24px_#ff0000]");
            var lg = HaloMean(Box.worldBound, RedOf);

            // Act
            yield return MountBox("ShadowFull", $"{CasterGeometry} rounded-full shadow-[0px_0px_24px_#ff0000]");
            var radiusExceededTheBox = Box.resolvedStyle.borderTopLeftRadius > Box.layout.height;
            var full = HaloMean(Box.worldBound, RedOf);

            // Assert
            Assert.That((radiusExceededTheBox, full > bare, lg > bare), Is.EqualTo((true, true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ARoundedFullBoxWearingADropShadow_When_Painted_Then_ItsHaloReachesTheScreen()
        {
            // Arrange — the drop-shadow presets paint the dark shadow colour, which the red channel cannot
            // tell from the cleared frame, so where the shadow-[…] case reads red this one reads coverage: the
            // frame is cleared transparent and only paint raises its alpha outside the box.
            yield return MountBox("NoDropShadow", $"{CasterGeometry} rounded-full");
            var bare = HaloMean(Box.worldBound, AlphaOf);

            yield return MountBox("DropShadowLg", $"{CasterGeometry} rounded-lg drop-shadow-2xl");
            var lg = HaloMean(Box.worldBound, AlphaOf);

            // Act
            yield return MountBox("DropShadowFull", $"{CasterGeometry} rounded-full drop-shadow-2xl");
            var radiusExceededTheBox = Box.resolvedStyle.borderTopLeftRadius > Box.layout.height;
            var full = HaloMean(Box.worldBound, AlphaOf);

            // Assert
            Assert.That((radiusExceededTheBox, full > bare, lg > bare), Is.EqualTo((true, true, true)));
        }

        private static float RedOf(Color32 p) => p.r;

        private static float AlphaOf(Color32 p) => p.a;

        // Mean of one channel over a strip just outside the caster's LEFT edge at its vertical centre, where
        // only the shadow paints and where a pill's boundary and an 8px-rounded box's lie within a pixel of
        // the edge.
        private float HaloMean(Rect box, Func<Color32, float> channel)
        {
            var pixels = ReadFrame();
            var col = Mathf.RoundToInt(box.xMin) - 5;
            var top = Mathf.RoundToInt(box.center.y) - 6;
            var total = 0f;
            var n = 0;
            for (var row = top; row < top + 12; row++)
            {
                for (var c = col; c < col + 4; c++)
                {
                    total += channel(At(pixels, c, row));
                    n++;
                }
            }
            return n == 0 ? 0f : total / n;
        }

        // GREEN_ON_BASE(characterization): the base already bakes a radius the box can carry as declared.
        // Bounding every radius instead, `Mathf.Min(binding.CornerRadius, bound)` -> `bound`, reddens it.
        [UnityTest]
        public IEnumerator Given_AShadowOffsetClearOfItsCaster_When_TheCasterIsRounded3xl_Then_TheShadowCornerKeepsItsTwentyFourPixelRadius()
        {
            // Arrange — the offset carries the shadow's bottom-right corner clear of the face painted over it,
            // and the 1px blur keeps its edge within half a pixel of the SDF boundary.
            yield return MountBox("Offset3xl", $"{OffsetCasterGeometry} rounded-3xl {OffsetShadow}");

            // Act — along the corner's diagonal, a 24px corner leaves the corner pixel outside the silhouette
            // (SDF +9.2px) and covers the pixel 14.5px in (-10.6px); the box's bound, 100px, puts that pixel at
            // +20.9px.
            var frame = ReadFrame();
            var cornerPixel = IsShadowAtCornerDiagonal(frame, Box.worldBound, 0.5f);
            var fourteenAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 14.5f);

            // Assert
            Assert.That((cornerPixel, fourteenAndAHalfIn), Is.EqualTo((false, true)));
        }

        [UnityTest]
        public IEnumerator Given_AShadowOffsetClearOfItsCaster_When_TheCasterIsRoundedFull_Then_TheShadowCornerTakesHalfTheShorterSide()
        {
            // Arrange — as for the rounded-3xl corner; the caster is 240 x 200, so the bound is 100px, twice the
            // 50 a 50% --radius-full would resolve to.
            yield return MountBox("OffsetFull", $"{OffsetCasterGeometry} rounded-full {OffsetShadow}");

            // Act — along the corner's diagonal, a 100px corner leaves the pixel 22.5px in outside the
            // silhouette (SDF +9.6px; a 50px corner puts it at -11.1px) and covers the pixel 45.5px in
            // (-22.9px; a 200px corner on the SDF's 120 x 100 half-extent puts it at +18.5px).
            var frame = ReadFrame();
            var twentyTwoAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 22.5f);
            var fortyFiveAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 45.5f);

            // Assert
            Assert.That((twentyTwoAndAHalfIn, fortyFiveAndAHalfIn), Is.EqualTo((false, true)));
        }

        // The offset exceeds the deepest sample, so every sample lies outside the caster's face.
        private const string OffsetCasterGeometry = "w-[240px] h-[200px] mt-[10px] ml-[10px] bg-[#ffffff]";
        private const float ShadowOffset = 60f;
        private const string OffsetShadow = "shadow-[60px_60px_1px_#ff0000]";

        // Whether the pixel whose centre sits `inset` px in from the offset shadow's bottom-right corner, along
        // the corner's diagonal, reads as the shadow's red. Derived from the caster's measured box.
        private static bool IsShadowAtCornerDiagonal(Color32[] frame, Rect caster, float inset)
        {
            var cornerX = caster.xMax + ShadowOffset;
            var cornerY = caster.yMax + ShadowOffset;
            var col = Mathf.RoundToInt(cornerX - inset - 0.5f);
            var row = Mathf.RoundToInt(cornerY - inset - 0.5f);
            return RenderTexturePixelReader.IsRedPixel(At(frame, col, row));
        }
    }
}

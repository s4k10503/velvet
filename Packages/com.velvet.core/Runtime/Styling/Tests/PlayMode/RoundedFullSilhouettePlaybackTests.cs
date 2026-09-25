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
    /// <c>rounded-full</c> and <c>rounded-lg</c> are plain USS rules: without the sheet the box stays
    /// square-cornered, which reddens every case here.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RoundedFullSilhouettePlaybackTests
    {
        private const int Width = 360;
        private const int Height = 160;

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
        public IEnumerator Given_AShadowOffsetClearOfItsCaster_When_TheCasterIsRoundedLg_Then_TheShadowCornerKeepsTheEightPixelRadius()
        {
            // Arrange — the offset carries the shadow's bottom-right corner clear of the face painted over it,
            // and the 1px blur keeps its edge within half a pixel of the SDF boundary.
            yield return MountBox("OffsetLg", $"{OffsetCasterGeometry} rounded-lg {OffsetShadow}");

            // Act — an 8px corner leaves the corner pixel outside the silhouette (SDF +2.6px at 0.5px in along
            // the diagonal) and covers the pixel 4.5px in (-3.1px); the box's bound, 60px, covers neither.
            var frame = ReadFrame();
            var cornerPixel = IsShadowAtCornerDiagonal(frame, Box.worldBound, 0.5f);
            var fourAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 4.5f);

            // Assert
            Assert.That((cornerPixel, fourAndAHalfIn), Is.EqualTo((false, true)));
        }

        [UnityTest]
        public IEnumerator Given_AShadowOffsetClearOfItsCaster_When_TheCasterIsRoundedFull_Then_TheShadowCornerTakesHalfTheShorterSide()
        {
            // Arrange — as for the rounded-lg corner; the caster is 160 x 120, so the bound is 60px, and its
            // shorter side is over twice the 50 a 50% --radius-full would resolve to.
            yield return MountBox("OffsetFull", $"{OffsetCasterGeometry} rounded-full {OffsetShadow}");

            // Act — a 60px corner leaves the pixel 16.5px in along the diagonal outside the silhouette (SDF
            // +1.5px; a 50px corner puts it at -2.6px) and covers the pixel 25.5px in (-11.2px; a 120px corner
            // on the SDF's 80 x 60 half-extent puts it at +13.6px).
            var frame = ReadFrame();
            var sixteenAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 16.5f);
            var twentyFiveAndAHalfIn = IsShadowAtCornerDiagonal(frame, Box.worldBound, 25.5f);

            // Assert
            Assert.That((sixteenAndAHalfIn, twentyFiveAndAHalfIn), Is.EqualTo((false, true)));
        }

        private const string OffsetCasterGeometry = "w-[160px] h-[120px] mt-[10px] ml-[20px] bg-[#ffffff]";
        private const float ShadowOffset = 20f;
        private const string OffsetShadow = "shadow-[20px_20px_1px_#ff0000]";

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

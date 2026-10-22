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
    /// Pins, by real GPU pixel readback, that a face a silhouette layer repaints keeps each border side's own colour:
    /// a red border whose top side alone is blue, eight pixels wide, on a box the skew layer repaints and on one the
    /// shadow layer repaints; and where the two sides meet in a rounded corner, divided by their widths. A shadowed box
    /// keeps its border where it clips its content, and dashes a dashed one.
    /// </summary>
    [Timeout(600000)]
    internal sealed class SilhouetteBorderSidesPlaybackTests
    {
        private const int Size = 240;
        private const string Box = "w-[120px] h-[80px] mt-[60px] ml-[60px] bg-[#000000] border-[8px] border-[#ff0000] border-t-[#0000ff]";

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
            _host?.Dispose();
            yield return null;
        }

        private IEnumerator Mount(string name, string className)
        {
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = Color.black;
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: className));
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        // The colour at a point given in the box's own coordinates, carried by the shear the box is painted with
        // (SilhouetteFace.Shear), named.
        private string ColourAt(Vector2 local, float tanX)
        {
            var box = _host.Root.Q<VisualElement>("box");
            var bound = box.worldBound;
            var sheared = SilhouetteFace.Shear(local, bound.width, bound.height, tanX, 0f);
            var pixel = Vector2Int.FloorToInt(bound.min + sheared);
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var p = pixels[((Size - 1 - pixel.y) * Size) + pixel.x];
            if (p.b > 140 && p.r < 90 && p.g < 90) return "blue";
            if (p.r > 140 && p.g < 90 && p.b < 90) return "red";
            return "other";
        }

        [UnityTest]
        public IEnumerator Given_ASkewedBoxWhoseTopBorderAloneIsBlue_When_ItsFaceIsRepainted_Then_ItsTopBandIsBlue()
        {
            // Arrange / Act
            yield return Mount("SkewTopBand", $"{Box} skew-x-[10deg]");

            // Assert — halfway along the top band and halfway into it.
            Assert.That(ColourAt(new Vector2(60f, 4f), Mathf.Tan(10f * Mathf.Deg2Rad)), Is.EqualTo("blue"));
        }

        [UnityTest]
        public IEnumerator Given_AShadowedBoxWhoseTopBorderAloneIsBlue_When_ItsFaceIsRepainted_Then_ItsTopBandIsBlue()
        {
            // Arrange / Act
            yield return Mount("ShadowTopBand", $"{Box} shadow-[0px_0px_8px_#ffffff]");

            // Assert — halfway along the top band and halfway into it.
            Assert.That(ColourAt(new Vector2(60f, 4f), 0f), Is.EqualTo("blue"));
        }

        [UnityTest]
        public IEnumerator Given_AShadowedBoxThatClipsItsContent_When_Painted_Then_ItsTopBandIsBlue()
        {
            // Arrange / Act
            yield return Mount("ShadowClipped", $"{Box} overflow-hidden shadow-[0px_0px_8px_#ffffff]");

            // Assert — halfway along the top band and halfway into it.
            Assert.That(ColourAt(new Vector2(60f, 4f), 0f), Is.EqualTo("blue"));
        }

        [UnityTest]
        public IEnumerator Given_AShadowedBoxWithADashedBorder_When_ItsFaceIsRepainted_Then_ItsTopBandHasGaps()
        {
            // Arrange
            yield return Mount("ShadowDashed", "w-[120px] h-[80px] mt-[60px] ml-[60px] bg-[#000000] border-[8px] "
                + "border-[#ff0000] border-dashed shadow-[0px_0px_8px_#ffffff]");

            // Act — every other pixel along the top band, halfway into it.
            var seen = new System.Collections.Generic.HashSet<string>();
            for (var x = 8f; x <= 112f; x += 2f)
            {
                seen.Add(ColourAt(new Vector2(x, 4f), 0f));
            }

            // Assert — both dash and gap, which a solid band, or none, would not show.
            Assert.That((seen.Contains("red"), seen.Contains("other")), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base strokes every side in the left one's colour, red here too.
        // Cutting BorderBandPainter's sides on each corner's diagonal rather than on DashedBorderPainter.SideQuad's
        // line reddens this case.
        [UnityTest]
        public IEnumerator Given_AShadowedRoundedBoxWhoseLeftBorderIsFourTimesItsTop_When_ItsFaceIsRepainted_Then_TheLeftSideReachesPastTheCornersDiagonal()
        {
            // Arrange / Act — a 24px corner between a sixteen-pixel red left side and a four-pixel blue top.
            yield return Mount("ShadowCornerSplit", "w-[120px] h-[80px] mt-[60px] ml-[60px] bg-[#000000] rounded-[24px] "
                + "border-[4px] border-l-[16px] border-[#ff0000] border-t-[#0000ff] shadow-[0px_0px_8px_#ffffff]");

            // Assert — a point of the band above the corner's diagonal and below the line from its outer corner to its
            // inner one, (16, 4).
            Assert.That(ColourAt(new Vector2(14.15f, 8f), 0f), Is.EqualTo("red"));
        }
    }
}

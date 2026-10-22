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
    /// shadow layer repaints.
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
    }
}

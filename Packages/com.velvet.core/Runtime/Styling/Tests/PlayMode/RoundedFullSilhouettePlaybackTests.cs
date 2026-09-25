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
    /// that a shadow behind it keeps its halo.
    /// </summary>
    /// <remarks>
    /// <c>--radius-full</c> is larger than the boxes these cases mount, so each layer that reads it decides for
    /// itself what a radius the box cannot carry becomes. <c>Documentation~/styling-variants.md</c> states the
    /// deviation the first case pins. <see cref="VelvetStyleUtilities"/> is attached because
    /// <c>rounded-full</c> is a plain USS rule: without the sheet the box stays square-cornered and each case
    /// reads its control's outcome.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RoundedFullSilhouettePlaybackTests
    {
        private const int Width = 360;
        private const int Height = 80;

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

        [UnityTest]
        public IEnumerator Given_ARoundedFullBoxWearingAShadow_When_Painted_Then_ItsHaloReachesTheScreen()
        {
            // Arrange — a shadow-free mount says how much red sits beside the box with no shadow at all, and
            // the same shadow behind a radius the box can carry says the shadow layer paints on this panel.
            yield return MountBox("NoShadow", $"{CasterGeometry} rounded-full");
            var bare = HaloRed(Box.worldBound);

            yield return MountBox("ShadowLg", $"{CasterGeometry} rounded-lg shadow-[0px_0px_24px_#ff0000]");
            var lg = HaloRed(Box.worldBound);

            // Act
            yield return MountBox("ShadowFull", $"{CasterGeometry} rounded-full shadow-[0px_0px_24px_#ff0000]");
            var radiusExceededTheBox = Box.resolvedStyle.borderTopLeftRadius > Box.layout.height;
            var full = HaloRed(Box.worldBound);

            // Assert
            Assert.That((radiusExceededTheBox, full > bare, lg > bare), Is.EqualTo((true, true, true)));
        }

        // Mean red of a strip just outside the caster's LEFT edge at its vertical centre, where only the
        // shadow paints and where a pill's boundary and an 8px-rounded box's lie within a pixel of the edge.
        private float HaloRed(Rect box)
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
                    total += At(pixels, c, row).r;
                    n++;
                }
            }
            return n == 0 ? 0f : total / n;
        }
    }
}

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
    /// Pins, by real GPU pixel readback, the silhouette <c>rounded-full</c> paints on a box that is not
    /// square, and the shadow that rounding leaves behind it.
    /// </summary>
    /// <remarks>
    /// <c>--radius-full</c> is deliberately oversized, so what reaches the screen is decided by
    /// whatever the renderer does with a radius it cannot honour. UI Toolkit's answer and CSS's part company
    /// on a non-square box — a pill button, a badge, a search field. A comment in
    /// <c>_tokens.uss</c> asserted the CSS outcome with nothing measuring it; these cases are what a future
    /// reader gets instead of that sentence. The token's magnitude is free to change as long as it stays
    /// saturating: these cases mount <c>rounded-full</c> rather than a literal.
    /// <para>
    /// Each case carries a control arrangement rendered through the same instrument — a square-cornered box
    /// for the silhouette, a shadow-free box for the halo — because a panel that renders nothing, and a class
    /// the panel resolves nowhere, both read as the outcome under test. <see cref="VelvetStyleUtilities"/> is
    /// attached for that second reason: <c>rounded-full</c> is a plain USS rule and is inert without the
    /// sheet, which would leave the box wearing it square-cornered and read as the control's own count.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RoundedFullSilhouettePlaybackTests
    {
        private const int Width = 360;
        private const int Height = 80;

        // Wider than it is tall by roughly ten to one, which is what separates the two roundings: on a square
        // box they agree.
        private const string BoxGeometry = "w-[330px] h-[34px] mt-[20px] ml-[15px]";

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

        private int CountBlue()
        {
            var pixels = ReadFrame();
            var n = 0;
            foreach (var p in pixels)
            {
                if (IsBlue(p))
                {
                    n++;
                }
            }
            return n;
        }

        private IEnumerator MountBox(string name, string className, Action<VisualElement> configure = null)
        {
            DisposePanel();
            _host = new RenderTexturePanelHost(name, Width, Height);
            VelvetStyleUtilities.AttachTo(_host.Root);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: className));
            configure?.Invoke(_host.Root.Q<VisualElement>("box"));
            return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        private VisualElement Box => _host.Root.Q<VisualElement>("box");

        // GREEN_ON_BASE(characterization): the base paints both of these silhouettes the same way.
        // This branch does not change the renderer-owned path, so the case pins the fact a comment in
        // _tokens.uss asserted the opposite of.
        [UnityTest]
        public IEnumerator Given_ARoundedFullBox_When_Painted_Then_ItsSilhouetteMatchesAPerAxisHalfPercentRadius()
        {
            // Arrange — the same box three ways: the token, an explicit 50% on all four corners, and square
            // corners. 50% is per-axis by construction, so a renderer scaling every radius by one factor (what
            // CSS does, and what leaves a pill) separates the first two; one clamping each component against
            // its own axis collapses them onto the same silhouette.
            yield return MountBox("Full", $"{BoxGeometry} bg-[#0000ff] rounded-full");
            var full = CountBlue();

            yield return MountBox("HalfPercent", $"{BoxGeometry} bg-[#0000ff]", box =>
            {
                box.style.borderTopLeftRadius = new StyleLength(Length.Percent(50));
                box.style.borderTopRightRadius = new StyleLength(Length.Percent(50));
                box.style.borderBottomRightRadius = new StyleLength(Length.Percent(50));
                box.style.borderBottomLeftRadius = new StyleLength(Length.Percent(50));
            });
            var halfPercent = CountBlue();

            // Act — square corners, the control: it is the only term here that says the boxes rendered at all
            // and that the rounding removed anything.
            yield return MountBox("Square", $"{BoxGeometry} bg-[#0000ff]");
            var square = CountBlue();

            // Assert
            Assert.That((full == halfPercent, square > full), Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base leaves this strip of the top edge unpainted too.
        // The branch moves the shadow's copy of the radius and nothing on the path that paints the box, so
        // the outcome here is the base's.
        [UnityTest]
        public IEnumerator Given_ARoundedFullBox_When_Painted_Then_ItsTopEdgeCarriesNoFlatRun()
        {
            // Arrange — a strip on the top edge, four fifths of the way along, two rows deep. A pill's flat run
            // covers it; a full ellipse's boundary has already fallen below it there. Both the
            // column span and the rows come off the measured box, so a layout that did not land reads as a
            // failure rather than as the verdict.
            yield return MountBox("FullEdge", $"{BoxGeometry} bg-[#0000ff] rounded-full");
            var fullBox = Box.worldBound;
            var fullStrip = CountStrip(fullBox);

            // Act — square corners through the same strip: the control that says the strip is inside the box
            // and that the instrument reads paint there.
            yield return MountBox("SquareEdge", $"{BoxGeometry} bg-[#0000ff]");
            var squareStrip = CountStrip(Box.worldBound);

            // Assert
            Assert.That((fullStrip, squareStrip > 0), Is.EqualTo((0, true)));
        }

        private int CountStrip(Rect box)
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
        public IEnumerator Given_ARoundedFullBoxWearingAShadow_When_LaidOut_Then_ItsHaloStillReachesTheScreen()
        {
            // Arrange — the shadow reads the laid-out corner radius back off the element, and the caster's own
            // face is repainted rounded by the shared face kernel. A radius the box cannot carry has to round
            // the same way in both or the halo and the face disagree; a background reading through a
            // shadow-free mount is what says how much red is there with no shadow at all.
            yield return MountBox("NoShadow", "w-[160px] h-[40px] mt-[20px] ml-[40px] bg-[#ffffff] rounded-full");
            var bare = HaloRed(Box.worldBound);

            yield return MountBox("ShadowFull",
                "w-[160px] h-[40px] mt-[20px] ml-[40px] bg-[#ffffff] rounded-full shadow-[0px_0px_24px_#ff0000]");
            var radiusExceededTheBox = Box.resolvedStyle.borderTopLeftRadius > Box.layout.height;
            var full = HaloRed(Box.worldBound);

            // Act — the same shadow behind a radius the box can carry, which is what says the shadow layer
            // paints anything at all on this panel.
            yield return MountBox("ShadowLg",
                "w-[160px] h-[40px] mt-[20px] ml-[40px] bg-[#ffffff] rounded-lg shadow-[0px_0px_24px_#ff0000]");
            var lg = HaloRed(Box.worldBound);

            // Assert
            Assert.That((radiusExceededTheBox, full > bare, lg > bare), Is.EqualTo((true, true, true)));
        }

        // Mean red of a strip just outside the caster's LEFT edge at its vertical centre, where only the
        // shadow paints and where both roundings put the silhouette boundary in the same place.
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

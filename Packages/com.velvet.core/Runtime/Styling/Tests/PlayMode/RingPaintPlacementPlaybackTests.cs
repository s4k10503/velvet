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
    /// Where a ring, an outline and the inset bands land against the content around them, read back from the GPU:
    /// an outline paints over a later sibling, an inset band paints under the element's own child, and a square
    /// corner stays square under a ring's spread.
    /// </summary>
    /// <remarks>
    /// The panel carries no stylesheet, so every utility here is one Velvet resolves itself and the row direction
    /// is set inline, as <c>RingSiblingPaintOrderPlaybackTests</c> sets it. Every sample is cut from measured
    /// <see cref="VisualElement.worldBound"/>s, with the geometry that licenses it folded into the assertion.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RingPaintPlacementPlaybackTests
    {
        private const int Size = 240;
        private const float BandWidth = 8f;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private void NewPanel(string name)
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = new RenderTexturePanelHost(name, Size, Size);
        }

        // Counts matching pixels inside a PANEL-space rect; the texture is bottom-up, so rows are flipped.
        private int CountInPanelRect(RectInt panelRect, Func<Color32, bool> match)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var n = 0;
            for (var y = panelRect.yMin; y < panelRect.yMax; y++)
            {
                var texY = Size - 1 - y;
                if (texY < 0 || texY >= Size)
                {
                    continue;
                }
                for (var x = panelRect.xMin; x < panelRect.xMax; x++)
                {
                    if (x >= 0 && x < Size && match(pixels[(texY * Size) + x]))
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;
        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;

        // A vertical slice two pixels inside a band BandWidth wide, outside the box on the named side, across the
        // middle third of its height.
        private static RectInt OuterStrip(Rect box, bool right)
            => new(Mathf.RoundToInt(right ? box.xMax + 2f : box.xMin - BandWidth + 2f),
                Mathf.RoundToInt(box.yMin + (box.height / 3f)), Mathf.RoundToInt(BandWidth - 4f),
                Mathf.RoundToInt(box.height / 3f));

        // The same slice for a band inside the box, along its left edge.
        private static RectInt InnerStrip(Rect box)
            => new(Mathf.RoundToInt(box.xMin + 2f), Mathf.RoundToInt(box.yMin + (box.height / 3f)),
                Mathf.RoundToInt(BandWidth - 4f), Mathf.RoundToInt(box.height / 3f));

        private IEnumerator MountRow(string name, VNode[] children)
        {
            NewPanel(name);
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "ml-[30px] mt-[70px] w-[200px] h-[60px]",
                children: children));
            _host.Root.Q<VisualElement>("row").style.flexDirection = FlexDirection.Row;
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        [UnityTest]
        public IEnumerator Given_AnOutlinedElementOverlappedByALaterSibling_When_Rendered_Then_TheOutlinePaintsOverIt()
        {
            // Arrange — CSS paints outlines after the content of their stacking context, so b's face does not hide
            // a's outline where it overlaps it.
            yield return MountRow("OutlineOverSibling", new VNode[]
            {
                V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] outline-[8px] outline-[#ff0000]"),
                V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#00ff00]"),
            });
            _host.Root.Q<VisualElement>("b").style.marginLeft = -20f;

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var a = _host.Root.Q<VisualElement>("a").worldBound;
            var b = _host.Root.Q<VisualElement>("b").worldBound;
            var overlapped = OuterStrip(a, right: true);
            var control = OuterStrip(a, right: false);

            // Assert — b's face spans the strip, the outline reached the screen where nothing overlaps it, and the
            // overlapped strip still reads as the outline's red.
            Assert.That(
                (b.xMin < overlapped.xMin, b.xMax > overlapped.xMax, CountInPanelRect(control, IsRed) > 0,
                    CountInPanelRect(overlapped, IsRed) > 0, CountInPanelRect(overlapped, IsGreen) == 0),
                Is.EqualTo((true, true, true, true, true)), $"a={a} b={b} strip={overlapped}");
        }

        [UnityTest]
        public IEnumerator Given_AnInsetRingUnderAFullBleedChild_When_Rendered_Then_TheChildCoversIt()
        {
            // Arrange — an inset box-shadow paints over the background and under the content; the control is the
            // same ring on an element with no child.
            yield return MountRow("InsetRingUnderChild", new VNode[]
            {
                V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] ring-inset ring-[8px] ring-[#ff0000]",
                    children: new VNode[] { V.Div(className: "w-[60px] h-[60px] bg-[#00ff00]") }),
                V.Div(name: "c", className: "w-[60px] h-[60px] ml-[20px] bg-[#0000ff] ring-inset ring-[8px] ring-[#ff0000]"),
            });

            // Act
            var covered = InnerStrip(_host.Root.Q<VisualElement>("a").worldBound);
            var control = InnerStrip(_host.Root.Q<VisualElement>("c").worldBound);

            // Assert
            Assert.That(
                (CountInPanelRect(control, IsRed) > 0, CountInPanelRect(covered, IsRed) == 0,
                    CountInPanelRect(covered, IsGreen) > 0),
                Is.EqualTo((true, true, true)));
        }

        [UnityTest]
        public IEnumerator Given_AnInsetRingUtility_When_Rendered_Then_ItPaintsInsideTheBox()
            => RendersInsideTheBox("inset-ring-[8px] inset-ring-[#ff0000]");

        [UnityTest]
        public IEnumerator Given_AnInsetShadowUtility_When_Rendered_Then_ItPaintsInsideTheBox()
            => RendersInsideTheBox("inset-shadow-[0_0_0_8px_#ff0000]");

        private IEnumerator RendersInsideTheBox(string utilities)
        {
            // Arrange — the control is the box's own blue at its centre, which the band does not reach.
            yield return MountRow("InsetLayer", new VNode[]
            {
                V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] " + utilities),
            });

            // Act
            var box = _host.Root.Q<VisualElement>("a").worldBound;
            var centre = new RectInt(Mathf.RoundToInt(box.center.x - 4f), Mathf.RoundToInt(box.center.y - 4f), 8, 8);

            // Assert
            Assert.That(
                (CountInPanelRect(centre, p => p.b > 140 && p.r < 90) > 0, CountInPanelRect(InnerStrip(box), IsRed) > 0),
                Is.EqualTo((true, true)));
        }

        [UnityTest]
        public IEnumerator Given_ARingOnASquareBox_When_Rendered_Then_ItsOuterCornerStaysSquare()
        {
            // Arrange — CSS grows a corner radius of 0 by no amount under a box-shadow's spread.
            yield return MountRow("SquareSpreadCorner", new VNode[]
            {
                V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] ring-[8px] ring-[#ff0000]"),
            });

            // Act — a 3px square just inside the band's outer top-left corner, which a rounded corner leaves bare,
            // beside the same square along the band's left edge as the control.
            var box = _host.Root.Q<VisualElement>("a").worldBound;
            var corner = new RectInt(Mathf.RoundToInt(box.xMin - BandWidth), Mathf.RoundToInt(box.yMin - BandWidth), 3, 3);
            var edge = new RectInt(Mathf.RoundToInt(box.xMin - BandWidth), Mathf.RoundToInt(box.center.y), 3, 3);

            // Assert
            Assert.That((CountInPanelRect(edge, IsRed) > 0, CountInPanelRect(corner, IsRed) > 0), Is.EqualTo((true, true)));
        }
    }
}

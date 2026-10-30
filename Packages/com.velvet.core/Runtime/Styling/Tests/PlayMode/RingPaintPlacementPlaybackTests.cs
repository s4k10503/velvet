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
    /// Where an outline and a ring's corner land against the content around them, read back from the GPU: an outline
    /// paints over a later sibling and inside only its ancestors' clips, and a square corner stays square under a
    /// ring's spread.
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

        // Counts matching pixels inside a PANEL-space rect. GetPixels32 returns the texture bottom-up while
        // panel y grows downward, so the row index is flipped; a sample rect read without the flip lands on
        // the mirror image of the intended region, which for a vertically centred probe still returns
        // plausible numbers. The control term in each case is what proves the mapping landed.
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
                    if (x < 0 || x >= Size)
                    {
                        continue;
                    }
                    if (match(pixels[(texY * Size) + x]))
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        // Where every outline node and clip proxy sits, for a failure message.
        private string OutlineNodes()
        {
            var parts = new System.Text.StringBuilder();
            _host.Root.Query<VisualElement>(className: RingOverlay.OutlineMarkerClass).ForEach(n => parts
                .Append(n.name).Append(" under ").Append(n.parent?.name).Append(' ').Append(n.worldBound)
                .Append(" overflow=").Append(n.resolvedStyle.overflow).Append("; "));
            parts.Append("clip clips=").Append(OutlineHosting.Clips(_host.Root.Q<VisualElement>("clip")));
            return parts.ToString();
        }

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;
        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;

        // A vertical slice two pixels inside a band BandWidth wide, outside the box on the named side, across the
        // middle third of its height.
        private static RectInt OuterStrip(Rect box, bool right)
            => new(Mathf.RoundToInt(right ? box.xMax + 2f : box.xMin - BandWidth + 2f),
                Mathf.RoundToInt(box.yMin + (box.height / 3f)), Mathf.RoundToInt(BandWidth - 4f),
                Mathf.RoundToInt(box.height / 3f));

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

            // Assert — b's face spans the strip, which still reads as the outline's red and nowhere green.
            Assert.That(b.xMin < overlapped.xMin && b.xMax > overlapped.xMax && CountInPanelRect(overlapped, IsRed) > 0
                && CountInPanelRect(overlapped, IsGreen) == 0, Is.True, $"a={a} b={b} strip={overlapped}");
        }

        [UnityTest]
        public IEnumerator Given_AnOutlinedElementBesideALaterSibling_When_Rendered_Then_TheOutlineReachesTheScreen()
        {
            // Arrange — the control for the case above: the same outline on the side nothing overlaps.
            yield return MountRow("OutlineControl", new VNode[]
            {
                V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] outline-[8px] outline-[#ff0000]"),
                V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#00ff00]"),
            });
            _host.Root.Q<VisualElement>("b").style.marginLeft = -20f;

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var a = _host.Root.Q<VisualElement>("a").worldBound;

            // Assert
            Assert.That(CountInPanelRect(OuterStrip(a, right: false), IsRed), Is.GreaterThan(0), $"a={a}");
        }

        [UnityTest]
        public IEnumerator Given_AnOutlineInsideAClipUnderLaterContent_When_Rendered_Then_ItPaintsOverThatContentWithinTheClip()
        {
            // Arrange — a sits at the clip's top-left, so its outline's top band lies outside the clip and its right
            // band inside it, where b, later and outside the clip, overlaps it. The clip is set inline: the panel
            // carries no stylesheet for overflow-hidden.
            yield return MountRow("OutlineInClip", new VNode[]
            {
                V.Div(name: "clip", className: "w-[100px] h-[60px]", children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[40px] h-[40px] bg-[#0000ff] outline-[8px] outline-[#ff0000]"),
                }),
                V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#00ff00]"),
            });
            _host.Root.Q<VisualElement>("clip").style.overflow = Overflow.Hidden;
            _host.Root.Q<VisualElement>("b").style.marginLeft = -60f;

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var a = _host.Root.Q<VisualElement>("a").worldBound;
            var b = _host.Root.Q<VisualElement>("b").worldBound;
            var inside = OuterStrip(a, right: true);

            // Assert — b covers the right band's strip, which still reads red.
            Assert.That(b.xMin < inside.xMin && b.xMax > inside.xMax && CountInPanelRect(inside, IsRed) > 0, Is.True,
                $"a={a} b={b}");
        }

        [UnityTest]
        public IEnumerator Given_AnOutlineInsideAClipUnderLaterContent_When_Rendered_Then_TheClipCutsTheBandAboveIt()
        {
            // Arrange — the same arrangement; the band above the clip's top edge is outside the clip.
            yield return MountRow("OutlineClipCut", new VNode[]
            {
                V.Div(name: "clip", className: "w-[100px] h-[60px]", children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[40px] h-[40px] bg-[#0000ff] outline-[8px] outline-[#ff0000]"),
                }),
            });
            _host.Root.Q<VisualElement>("clip").style.overflow = Overflow.Hidden;

            // Act
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var a = _host.Root.Q<VisualElement>("a").worldBound;
            var outside = new RectInt(Mathf.RoundToInt(a.xMin + 10f), Mathf.RoundToInt(a.yMin - BandWidth + 2f), 10, 4);
            var inside = OuterStrip(a, right: true);

            // Assert — the right band, inside the clip, is the control: it reads red where the one above does not.
            Assert.That(CountInPanelRect(inside, IsRed) > 0 && CountInPanelRect(outside, IsRed) == 0, Is.True,
                $"a={a} inside={CountInPanelRect(inside, IsRed)} outside={CountInPanelRect(outside, IsRed)} {OutlineNodes()}");
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
            Assert.That(CountInPanelRect(edge, IsRed) > 0 && CountInPanelRect(corner, IsRed) > 0, Is.True);
        }
    }
}

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
    /// Pins, by real GPU pixel readback, the engine fact the ring's hosting model rests on and the pattern
    /// that fact would otherwise break.
    /// </summary>
    /// <remarks>
    /// The measured fact: UI Toolkit applies an element's own <c>overflow: hidden</c> to the element's own
    /// <c>generateVisualContent</c>, not merely to its children. So a band painted in the ringed element's
    /// own content — the model the drop shadow uses — disappears entirely on
    /// <c>overflow-hidden rounded-full ring-2</c>, the avatar pattern. CSS clips neither an outline nor a
    /// box-shadow by the element's own overflow, so that would also be a parity deviation. Hosting the band
    /// on a sibling is what avoids it.
    /// <para>
    /// Each case carries a GREEN control child that overflows the box on the opposite side. Without one,
    /// "the paint was clipped" and "the clip never applied" are indistinguishable — a probe built from
    /// arbitrary-value utilities (<c>w-[60px]</c>, <c>bg-[#00f]</c>, which Velvet resolves to INLINE style)
    /// on a panel carrying no stylesheet looks correctly constructed while the one class under test is
    /// silently inert, and reads as a false negative. The overflow here is therefore set inline, and the
    /// control child is what proves it took.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RingOverflowClipPlaybackTests
    {
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
            _host = new RenderTexturePanelHost(name, 200, 200);
        }

        private int Count(Func<Color32, bool> match)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, 200, 200));
            var n = 0;
            foreach (var p in pixels)
            {
                if (match(p))
                {
                    n++;
                }
            }
            return n;
        }

        private int RedPixels() => Count(p => p.r > 140 && p.g < 90 && p.b < 90);
        private int GreenPixels() => Count(p => p.g > 140 && p.r < 90 && p.b < 90);

        // Sticks out to the RIGHT of the box. Clipped exactly when the box's overflow clip is in effect.
        private static void AddControlChild(VisualElement box)
        {
            var child = new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 65,
                    top = 15,
                    width = 25,
                    height = 30,
                    backgroundColor = new Color(0f, 1f, 0f, 1f),
                },
            };
            box.Add(child);
        }

        // Paints a red quad OUTSIDE the box, to its left, in the element's own generated content.
        private static void PaintOutsideOwnBox(VisualElement box)
        {
            box.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                p.MoveTo(new Vector2(-30f, 15f));
                p.LineTo(new Vector2(-5f, 15f));
                p.LineTo(new Vector2(-5f, 45f));
                p.LineTo(new Vector2(-30f, 45f));
                p.ClosePath();
                p.fillColor = new Color(1f, 0f, 0f, 1f);
                p.Fill();
            };
        }

        private IEnumerator MountBox(string name, bool clip, out VisualElement box)
        {
            NewPanel(name);
            _mounted = V.Mount(_host.Root,
                V.Div(name: "box", className: "w-[60px] h-[60px] mt-[80px] ml-[80px] bg-[#0000ff]"));
            box = _host.Root.Q<VisualElement>("box");
            if (clip)
            {
                box.style.overflow = Overflow.Hidden;
            }
            return WaitRealtime(0.8);
        }

        [UnityTest]
        public IEnumerator Given_AnElementWithOverflowHidden_When_ItPaintsOutsideItsOwnBox_Then_TheEngineClipsItsOwnGeneratedContent()
        {
            // Arrange — the same element twice, once unclipped, once with its own overflow hidden.
            yield return MountBox("GvcOpen", clip: false, out var open);
            PaintOutsideOwnBox(open);
            AddControlChild(open);
            open.MarkDirtyRepaint();
            yield return WaitRealtime(0.8);
            var openPaint = RedPixels();

            // Act
            yield return MountBox("GvcClipped", clip: true, out var clipped);
            PaintOutsideOwnBox(clipped);
            AddControlChild(clipped);
            clipped.MarkDirtyRepaint();
            yield return WaitRealtime(0.8);
            var clippedControl = GreenPixels();
            var clippedPaint = RedPixels();

            // Assert — the paint reaches the screen at all (so the instrument works), the control child is
            // gone (so the clip is genuinely in effect), and the element's own out-of-box paint is gone with
            // it. All three in one comparison: any one alone is satisfiable by a broken panel.
            Assert.That((openPaint > 0, clippedControl == 0, clippedPaint == 0),
                Is.EqualTo((true, true, true)));
        }

        [UnityTest]
        public IEnumerator Given_AnOverflowHiddenElement_When_Ringed_Then_TheBandStillRenders()
        {
            // Arrange — the avatar pattern: a clipped box wearing a ring. This renders today and must keep
            // rendering; it is the regression the sibling-overlay hosting was chosen to prevent.
            NewPanel("RingClipped");
            _mounted = V.Mount(_host.Root, V.Div(name: "avatar",
                className: "w-[60px] h-[60px] mt-[80px] ml-[80px] bg-[#0000ff] ring-4 ring-[#ff0000]"));
            var avatar = _host.Root.Q<VisualElement>("avatar");
            avatar.style.overflow = Overflow.Hidden;
            AddControlChild(avatar);

            // Act
            yield return WaitRealtime(0.8);
            var control = GreenPixels();
            var band = RedPixels();

            // Assert — the control child is clipped away (so the element's own overflow clip really is on)
            // AND the band is on screen anyway.
            Assert.That((control == 0, band > 0), Is.EqualTo((true, true)));
        }
    }
}

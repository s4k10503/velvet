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
    /// Answers, by real GPU pixel readback, the engine question the ring band's sibling hosting rests on:
    /// whether UI Toolkit paints an absolutely-positioned sibling in child order against an in-flow one.
    /// </summary>
    /// <remarks>
    /// Child adjacency (the band directly after its own element) is pinned structurally in
    /// <c>ClipPathWrapTests</c>. Adjacency alone does not decide paint: CSS would paint a positioned sibling
    /// above every in-flow one regardless of document order, and under that rule the band would still cover
    /// the following avatar's face and the overlapping-avatar pattern would still be wrong. These cases
    /// measure which rule the engine actually follows.
    /// <para>
    /// Every sample rect is derived from the elements' measured <see cref="VisualElement.worldBound"/>, and
    /// the geometry that licenses it is folded into the same assertion. A probe built from expected instead
    /// of measured coordinates reads as a confident pass over a layout that never happened: plain USS classes
    /// are inert on this panel (it carries no stylesheet — Velvet resolves only arbitrary-value utilities such
    /// as <c>w-[60px]</c> / <c>bg-[#0000ff]</c> to inline style), so a <c>flex-row</c> that silently stayed
    /// column leaves the avatars stacked and never overlapping. The two geometry terms are what make that
    /// state fail rather than pass, so the layout here is set inline outright.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class RingSiblingPaintOrderPlaybackTests
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

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;
        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;

        // A vertical slice two pixels inside the band on the named side, spanning the middle third of the
        // element's height so no corner radius or edge anti-aliasing enters the sample.
        private static RectInt BandStrip(Rect box, bool right)
        {
            var x = right ? box.xMax + 2f : box.xMin - BandWidth + 2f;
            return new RectInt(
                Mathf.RoundToInt(x),
                Mathf.RoundToInt(box.yMin + (box.height / 3f)),
                Mathf.RoundToInt(BandWidth - 4f),
                Mathf.RoundToInt(box.height / 3f));
        }

        [UnityTest]
        public IEnumerator Given_ARingedElementOverlappedByALaterSibling_When_Rendered_Then_TheLaterFaceCoversTheBand()
        {
            // Arrange — the overlapping-avatar pattern reduced to its two load-bearing elements: a ringed
            // element and an opaque one that starts inside it. Row direction and the negative margin are set
            // inline because the equivalent utility classes need the stylesheet this panel does not carry.
            NewPanel("SiblingPaintOrder");
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "ml-[20px] mt-[70px] w-[200px] h-[60px]",
                children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] ring-[8px] ring-[#ff0000]"),
                    V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#00ff00]"),
                }));
            _host.Root.Q<VisualElement>("row").style.flexDirection = FlexDirection.Row;
            _host.Root.Q<VisualElement>("b").style.marginLeft = -20f;

            // Act
            yield return WaitRealtime(0.8);
            var a = _host.Root.Q<VisualElement>("a").worldBound;
            var b = _host.Root.Q<VisualElement>("b").worldBound;
            var covered = BandStrip(a, right: true);
            var control = BandStrip(a, right: false);
            var coveredRed = CountInPanelRect(covered, IsRed);
            var coveredGreen = CountInPanelRect(covered, IsGreen);
            var controlRed = CountInPanelRect(control, IsRed);

            // Assert — the geometry that licenses the sample and the paint result in one comparison. The two
            // geometry terms say b's face really does span the strip cut from a's right band; controlRed says
            // the band reached the screen at all on the side nothing overlaps; and the strip reads as b's
            // green rather than the band's red, which is child-order paint.
            Assert.That(
                (b.xMin < covered.xMin, b.xMax > covered.xMax, Mathf.Abs(a.yMin - b.yMin) < 0.5f,
                    controlRed > 0, coveredRed == 0, coveredGreen > 0),
                Is.EqualTo((true, true, true, true, true, true)),
                $"a={a} b={b} strip={covered} coveredRed={coveredRed} coveredGreen={coveredGreen} "
                + $"controlRed={controlRed}");
        }

        // The horizontal slice through both bands' TOP edges where they overlap, inset from every edge so no
        // corner or anti-aliased boundary enters it. Above both faces, so only the two bands can colour it and
        // whichever reads back is the one the engine painted last.
        private static RectInt SharedTopBandStrip(Rect a, Rect b)
            => new(
                Mathf.RoundToInt(b.xMin + 5f),
                Mathf.RoundToInt(a.yMin - BandWidth + 2f),
                Mathf.RoundToInt(a.xMax - 5f - (b.xMin + 5f)),
                Mathf.RoundToInt(BandWidth - 4f));

        [UnityTest]
        public IEnumerator Given_TwoRingedSiblingsWhoseBandsOverlap_When_TheEarlierBandReSyncsLast_Then_TheLaterChildStillPaintsOnTop()
        {
            // Arrange — two heavily overlapping ringed siblings whose bands cross at their top edges, in
            // distinguishable colours. Ordering the bands by ATTACH time rather than by child index is what
            // made identical markup render two ways, because a variant-applied ring attaches whenever the
            // variant fires.
            NewPanel("InterBandOrder");
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "ml-[20px] mt-[70px] w-[200px] h-[60px]",
                children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] ring-[8px] ring-[#ff0000]"),
                    V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#0000ff] ring-[8px] ring-[#00ff00]"),
                }));
            _host.Root.Q<VisualElement>("row").style.flexDirection = FlexDirection.Row;
            _host.Root.Q<VisualElement>("b").style.marginLeft = -40f;
            yield return WaitRealtime(0.8);

            var elementA = _host.Root.Q<VisualElement>("a");
            var a = elementA.worldBound;
            var b = _host.Root.Q<VisualElement>("b").worldBound;
            var shared = SharedTopBandStrip(a, b);
            var greenAtMount = CountInPanelRect(shared, IsGreen);
            var redAtMount = CountInPanelRect(shared, IsRed);

            // Act — re-sync the EARLIER sibling's band, which is the call a variant re-application makes and
            // therefore the last band touch in this frame.
            var binding = _mounted.Root.Reconciler.Context.RingBindings[elementA];
            RingOverlay.Sync(elementA, binding, binding.Spec, binding.ClassNames);
            yield return WaitRealtime(0.8);
            var greenAfterReSync = CountInPanelRect(shared, IsGreen);
            var redAfterReSync = CountInPanelRect(shared, IsRed);

            // Assert — the geometry that licenses the sample, then the same verdict before and after: the
            // later CHILD's band is on top both times. Ordering by attach time would flip the strip to the
            // re-synced sibling's red on the second read while the first stayed green.
            Assert.That(
                (b.xMin < a.xMax, Mathf.Abs(a.yMin - b.yMin) < 0.5f, shared.width > 0,
                    greenAtMount > 0, redAtMount == 0, greenAfterReSync > 0, redAfterReSync == 0),
                Is.EqualTo((true, true, true, true, true, true, true)),
                $"a={a} b={b} strip={shared} atMount(green={greenAtMount},red={redAtMount}) "
                + $"afterReSync(green={greenAfterReSync},red={redAfterReSync})");
        }

        [UnityTest]
        public IEnumerator Given_ARowOfOverlappingAvatars_When_TheBundledSheetIsAttached_Then_FlexRowLaysThemOutOverlapping()
        {
            // Arrange — the same overlap expressed as the real utility classes rather than inline style, with
            // the bundled stylesheet attached. Without the sheet `flex flex-row` is inert and UI Toolkit's
            // default column direction stacks the avatars, which reads as a plausible horizontal offset if only
            // x is inspected. This separates that inertness from a layout defect in Velvet: with the sheet
            // present the declared row and the declared overlap must both land.
            NewPanel("SheetBackedRow");
            _host.Root.LoadBundledStyleUtilitiesForTest();
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "flex flex-row ml-[20px] mt-[70px]",
                children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[60px] h-[60px] bg-[#0000ff] ring-[8px] ring-[#ff0000]"),
                    V.Div(name: "b", className: "w-[60px] h-[60px] bg-[#00ff00] ml-[-20px]"),
                }));

            // Act
            yield return WaitRealtime(0.8);
            var a = _host.Root.Q<VisualElement>("a").worldBound;
            var b = _host.Root.Q<VisualElement>("b").worldBound;

            // Assert — measured against the DECLARED values: same top (a row, not a column) and b starting
            // exactly the declared 20px inside a's right edge.
            Assert.That((b.yMin - a.yMin, b.xMin - a.xMax), Is.EqualTo((0f, -20f)), $"a={a} b={b}");
        }
    }
}

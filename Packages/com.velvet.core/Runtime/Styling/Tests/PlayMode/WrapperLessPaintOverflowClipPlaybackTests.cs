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
    /// Surveys, by real GPU pixel readback, what each of Velvet's wrapper-less paints loses when its own
    /// element also carries <c>overflow: hidden</c>. The affected set is a fact about the engine's clip, so it
    /// is measured per paint rather than extrapolated from any one of them.
    /// </summary>
    /// <remarks>
    /// <c>RingOverflowClipPlaybackTests</c> pins that the clip reaches an element's own generated content at
    /// all, which is why the ring band is hosted on a sibling. These cases pin WHERE it cuts — the PADDING box,
    /// so a paint in the border band dies with the bleed — and which paints that costs. CSS clips neither a
    /// box-shadow nor a border by the element's own overflow, so every loss here is a parity deviation;
    /// <c>Documentation~/styling-variants.md</c> owns what a user writes instead.
    /// <para>
    /// Each case carries a control whose disappearance proves the clip took: a GREEN child overflowing the
    /// opposite side, or a second reading of the same paint on an unclipped twin. Without one, "the paint was
    /// clipped" and "the clip never applied" are indistinguishable — a probe built from arbitrary-value
    /// utilities on a panel carrying no stylesheet looks correctly constructed while the class under test is
    /// silently inert. The sheet is attached here and the overflow is set inline, so neither is in doubt.
    /// </para>
    /// </remarks>
    [Timeout(600000)]
    internal sealed class WrapperLessPaintOverflowClipPlaybackTests
    {
        private const int Size = 240;

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

        private void NewPanel(string name) => NewPanel(name, Color.black);

        // backdrop: what shows through wherever the card fails to paint. Black for the bleed cases, magenta
        // for the border cases, where "the border is missing" and "the card has a hole where its border was"
        // are different findings and only a non-black backdrop separates them.
        private void NewPanel(string name, Color backdrop)
        {
            DisposePanel();
            _host = new RenderTexturePanelHost(name, Size, Size);
            _host.Root.style.backgroundColor = backdrop;
            _host.Root.LoadBundledStyleUtilitiesForTest();
        }

        private static bool IsRed(Color32 p) => p.r > 140 && p.g < 90 && p.b < 90;
        private static bool IsGreen(Color32 p) => p.g > 140 && p.r < 90 && p.b < 90;
        private static bool IsBlue(Color32 p) => p.b > 140 && p.r < 90 && p.g < 90;
        private static bool IsMagenta(Color32 p) => p.r > 140 && p.b > 140 && p.g < 90;

        // A soft SDF halo never reaches the saturated-red threshold, so the shadow is counted by red DOMINANCE
        // over the black backdrop instead.
        private static bool IsRedTinted(Color32 p) => p.r > 8 && p.r > p.g + 6 && p.r > p.b + 6;

        // GetPixels32 returns the texture bottom-up while panel y grows downward, so the row index is flipped.
        private Color32[] Frame() =>
            RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));

        private int Count(RectInt rect, Func<Color32, bool> match)
        {
            var pixels = Frame();
            var n = 0;
            for (var y = Mathf.Max(0, rect.yMin); y < Mathf.Min(Size, rect.yMax); y++)
            {
                for (var x = Mathf.Max(0, rect.xMin); x < Mathf.Min(Size, rect.xMax); x++)
                {
                    if (match(pixels[((Size - 1 - y) * Size) + x])) n++;
                }
            }
            return n;
        }

        private int CountAll(Func<Color32, bool> match) => Count(new RectInt(0, 0, Size, Size), match);

        // The leftmost matching column in the frame, or -1. Enough to say where the clip cut.
        private int LeftmostMatch(Func<Color32, bool> match)
        {
            var pixels = Frame();
            for (var x = 0; x < Size; x++)
            {
                for (var y = 0; y < Size; y++)
                {
                    if (match(pixels[((Size - 1 - y) * Size) + x])) return x;
                }
            }
            return -1;
        }

        // Sticks out to the RIGHT of the box, so it vanishes exactly when the box's own clip is in effect
        // without ever touching the strips the paints are read from.
        private static void AddControlChild(VisualElement box)
        {
            box.Add(new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, left = 85, top = 15, width = 25, height = 30,
                    backgroundColor = new Color(0f, 1f, 0f, 1f),
                },
            });
        }

        // Every case mounts its box at the same place and size, so one strip definition serves them all: a
        // column entirely to the LEFT of the box, where only a paint that leaves the box can land.
        private const string BoxBase = "w-[80px] h-[80px] mt-[70px] ml-[70px]";
        private static readonly RectInt LeftOfBox = new(30, 70, 38, 80);
        private static readonly RectInt TopBorderBand = new(70, 70, 80, 12);
        private static readonly RectInt BoxRect = new(70, 70, 80, 80);

        private IEnumerator MountBox(string name, string className, bool clip)
        {
            NewPanel(name);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: className));
            var box = _host.Root.Q<VisualElement>("box");
            if (clip) box.style.overflow = Overflow.Hidden;
            AddControlChild(box);
            yield return WaitRealtime(0.9);
        }

        [UnityTest]
        public IEnumerator Given_AnOverflowHiddenElement_When_ItPaintsAcrossItsBorderBand_Then_TheClipCutsAtThePaddingBox()
        {
            // Arrange — a 10px-bordered box whose own generated content covers far more than the box, so
            // whatever survives is the clip rect itself rather than the paint's extent.
            NewPanel("ClipRect");
            _mounted = V.Mount(_host.Root,
                V.Div(name: "box", className: BoxBase + " bg-[#000040] border-[10px] border-[#000080]"));
            var box = _host.Root.Q<VisualElement>("box");
            box.style.overflow = Overflow.Hidden;
            box.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                p.MoveTo(new Vector2(-40f, -40f));
                p.LineTo(new Vector2(120f, -40f));
                p.LineTo(new Vector2(120f, 120f));
                p.LineTo(new Vector2(-40f, 120f));
                p.ClosePath();
                p.fillColor = new Color(1f, 0f, 0f, 1f);
                p.Fill();
            };
            box.MarkDirtyRepaint();

            // Act
            yield return WaitRealtime(0.9);
            var bounds = box.worldBound;
            var border = box.resolvedStyle.borderLeftWidth;
            var leftmost = LeftmostMatch(IsRed);

            // Assert — the surviving paint starts one border inside the box's own left edge, measured from the
            // laid-out box rather than the declared margins. So the clip is the PADDING box, and a paint in
            // the border band is cut even though it never left the element. The border term is what separates
            // this from a border-box clip, which would put the paint's edge at bounds.xMin.
            Assert.That((Mathf.RoundToInt(border), leftmost - Mathf.RoundToInt(bounds.xMin)), Is.EqualTo((10, 10)),
                $"bounds={bounds} border={border} leftmost={leftmost}");
        }

        [UnityTest]
        public IEnumerator Given_AShadowedElement_When_ItsOwnOverflowIsHidden_Then_TheWholeShadowIsLost()
        {
            // Arrange
            const string shadow = BoxBase + " bg-[#ffffff] shadow-[0px_0px_24px_#ff0000]";
            yield return MountBox("ShadowOpen", shadow, clip: false);
            var openBleed = Count(LeftOfBox, IsRedTinted);

            // Act
            yield return MountBox("ShadowClipped", shadow, clip: true);
            var clippedControl = CountAll(IsGreen);
            var clippedShadow = CountAll(IsRedTinted);

            // Assert — the bleed reaches the screen unclipped, the control child is gone (so the clip really
            // is on), and not one shadow pixel survives anywhere. The paint is not removed: it is cut at the
            // padding box like every other, and the surviving interior is hidden under the caster's own
            // repainted opaque fill by design, which is the whole point of the silhouette. So the only part a
            // user ever sees is the part the clip takes. Given_AShadowCasterWithNoOpaqueFill measures the
            // surviving interior directly.
            Assert.That((openBleed > 0, clippedControl == 0, clippedShadow == 0), Is.EqualTo((true, true, true)),
                $"openBleed={openBleed} clippedControl={clippedControl} clippedShadow={clippedShadow}");
        }

        [UnityTest]
        public IEnumerator Given_AShadowCasterWithNoOpaqueFill_When_ItsOwnOverflowIsHidden_Then_TheInteriorSurvivesAndOnlyTheOutsideIsCut()
        {
            // Arrange — the same caster with NO background, so nothing repaints over the silhouette and its
            // interior is visible. This is what separates "the clip removed the paint" from "the clip cut the
            // paint where it cuts every other, and the surviving part is one a user never sees".
            const string bare = BoxBase + " shadow-[0px_0px_24px_#ff0000]";
            yield return MountBox("BareShadowOpen", bare, clip: false);
            var openInside = Count(BoxRect, IsRedTinted);
            var openTotal = CountAll(IsRedTinted);

            // Act
            yield return MountBox("BareShadowClipped", bare, clip: true);
            var clippedInside = Count(BoxRect, IsRedTinted);
            var clippedTotal = CountAll(IsRedTinted);

            // Assert — the interior comes through the clip untouched while everything beyond the box goes. An
            // opaque-backed caster hides that same interior under its own repainted fill, which is why the
            // shadow reads as a total loss there without the paint having been removed.
            Assert.That((openTotal > openInside, clippedInside == openInside, clippedTotal == clippedInside),
                Is.EqualTo((true, true, true)),
                $"open inside={openInside} total={openTotal}; clipped inside={clippedInside} total={clippedTotal}");
        }

        // A strip through the LEFT border band at mid-height. Mid-height is where a shear displaces nothing, so
        // one strip definition serves the shadowed and the skewed caster alike, and it is clear of the corner
        // anti-aliasing that would otherwise decide the count.
        private static RectInt LeftBorderStrip(Rect box, float border)
            => new(Mathf.RoundToInt(box.xMin), Mathf.RoundToInt(box.center.y) - 5,
                Mathf.RoundToInt(border), 10);

        private const string BorderedCard =
            "w-[80px] h-[80px] mt-[70px] ml-[70px] bg-[#ffffff] border-[8px] border-[#0000ff]";

        private IEnumerator MountBorderedCard(string name, string extra, bool clip)
        {
            NewPanel(name, new Color(1f, 0f, 1f, 1f));
            _mounted = V.Mount(_host.Root, V.Div(name: "card", className: BorderedCard + extra));
            var card = _host.Root.Q<VisualElement>("card");
            if (clip) card.style.overflow = Overflow.Hidden;
            yield return WaitRealtime(0.9);
        }

        private (int Border, int Backdrop) SampleBorderStrip()
        {
            var card = _host.Root.Q<VisualElement>("card");
            var strip = LeftBorderStrip(card.worldBound, card.resolvedStyle.borderLeftWidth);
            return (Count(strip, IsBlue), Count(strip, IsMagenta));
        }

        [UnityTest]
        public IEnumerator Given_ABorderedShadowCaster_When_ItsOwnOverflowIsHidden_Then_TheBorderBandBecomesAHole()
        {
            // Arrange — the same bordered white card three ways. The shadow paint suppresses the native
            // background AND border and repaints both inside generated content, so the padding-box clip takes
            // the repaint with everything else; the card keeps only what lies inside its padding box.
            const string shadow = " shadow-[0px_0px_24px_#ff0000]";
            yield return MountBorderedCard("BorderedPlainClipped", "", clip: true);
            var plainClipped = SampleBorderStrip();
            yield return MountBorderedCard("BorderedShadowOpen", shadow, clip: false);
            var shadowOpen = SampleBorderStrip();

            // Act
            yield return MountBorderedCard("BorderedShadowClipped", shadow, clip: true);
            var shadowClipped = SampleBorderStrip();

            // Assert — an unshadowed card survives the identical clip with its border intact, so this is
            // Velvet's repaint being cut and not an engine-wide rule; the shadowed card's repainted border
            // reaches the screen unclipped; and under the clip the band holds no border AND shows the backdrop,
            // which is the finding — the card loses a border-wide ring of its own background too, not just its
            // border. All four in one comparison: the third term alone is satisfied by a card that never
            // painted.
            Assert.That(
                (plainClipped.Border > 0, shadowOpen.Border > 0, shadowClipped.Border, shadowClipped.Backdrop > 0),
                Is.EqualTo((true, true, 0, true)),
                $"plainClipped={plainClipped} shadowOpen={shadowOpen} shadowClipped={shadowClipped}");
        }

        [UnityTest]
        public IEnumerator Given_ABorderedSkewedCaster_When_ItsOwnOverflowIsHidden_Then_TheBorderBandBecomesAHole()
        {
            // Arrange — the skew layer owns the face on the same terms as the shadow layer, so it loses the
            // same ring. A shallow angle keeps the sheared face over the sampled strip while unclipped.
            const string skew = " skew-x-[10deg]";
            yield return MountBorderedCard("BorderedSkewOpen", skew, clip: false);
            var open = SampleBorderStrip();

            // Act
            yield return MountBorderedCard("BorderedSkewClipped", skew, clip: true);
            var clipped = SampleBorderStrip();

            // Assert — same shape as the shadow case: border present unclipped, gone under the clip, and the
            // backdrop showing through where it was.
            Assert.That((open.Border > 0, clipped.Border, clipped.Backdrop > 0), Is.EqualTo((true, 0, true)),
                $"open={open} clipped={clipped}");
        }

        [UnityTest]
        public IEnumerator Given_ASkewedElement_When_ItsOwnOverflowIsHidden_Then_TheShearOverhangIsLostAndTheFaceSurvives()
        {
            // Arrange
            const string skew = BoxBase + " bg-[#ff0000] skew-x-[30deg]";
            yield return MountBox("SkewOpen", skew, clip: false);
            var openOverhang = Count(LeftOfBox, IsRed);

            // Act
            yield return MountBox("SkewClipped", skew, clip: true);
            var clippedControl = CountAll(IsGreen);
            var clippedOverhang = Count(LeftOfBox, IsRed);
            var clippedFace = CountAll(IsRed);

            // Assert — unlike the shadow, the sheared face is mostly inside the box, so the clip trims it
            // rather than erasing it: the overhang past the box edge goes and the rest stays. The face term is
            // what distinguishes trimming from a paint that never ran.
            Assert.That((openOverhang > 0, clippedControl == 0, clippedOverhang == 0, clippedFace > 0),
                Is.EqualTo((true, true, true, true)),
                $"openOverhang={openOverhang} clippedOverhang={clippedOverhang} clippedFace={clippedFace}");
        }

        [UnityTest]
        public IEnumerator Given_ASkewedGradientElement_When_ItsOwnOverflowIsHidden_Then_TheShearOverhangIsLost()
        {
            // Arrange — a gradient alone is a background-image and is clipped to the box either way; the
            // skewed one is the case that becomes a generated-content quad, so it is the one at risk.
            const string skewGradient = BoxBase + " skew-x-[30deg] bg-gradient-to-r from-[#ff0000] to-[#ff0000]";
            yield return MountBox("SkewGradOpen", skewGradient, clip: false);
            var openOverhang = Count(LeftOfBox, IsRed);

            // Act
            yield return MountBox("SkewGradClipped", skewGradient, clip: true);
            var clippedControl = CountAll(IsGreen);
            var clippedOverhang = Count(LeftOfBox, IsRed);
            var clippedFace = CountAll(IsRed);

            // Assert — the baked gradient quad follows the sheared silhouette, so it loses exactly what the
            // painted face loses.
            Assert.That((openOverhang > 0, clippedControl == 0, clippedOverhang == 0, clippedFace > 0),
                Is.EqualTo((true, true, true, true)),
                $"openOverhang={openOverhang} clippedOverhang={clippedOverhang} clippedFace={clippedFace}");
        }

        [UnityTest]
        public IEnumerator Given_ADashedBorderElement_When_ItsOwnOverflowIsHidden_Then_TheWholeOutlineIsLost()
        {
            // Arrange
            const string border = BoxBase + " bg-[#000040] border-[10px] border-[#ff0000]";
            const string dashed = border + " border-dashed";
            yield return MountBox("DashedOpen", dashed, clip: false);
            var openBand = Count(TopBorderBand, IsRed);

            // Act — the same box clipped, then the same width and colour as a plain SOLID border, also clipped.
            yield return MountBox("DashedClipped", dashed, clip: true);
            var clippedControl = CountAll(IsGreen);
            var clippedOutline = CountAll(IsRed);
            yield return MountBox("SolidClipped", border, clip: true);
            var clippedSolid = Count(TopBorderBand, IsRed);

            // Assert — the dashed outline is drawn in the border band, which the padding-box clip excludes, so
            // the element renders with NO border at all rather than a trimmed one. The solid term is what makes
            // this a Velvet deviation rather than an engine-wide rule: UI Toolkit's own border property is not
            // painted through generated content and survives the same clip, so one style of the same border
            // renders and the other vanishes.
            Assert.That((openBand > 0, clippedControl == 0, clippedOutline == 0, clippedSolid > 0),
                Is.EqualTo((true, true, true, true)),
                $"openBand={openBand} clippedControl={clippedControl} clippedOutline={clippedOutline} "
                + $"clippedSolid={clippedSolid}");
        }

        [UnityTest]
        public IEnumerator Given_ADashedDividedChild_When_ItsOwnOverflowIsHidden_Then_TheDividerIsLost()
        {
            // Arrange — the divider is painted by the DIVIDED CHILD, on its own divider edge, so it is that
            // child's overflow that decides its fate rather than the list container's.
            const string list = "w-[120px] h-[120px] mt-[60px] ml-[60px] bg-[#000040] "
                + "divide-y-[8px] divide-[#ff0000] divide-dashed";
            var children = new Func<VNode[]>(() => new[]
            {
                V.Div(name: "first", className: "w-[120px] h-[50px]"),
                V.Div(name: "second", className: "w-[120px] h-[50px]"),
            });

            NewPanel("DivideOpen");
            _mounted = V.Mount(_host.Root, V.Div(name: "list", className: list, children: children()));
            yield return WaitRealtime(0.9);
            var openDivider = CountAll(IsRed);

            // Act
            NewPanel("DivideClipped");
            _mounted = V.Mount(_host.Root, V.Div(name: "list", className: list, children: children()));
            var second = _host.Root.Q<VisualElement>("second");
            second.style.overflow = Overflow.Hidden;
            yield return WaitRealtime(0.9);
            var clippedDivider = CountAll(IsRed);

            // Assert — the dashed divider sits in the child's own border band, so the same padding-box clip
            // removes it entirely while the layout gutter it occupies stays reserved: the row keeps its gap
            // and loses its rule.
            Assert.That((openDivider > 0, clippedDivider), Is.EqualTo((true, 0)),
                $"openDivider={openDivider} clippedDivider={clippedDivider}");
        }

        // A themeless RenderTexture panel supplies no font, so an empty runtime theme measures every label
        // 0 tall; both the font and the wrap behaviour are supplied inline through the ref, the one channel
        // that reaches a bare panel (the same incantation TextOverlinePlaybackTests uses).
        private static readonly Func<VisualElement, Action> s_enableTextMeasurement = el =>
        {
            el.style.whiteSpace = WhiteSpace.Normal;
            el.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            return null;
        };

        private IEnumerator MountOverlinedLabel(string name, bool clip)
        {
            NewPanel(name);
            _mounted = V.Mount(_host.Root, V.Label(name: "lbl",
                className: "overline text-[48px] text-[#ff0000] mt-[60px] ml-[40px]",
                text: "moon", refCallback: s_enableTextMeasurement));
            if (clip) _host.Root.Q<Label>("lbl").style.overflow = Overflow.Hidden;
            yield return WaitRealtime(0.9);
        }

        [UnityTest]
        public IEnumerator Given_AnOverlinedLabel_When_ItsOwnOverflowIsHidden_Then_TheRuleIsUnaffected()
        {
            // Arrange
            yield return MountOverlinedLabel("OverlineOpen", clip: false);
            var open = CountAll(IsRed);

            // Act
            yield return MountOverlinedLabel("OverlineClipped", clip: true);
            var clipped = CountAll(IsRed);

            // Assert — the rule is placed in the label's CONTENT box, inside the clip rect on every side, so
            // this is the one wrapper-less paint the element's own overflow costs nothing. Comparing the two
            // counts rather than asserting a bare non-zero is what would catch a rule that partly survived.
            Assert.That((open > 0, clipped), Is.EqualTo((true, open)), $"open={open} clipped={clipped}");
        }

        [UnityTest]
        public IEnumerator Given_AScaledElement_When_ItPaintsBesideAnAdjacentSibling_Then_OnlyItsOwnPaintFollowsTheScale()
        {
            // Arrange — the same mark twice over: once in the element's own generated content, once as the
            // absolutely-positioned adjacent sibling the ring band uses. Placed apart so neither can hide the
            // other. This is what hosting a paint outside its caster costs, so it is measured rather than
            // assumed: a caster that scales takes its own paint with it and leaves a sibling behind.
            NewPanel("TransformFollowing");
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "w-[240px] h-[240px]",
                children: new[] { V.Div(name: "box", className: "w-[60px] h-[60px] mt-[90px] ml-[90px] bg-[#000040]") }));
            var box = _host.Root.Q<VisualElement>("box");
            box.generateVisualContent += mgc =>
            {
                var p = mgc.painter2D;
                p.BeginPath();
                p.MoveTo(new Vector2(-20f, 20f));
                p.LineTo(new Vector2(-4f, 20f));
                p.LineTo(new Vector2(-4f, 40f));
                p.LineTo(new Vector2(-20f, 40f));
                p.ClosePath();
                p.fillColor = new Color(1f, 0f, 0f, 1f);
                p.Fill();
            };
            box.parent.Add(new VisualElement
            {
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute, left = 70, top = 150, width = 16, height = 20,
                    backgroundColor = new Color(0f, 1f, 0f, 1f),
                },
            });
            yield return WaitRealtime(0.9);
            var ownAtRest = LeftmostMatch(IsRed);
            var siblingAtRest = LeftmostMatch(IsGreen);

            // Act
            box.style.scale = new StyleScale(new Scale(new Vector3(2f, 2f, 1f)));
            box.MarkDirtyRepaint();
            yield return WaitRealtime(0.9);
            var ownScaled = LeftmostMatch(IsRed);
            var siblingScaled = LeftmostMatch(IsGreen);

            // Assert — both marks are on screen before the scale, the element's own paint moves with it, and
            // the sibling does not. The first term is what makes an empty frame fail rather than read as "the
            // sibling did not move".
            Assert.That((ownAtRest > 0, siblingAtRest > 0, ownScaled < ownAtRest, siblingScaled == siblingAtRest),
                Is.EqualTo((true, true, true, true)),
                $"own={ownAtRest}->{ownScaled} sibling={siblingAtRest}->{siblingScaled}");
        }
    }
}

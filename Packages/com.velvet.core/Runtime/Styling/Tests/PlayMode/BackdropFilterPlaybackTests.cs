using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Measures the <c>backdrop-*</c> utilities on a panel rendering into a RenderTexture, by GPU pixel readback or by
    /// the renderer's reading of the frame it drew: a red half and a blue half meet at x = 60 (at y = 60 in a
    /// column), and the element under test, absolutely positioned over them, carries no background of its own, so
    /// what it shows is the filtered backdrop.
    /// </summary>
    /// <remarks>
    /// The panel carries no stylesheet, so the row direction and the absolute placement are set inline.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class BackdropFilterPlaybackTests
    {
        private const int Size = 120;
        private const int Edge = 60;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private Color32[] _pixels;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        // The red and blue halves, and the element under test over them at (left, top) with the given size.
        private IEnumerator Render(string className, float left, float top, float size, VNode child = null,
            Action arrange = null, FlexDirection direction = FlexDirection.Row)
        {
            var across = direction == FlexDirection.Row;
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]",
                children: new VNode[]
                {
                    V.Div(name: "red", className: across
                        ? $"w-[{Edge}px] h-[{Size}px] bg-[#ff0000]"
                        : $"w-[{Size}px] h-[{Edge}px] bg-[#ff0000]"),
                    V.Div(name: "blue", className: across
                        ? $"w-[{Size - Edge}px] h-[{Size}px] bg-[#0000ff]"
                        : $"w-[{Size}px] h-[{Size - Edge}px] bg-[#0000ff]"),
                    V.Div(name: "glass", className: $"w-[{size}px] h-[{size}px] {className}",
                        children: child == null ? null : new[] { child }),
                }));
            _host.Root.Q<VisualElement>("stage").style.flexDirection = direction;
            var glass = Glass();
            glass.style.position = Position.Absolute;
            glass.style.left = left;
            glass.style.top = top;
            arrange?.Invoke();
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            _pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
        }

        private VisualElement Glass() => _host.Root.Q<VisualElement>("glass");

        private sealed class Flag : Store<bool>
        {
            public Flag() : base(false) { }

            public void Set(bool value) => SetState(_ => value);

            protected override void ResetCore() => SetState(_ => false);
        }

        private static Flag s_dim;

        [Component]
        private static VNode Dimming()
        {
            var dim = Hooks.UseStore(s_dim, x => x);
            return V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px] bg-[#ff0000]", children: new VNode[]
            {
                V.Div(name: "glass", className: "w-[40px] h-[40px] " + (dim ? "backdrop-brightness-50" : "backdrop-brightness-100")),
            });
        }

        private Color32 ReadAt(int x, int y)
            => RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(x, Size - 1 - y, 1, 1))[0];

        // The pixel at a panel point; the readback runs bottom-up while panel y grows downward.
        private Color32 At(int x, int y) => _pixels[((Size - 1 - y) * Size) + x];

        // Holds a good share of both halves, as a blur across the edge leaves a pixel next to it.
        private static bool Mixed(Color32 p) => p.r > 60 && p.b > 60;

        private static bool PureRed(Color32 p) => p.r > 200 && p.g < 20 && p.b < 20;

        private static bool PureBlue(Color32 p) => p.b > 200 && p.r < 20 && p.g < 20;

        private bool Placed(VisualElement element, float left, float top, float size)
        {
            var box = element.worldBound;
            var blue = _host.Root.Q<VisualElement>("blue").worldBound;
            return Near(box.x, left) && Near(box.y, top) && Near(box.width, size) && Near(box.height, size)
                && (Near(blue.xMin, Edge) || Near(blue.yMin, Edge));
        }

        private static bool Near(float a, float b) => Mathf.Abs(a - b) < 0.01f;

        [UnityTest]
        public IEnumerator Given_ABackdropBlurAcrossAnEdge_When_Rendered_Then_TheEdgeBlursInsideTheBoxAndStaysSharpOutsideIt()
        {
            // Arrange
            yield return Render("backdrop-blur-md", 30f, 30f, 60f);

            // Act
            var inside = At(Edge + 1, 60);
            var outside = At(Edge + 1, 10);

            // Assert
            Assert.That((Placed(Glass(), 30f, 30f, 60f), Mixed(inside), PureBlue(outside)),
                Is.EqualTo((true, true, true)), $"inside={inside} outside={outside} glass={Glass().worldBound}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropBlurBehindAChild_When_Rendered_Then_TheChildPaintsUnfiltered()
        {
            // Arrange — a green square astride the edge, inside the element.
            var dot = V.Div(name: "dot", className: "w-[10px] h-[10px] bg-[#00ff00]");
            yield return Render("backdrop-blur-md", 30f, 30f, 60f, dot, () =>
            {
                var style = _host.Root.Q<VisualElement>("dot").style;
                style.position = Position.Absolute;
                style.left = Edge - 35f;
                style.top = 0f;
            });
            var dotElement = _host.Root.Q<VisualElement>("dot");

            // Act
            var onDot = At(Edge, 34);
            var beside = At(Edge + 1, 60);

            // Assert
            Assert.That(
                (Placed(Glass(), 30f, 30f, 60f), dotElement.worldBound.Contains(new Vector2(Edge + 0.5f, 34.5f)),
                    Mixed(beside), onDot.g > 200 && onDot.r < 20 && onDot.b < 20),
                Is.EqualTo((true, true, true, true)), $"onDot={onDot} beside={beside} dot={dotElement.worldBound}");
        }

        [UnityTest]
        public IEnumerator Given_ARoundedBackdropBlur_When_Rendered_Then_TheCornerOutsideTheRadiusStaysSharp()
        {
            // Arrange — the box's top-left corner lies 15px left of the edge, within reach of a 24px blur.
            yield return Render("rounded-[30px] backdrop-blur-xl", 45f, 30f, 60f);

            // Act
            var corner = At(47, 32);
            var inside = At(Edge - 2, 60);

            // Assert
            Assert.That((Placed(Glass(), 45f, 30f, 60f), Mixed(inside), PureRed(corner)),
                Is.EqualTo((true, true, true)), $"corner={corner} inside={inside} glass={Glass().worldBound}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropOverAHorizontalEdge_When_Rendered_Then_EachRowShowsWhatLiesBehindIt()
        {
            // Arrange — red above y = 60 and blue below it; the box spans 30..70, so a backdrop read upside down
            // would put blue at its top.
            yield return Render("backdrop-brightness-50", 30f, 30f, 40f, direction: FlexDirection.Column);

            // Act
            var top = At(50, 35);
            var bottom = At(50, 65);

            // Assert — each half at half its brightness.
            Assert.That(
                (Placed(Glass(), 30f, 30f, 40f), top.r > 100 && top.r < 160 && top.b < 20,
                    bottom.b > 100 && bottom.b < 160 && bottom.r < 20),
                Is.EqualTo((true, true, true)), $"top={top} bottom={bottom}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropBlurInsideARoundedOverflowClip_When_Rendered_Then_TheCornerOutsideTheClipStaysSharp()
        {
            // Arrange — the element fills an ancestor that clips to a 30px radius; the element itself is square.
            var clipped = V.Div(name: "glass-inner", className: "w-[60px] h-[60px] backdrop-blur-xl");
            yield return Render(string.Empty, 45f, 30f, 60f, clipped, () =>
            {
                var style = Glass().style;
                style.overflow = Overflow.Hidden;
                style.borderTopLeftRadius = style.borderTopRightRadius = 30f;
                style.borderBottomLeftRadius = style.borderBottomRightRadius = 30f;
            });

            // Act
            var corner = At(47, 32);
            var inside = At(Edge - 2, 60);

            // Assert
            Assert.That((Placed(Glass(), 45f, 30f, 60f), Mixed(inside), PureRed(corner)),
                Is.EqualTo((true, true, true)), $"corner={corner} inside={inside}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropBlurUnderAClipPath_When_Rendered_Then_ThePixelsOutsideThePathStaySharp()
        {
            // Arrange — the clip wrapper stands in for the element in its parent, so the placement goes on it.
            yield return Render("clip-path-[circle(50%)] backdrop-blur-xl", 45f, 30f, 60f, arrange: () =>
            {
                var inner = Glass().style;
                inner.position = Position.Relative;
                inner.left = 0f;
                inner.top = 0f;
                var wrapper = Glass().parent.style;
                wrapper.position = Position.Absolute;
                wrapper.left = 45f;
                wrapper.top = 30f;
            });

            // Act
            var corner = At(47, 32);
            var inside = At(Edge - 2, 60);

            // Assert
            var box = Glass().worldBound;
            Assert.That((Near(box.x, 45f) && Near(box.y, 30f) && Near(box.width, 60f), Mixed(inside), PureRed(corner)),
                Is.EqualTo((true, true, true)), $"corner={corner} inside={inside} box={box}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropFilterListInvertThenBrightness_When_Rendered_Then_ItRunsInTheOrderWritten()
        {
            // Arrange — over the red half only. Invert first gives (0, 0.5, 0.5); the utilities' order would give
            // (0.5, 1, 1).
            yield return Render("backdrop-filter-[invert(1)_brightness(0.5)]", 5f, 30f, 40f);

            // Act
            var inside = At(25, 50);

            // Assert
            Assert.That((Placed(Glass(), 5f, 30f, 40f), inside.r < 20 && inside.g > 100 && inside.g < 160
                    && inside.b > 100 && inside.b < 160),
                Is.EqualTo((true, true)), $"inside={inside}");
        }

        [UnityTest]
        public IEnumerator Given_TransitionAll_When_TheBackdropBrightnessHalves_Then_HalfTheDurationInPaintsThreeQuarters()
        {
            // Arrange — a held clock, so the frame read is the one half way through the second.
            var clock = new HeldMotionClock();
            s_dim = new Flag();
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            _mounted = V.Mount(_host.Root, V.Component(Dimming, key: "root"), new MountOptions { MotionClock = clock });
            var style = Glass().style;
            style.position = Position.Absolute;
            style.left = 40f;
            style.top = 40f;
            style.transitionProperty = new System.Collections.Generic.List<StylePropertyName> { new("all") };
            style.transitionDuration = new System.Collections.Generic.List<TimeValue> { new(1f, TimeUnit.Second) };
            style.transitionTimingFunction = new System.Collections.Generic.List<EasingFunction> { new(EasingMode.Linear) };
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            var before = ReadAt(60, 60);

            // Act
            s_dim.Set(true);
            _mounted.FlushStateForTest();
            clock.Now += 0.5;
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
            var halfWay = ReadAt(60, 60);

            // Assert — red at brightness 1, then at 0.75 of it: 191.
            Assert.That((before.r > 240, halfWay.r > 175 && halfWay.r < 207),
                Is.EqualTo((true, true)), $"before={before} halfWay={halfWay}");
        }

        [UnityTest]
        public IEnumerator Given_ADropShadowOverATransparentBackdrop_When_Rendered_Then_TheShadowFillsWhereTheBackdropWasClear()
        {
            // Arrange — a red square on a clear target, under a box casting its backdrop's shadow 10px right in blue.
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            var settings = ((IRuntimePanel)_host.Root.panel).panelSettings;
            settings.clearColor = true;
            settings.colorClearValue = Color.clear;
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]", children: new VNode[]
            {
                V.Div(name: "square", className: "w-[20px] h-[20px] bg-[#ff0000]"),
                V.Div(name: "glass", className: "w-[80px] h-[80px] backdrop-filter-[drop-shadow(10px_0_0_#0000ff)]"),
            }));
            foreach (var (name, at) in new[] { ("square", 40f), ("glass", 20f) })
            {
                var style = _host.Root.Q<VisualElement>(name).style;
                style.position = Position.Absolute;
                style.left = at;
                style.top = at;
            }
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Act
            var source = ReadAt(45, 50);
            var shadow = ReadAt(65, 50);
            var clear = ReadAt(65, 30);

            // Assert
            Assert.That((PureRed(source), shadow.b > 200 && shadow.r < 20 && shadow.a > 200, clear.a < 10),
                Is.EqualTo((true, true, true)), $"source={source} shadow={shadow} clear={clear}");
        }

        [UnityTest]
        public IEnumerator Given_BrightnessAndInvertWrittenInEitherOrder_When_Rendered_Then_BrightnessAppliesFirst()
        {
            // Arrange — over the red half only. Brightness first gives (0.5, 1, 1); invert first would give (0, 0.5, 0.5).
            yield return Render("backdrop-invert backdrop-brightness-50", 5f, 30f, 40f);

            // Act
            var inside = At(25, 50);
            var outside = At(25, 10);

            // Assert
            Assert.That(
                (Placed(Glass(), 5f, 30f, 40f), inside.r > 100 && inside.r < 160 && inside.g > 220 && inside.b > 220,
                    PureRed(outside)),
                Is.EqualTo((true, true, true)), $"inside={inside} outside={outside}");
        }

        [UnityTest]
        public IEnumerator Given_ABackdropBlurPastTheShadersReach_When_Rendered_Then_APixelBeyondThatReachFromTheEdgeIsBlended()
        {
            // Arrange — a red block 1000 pixels wide and blue after it, under a box over both. A deviation of 1000
            // reaches 3000 pixels, past the 1024 one pass of the shader holds; the sample sits 1100 pixels from the
            // edge, where a blur cut at 1024 pixels sees only blue.
            const int width = 2400;
            const int sample = 2100;
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), width, 16);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{width}px] h-[16px]", children: new VNode[]
            {
                V.Div(name: "red", className: "w-[1000px] h-[16px] bg-[#ff0000]"),
                V.Div(name: "blue", className: $"w-[{width - 1000}px] h-[16px] bg-[#0000ff]"),
                V.Div(name: "glass", className: $"w-[{width}px] h-[16px] backdrop-blur-[1000px]"),
            }));
            _host.Root.Q<VisualElement>("stage").style.flexDirection = FlexDirection.Row;
            var glass = Glass();
            glass.style.position = Position.Absolute;
            glass.style.left = 0f;
            glass.style.top = 0f;
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Act
            var far = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(sample, 8, 1, 1))[0];

            // Assert
            Assert.That((Near(glass.worldBound.width, width), far.r > 10 && far.b > 100),
                Is.EqualTo((true, true)), $"far={far} glass={glass.worldBound}");
        }

        // The renderer's reading of the target as it began the last layer it drew.
        private static BackdropFrame LastFrame()
            => (BackdropFrame)typeof(BackdropRenderer).GetField("s_frame", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        // GREEN_ON_BASE(characterization): the base copies the target from the box's own left edge.
        [UnityTest]
        public IEnumerator Given_ABackdropBlurAwayFromTheTargetsCorner_When_Rendered_Then_TheRegionItCopiesStartsAtTheBoxsLeftEdge()
        {
            // Arrange — the box's left edge stands at x = 30.
            yield return Render("backdrop-blur-md", 30f, 30f, 60f);

            // Act
            var region = LastFrame().Region;

            // Assert — the pixel column holding that edge, or the one before it.
            Assert.That(region.x, Is.InRange(29, 30), $"region={region}");
        }

        // GREEN_ON_BASE(characterization): the base reads one target pixel per point of a box on a panel at scale 1.
        [UnityTest]
        public IEnumerator Given_ABackdropBlurOnAPanelAtScaleOne_When_Rendered_Then_ItsDeviationIsTakenAtOnePixelPerPoint()
        {
            // Arrange
            yield return Render("backdrop-blur-md", 30f, 30f, 60f);

            // Act
            var scale = LastFrame().PixelScale;

            // Assert
            Assert.That(scale, Is.EqualTo(1f).Within(1e-3f));
        }

        // GREEN_ON_BASE(characterization): the base samples the captured region mirrored past its edges.
        [UnityTest]
        public IEnumerator Given_ABackdropBlurWhoseFirstColumnDiffersFromTheNext_When_Rendered_Then_ThatColumnReadsItsNeighboursMirrored()
        {
            // Arrange — a 120px box from x = 60 whose first column is red, whose next 79 are green and whose last 40
            // are blue. Mirrored, an 8px blur at that first column puts a tenth of its weight on red, on the column and
            // its mirror image; clamped, every tap left of it is red, and repeated, every one is the blue end. The
            // target left of the box holds the box's first 60 columns mirrored, so a copy reaching past the box's
            // left edge reads the same.
            const int width = 200;
            const int height = 20;
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), width, height);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{width}px] h-[{height}px]",
                children: new VNode[]
                {
                    V.Div(className: $"w-[59px] h-[{height}px] bg-[#00ff00]"),
                    V.Div(className: $"w-[2px] h-[{height}px] bg-[#ff0000]"),
                    V.Div(className: $"w-[79px] h-[{height}px] bg-[#00ff00]"),
                    V.Div(className: $"w-[60px] h-[{height}px] bg-[#0000ff]"),
                    V.Div(name: "glass", className: $"w-[120px] h-[{height}px] backdrop-blur-sm"),
                }));
            _host.Root.Q<VisualElement>("stage").style.flexDirection = FlexDirection.Row;
            var glass = Glass().style;
            glass.position = Position.Absolute;
            glass.left = 60f;
            glass.top = 0f;
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Act
            var first = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(60, height / 2, 1, 1))[0];

            // Assert — nine tenths green, where clamping or repeating leaves about half.
            Assert.That((int)first.g, Is.GreaterThan(175), $"first={first}");
        }

        // GREEN_ON_BASE(characterization): the base runs a pending blur's second half before a drop shadow reads it.
        [UnityTest]
        public IEnumerator Given_ABlurThenADropShadowOverAHorizontalEdge_When_Rendered_Then_TheEdgeBlursAcrossTheRows()
        {
            // Arrange — red above y = 60 and blue below it. An opaque backdrop covers its own shadow, so what shows
            // is the blur.
            yield return Render("backdrop-filter-[blur(8px)_drop-shadow(0_0_0_#0000ff)]", 30f, 30f, 60f,
                direction: FlexDirection.Column);

            // Act
            var below = At(50, 61);

            // Assert
            Assert.That(Mixed(below), Is.True, $"below={below}");
        }

        // GREEN_ON_BASE(characterization): the base blurs a drop shadow's alpha along x as well as along y.
        [UnityTest]
        public IEnumerator Given_ABlurredDropShadowOverATransparentBackdrop_When_Rendered_Then_ItFadesPastTheShadowsSideEdge()
        {
            // Arrange — a red square from (40, 40) on a clear target, under a box casting its backdrop's shadow 10px
            // right in blue with a 4px deviation: the shadow's right edge stands at x = 70.
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size, clear: true);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]", children: new VNode[]
            {
                V.Div(name: "square", className: "w-[20px] h-[20px] bg-[#ff0000]"),
                V.Div(name: "glass", className: "w-[80px] h-[80px] backdrop-filter-[drop-shadow(10px_0_4px_#0000ff)]"),
            }));
            foreach (var (name, at) in new[] { ("square", 40f), ("glass", 20f) })
            {
                var style = _host.Root.Q<VisualElement>(name).style;
                style.position = Position.Absolute;
                style.left = at;
                style.top = at;
            }
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Act — 2px past that edge, on the square's middle row.
            var beside = ReadAt(72, 50);

            // Assert — a quarter of the shadow's alpha; a shadow sharp along x leaves none.
            Assert.That((int)beside.a, Is.GreaterThan(25), $"beside={beside}");
        }

        // GREEN_ON_BASE(characterization): the base runs the colour functions before a halved blur on its first halving.
        [UnityTest]
        public IEnumerator Given_AnInvertBeforeABlurPastTheShadersReach_When_Rendered_Then_TheBlurredBackdropIsFullyGreen()
        {
            // Arrange — a 400px deviation reaches 1200 pixels, which one halving brings inside the shader's 1024.
            yield return Render("backdrop-filter-[invert(1)_blur(400px)]", 30f, 30f, 60f);

            // Act
            var inside = At(60, 60);

            // Assert — the red and the blue under the box carry no green, and inverted both carry all of it.
            Assert.That((int)inside.g, Is.GreaterThan(200), $"inside={inside}");
        }

        // GREEN_ON_BASE(characterization): the base binds the panel's target again after blitting a multisampled one.
        [UnityTest]
        public IEnumerator Given_ABackdropBrightnessOnAMultisampledTarget_When_Rendered_Then_TheRedUnderItIsHalved()
        {
            // Arrange — over the red half only. A multisampled target is captured by a blit rather than a copy, and a
            // brightness alone runs no pass of its own after it.
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            _host.TargetTexture.Release();
            _host.TargetTexture.antiAliasing = 4;
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]", children: new VNode[]
            {
                V.Div(className: $"w-[{Edge}px] h-[{Size}px] bg-[#ff0000]"),
                V.Div(className: $"w-[{Size - Edge}px] h-[{Size}px] bg-[#0000ff]"),
                V.Div(name: "glass", className: "w-[40px] h-[40px] backdrop-brightness-50"),
            }));
            _host.Root.Q<VisualElement>("stage").style.flexDirection = FlexDirection.Row;
            var glass = Glass().style;
            glass.position = Position.Absolute;
            glass.left = 5f;
            glass.top = 30f;
            yield return WaitRealtime(0.8);

            // Act
            var inside = Resolved(_host.TargetTexture, 25, 50);

            // Assert — red at brightness 0.5: 128.
            Assert.That((int)inside.r, Is.InRange(100, 160), $"inside={inside}");
        }

        private static Color32 Resolved(RenderTexture target, int x, int y)
        {
            var previous = RenderTexture.active;
            var plain = RenderTexture.GetTemporary(target.width, target.height, 0);
            try
            {
                Graphics.Blit(target, plain);
                return RenderTexturePixelReader.ReadPixels(plain, new RectInt(x, target.height - 1 - y, 1, 1))[0];
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(plain);
            }
        }

        // GREEN_ON_BASE(characterization): the base paints a backdrop under a filtered ancestor over its own box alone.
        [UnityTest]
        public IEnumerator Given_ABackdropBlurUnderAFilteredAncestor_When_Rendered_Then_ItPaintsAcrossItsBoxAlone()
        {
            // Arrange — a 40px box from x = 60 over a blue fill, both inside a stage carrying a filter at its identity
            // amount; the inversion paints the box yellow.
            var identity = new FilterFunction(FilterFunctionType.Grayscale);
            identity.AddParameter(new FilterParameter(0f));
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]", children: new VNode[]
            {
                V.Div(className: $"w-[{Size}px] h-[{Size}px] bg-[#0000ff]"),
                V.Div(name: "glass", className: "w-[40px] h-[40px] backdrop-filter-[blur(4px)_invert(1)]"),
            }));
            _host.Root.Q<VisualElement>("stage").style.filter = new List<FilterFunction> { identity };
            var glass = Glass().style;
            glass.position = Position.Absolute;
            glass.left = 60f;
            glass.top = 30f;
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
            var row = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, Size - 1 - 50, Size, 1));

            // Act
            var yellow = 0;
            foreach (var p in row)
            {
                yellow += p.r > 200 && p.g > 200 && p.b < 60 ? 1 : 0;
            }

            // Assert — the box's 40 columns, give or take the one at either edge.
            Assert.That(yellow, Is.InRange(38, 42), $"yellow={yellow} at x=60: {row[60]} x=58: {row[58]}");
        }

        // A blue stage under a 20px clip at (10, 40), scaled by 1.02 from its corner and moved 0.4px right, so that
        // its right edge stands at x = 30.8, part way into column 30. The clip holds a 40px box running past that edge.
        private IEnumerator RenderUnderAFractionalClip(string className, int antiAliasing)
        {
            _host = new RenderTexturePanelHost(nameof(BackdropFilterPlaybackTests), Size, Size);
            _host.TargetTexture.Release();
            _host.TargetTexture.antiAliasing = antiAliasing;
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px] bg-[#0000ff]",
                children: new VNode[]
                {
                    V.Div(name: "clip", className: "w-[20px] h-[20px]",
                        children: new VNode[] { V.Div(name: "glass", className: $"w-[40px] h-[20px] {className}") }),
                }));
            var clip = _host.Root.Q<VisualElement>("clip").style;
            clip.position = Position.Absolute;
            clip.left = 10f;
            clip.top = 40f;
            clip.overflow = Overflow.Hidden;
            clip.transformOrigin = new TransformOrigin(0f, 0f, 0f);
            clip.scale = new Scale(new Vector2(1.02f, 1.02f));
            clip.translate = new Translate(0.4f, 0f);
            var glass = Glass().style;
            glass.position = Position.Absolute;
            glass.left = 0f;
            glass.top = 0f;
            yield return WaitRealtime(0.8);
        }

        // Column 30's red on the clip's middle row, or -1 where the clip does not span x = 10.4 to 30.8.
        private int RedOfTheColumnTheClipEndsIn()
        {
            var clip = _host.Root.Q<VisualElement>("clip").worldBound;
            return Near(clip.x, 10.4f) && Near(clip.xMax, 30.8f) ? Resolved(_host.TargetTexture, 30, 50).r : -1;
        }

        // GREEN_ON_BASE(characterization): the base paints a blurred backdrop up to its clip's fractional edge.
        [UnityTest]
        public IEnumerator Given_ABackdropBlurUnderAClipEndingPartWayIntoAPixel_When_Rendered_Then_ItPaintsThatPixel()
        {
            // Arrange — the inversion paints the box yellow over the blue.
            yield return RenderUnderAFractionalClip("backdrop-filter-[blur(1px)_invert(1)]", 1);

            // Act
            var red = RedOfTheColumnTheClipEndsIn();

            // Assert — column 30's centre, at 30.5, lies inside the clip: yellow there, where the stage's blue reads 0.
            Assert.That(red, Is.GreaterThan(200), $"clip={_host.Root.Q<VisualElement>("clip").worldBound}");
        }

        // GREEN_ON_BASE(characterization): the base paints a backdrop off a multisampled target up to its clip's fractional edge.
        [UnityTest]
        public IEnumerator Given_ABackdropInvertOnAMultisampledTargetUnderAClipEndingPartWayIntoAPixel_When_Rendered_Then_ItPaintsThatPixel()
        {
            // Arrange — a multisampled target is captured by a blit, and an inversion alone runs no pass after it.
            yield return RenderUnderAFractionalClip("backdrop-invert", 4);

            // Act
            var red = RedOfTheColumnTheClipEndsIn();

            // Assert — column 30's centre, at 30.5, lies inside the clip: yellow there, where the stage's blue reads 0.
            Assert.That(red, Is.GreaterThan(200), $"clip={_host.Root.Q<VisualElement>("clip").worldBound}");
        }
    }
}

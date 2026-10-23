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
    /// Measures the <c>backdrop-*</c> utilities by GPU pixel readback on a panel rendering into a RenderTexture: a
    /// red half and a blue half meet at x = 60 (at y = 60 in a column), and the element under test, absolutely
    /// positioned over them, carries no background of its own, so what it shows is the filtered backdrop.
    /// </summary>
    /// <remarks>
    /// The panel carries no stylesheet, so the row direction and the absolute placement are set inline. The samples
    /// sit at fixed points, and the measured boxes that put them where they are meant to be are folded into the
    /// assertion beside the control sample that shows the backdrop filter drew at all.
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
    }
}

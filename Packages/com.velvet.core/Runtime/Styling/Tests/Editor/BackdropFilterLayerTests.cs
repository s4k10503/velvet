using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what the <c>backdrop-*</c> utilities compose and where their layer is hosted: directly before the
    /// element, among its parent's children, and invisible to the reconciler's slots. What the layer paints is
    /// measured in <c>BackdropFilterPlaybackTests</c>.
    /// </summary>
    internal sealed class BackdropFilterLayerTests : PanelTestBase
    {
        // The layer and its spec are read by name, so this fixture reads "none" rather than failing to build where
        // they do not exist.
        private const string LayerClass = "velvet-backdrop-layer";

        // transition-all, duration-* and ease-* are the sheet's.
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

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
            return V.Div(name: "glass", className: "w-[40px] h-[40px] transition-all duration-1000 ease-linear "
                + (dim ? "backdrop-brightness-50" : "backdrop-brightness-100"));
        }

        // The first function's amount the layer paints now, NaN where there is no layer.
        private static float ShownAmount(VisualElement element)
        {
            var layer = LayerBefore(element);
            var shown = layer?.GetType().GetMethod("Shown", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.Invoke(layer, null) as Array;
            var step = shown is { Length: > 0 } ? shown.GetValue(0) : null;
            return step?.GetType().GetField("A")?.GetValue(step) is float amount ? amount : float.NaN;
        }

        private static VisualElement LayerBefore(VisualElement element)
        {
            var host = element.parent;
            var index = host.IndexOf(element) - 1;
            return index >= 0 && host[index].ClassListContains(LayerClass) ? host[index] : null;
        }

        private static string SpecOf(VisualElement element)
        {
            var layer = LayerBefore(element);
            var field = layer?.GetType().GetField("_spec", BindingFlags.NonPublic | BindingFlags.Instance);
            return field?.GetValue(layer)?.ToString() ?? "none";
        }

        private VisualElement Mount(string className, string name = "glass")
        {
            _mounted?.Dispose();
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: name, className: $"w-[40px] h-[40px] {className}"));
            var element = _window.rootVisualElement.Q<VisualElement>(name);
            ForcePanelUpdate(element.panel);
            return element;
        }

        [Test]
        public void Given_EveryBackdropFunctionInClassOrderReversed_When_Mounted_Then_TheyComposeInTailwindsOrder()
        {
            // Arrange & Act — a bracket, a bare form, Tailwind v4's bare integers, a clamp and a negative hue each.
            var glass = Mount("backdrop-sepia-[0.25] -backdrop-hue-rotate-90 backdrop-opacity-50 backdrop-saturate-150 "
                + "backdrop-invert backdrop-grayscale-200 backdrop-contrast-[125%] backdrop-brightness-75 "
                + "backdrop-blur-[1rem]");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(16px) brightness(0.75) contrast(1.25) grayscale(1) "
                + "hue-rotate(-90deg) invert(1) opacity(0.5) saturate(1.5) sepia(0.25)"));
        }

        [Test]
        public void Given_ABracketTheBrowserRejects_When_Mounted_Then_TheElementGetsNoBackdropFilter()
        {
            // Arrange — the same element without the rejected bracket is the control.
            var accepted = SpecOf(Mount("backdrop-grayscale"));

            // Act
            var rejected = SpecOf(Mount("backdrop-grayscale backdrop-blur-[-4px]"));

            // Assert
            Assert.That((accepted, rejected), Is.EqualTo(("grayscale(1)", "none")));
        }

        [Test]
        public void Given_ANegativeBlurAfterASpace_When_Mounted_Then_TheElementGetsNoBackdropFilter()
        {
            // Arrange — the `_` is a space the browser skips; the same bracket unsigned is the control.
            var accepted = SpecOf(Mount("backdrop-grayscale backdrop-blur-[_4px]"));

            // Act
            var rejected = SpecOf(Mount("backdrop-grayscale backdrop-blur-[_-4px]"));

            // Assert
            Assert.That((accepted, rejected), Is.EqualTo(("blur(4px) grayscale(1)", "none")));
        }

        [Test]
        public void Given_AListHoldingANegativeBlur_When_Mounted_Then_TheClassUnderItHoldsTheDeclaration()
        {
            // Act — Tailwind emits backdrop-filter-[…] after backdrop-brightness-*, so the list holds the declaration
            // where it is taken.
            var glass = Mount("backdrop-filter-[blur(-4px)] backdrop-brightness-50");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("brightness(0.5)"));
        }

        [Test]
        public void Given_TwoBlursWrittenInReverse_When_Mounted_Then_TheOneTailwindEmitsLastHoldsTheBlur()
        {
            // Act — Tailwind emits backdrop-blur-sm after backdrop-blur-lg, as it does shadow-sm after shadow-lg.
            var glass = Mount("backdrop-blur-sm backdrop-blur-lg");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(8px)"));
        }

        [Test]
        public void Given_AnImportantBlurBeforeAPlainOne_When_Mounted_Then_TheImportantOneHoldsTheBlur()
        {
            // Act
            var glass = Mount("backdrop-blur-lg! backdrop-blur-sm");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(16px)"));
        }

        [Test]
        public void Given_BackdropFilterNoneBesideFunctionUtilities_When_Mounted_Then_TheClassTailwindEmitsLastHoldsTheDeclaration()
        {
            // Arrange — Tailwind emits backdrop-filter-none after backdrop-blur-sm and before backdrop-sepia.
            var afterBlur = SpecOf(Mount("backdrop-filter-none backdrop-blur-sm"));

            // Act
            var beforeSepia = SpecOf(Mount("backdrop-sepia backdrop-filter-none backdrop-blur-sm"));

            // Assert
            Assert.That((afterBlur, beforeSepia), Is.EqualTo(("none", "blur(8px) sepia(1)")));
        }

        [Test]
        public void Given_ABackdropFilterList_When_Mounted_Then_ItsFunctionsRunInTheOrderWritten()
        {
            // Act
            var glass = Mount("backdrop-filter-[invert(1)_brightness(50%)_drop-shadow(0_4px_8px_#ff0000)_blur(2px)]");

            // Assert
            Assert.That(SpecOf(glass),
                Is.EqualTo("invert(1) brightness(0.5) drop-shadow(0px 4px 8px #FF0000FF) blur(2px)"));
        }

        [Test]
        public void Given_AListHoldingAReferenceNothingResolves_When_Mounted_Then_TheRestOfTheListRuns()
        {
            // Act
            var glass = Mount("backdrop-filter-[url(#frost)_blur(4px)_grayscale(1)]");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(4px) grayscale(1)"));
        }

        [Test]
        public void Given_ABlurReadingACustomProperty_When_Mounted_Then_ItBlursByThePropertysValue()
        {
            // Act
            var glass = Mount("[--b:4px] backdrop-blur-(--b)");

            // Assert
            Assert.That(SpecOf(glass), Is.EqualTo("blur(4px)"));
        }

        [Test]
        public void Given_TransitionAll_When_TheBackdropBrightnessChanges_Then_HalfTheDurationInItIsHalfWay()
        {
            // Arrange
            var clock = new HeldMotionClock();
            s_dim = new Flag();
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Dimming, key: "root"),
                new MountOptions { MotionClock = clock });
            var glass = _window.rootVisualElement.Q<VisualElement>("glass");
            ForcePanelUpdate(glass.panel);

            // Act
            s_dim.Set(true);
            _mounted.FlushStateForTest();
            clock.Now += 0.5;

            // Assert — brightness 1 to 0.5, linear.
            Assert.That(ShownAmount(glass), Is.EqualTo(0.75f).Within(1e-3f));
        }

        // The window as it painted, read back through the editor's own capture of a window's pixels, and the scale from
        // the window's points to those pixels.
        private (Color32[] Pixels, int Width, int Height, float Scale) CaptureWindow()
        {
            var scale = EditorGUIUtility.pixelsPerPoint;
            var width = Mathf.RoundToInt(_window.position.width * scale);
            var height = Mathf.RoundToInt(_window.position.height * scale);
            var target = new RenderTexture(width, height, 0);
            try
            {
                _window.Focus();
                typeof(EditorWindow).GetMethod("RepaintImmediately", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.Invoke(_window, null);
                InternalEditorUtility.CaptureEditorWindow(_window, target);
                return (RenderTexturePixelReader.ReadPixels(target, new RectInt(0, 0, width, height)), width, height, scale);
            }
            finally
            {
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void Given_ABackdropBlurInAnEditorWindow_When_TheWindowIsCaptured_Then_TheEdgeBlursOnlyInsideTheBox()
        {
            // Arrange — red and blue halves meeting at x = 100, under a 60px box from (70, 70).
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "stage", className: "w-[200px] h-[200px]",
                children: new VNode[]
                {
                    V.Div(className: "w-[100px] h-[200px] bg-[#ff0000]"),
                    V.Div(className: "w-[100px] h-[200px] bg-[#0000ff]"),
                    V.Div(name: "glass", className: "w-[60px] h-[60px] backdrop-blur-md"),
                }));
            var stage = _window.rootVisualElement.Q<VisualElement>("stage");
            stage.style.flexDirection = FlexDirection.Row;
            var glass = stage.Q<VisualElement>("glass");
            glass.style.position = Position.Absolute;
            glass.style.left = 70f;
            glass.style.top = 70f;
            var probe = new CopyProbe();
            stage.Insert(stage.IndexOf(LayerBefore(glass) ?? glass), probe);
            ForcePanelUpdate(stage.panel);
            MarkFrameUnread();

            // Act
            var (pixels, width, height, scale) = CaptureWindow();
            Color32 At(float x, float y)
            {
                var column = Mathf.Clamp(Mathf.FloorToInt((stage.worldBound.x + x) * scale), 0, width - 1);
                var row = Mathf.Clamp(height - 1 - Mathf.FloorToInt((stage.worldBound.y + y) * scale), 0, height - 1);
                return pixels[(row * width) + column];
            }
            var red = At(40f, 100f);
            var inside = At(101f, 100f);
            var outside = At(101f, 20f);
            var stageOutsideBox = At(160f, 100f);

            // Assert — the two halves read as themselves, which says the capture landed where the stage is; then the
            // pixel just right of the edge is mixed inside the box and pure blue above it.
            Assert.That((red.r > 200 && red.b < 20, inside.r > 60 && inside.b > 60, outside.b > 200 && outside.r < 20),
                Is.EqualTo((true, true, true)), $"red={red} inside={inside} outside={outside} "
                + $"api={SystemInfo.graphicsDeviceType} uvStartsAtTop={SystemInfo.graphicsUVStartsAtTop} "
                + $"window={width}x{height} stage={stage.worldBound} {LastFrame()} "
                + $"stageOutsideBox={stageOutsideBox} {probe.Reading}");
        }

        // A width no layer has, so a frame still holding it after the capture was not begun by the layer.
        private const float UnreadWidth = -12345f;

        private static object Frame()
            => typeof(BackdropRenderer).GetField("s_frame", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);

        private static void MarkFrameUnread()
        {
            if (Frame() is BackdropFrame frame)
            {
                frame.Width = UnreadWidth;
            }
        }

        // Sits over the glass's box directly before its layer, so it paints in the same pass with the same transform,
        // and takes the copy the layer is about to take with its own frame: the mean of the target's region as
        // ReadPixels reads it before the copy, and the mean of the copied texture read back.
        private sealed class CopyProbe : ImmediateModeElement
        {
            private int _paints;
            private string _last = "never painted";

            public CopyProbe()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 70f;
                style.top = 70f;
                style.width = 60f;
                style.height = 60f;
            }

            public string Reading => $"probePaints={_paints} {_last}";

            protected override void ImmediateRepaint()
            {
                _paints++;
                try
                {
                    _last = Read();
                }
                catch (Exception e)
                {
                    _last = $"probeError={e.GetType().Name}: {e.Message}";
                }
            }

            private string Read()
            {
                const BindingFlags Static = BindingFlags.NonPublic | BindingFlags.Static;
                var viewport = (Func<RectInt>)typeof(BackdropRenderer).GetField("s_viewport", Static).GetValue(null);
                var projection = (Func<Matrix4x4>)typeof(BackdropRenderer).GetField("s_projection", Static).GetValue(null);
                var frame = new BackdropFrame();
                var active = RenderTexture.active;
                if (!frame.TryBegin(this, viewport(), projection()))
                {
                    return $"probeActive={Name(active)} probeBegan=False";
                }
                var region = frame.Region;
                var before = MeanOfActive(new RectInt(0, 0, region.width, region.height), region);
                var captured = frame.Capture();
                try
                {
                    RenderTexture.active = captured;
                    var copied = MeanOfActive(new RectInt(0, 0, region.width, region.height),
                        new RectInt(0, 0, captured.width, captured.height));
                    return $"probeActive={Name(active)} probeRegion={region} targetBeforeCopy={before} "
                        + $"copied={copied} copySize={captured.width}x{captured.height}";
                }
                finally
                {
                    frame.Rebound = true;
                    frame.Rebind();
                    RenderTexture.ReleaseTemporary(captured);
                }
            }

            private static string Name(RenderTexture texture) => texture == null ? "null" : texture.name;

            // The mean colour of a rectangle of the active target, origin bottom-left.
            private static Color32 MeanOfActive(RectInt size, RectInt source)
            {
                var texture = new Texture2D(size.width, size.height, TextureFormat.RGBA32, false);
                try
                {
                    texture.ReadPixels(new Rect(source.x, source.y, source.width, source.height), 0, 0, false);
                    var pixels = texture.GetPixels32();
                    long r = 0, g = 0, b = 0, a = 0;
                    foreach (var p in pixels)
                    {
                        r += p.r;
                        g += p.g;
                        b += p.b;
                        a += p.a;
                    }
                    var n = Math.Max(1, pixels.Length);
                    return new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }
            }
        }

        // The renderer's reading of the target as the layer last drew, which says where it copied from.
        private static string LastFrame()
        {
            const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
            var frame = Frame();
            if (frame is not BackdropFrame begun)
            {
                return "frame=unread";
            }
            if (begun.Width == UnreadWidth)
            {
                return "layerBegan=False";
            }
            string Read(string field) => frame.GetType().GetField(field, Any)?.GetValue(frame)?.ToString() ?? "null";
            return $"layerBegan=True target={Read("_target")} targetSize={Read("_targetWidth")}x{Read("_targetHeight")} "
                + $"viewport={Read("_viewport")} region={Read("Region")}";
        }

        [Test]
        public void Given_ABackdropElementBetweenSiblings_When_Mounted_Then_ItsLayerSitsBeforeItAndTakesNoSlot()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "row", children: new VNode[]
            {
                V.Div(name: "a", className: "w-[10px] h-[10px]"),
                V.Div(name: "glass", className: "w-[40px] h-[40px] backdrop-blur-sm"),
                V.Div(name: "b", className: "w-[10px] h-[10px]"),
            }));
            var row = _window.rootVisualElement.Q<VisualElement>("row");

            // Act
            ForcePanelUpdate(row.panel);

            // Assert — four children, three slots, and the layer at the glass's own index less one.
            var glass = row.Q<VisualElement>("glass");
            Assert.That((row.childCount, LogicalChildSlots.Count(row), LayerBefore(glass) != null),
                Is.EqualTo((4, 3, true)));
        }

        [Test]
        public void Given_AHoverBackdropVariant_When_ThePointerIsOver_Then_TheLayerIsHosted()
        {
            // Arrange
            var glass = Mount("hover:backdrop-blur-sm");
            var before = SpecOf(glass);

            // Act
            using (var e = PointerOverEvent.GetPooled()) { glass.SimulateEvent(e); }

            // Assert
            Assert.That((before, SpecOf(glass)), Is.EqualTo(("none", "blur(8px)")));
        }
    }
}

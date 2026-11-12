using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewPanelTests
    {
        private const int Size = 64;

        // Wider than sm:'s breakpoint.
        private const int SmPanelWidth = 800;

        // The story paints the left half only, so the right half is a control that a whole-frame fill turns red too.
        private static readonly Vector2Int s_storySample = new(Size / 4, Size / 2);
        private static readonly Vector2Int s_controlSample = new(Size * 3 / 4, Size / 2);

        // The preview window takes a panel without settings to be at a scale of one.
        [Test]
        public void Given_NoPanelSettings_When_APreviewPanelIsBuilt_Then_ItIsAConstantPixelSizePanelAtScaleOne()
        {
            // Arrange
            const int width = 64;
            const int height = 32;

            // Act
            using var panel = new VelvetPreviewPanel(null, width, height);
            var settings = (panel.Root.panel as IRuntimePanel)?.panelSettings;

            // Assert
            Assert.That(
                (settings?.scaleMode, settings?.scale),
                Is.EqualTo(((PanelScaleMode?)PanelScaleMode.ConstantPixelSize, (float?)1f)));
        }

        [Test]
        public void Given_AStoryMountedOnAPreviewPanel_When_RenderedWithinTheMountsOwnFrame_Then_TheTextureHoldsTheStory()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            Fill(panel.Texture, Color.blue);
            using var mounted = V.Mount(panel.Root, LeftHalfRed());

            // Act
            panel.Render();
            var storyDrawn = StoryRedBesideAnUnpaintedControl(panel.Texture);

            // Assert
            Assert.That(storyDrawn, Is.True);
        }

        [Test]
        public void Given_AStoryWhoseMountEffectSetsState_When_RenderedOnce_Then_TheTextureHoldsTheStateTheEffectSet()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            Fill(panel.Texture, Color.blue);
            using var mounted = V.Mount(panel.Root, V.Component(RedOnceItsEffectRan));

            // Act
            panel.Render();
            var readyDrawn = StoryRedBesideAnUnpaintedControl(panel.Texture);

            // Assert
            Assert.That(readyDrawn, Is.True);
        }

        [Test]
        public void Given_ACallbackTheFrameRunsThrows_When_Rendered_Then_TheCallerCatchesItsOwnException()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            panel.Root.schedule.Execute(() => throw new InvalidOperationException("scheduled boom"));

            // Act + Assert
            Assert.That(() => panel.Render(), Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Given_AMeasuredWidthOnAPanelWhoseClockIsHeld_When_RenderedOnce_Then_ItIsLaidOutAtTheWidthItMeasures()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            // Held, so the result does not depend on how long the passes take.
            EditorPanelTestHelpers.SetPanelTimeFunction(panel.Root.panel, () => 0d);
            using var mounted = V.Mount(panel.Root, V.Div(className: "w-[calc(100%-16px)] h-[32px] bg-[#ff0000]", name: "measured"));

            // Act
            panel.Render();
            var width = panel.Root.Q<VisualElement>("measured").layout.width;

            // Assert
            Assert.That(width, Is.EqualTo(Size - 16f).Within(0.01f));
        }

        [Test]
        public void Given_AMeasuredWidthInsideAnotherOnAPanelWhoseClockIsHeld_When_RenderedOnce_Then_ItMeasuresTheWidthTheOuterResolvedTo()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            EditorPanelTestHelpers.SetPanelTimeFunction(panel.Root.panel, () => 0d);
            using var mounted = V.Mount(panel.Root, V.Div(className: "w-[calc(100%-16px)] h-[32px]", name: "outer",
                children: new VNode[] { V.Div(className: "w-[calc(100%-8px)] h-[16px]", name: "inner") }));

            // Act
            panel.Render();
            var width = panel.Root.Q<VisualElement>("inner").layout.width;

            // Assert
            Assert.That(width, Is.EqualTo(Size - 16f - 8f).Within(0.01f));
        }

        [Test]
        public void Given_AnSmWidthOnAPanelWideEnoughForSmWhoseClockIsHeld_When_RenderedOnce_Then_ItIsLaidOutAtTheSmWidth()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, SmPanelWidth, Size);
            EditorPanelTestHelpers.SetPanelTimeFunction(panel.Root.panel, () => 0d);
            using var mounted = V.Mount(panel.Root, V.Div(className: "w-[20px] sm:w-[40px] h-[10px]", name: "responsive"));

            // Act
            panel.Render();
            var width = panel.Root.Q<VisualElement>("responsive").layout.width;

            // Assert
            Assert.That(width, Is.EqualTo(40f).Within(0.01f));
        }

        [Test]
        public void Given_APanelWhoseClockIsHeld_When_RenderedTwice_Then_ItsScheduledWorkHasSeenTwiceTheSettlePasses()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            EditorPanelTestHelpers.SetPanelTimeFunction(panel.Root.panel, () => 0d);
            var lastTickMs = -1L;
            panel.Root.schedule.Execute((TimerState timer) => lastTickMs = timer.now).Every(0);

            // Act
            panel.Render();
            panel.Render();

            // Assert
            Assert.That(lastTickMs,
                Is.EqualTo(2L * VelvetPreviewPanel.SettlePasses * VelvetPreviewPanel.SettlePassMilliseconds).Within(1L));
        }

        [Test]
        public void Given_AModelviewTheCallerSet_When_APreviewPanelRenders_Then_TheCallerStillHasIt()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            Fill(panel.Texture, Color.blue);
            using var mounted = V.Mount(panel.Root, LeftHalfRed());
            var callers = GL.modelview;
            GL.modelview = Matrix4x4.Translate(new Vector3(3f, 5f, 7f));

            // Act
            float kept;
            try
            {
                panel.Render();
                kept = GL.modelview.m03;
            }
            finally
            {
                GL.modelview = callers;
            }
            // NaN unless the frame drew, so a Render that draws nothing cannot pass.
            var translation = StoryRedBesideAnUnpaintedControl(panel.Texture) == true ? kept : float.NaN;

            // Assert
            Assert.That(translation, Is.EqualTo(3f));
        }

        [Test]
        public void Given_SettingsThatClearTheirPanelAndABackgroundPortal_When_Rendered_Then_ThePortalsPixelsSurvive()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            using var destroySettings = new ActionDisposable(() => UnityEngine.Object.DestroyImmediate(settings));
            settings.scaleMode = PanelScaleMode.ConstantPixelSize;
            settings.scale = 1f;
            settings.clearColor = true;
            settings.colorClearValue = Color.blue;
            using var panel = new VelvetPreviewPanel(settings, Size, Size);
            using var mounted = V.Mount(panel.Root,
                V.Div("w-[64px] h-[64px]", V.Portal(UILayer.Background, new VNode[] { LeftHalfRed() })));

            // Act
            panel.Render();
            var portalDrawn = StoryRedBesideAnUnpaintedControl(panel.Texture);

            // Assert
            Assert.That(portalDrawn, Is.True);
        }

        [Test]
        public void Given_APreviewPanel_When_Built_Then_ItsPanelIsSizedToItsTexture()
        {
            // Arrange
            const int width = 32;

            // Act
            using var panel = new VelvetPreviewPanel(null, width, Size);
            var panelWidth = panel.Root.panel.visualTree.style.width.value.value;

            // Assert
            Assert.That(panelWidth, Is.EqualTo((float)width));
        }

        // GREEN_ON_BASE(characterization): the base already maps every screen position off the panel.
        // This change moves the call that does it behind the texture's assignment, which must keep the mapping.
        [Test]
        public void Given_APreviewPanel_When_AScreenPositionIsMappedOntoIt_Then_ItLandsOffThePanel()
        {
            // Arrange
            using var panel = new VelvetPreviewPanel(null, Size, Size);

            // Act
            var mapped = RuntimePanelUtils.ScreenToPanel(panel.Root.panel, new Vector2(10f, 10f));

            // Assert
            Assert.That(float.IsNaN(mapped.x), Is.True);
        }

        [Test]
        public void Given_TwoPreviewPanelsWithAStoryEach_When_OneIsRendered_Then_TheOthersTextureIsNotDrawnInto()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var rendered = new VelvetPreviewPanel(null, Size, Size);
            using var other = new VelvetPreviewPanel(null, Size, Size);
            Fill(rendered.Texture, Color.blue);
            Fill(other.Texture, Color.blue);
            using var renderedStory = V.Mount(rendered.Root, LeftHalfRed());
            using var otherStory = V.Mount(other.Root, LeftHalfRed());

            // Act
            rendered.Render();
            // Null unless the rendered panel drew its own story, so a Render that draws nothing cannot pass.
            var otherDrawnInto = StoryRedBesideAnUnpaintedControl(rendered.Texture) == true
                ? IsRed(other.Texture, s_storySample)
                : (bool?)null;

            // Assert
            Assert.That(otherDrawnInto, Is.False);
        }

        [Test]
        public void Given_APreviewPanelResizedAfterItWasBuilt_When_Rendered_Then_ItsPanelIsLaidOutAtTheNewWidth()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size / 2, Size);
            panel.Resize(Size, Size);

            // Act
            panel.Render();
            var width = panel.Root.panel.visualTree.layout.width;

            // Assert
            Assert.That(width, Is.EqualTo((float)Size));
        }

        [Test]
        public void Given_APreviewPanel_When_Rendered_Then_TheTextGenerationIterationMovesOnByOne()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render");
            using var panel = new VelvetPreviewPanel(null, Size, Size);
            var before = TextGenerationIteration();

            // Act
            panel.Render();
            var after = TextGenerationIteration();

            // Assert
            Assert.That(after, Is.EqualTo(before + 1));
        }

        [Test]
        public void Given_ADisposedPreviewPanel_When_Rendered_Then_ItThrowsObjectDisposedException()
        {
            // Arrange
            var panel = new VelvetPreviewPanel(null, Size, Size);
            panel.Dispose();

            // Act + Assert
            Assert.That(() => panel.Render(), Throws.TypeOf<ObjectDisposedException>());
        }

        private static VNode LeftHalfRed() => V.Div("w-[32px] h-[64px] bg-[#ff0000]");

        [Component]
        private static VNode RedOnceItsEffectRan()
        {
            var (ready, setReady) = Hooks.UseState(false);
            Hooks.UseEffect(() =>
            {
                setReady.Invoke(true);
                return (Action)null;
            }, Array.Empty<object>());
            return ready ? LeftHalfRed() : V.Div("w-[32px] h-[64px] bg-[#0000ff]");
        }

        // A known non-red start, so a red pixel can only be one a render drew.
        private static void Fill(RenderTexture texture, Color color)
        {
            var previouslyActive = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(true, true, color);
            RenderTexture.active = previouslyActive;
        }

        // Null where the control is red as well, so a fill over the whole texture cannot pass.
        private static bool? StoryRedBesideAnUnpaintedControl(RenderTexture texture)
            => IsRed(texture, s_controlSample) ? null : IsRed(texture, s_storySample);

        private static bool IsRed(RenderTexture texture, Vector2Int at)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, Size, Size));
            return RenderTexturePixelReader.IsRedPixel(pixels[at.y * Size + at.x]);
        }

        private static int TextGenerationIteration()
            => (int)typeof(UnityEngine.TextCore.Text.FontAsset).Assembly
                .GetType("UnityEngine.TextCore.Text.TextGenerationInfo", throwOnError: true)
                .GetProperty("CurrentGenerationIteration", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null);
    }
}

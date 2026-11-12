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

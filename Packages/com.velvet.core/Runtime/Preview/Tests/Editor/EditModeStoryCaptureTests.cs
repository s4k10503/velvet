using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class EditModeStoryCaptureTests
    {
        private const int Size = 64;
        private const int FramesBeforeReadback = 4;

        // The story paints the left half only, so the right half is a control that a backdrop or a
        // whole-frame fill would turn red as well.
        private static readonly Vector2Int s_storySample = new(Size / 4, Size / 2);
        private static readonly Vector2Int s_controlSample = new(Size * 3 / 4, Size / 2);

        private static VNode LeftHalfRed() => V.Div("w-[32px] h-[64px] bg-[#ff0000]");

        private static VelvetPreviewStory Story() =>
            PreviewStoryTestFactory.Build(
                typeof(EditModeStoryCaptureTests).GetMethod(
                    nameof(LeftHalfRed), BindingFlags.Static | BindingFlags.NonPublic),
                new VelvetPreviewAttribute { Name = nameof(LeftHalfRed), Group = nameof(EditModeStoryCaptureTests) });

        // GREEN_ON_BASE(characterization): Edit Mode editor frames already paint an offscreen story panel.
        // The mount, the frames and the readback are all public API; deleting the frame loop reddens it.
        [UnityTest]
        public IEnumerator Given_AStoryMountedOnARenderTexturePanel_When_EditorFramesRun_Then_TheTextureHoldsTheStory()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var host = new RenderTexturePanelHost(nameof(EditModeStoryCaptureTests), Size, Size);
            using var preview = new VelvetPreviewHost(host.Root);
            preview.Mount(Story());

            // Act
            for (var frame = 0; frame < FramesBeforeReadback; frame++) yield return null;
            var (story, control) = Sample(host);

            // Assert
            Assert.That(
                (MountErrorName(preview), RenderTexturePixelReader.IsRedPixel(story),
                    RenderTexturePixelReader.IsRedPixel(control)),
                Is.EqualTo((string.Empty, true, false)));
        }

        // GREEN_ON_BASE(characterization): a mount alone does not write the texture in Edit Mode.
        // That is why the guide has a harness wait for editor frames; waiting them before the readback reddens it.
        [Test]
        public void Given_AStoryMountedOnARenderTexturePanel_When_ReadBackInTheMountsOwnFrame_Then_TheTextureDoesNotHoldTheStoryYet()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel render and a ReadPixels readback");
            using var host = new RenderTexturePanelHost(nameof(EditModeStoryCaptureTests), Size, Size);
            using var preview = new VelvetPreviewHost(host.Root);

            // Act
            preview.Mount(Story());
            var (story, _) = Sample(host);

            // Assert
            Assert.That(
                (MountErrorName(preview), host.Root.childCount, RenderTexturePixelReader.IsRedPixel(story)),
                Is.EqualTo((string.Empty, 1, false)));
        }

        private static string MountErrorName(VelvetPreviewHost preview) =>
            preview.MountError?.GetType().Name ?? string.Empty;

        private static (Color32 Story, Color32 Control) Sample(RenderTexturePanelHost host)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(host.TargetTexture, new RectInt(0, 0, Size, Size));
            return (Pixel(pixels, s_storySample), Pixel(pixels, s_controlSample));
        }

        private static Color32 Pixel(Color32[] pixels, Vector2Int at) => pixels[at.y * Size + at.x];
    }
}

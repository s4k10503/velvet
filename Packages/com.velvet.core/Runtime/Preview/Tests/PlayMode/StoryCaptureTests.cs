using System.Collections;
using System.Collections.Generic;
using System.IO;
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
    /// Renders every <c>[VelvetPreview]</c> story onto a real ticking panel and writes one PNG per story, so
    /// that what Velvet draws can be looked at and not only measured by code that measures itself.
    /// </summary>
    /// <remarks>
    /// <c>Documentation~/preview-tooling.md</c> states that Velvet ships no prebuilt capture harness and
    /// exposes <see cref="VelvetPreviewRegistry"/> and <see cref="VelvetPreviewHost"/> so a consumer builds
    /// its own. That is about the published surface; this is a test, which no consumer imports. Driving the
    /// same registry the live window drives is what keeps the captured set and the shown set from diverging.
    ///
    /// The PNGs land in <c>Logs/story-captures</c> under the project root — already git-ignored, and outside
    /// <c>Assets</c> so Unity does not import a directory of generated images — or in the directory named by
    /// <c>VELVET_STORY_CAPTURE_DIR</c>.
    /// </remarks>
    [Timeout(900000)]
    internal sealed class StoryCaptureTests
    {
        private const string OutputDirectoryVariable = "VELVET_STORY_CAPTURE_DIR";
        private const string DefaultOutputDirectory = "Logs/story-captures";

        // A story with no explicit size fills the preview window's canvas, which has no counterpart here.
        private const int FallbackWidth = 480;
        private const int FallbackHeight = 320;

        // A themeless RenderTexture panel supplies no font, and an empty runtime theme measures every label
        // 0 tall — a story made only of text would then capture as an empty frame and read as a successful
        // capture. unityFontDefinition inherits, so setting it on the panel root reaches every descendant
        // without the story having to cooperate.
        private static readonly Font s_builtinFont =
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

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
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_EveryRegisteredStory_When_CapturedOnARealPanel_Then_NoneRendersAnEmptyFrame()
        {
            // Arrange
            var stories = VelvetPreviewRegistry.DiscoverStories();
            Assume.That(stories, Is.Not.Empty, "Precondition: the project declares at least one [VelvetPreview] story");
            var outputDirectory = ResolveOutputDirectory();
            Directory.CreateDirectory(outputDirectory);

            // Act
            var empty = new List<string>();
            var captured = 0;
            foreach (var assemblyGroup in GroupByAssembly(stories))
            {
                // The story's [VelvetPreviewSetup] environment registers fonts, seeds stores and wires
                // resolvers; a story captured without it is not the story the preview window shows.
                using (VelvetPreviewRegistry.RunSetupFor(assemblyGroup.Key))
                {
                    foreach (var story in assemblyGroup.Value)
                    {
                        var result = new CaptureResult();
                        yield return Capture(story, outputDirectory, result);
                        captured++;
                        if (result.IsEmptyFrame) empty.Add(story.Id);
                    }
                }
            }

            Debug.Log($"[StoryCapture] wrote {captured} PNG(s) to {outputDirectory}");

            // Assert
            // A uniform frame is the failure this exists to catch, and it is indistinguishable from success
            // by any measure that only asks whether a file was written. The count rides in on the same
            // comparison because a story dropped before capture would otherwise leave an empty list.
            Assert.That(
                (captured, string.Join(", ", empty)),
                Is.EqualTo((stories.Count, string.Empty)));
        }

        private sealed class CaptureResult
        {
            public bool IsEmptyFrame;
        }

        private static IEnumerator Capture(VelvetPreviewStory story, string outputDirectory, CaptureResult result)
        {
            var width = story.Width > 0 ? story.Width : FallbackWidth;
            var height = story.Height > 0 ? story.Height : FallbackHeight;

            using var host = new RenderTexturePanelHost(SanitizeFileName(story.Id), width, height);
            host.Root.style.unityFontDefinition =
                new StyleFontDefinition(FontDefinition.FromFont(s_builtinFont));
            host.Root.LoadBundledStyleUtilitiesForTest();

            using var previewHost = new VelvetPreviewHost(host.Root);
            previewHost.Mount(story);
            Assert.That(previewHost.MountError, Is.Null, $"Story '{story.Id}' failed to mount");

            // Long enough to absorb a first-run text-shaping and glyph-atlas warm-up, the same posture the
            // package's own playback specs take for their first draw.
            yield return WaitRealtime(0.5);

            var pixels = RenderTexturePixelReader.ReadPixels(
                host.TargetTexture, new RectInt(0, 0, width, height));
            result.IsEmptyFrame = IsUniform(pixels);
            WritePng(pixels, width, height, Path.Combine(outputDirectory, SanitizeFileName(story.Id) + ".png"));
        }

        // Compared against the frame's own first pixel rather than against a known clear colour: the clear
        // colour depends on the PanelSettings and on whether the story paints a background, so "every pixel
        // agrees with every other" is the question that stays answerable without pinning either.
        private static bool IsUniform(Color32[] pixels)
        {
            if (pixels.Length == 0) return true;
            var first = pixels[0];
            foreach (var pixel in pixels)
            {
                if (pixel.r != first.r || pixel.g != first.g || pixel.b != first.b || pixel.a != first.a)
                {
                    return false;
                }
            }

            return true;
        }

        private static void WritePng(Color32[] pixels, int width, int height, string path)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.Destroy(texture);
            }
        }

        private static Dictionary<Assembly, List<VelvetPreviewStory>> GroupByAssembly(
            List<VelvetPreviewStory> stories)
        {
            var grouped = new Dictionary<Assembly, List<VelvetPreviewStory>>();
            foreach (var story in stories)
            {
                if (story.Assembly == null) continue;
                if (!grouped.TryGetValue(story.Assembly, out var list))
                {
                    list = new List<VelvetPreviewStory>();
                    grouped[story.Assembly] = list;
                }

                list.Add(story);
            }

            return grouped;
        }

        private static string ResolveOutputDirectory()
        {
            var configured = System.Environment.GetEnvironmentVariable(OutputDirectoryVariable);
            if (!string.IsNullOrEmpty(configured)) return configured;
            // Application.dataPath is <project>/Assets, so the parent is the project root a worktree's own
            // Logs sits under — the captures follow the checkout that produced them.
            return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, DefaultOutputDirectory);
        }

        private static string SanitizeFileName(string id)
        {
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                id = id.Replace(invalid, '_');
            }

            return id.Replace(' ', '_');
        }
    }
}

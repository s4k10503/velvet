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

        // _tokens.uss's semantic colors are translucent whites meant to composite over a backdrop the host
        // supplies — --color-surface is rgba(255,255,255,0.12), --color-border rgba(255,255,255,0.36) —
        // while --color-text is an opaque near-white, and no background token is declared. A story built
        // from those (the example set is) captured without a backdrop composites near-white onto nothing
        // and reads as blank, having passed every check that asks only whether pixels differ. The
        // requirement here is just that the backdrop be opaque and dark enough for that layer to separate;
        // the value matches what the preview window puts behind the same stories, so a capture and the live
        // view stay comparable. Utilities drawn from _palette.uss's Tailwind scale are opaque and would not
        // have needed this.
        private static readonly Color BackdropBehindTheStory = new(0.09f, 0.10f, 0.13f, 1f);

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
            var bad = new List<string>();
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
                        if (result.Problem != null) bad.Add($"{story.Id}: {result.Problem}");
                    }
                }
            }

            Debug.Log($"[StoryCapture] wrote {captured} PNG(s) to {outputDirectory}");

            // Assert
            // Three ways a capture lies, folded into one comparison because each of them writes a file and
            // reports success: the story never mounted, it rendered onto a panel that is not the size it was
            // authored at (so every `h-full` resolved against something else), and the frame came out
            // uniform. The count rides in on the same tuple, since a story dropped before capture would
            // otherwise leave the list empty and pass.
            Assert.That(
                (captured, string.Join(" | ", bad)),
                Is.EqualTo((stories.Count, string.Empty)));
        }

        private sealed class CaptureResult
        {
            public string Problem;
        }

        private static IEnumerator Capture(VelvetPreviewStory story, string outputDirectory, CaptureResult result)
        {
            var width = story.Width > 0 ? story.Width : FallbackWidth;
            var height = story.Height > 0 ? story.Height : FallbackHeight;

            using var host = new RenderTexturePanelHost(SanitizeFileName(story.Id), width, height);
            host.Root.style.unityFontDefinition =
                new StyleFontDefinition(FontDefinition.FromFont(s_builtinFont));
            host.Root.style.backgroundColor = BackdropBehindTheStory;
            // The panel root stretches to the texture's width on its own but its height hugs its content —
            // measured at 95 of 200, 144 of 320 and 1230 of 1400 before this was set. A story authored with
            // `h-full` then resolves against whatever its own content happened to be tall, which is
            // circular, and everything below that line captures as untouched texture. The preview window
            // sizes its canvas to the story's declared footprint; this is the same statement.
            host.Root.style.width = width;
            host.Root.style.height = height;
            host.Root.LoadBundledStyleUtilitiesForTest();

            using var previewHost = new VelvetPreviewHost(host.Root);
            previewHost.Mount(story);

            // Long enough to absorb a first-run text-shaping and glyph-atlas warm-up, the same posture the
            // package's own playback specs take for their first draw.
            yield return WaitRealtime(0.5);

            var layout = host.Root.layout;
            Debug.Log($"[StoryCapture] {story.Id}: texture {width}x{height}, root layout {layout}");

            var pixels = RenderTexturePixelReader.ReadPixels(
                host.TargetTexture, new RectInt(0, 0, width, height));
            WritePng(pixels, width, height, Path.Combine(outputDirectory, SanitizeFileName(story.Id) + ".png"));

            result.Problem =
                previewHost.MountError != null ? $"did not mount ({previewHost.MountError.GetType().Name})"
                : !Mathf.Approximately(layout.width, width) || !Mathf.Approximately(layout.height, height)
                    ? $"rendered at {layout.width}x{layout.height}, authored at {width}x{height}"
                : IsUniform(pixels) ? "rendered a uniform frame"
                : null;
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

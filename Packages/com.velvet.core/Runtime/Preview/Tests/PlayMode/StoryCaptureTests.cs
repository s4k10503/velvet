// Preview runtime types are editor-only, so this fixture must also be excluded from player builds.
#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    [Timeout(900000)]
    internal sealed class StoryCaptureTests
    {
        private const string OutputDirectoryVariable = "VELVET_STORY_CAPTURE_DIR";
        private const string DefaultOutputDirectory = "Logs/story-captures";

        // Headless capture needs a concrete canvas size for stories that otherwise fill the preview window.
        private const int FallbackWidth = 480;
        private const int FallbackHeight = 320;

        // Set an explicit font so capture does not depend on the panel theme.
        private static readonly Font s_builtinFont =
            Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Give semantic-token stories the page color their surfaces expect.
        private const string BackdropClass = "bg-background";

        private TargetFrameRateScope _frameRateScope;
        private bool _darkBefore;

        [UnitySetUp]
        public IEnumerator UnitySetUp()
        {
            _frameRateScope = new TargetFrameRateScope(120);
            // Resolve dark token variants during capture.
            _darkBefore = VelvetTheme.IsDark;
            VelvetTheme.IsDark = true;
            yield break;
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
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
            DeletePathsFromPreviousManifest(outputDirectory);

            // Act
            // A dedicated host keeps the stylesheet probe from changing a story's logical child positions.
            var probeResult = new CaptureResult();
            yield return CheckBundledStyleSheetResolves(probeResult);

            var bad = new List<string>();
            if (probeResult.Problem != null) bad.Add($"(all stories): {probeResult.Problem}");
            var written = new List<string>();
            foreach (var story in stories)
            {
                // Do not duplicate setup around the loop; that would hide a host that stopped opening it.
                var result = new CaptureResult();
                var path = CapturePath(outputDirectory, story);
                yield return Capture(story, path, result);
                written.Add(path);
                if (result.Problem != null) bad.Add($"{story.Id}: {result.Problem}");
            }

            File.WriteAllLines(ManifestPath(outputDirectory), written);
            // Count files on disk so path collisions follow the filesystem's equality, not string equality.
            var onDisk = Directory.EnumerateFiles(outputDirectory, "*.png", SearchOption.AllDirectories).Count();
            Debug.Log($"[StoryCapture] wrote {onDisk} PNG(s) to {outputDirectory}");

            // Assert
            Assert.That(
                (onDisk, string.Join(" | ", bad)),
                Is.EqualTo((stories.Count, string.Empty)));
        }

        private sealed class CaptureResult
        {
            public string Problem;
        }

        private static void DeletePathsFromPreviousManifest(string outputDirectory)
        {
            var manifest = ManifestPath(outputDirectory);
            if (!File.Exists(manifest)) return;
            foreach (var previous in File.ReadAllLines(manifest))
            {
                if (previous.Length > 0 && File.Exists(previous)) File.Delete(previous);
            }
        }

        private static string ManifestPath(string outputDirectory) =>
            Path.Combine(outputDirectory, ".velvet-story-captures");

        // Probe a resolved plain class rather than sheet attachment; inline utilities can work while the bundled
        // stylesheet is inert.
        private static IEnumerator CheckBundledStyleSheetResolves(CaptureResult result)
        {
            using var host = new RenderTexturePanelHost("StyleSheetProbe", 16, 16);
            VelvetStyleUtilities.AttachTo(host.Root);
            var probe = new VisualElement();
            probe.AddToClassList("bg-slate-700");
            host.Root.Add(probe);

            yield return WaitFramesDraining(8, host.TargetTexture);

            result.Problem = probe.resolvedStyle.backgroundColor == default
                ? "the bundled stylesheet does not resolve bg-slate-700"
                : null;
        }

        private static IEnumerator Capture(VelvetPreviewStory story, string path, CaptureResult result)
        {
            var width = story.Width > 0 ? story.Width : FallbackWidth;
            var height = story.Height > 0 ? story.Height : FallbackHeight;

            using var host = new RenderTexturePanelHost(story.Id, width, height);
            host.Root.style.unityFontDefinition =
                new StyleFontDefinition(FontDefinition.FromFont(s_builtinFont));
            host.Root.AddToClassList(BackdropClass);
            // Explicit dimensions keep h-full stories from resolving against a content-sized root.
            host.Root.style.width = width;
            host.Root.style.height = height;
            VelvetStyleUtilities.AttachTo(host.Root);

            using var previewHost = new VelvetPreviewHost(host.Root);
            previewHost.Mount(story);

            // Drain frames before readback; queued frames are not evidence that the panel was drawn.
            yield return WaitFramesDraining(8, host.TargetTexture);

            Debug.Log($"[StoryCapture] {story.Id}: texture {width}x{height}, root layout {host.Root.layout}");

            var pixels = RenderTexturePixelReader.ReadPixels(
                host.TargetTexture, new RectInt(0, 0, width, height));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WritePng(pixels, width, height, path);

            result.Problem =
                previewHost.MountError != null ? $"did not mount ({previewHost.MountError.GetType().Name})"
                : IsUniform(pixels) ? "rendered a uniform frame"
                : null;
        }

        // Compare against the frame itself so the check does not depend on a particular clear color.
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

        private static string ResolveOutputDirectory()
        {
            var configured = System.Environment.GetEnvironmentVariable(OutputDirectoryVariable);
            if (!string.IsNullOrEmpty(configured)) return configured;
            return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, DefaultOutputDirectory);
        }

        // Keep Group and Name as separate path segments and percent-escape filename-invalid characters.
        private static string CapturePath(string outputDirectory, VelvetPreviewStory story) =>
            Path.Combine(outputDirectory, EscapeSegment(story.Group), EscapeSegment(story.Name) + ".png");

        private static string EscapeSegment(string segment)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '%' };
            var escaped = new System.Text.StringBuilder(segment.Length);
            foreach (var c in segment)
            {
                if (invalid.Contains(c)) escaped.Append('%').Append(((int)c).ToString("X2"));
                else escaped.Append(c);
            }

            return escaped.ToString();
        }

    }
}
#endif

using System.Collections;
using System.Collections.Generic;
using System.IO;
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
        // and reads as blank, having passed every check that asks only whether pixels differ. The whole
        // requirement is that it be opaque and dark enough for that layer to separate — deliberately not
        // pinned to the preview window's own backdrop, which is private to the editor assembly, so writing
        // its value here would be an unpinned mirror that drifts the first time the window is restyled.
        // Utilities drawn from _palette.uss's Tailwind scale are opaque and would not have needed this.
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
            ClearCapturesThisHarnessWrote(outputDirectory);

            // Act
            // Asked once, on a host of its own: whether the bundled sheet's plain classes resolve is a
            // property of the sheet, not of a story, and a probe living on a story's own panel root joins
            // that panel's logical child slots — where ChildReconciler expects the mounted tree alone, so
            // only:/last:/even:/nth-last-child would resolve against a sibling the preview window does not
            // have, and the capture would differ from the thing it exists to show.
            var probeResult = new CaptureResult();
            yield return CheckBundledStyleSheetResolves(probeResult);

            var bad = new List<string>();
            if (probeResult.Problem != null) bad.Add($"(all stories): {probeResult.Problem}");
            var written = new List<string>();
            foreach (var story in stories)
            {
                // The [VelvetPreviewSetup] environment is not run here: VelvetPreviewHost.Mount runs it for
                // the story's own assembly and tears it down on Dispose. Running it around the loop as well
                // would stand in for that call if it ever stopped happening, which is the behaviour a capture
                // is supposed to be reporting on.
                var result = new CaptureResult();
                var path = CapturePath(outputDirectory, story);
                if (written.Contains(path)) bad.Add($"{story.Id}: would overwrite the capture of another story");
                yield return Capture(story, path, result);
                if (File.Exists(path)) written.Add(path);
                if (result.Problem != null) bad.Add($"{story.Id}: {result.Problem}");
            }

            File.WriteAllLines(ManifestPath(outputDirectory), written);
            Debug.Log($"[StoryCapture] wrote {written.Count} PNG(s) to {outputDirectory}");

            // Assert
            // Every way a capture lies that still writes a file and reports success, in one comparison: the
            // story did not mount, the bundled stylesheet was inert, the frame came out uniform, or two
            // stories resolved to one path so the second silently replaced the first. The count is of files
            // that actually exist on disk afterwards — counting loop iterations against the list the loop
            // walks cannot fail, which is the shape this fixture already shipped once.
            //
            // The uniform check is by far the weakest of them: one differing pixel satisfies it. That is why
            // the stylesheet probe stands beside it rather than being folded into it, and why CONTRIBUTING
            // tells the reader to open the images.
            Assert.That(
                (written.Count, string.Join(" | ", bad)),
                Is.EqualTo((stories.Count, string.Empty)));
        }

        private sealed class CaptureResult
        {
            public string Problem;
        }

        // A story since renamed or deleted otherwise leaves its last capture beside the current ones with
        // nothing marking it stale, and someone reading the directory to see what Velvet renders today
        // believes it — observed as four images for three stories, with the run reporting three and passing.
        //
        // Driven from a manifest of what the previous run wrote, not from a marker vouching for a whole
        // subtree: VELVET_STORY_CAPTURE_DIR points wherever its author likes, and a file this harness never
        // wrote is never ours to delete. A directory with no manifest loses nothing and gains one, so the
        // only run that can leave a stale capture behind is the first into a folder that was not ours.
        // The converse is not safety: a file this harness DID write is deleted on the next run, so a
        // directory holding both is not one to point the variable at.
        private static void ClearCapturesThisHarnessWrote(string outputDirectory)
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

        // Asked of a class rather than of the styleSheets list: a sheet that is attached and a sheet whose
        // rules reach an element are different questions, and only the second one matters. This is the trap
        // the harness itself fell into — arbitrary-value utilities resolve to inline style and keep working
        // with no sheet at all, so a capture built from them looks correct while every plain class silently
        // does nothing, and the uniform-frame check does not notice because the backdrop and the font alone
        // leave a frame that differs from itself.
        private static IEnumerator CheckBundledStyleSheetResolves(CaptureResult result)
        {
            using var host = new RenderTexturePanelHost("StyleSheetProbe", 16, 16);
            host.Root.LoadBundledStyleUtilitiesForTest();
            var probe = new VisualElement();
            probe.AddToClassList("bg-slate-700");
            host.Root.Add(probe);

            yield return WaitRealtime(0.2);

            result.Problem = probe.resolvedStyle.backgroundColor == default
                ? "the bundled stylesheet resolves none of its plain classes"
                : null;
        }

        private static IEnumerator Capture(VelvetPreviewStory story, string path, CaptureResult result)
        {
            var width = story.Width > 0 ? story.Width : FallbackWidth;
            var height = story.Height > 0 ? story.Height : FallbackHeight;

            using var host = new RenderTexturePanelHost(story.Id, width, height);
            host.Root.style.unityFontDefinition =
                new StyleFontDefinition(FontDefinition.FromFont(s_builtinFont));
            host.Root.style.backgroundColor = BackdropBehindTheStory;
            // The panel root stretches to the texture's width on its own but its height hugs its content —
            // measured at 95 of 200, 144 of 320 and 1230 of 1400 before this was set. A story authored with
            // `h-full` then resolves against whatever its own content happened to be tall, which is
            // circular, and everything below that line captures as untouched texture. Set rather than
            // asserted afterwards: an assertion comparing the root's resolved layout against the same two
            // locals written here cannot fail, and reads as a guard while being one.
            host.Root.style.width = width;
            host.Root.style.height = height;
            host.Root.LoadBundledStyleUtilitiesForTest();

            using var previewHost = new VelvetPreviewHost(host.Root);
            previewHost.Mount(story);

            // Long enough to absorb a first-run text-shaping and glyph-atlas warm-up, the same posture the
            // package's own playback specs take for their first draw.
            yield return WaitRealtime(0.5);

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

        private static string ResolveOutputDirectory()
        {
            var configured = System.Environment.GetEnvironmentVariable(OutputDirectoryVariable);
            if (!string.IsNullOrEmpty(configured)) return configured;
            // Application.dataPath is <project>/Assets, so the parent is the project root a worktree's own
            // Logs sits under — the captures follow the checkout that produced them.
            return Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, DefaultOutputDirectory);
        }

        // The story's group becomes a directory and its name the file, mirroring how the preview window
        // lists them, so the output stays readable to whoever opens it. Each segment is escaped on its own
        // rather than the id as a whole: an id is Group + "/" + Name and the registry rejects only duplicate
        // ids, so flattening it with a character that either half may also contain collapses two stories
        // onto one path — "Examples/Tall List" and "Examples/Tall_List" both became Examples_Tall_List.png,
        // one overwriting the other while the count still agreed. Escaping '%' first keeps it reversible.
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

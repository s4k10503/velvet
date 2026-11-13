using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a command-buffer blit does inside an immediate draw the renderer clips with a scissor, which the
    /// declarations over the DisableScissorRect commands in <c>BackdropRenderer.Blit</c> and
    /// <c>BackdropFrame.Capture</c> rest on: the probe blits through a command buffer as they do, without that command.
    /// </summary>
    [Timeout(600000)]
    internal sealed class BackdropBlitScissorTests
    {
        private const int Size = 120;
        private const int DestinationSize = 64;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;
        private ScissorBlitProbe _probe;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _probe?.Release();
            _probe = null;
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private sealed class ScissorBlitProbe : ImmediateModeElement
        {
            private static readonly MethodInfo s_scissorEnabled = typeof(IPanel).Assembly
                .GetType("UnityEngine.UIElements.UIR.Utility")
                ?.GetMethod("IsScissorEnabled", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            private readonly CommandBuffer _commands = new() { name = nameof(ScissorBlitProbe) };

            public ScissorBlitProbe()
            {
                Destination = new RenderTexture(DestinationSize, DestinationSize, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                RenderTexture.active = Destination;
                GL.Clear(false, true, Color.black);
                RenderTexture.active = previous;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.width = 20f;
                style.height = 20f;
            }

            public RenderTexture Destination { get; }

            public bool? ScissorBefore { get; private set; }

            public bool? ScissorAfter { get; private set; }

            protected override void ImmediateRepaint()
            {
                ScissorBefore = (bool)s_scissorEnabled.Invoke(null, null);
                _commands.Clear();
                _commands.Blit(Texture2D.whiteTexture, Destination);
                Graphics.ExecuteCommandBuffer(_commands);
                ScissorAfter = (bool)s_scissorEnabled.Invoke(null, null);
            }

            public void Release()
            {
                RemoveFromHierarchy();
                _commands.Release();
                Destination.Release();
                Object.Destroy(Destination);
            }
        }

        // The clip, 20px at (10, 70), is smaller than the destination, so a blit it cut leaves texels unwritten.
        private IEnumerator RenderProbe()
        {
            _probe = new ScissorBlitProbe();
            _host = new RenderTexturePanelHost(nameof(BackdropBlitScissorTests), Size, Size);
            _mounted = V.Mount(_host.Root, V.Div(name: "stage", className: $"w-[{Size}px] h-[{Size}px]",
                children: new VNode[] { V.Div(name: "clip", className: "w-[20px] h-[20px]") }));
            var clip = _host.Root.Q<VisualElement>("clip");
            clip.style.position = Position.Absolute;
            clip.style.left = 10f;
            clip.style.top = 70f;
            clip.style.overflow = Overflow.Hidden;
            clip.Add(_probe);
            yield return WaitRealtimeDraining(0.5, _host.TargetTexture);
        }

        // GREEN_ON_BASE(characterization): pins the engine behaviour BackdropRenderer's scissor declarations name.
        [UnityTest]
        public IEnumerator Given_AnImmediateDrawUnderAClippingParent_When_ItBlitsThroughACommandBuffer_Then_TheWholeDestinationIsWritten()
        {
            // Arrange
            yield return RenderProbe();

            // Act — the white texels, or -1 where no scissor was on when the probe drew.
            var written = -1;
            if (_probe.ScissorBefore == true)
            {
                written = 0;
                var texels = RenderTexturePixelReader.ReadPixels(_probe.Destination,
                    new RectInt(0, 0, DestinationSize, DestinationSize));
                foreach (var p in texels)
                {
                    written += p.r > 200 && p.g > 200 && p.b > 200 ? 1 : 0;
                }
            }

            // Assert
            Assert.That(written, Is.EqualTo(DestinationSize * DestinationSize));
        }

        // GREEN_ON_BASE(characterization): pins the engine behaviour BackdropRenderer's scissor declarations name.
        [UnityTest]
        public IEnumerator Given_AnImmediateDrawUnderAClippingParent_When_ItBlitsThroughACommandBuffer_Then_NoScissorIsOnAfterIt()
        {
            // Arrange
            yield return RenderProbe();

            // Act — 1 where a scissor was on before the blit and none after it, 0 where one stayed on, and -1 where
            // none was on before.
            var reading = _probe.ScissorBefore != true ? -1 : _probe.ScissorAfter == false ? 1 : 0;

            // Assert
            Assert.That(reading, Is.EqualTo(1));
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the backdrop renderer records a <c>DisableScissorRect</c> before each blit it records, read off the
    /// size of the command buffer it records into against a buffer recording the same blit alone.
    /// </summary>
    internal sealed class BackdropScissorResetTests
    {
        private static void Set(BackdropFrame frame, string field, object value)
            => typeof(BackdropFrame).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(frame, value);

        // GREEN_ON_BASE(characterization): the base records a scissor reset before a pass's blit.
        [Test]
        public void Given_APassBlit_When_TheRendererRecordsIt_Then_ItsBufferHoldsMoreThanTheBlitAlone()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("a command-buffer blit");
            var material = new Material(Shader.Find(VelvetShaders.BackdropFilter));
            var destination = RenderTexture.GetTemporary(8, 8);
            using var alone = new CommandBuffer();
            alone.Blit(Texture2D.whiteTexture, destination, material, BackdropShader.StagePass);
            BackdropRenderer.Commands.Clear();

            try
            {
                // Act
                BackdropRenderer.Blit(material, Texture2D.whiteTexture, destination);
                var recorded = BackdropRenderer.Commands.sizeInBytes;

                // Assert — what the renderer recorded beyond the blit, or -1 where the blit alone records nothing.
                Assert.That(alone.sizeInBytes > 0 ? recorded - alone.sizeInBytes : -1, Is.GreaterThan(0),
                    $"recorded={recorded} alone={alone.sizeInBytes}");
            }
            finally
            {
                RenderTexture.ReleaseTemporary(destination);
                Object.DestroyImmediate(material);
            }
        }

        // GREEN_ON_BASE(characterization): the base records a scissor reset before a capture's blit.
        [Test]
        public void Given_AMultisampledTarget_When_AFrameCapturesIt_Then_ItsBufferHoldsMoreThanTheBlitAlone()
        {
            // Arrange — a multisampled target is captured by a blit rather than a copy.
            TestGraphics.IgnoreIfHeadless("a command-buffer blit");
            var target = new RenderTexture(8, 8, 0) { antiAliasing = 4 };
            var frame = new BackdropFrame { Region = new RectInt(0, 0, 8, 8) };
            Set(frame, "_target", target);
            Set(frame, "_targetWidth", 8);
            Set(frame, "_targetHeight", 8);
            var previous = RenderTexture.active;
            RenderTexture captured = null;
            using var alone = new CommandBuffer();
            BackdropRenderer.Commands.Clear();

            try
            {
                // Act
                RenderTexture.active = target;
                captured = frame.Capture();
                var recorded = BackdropRenderer.Commands.sizeInBytes;
                alone.Blit(BuiltinRenderTextureType.CurrentActive, captured, Vector2.one, Vector2.zero);

                // Assert — what the frame recorded beyond the blit, or -1 where the blit alone records nothing.
                Assert.That(alone.sizeInBytes > 0 ? recorded - alone.sizeInBytes : -1, Is.GreaterThan(0),
                    $"recorded={recorded} alone={alone.sizeInBytes}");
            }
            finally
            {
                RenderTexture.active = previous;
                if (captured != null)
                {
                    RenderTexture.ReleaseTemporary(captured);
                }
                target.Release();
                Object.DestroyImmediate(target);
            }
        }
    }
}

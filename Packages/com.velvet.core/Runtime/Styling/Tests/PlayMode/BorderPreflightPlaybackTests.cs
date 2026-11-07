using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// What a border-width utility draws with no border colour of its own, read back from the GPU: a band in the
    /// element's text colour, as Tailwind's preflight gives it.
    /// </summary>
    /// <remarks>
    /// The panel carries no stylesheet, so the width and the colour are bracket values Velvet resolves itself.
    /// Every sample is cut from the measured <see cref="VisualElement.worldBound"/>, and the box's interior and
    /// an element without a width are the controls that show the strip landed.
    /// </remarks>
    [Timeout(600000)]
    internal sealed class BorderPreflightPlaybackTests
    {
        private const int Size = 160;
        private const float BandWidth = 8f;

        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        // Cleared, so that a band read back as red is one this panel drew.
        private IEnumerator MountBoxes()
        {
            _host = new RenderTexturePanelHost("BorderPreflight", Size, Size, clear: true);
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "flex-row", children: new VNode[]
            {
                V.Div(name: "bordered", className: "ml-[10px] mt-[10px] w-[60px] h-[60px] border-[8px] text-[#ff0000]"),
                V.Div(name: "plain", className: "ml-[10px] mt-[10px] w-[60px] h-[60px] text-[#ff0000]"),
            }));
            _host.Root.Q<VisualElement>("row").style.flexDirection = FlexDirection.Row;
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);
        }

        private int CountRed(Rect panelRect)
        {
            var pixels = RenderTexturePixelReader.ReadPixels(_host.TargetTexture, new RectInt(0, 0, Size, Size));
            var count = 0;
            for (var y = Mathf.RoundToInt(panelRect.yMin); y < Mathf.RoundToInt(panelRect.yMax); y++)
            {
                for (var x = Mathf.RoundToInt(panelRect.xMin); x < Mathf.RoundToInt(panelRect.xMax); x++)
                {
                    // The texture is read bottom-up and panel y grows downward.
                    count += RenderTexturePixelReader.IsRedPixel(pixels[((Size - 1 - y) * Size) + x]) ? 1 : 0;
                }
            }
            return count;
        }

        // A panel with the bundled utilities, and one bordered element whose text colour hover changes on a transition.
        private IEnumerator MountTransitioning()
        {
            _host = new RenderTexturePanelHost("BorderPreflight", Size, Size, clear: true);
            VelvetStyleUtilities.AttachTo(_host.Root);
            yield return Frames(3);
            _mounted = V.Mount(_host.Root, V.Div(name: "button",
                className: "w-[60px] h-[30px] border transition-colors text-blue-500 hover:text-red-500"));
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);
        }

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private static void Hover(VisualElement element, bool on)
        {
            var property = EngineMember.PseudoStates.ResolveProperty();
            var flag = Convert.ToInt32(Enum.Parse(property.PropertyType, "Hover"));
            var current = Convert.ToInt32(property.GetValue(element));
            property.SetValue(element, Enum.ToObject(property.PropertyType, on ? current | flag : current & ~flag));
            if (on)
            {
                using var over = PointerOverEvent.GetPooled();
                element.SimulateEvent(over);
            }
            else
            {
                using var leave = PointerOutEvent.GetPooled();
                element.SimulateEvent(leave);
            }
        }

        [UnityTest]
        public IEnumerator Given_ABorderedElementWhoseTextColourTransitionsOnHover_When_TheTransitionEnds_Then_ItsBorderIsTheTextColour()
        {
            // Arrange
            yield return MountTransitioning();
            var button = _host.Root.Q<VisualElement>("button");

            // Act
            Hover(button, true);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(button.resolvedStyle.borderTopColor, Is.EqualTo(button.resolvedStyle.color));
        }

        [UnityTest]
        public IEnumerator Given_ABorderedElementWhoseHoverTransitionEnded_When_ThePointerLeaves_Then_ItsBorderFollowsTheTextColourBack()
        {
            // Arrange
            yield return MountTransitioning();
            var button = _host.Root.Q<VisualElement>("button");
            Hover(button, true);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Act
            Hover(button, false);
            yield return WaitRealtimeDraining(0.8, _host.TargetTexture);

            // Assert
            Assert.That(button.resolvedStyle.borderTopColor, Is.EqualTo(button.resolvedStyle.color));
        }

        [UnityTest]
        public IEnumerator Given_AnElementWithAColourTransition_When_ItMountsWithABorderWidth_Then_ItsBorderIsNotFadingIn()
        {
            // Arrange
            _host = new RenderTexturePanelHost("BorderPreflight", Size, Size, clear: true);
            VelvetStyleUtilities.AttachTo(_host.Root);
            yield return Frames(3);

            // Act
            _mounted = V.Mount(_host.Root, V.Div(name: "button",
                className: "w-[60px] h-[30px] border transition-colors duration-1000 text-red-500"));
            yield return Frames(2);
            var button = _host.Root.Q<VisualElement>("button");

            // Assert
            Assert.That(button.resolvedStyle.borderTopColor, Is.EqualTo(button.resolvedStyle.color));
        }

        [UnityTest]
        public IEnumerator Given_AStylesheetBorderColourThatHoverSetsOnATransition_When_ThePointerLeaves_Then_ItTransitionsBackToTheTextColour()
        {
            // Arrange
            _host = new RenderTexturePanelHost("BorderPreflight", Size, Size, clear: true);
            VelvetStyleUtilities.AttachTo(_host.Root);
            yield return Frames(3);
            _mounted = V.Mount(_host.Root, V.Div(name: "button",
                className: "w-[60px] h-[30px] border transition-colors duration-1000 text-[#ff0000] hover:border-blue-500"));
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);
            var button = _host.Root.Q<VisualElement>("button");
            Hover(button, true);
            yield return WaitRealtimeDraining(1.4, _host.TargetTexture);

            // Act
            Hover(button, false);
            yield return WaitRealtimeDraining(0.4, _host.TargetTexture);
            var mid = button.resolvedStyle.borderTopColor;

            // Assert — neither where the transition began nor where it ends.
            Color blue = new Color32(0x2B, 0x7F, 0xFF, 0xFF);
            Assert.That(mid != blue && mid != new Color(1f, 0f, 0f, 1f), Is.True, $"mid={mid}");
        }

        // The middle third of the left band, two pixels in from each edge of it.
        private static Rect LeftBand(Rect box)
            => new(box.xMin + 2f, box.yMin + (box.height / 3f), BandWidth - 4f, box.height / 3f);

        [UnityTest]
        public IEnumerator Given_ABorderWidthAndATextColour_When_Rendered_Then_TheBandIsInTheTextColour()
        {
            // Arrange
            yield return MountBoxes();

            // Act
            var box = _host.Root.Q<VisualElement>("bordered").worldBound;

            // Assert — the strip lies on the 8px band, which the box's interior beside it does not share.
            Assert.That(box.width == 60f && CountRed(LeftBand(box)) > 0, Is.True, $"box={box}");
        }

        [UnityTest]
        public IEnumerator Given_ABorderWidthAndATextColour_When_Rendered_Then_TheInteriorIsNotInTheTextColour()
        {
            // Arrange
            yield return MountBoxes();

            // Act
            var box = _host.Root.Q<VisualElement>("bordered").worldBound;
            var interior = new Rect(box.xMin + 20f, box.yMin + 20f, 20f, 20f);

            // Assert — the control: the strip above is a band, not a filled box.
            Assert.That(box.width == 60f ? CountRed(interior) : -1, Is.Zero, $"box={box}");
        }

        [UnityTest]
        public IEnumerator Given_ATextColourWithoutABorderWidth_When_Rendered_Then_NoBandIsDrawn()
        {
            // Arrange
            yield return MountBoxes();

            // Act
            var box = _host.Root.Q<VisualElement>("plain").worldBound;

            // Assert
            Assert.That(box.width == 60f ? CountRed(LeftBand(box)) : -1, Is.Zero, $"box={box}");
        }
    }
}

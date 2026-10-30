using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a border width written in pixels on a runtime panel whose scale is not a whole number of device pixels
    /// a point: the browser takes it down to the device-pixel grid, 3px at 1.5 device pixels a point being 8/3 of one,
    /// both where the element mounts onto that panel and where the panel's scale changes under it.
    /// </summary>
    internal sealed class BorderWidthScalePlaybackTests
    {
        private const float SnappedThree = 8f / 3f;

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

        private static IEnumerator Frames(int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return null;
            }
        }

        private VisualElement Mount(string className)
        {
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: className));
            return _host.Root.Q<VisualElement>("box");
        }

        [UnityTest]
        public IEnumerator Given_AWholePixelBorderWidthOnAPanelAtOneAndAHalfDevicePixels_When_Mounted_Then_ItIsSnappedDown()
        {
            // Arrange
            _host = new RenderTexturePanelHost("BorderScale", 200, 200) { Scale = 1.5f };
            yield return Frames(3);

            // Act
            var box = Mount("w-[50px] h-[50px] border-solid border-[3px]");
            yield return Frames(5);

            // Assert
            Assert.That(box.style.borderTopWidth.value, Is.EqualTo(SnappedThree).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_AWholePixelBorderWidthMountedAtOneDevicePixel_When_ThePanelScaleBecomesOneAndAHalf_Then_ItIsSnappedDown()
        {
            // Arrange
            _host = new RenderTexturePanelHost("BorderScale", 200, 200) { Scale = 1f };
            yield return Frames(3);
            var box = Mount("w-[50px] h-[50px] border-solid border-[3px]");
            yield return Frames(5);

            // Act
            _host.Scale = 1.5f;
            yield return Frames(10);

            // Assert
            Assert.That(box.style.borderTopWidth.value, Is.EqualTo(SnappedThree).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_AWholePixelBorderWidthDetachedAcrossAScaleChange_When_ItIsAttachedAgain_Then_LaterScaleChangesSnapItAgain()
        {
            // Arrange — detached while the scale moves, so the watch lets go of it.
            _host = new RenderTexturePanelHost("BorderScale", 200, 200) { Scale = 1f };
            yield return Frames(3);
            var box = Mount("w-[50px] h-[50px] border-solid border-[3px]");
            yield return Frames(5);
            box.RemoveFromHierarchy();
            _host.Scale = 1.5f;
            yield return Frames(10);
            _host.Root.Add(box);
            yield return Frames(5);

            // Act
            _host.Scale = 1.25f;
            yield return Frames(10);

            // Assert — 3px is three and three quarters of a device pixel, taken down to three: 2.4px.
            Assert.That(box.style.borderTopWidth.value, Is.EqualTo(2.4f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_AWholePixelDivideWidth_When_ThePanelScaleBecomesOneAndAHalf_Then_TheDividerIsSnappedDown()
        {
            // Arrange
            _host = new RenderTexturePanelHost("BorderScale", 200, 200) { Scale = 1f };
            yield return Frames(3);
            _mounted = V.Mount(_host.Root, V.Div(name: "row", className: "flex flex-row w-[100px] h-[50px] divide-x-[3px]",
                children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[10px] h-[10px]"),
                    V.Div(name: "b", className: "w-[10px] h-[10px]"),
                }));
            yield return Frames(5);

            // Act
            _host.Scale = 1.5f;
            yield return Frames(10);

            // Assert
            Assert.That(_host.Root.Q<VisualElement>("b").style.borderLeftWidth.value, Is.EqualTo(SnappedThree).Within(1e-4f));
        }
    }
}

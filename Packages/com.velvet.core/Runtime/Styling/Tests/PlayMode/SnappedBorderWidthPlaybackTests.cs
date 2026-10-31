using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A bundled border width on a runtime panel whose scale is not a whole number of device pixels a point: the
    /// browser takes it down to the device-pixel grid, never below one device pixel for a width that is not zero, both
    /// where the element mounts onto that panel and where the panel's scale changes under it.
    /// </summary>
    internal sealed class SnappedBorderWidthPlaybackTests
    {
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

        private IEnumerator Mount(float scale, string className)
        {
            _host = new RenderTexturePanelHost("SnappedBorder", 200, 200) { Scale = scale };
            VelvetStyleUtilities.AttachTo(_host.Root);
            yield return Frames(3);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: "w-[50px] h-[50px] " + className));
            yield return Frames(5);
        }

        private IEnumerator MountHandAttached(float scale, string className)
        {
            _host = new RenderTexturePanelHost("SnappedBorder", 200, 200) { Scale = scale };
            _host.Root.styleSheets.Add(VelvetStyleUtilities.Sheet);
            yield return Frames(3);
            _mounted = V.Mount(_host.Root, V.Div(name: "box", className: "w-[50px] h-[50px] " + className));
            yield return Frames(5);
        }

        private VisualElement Box => _host.Root.Q<VisualElement>("box");

        [UnityTest]
        public IEnumerator Given_TheBareBorderOnAPanelAtOneAndAHalf_When_Mounted_Then_ItIsTwoThirdsOfAPixel()
        {
            // Act
            yield return Mount(1.5f, "border");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(2f / 3f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_BorderTwoOnAPanelAtOneAndAQuarter_When_Mounted_Then_ItIsOneAndSixTenths()
        {
            // Act
            yield return Mount(1.25f, "border-2");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(1.6f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_TheBareBorderOnAPanelAtOneAndAQuarter_When_Mounted_Then_ItIsFourFifthsOfAPixel()
        {
            // Act
            yield return Mount(1.25f, "border");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(0.8f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_ASideWidthOnAPanelAtOneAndAHalf_When_Mounted_Then_OnlyThatSideIsSnapped()
        {
            // Act
            yield return Mount(1.5f, "border-l");

            // Assert
            Assert.That(Box.resolvedStyle.borderLeftWidth, Is.EqualTo(2f / 3f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_ASideWidthOnAPanelAtOneAndAHalf_When_Mounted_Then_TheOtherSidesHaveNone()
        {
            // Act
            yield return Mount(1.5f, "border-l");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Given_AnAxisWidthAndASideWidthOnAPanelAtOneAndAHalf_When_Mounted_Then_TheLaterSideRuleWinsItsSide()
        {
            // Act
            yield return Mount(1.5f, "border-x border-l-8");

            // Assert — 8px is twelve device pixels, which is eight whole points.
            Assert.That(Box.resolvedStyle.borderLeftWidth, Is.EqualTo(8f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_AnAxisWidthAndASideWidthOnAPanelAtOneAndAHalf_When_Mounted_Then_TheAxisRuleKeepsTheOtherSide()
        {
            // Act
            yield return Mount(1.5f, "border-x border-l-8");

            // Assert
            Assert.That(Box.resolvedStyle.borderRightWidth, Is.EqualTo(2f / 3f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_TheBundledUtilitiesAttachedWithoutAttachTo_When_TheBareBorderIsMountedAtOneAndAHalf_Then_ItIsSnapped()
        {
            // Act
            yield return MountHandAttached(1.5f, "border");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(2f / 3f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_TheBareBorderOnAPanelAtTwo_When_Mounted_Then_ItIsOnePoint()
        {
            // Act
            yield return Mount(2f, "border");

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(1f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_TheBareBorderMountedAtOneDevicePixel_When_ThePanelScaleBecomesOneAndAHalf_Then_ItIsSnapped()
        {
            // Arrange
            yield return Mount(1f, "border");

            // Act
            _host.Scale = 1.5f;
            yield return Frames(30);

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(2f / 3f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_TheBareBorderSnappedAtOneAndAHalf_When_ThePanelScaleBecomesOne_Then_ItIsOnePointAgain()
        {
            // Arrange
            yield return Mount(1.5f, "border");

            // Act
            _host.Scale = 1f;
            yield return Frames(30);

            // Assert
            Assert.That(Box.resolvedStyle.borderTopWidth, Is.EqualTo(1f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Given_ASnappedPanel_When_ItsStyleSheetsAreRead_Then_TheSnappedSheetFollowsTheBundledUtilities()
        {
            // Arrange
            yield return Mount(1.5f, "border");
            var sheets = _host.Root.styleSheets;
            var at = -1;
            for (var i = 0; i < sheets.count; i++)
            {
                at = sheets[i] == VelvetStyleUtilities.Sheet ? i : at;
            }

            // Assert
            Assert.That(at >= 0 && at + 1 < sheets.count && SnappedBorderSheets.IsSnapped(sheets[at + 1]), Is.True);
        }
    }
}

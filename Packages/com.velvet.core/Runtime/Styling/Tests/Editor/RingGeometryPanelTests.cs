using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Geometry coverage for the ring overlay: once the inner is LAID OUT (a real panel), the band must size,
    /// position and round to the inner's resolved box. Outset (default): inflated by (offset + width) per side
    /// with outer radius = innerRadius + offset + width. Inset (ring-inset): matches the inner box at the inner
    /// radius. Off-panel the inner has no resolved size, so this needs a real <see cref="UnityEditor.EditorWindow"/>
    /// panel + forced layout (the spec's width/color are asserted off-panel in RingWrapTests). GWT, one assert each.
    /// </summary>
    [TestFixture]
    internal sealed class RingGeometryPanelTests : PanelTestBase
    {
        // The ring overlay is the wrapper's second child ([inner, overlay]).
        private VisualElement MountRinged(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: className, name: "card"));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            return _window.rootVisualElement[0][1];
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlayInflatesByTwicePerSideWidth()
        {
            // 100x40 card, ring-2, no offset: the band is 2px outside each edge → overlay is 100+4 wide.
            var overlay = MountRinged("w-[100px] h-[40px] ring-2");

            Assert.That(overlay.resolvedStyle.width, Is.EqualTo(104f));
        }

        [Test]
        public void Given_Ring2_When_LaidOut_Then_OverlaySitsTwoPixelsOutsideTheInnerLeftEdge()
        {
            // The band's left edge sits (offset + width) = 2px outside the inner's left within the wrapper.
            // Asserted RELATIVE to the inner (the passthrough wrapper may stretch + center the inner, so the
            // absolute left is the inner's centered x minus 2, not -2).
            MountRinged("w-[100px] h-[40px] ring-2");
            var inner = _window.rootVisualElement[0][0];
            var overlay = _window.rootVisualElement[0][1];

            Assert.That(inner.layout.x - overlay.resolvedStyle.left, Is.EqualTo(2f));
        }

        [Test]
        public void Given_Ring2WithRoundedLg_When_LaidOut_Then_OuterRadiusAddsTheRingWidth()
        {
            // rounded-lg = 8px inner radius; the outset band's outer corner = 8 + 0 + 2 = 10.
            var overlay = MountRinged("w-[100px] h-[40px] rounded-lg ring-2");

            Assert.That(overlay.resolvedStyle.borderTopLeftRadius, Is.EqualTo(10f));
        }

        [Test]
        public void Given_RingWithOffset_When_LaidOut_Then_BandInflatesByOffsetPlusWidth()
        {
            // ring-2 ring-offset-4: the band sits 4px out then a 2px stroke → overlay is 100 + 2*(4+2) = 112.
            var overlay = MountRinged("w-[100px] h-[40px] ring-2 ring-offset-4");

            Assert.That(overlay.resolvedStyle.width, Is.EqualTo(112f));
        }

        [Test]
        public void Given_InsetRing_When_LaidOut_Then_OverlayMatchesTheInnerBox()
        {
            // ring-inset: the band is drawn inside, so the overlay matches the inner box exactly (no inflation).
            var overlay = MountRinged("w-[100px] h-[40px] ring-2 ring-inset");

            Assert.That(overlay.resolvedStyle.width, Is.EqualTo(100f));
        }

        [Test]
        public void Given_InsetRingWithRoundedLg_When_LaidOut_Then_OuterRadiusEqualsTheInnerRadius()
        {
            // An inset band hugs the inner edge, so its corner radius is the inner radius (8px), NOT inflated.
            var overlay = MountRinged("w-[100px] h-[40px] rounded-lg ring-2 ring-inset");

            Assert.That(overlay.resolvedStyle.borderTopLeftRadius, Is.EqualTo(8f));
        }
    }
}

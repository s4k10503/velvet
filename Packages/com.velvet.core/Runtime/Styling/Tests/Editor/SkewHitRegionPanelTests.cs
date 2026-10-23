using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins picking at two coordinates derived from a <c>skew-*</c> caster on the painted layer: a direct child's
    /// translated seat, and a corner of the caster's layout box that the shear leaves bare.
    /// </summary>
    /// <remarks>
    /// The child reading is the control for the corner one: it is true only where the picker reaches the caster's
    /// subtree at all, and a caster picked by its box rather than its sheared shape would leave the corner true. Both
    /// points come off the measured pre-transform layout: the child one from the seat given to its centroid, which
    /// <see cref="SilhouetteFace"/>'s shear map decides, and the corner one two pixels inside the top-right corner of
    /// the layout box. Reading the child's <c>worldBound</c> instead would fold the seat into the coordinate and leave
    /// the canary true without it.
    /// </remarks>
    internal sealed class SkewHitRegionPanelTests : PanelTestBase
    {
        private const float SkewDegrees = 12f;

        // A rotate class keeps the caster on the painted layer.
        [Test]
        public void Given_ASkewedCaster_When_PickedAtASeatedChildAndABareCorner_Then_OnlyTheChildIsPicked()
        {
            // Arrange — a wide short caster carrying one child low enough in the box that the shear seats it
            // measurably sideways. Sizes are arbitrary values, which resolve to inline style, so this panel
            // needs no stylesheet; skew-x-* carries no USS rule either.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "caster", className: $"w-[200px] h-[48px] skew-x-{(int)SkewDegrees} rotate-0",
                    children: new VNode?[] { V.Div(name: "tab", className: "w-[20px] h-[20px] mt-[28px]") }));
            var caster = _window.rootVisualElement.Q<VisualElement>("caster");
            var child = _window.rootVisualElement.Q<VisualElement>("tab");
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            var box = caster.worldBound;
            var tab = child.layout;

            // Act — a point one pixel past the child's unseated right edge, which its translated seat covers, and a
            // point two pixels inside the layout box's top-right corner, where the shear has moved the top edge's end
            // away.
            var onSeatedChild = new Vector2(box.xMin + tab.xMax + 1f, box.yMin + tab.center.y);
            var bareCorner = new Vector2(box.xMax - 2f, box.yMin + 2f);

            // Assert
            Assert.That((caster.panel.Pick(onSeatedChild) == child, caster.panel.Pick(bareCorner) == caster),
                Is.EqualTo((true, false)));
        }
    }
}

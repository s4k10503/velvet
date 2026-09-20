using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins picking at two coordinates derived from a <c>skew-*</c> caster: a direct child's translated seat
    /// and a point beyond the caster's axis-aligned layout box.
    /// </summary>
    /// <remarks>
    /// The outside-box reading is satisfiable with no skew, so the child reading is its control: the child's
    /// translated seat must reach the picker in the same frame. Both sample points come off the measured
    /// pre-transform layout through <see cref="SilhouetteFace"/>'s shear map: the outside point uses the
    /// direction toward the mapped bottom-edge x-coordinate, and the child point uses the seat given to its
    /// centroid. Reading the child's <c>worldBound</c> instead would fold the seat into the coordinate and leave
    /// the canary true without it.
    /// </remarks>
    internal sealed class SkewHitRegionPanelTests : PanelTestBase
    {
        private const float SkewDegrees = 12f;

        // GREEN_ON_BASE(characterization): the base picks by the unsheared box and seats children already.
        // Velvet's skew writes no transform on the caster and this branch changes neither half; the case is
        // what the styling guide's new skew paragraph rests on.
        [Test]
        public void Given_ASkewedCaster_When_PickedAtASeatedChildAndBeyondItsLayoutBox_Then_ChildIsPickedAndOutsidePointIsNot()
        {
            // Arrange — a wide short caster carrying one child low enough in the box that the shear seats it
            // measurably sideways. Sizes are arbitrary values, which resolve to inline style, so this panel
            // needs no stylesheet; skew-x-* carries no USS rule either.
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "caster", className: $"w-[200px] h-[48px] skew-x-{(int)SkewDegrees}",
                    children: new VNode?[] { V.Div(name: "tab", className: "w-[20px] h-[20px] mt-[28px]") }));
            var caster = _window.rootVisualElement.Q<VisualElement>("caster");
            var child = _window.rootVisualElement.Q<VisualElement>("tab");
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);

            var box = caster.worldBound;
            var tab = child.layout;
            var tan = Mathf.Tan(SkewDegrees * Mathf.Deg2Rad);

            // Act — a point one pixel past the child's unseated right edge, which its translated seat covers,
            // and a point beyond the caster's layout box, halfway toward the mapped bottom-edge x-coordinate.
            var onSeatedChild = new Vector2(box.xMin + tab.xMax + 1f, box.yMin + tab.center.y);
            var nearBottom = box.height - 2f;
            var beyondLayoutTowardShearedBottom = new Vector2(
                box.xMax + (((nearBottom - (box.height * 0.5f)) * tan) * 0.5f), box.yMin + nearBottom);

            // Assert
            Assert.That(
                (caster.panel.Pick(onSeatedChild) == child,
                    caster.panel.Pick(beyondLayoutTowardShearedBottom) == caster),
                Is.EqualTo((true, false)));
        }
    }
}

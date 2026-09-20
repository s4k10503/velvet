using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the asymmetry a <c>skew-*</c> caster leaves between what it paints and what answers a pointer:
    /// a direct child is moved by a real transform and its hit region goes with it, while the caster's own
    /// face is a paint and its hit region stays on the layout box.
    /// </summary>
    /// <remarks>
    /// The two readings are taken in one arrangement because the caster's alone is satisfiable with no skew
    /// at all — the caster goes unpicked outside its box whether or not it is skewed — so it is an absence,
    /// and the child's is the canary that says the shear's displacement is real, reaches the picker, and was
    /// applied in this frame. Both sample points come off the measured pre-transform layout through
    /// <see cref="SilhouetteFace"/>'s own shear map: the caster's from where that map carries its bottom
    /// edge, the child's from the seat the same map gives the child's centroid. Reading the child's
    /// <c>worldBound</c> instead would fold the seat into the coordinate and leave the canary true without it.
    /// </remarks>
    internal sealed class SkewHitRegionPanelTests : PanelTestBase
    {
        private const float SkewDegrees = 12f;

        // GREEN_ON_BASE(characterization): the base picks by the unsheared box and seats children already.
        // Velvet's skew writes no transform on the caster and this branch changes neither half; the case is
        // what the styling guide's new skew paragraph rests on.
        [Test]
        public void Given_ASkewedCaster_When_PickedAcrossItsLeanedFace_Then_OnlyTheChildsRegionFollowed()
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

            // Act — a point one pixel past the child's UNSEATED right edge, which only a seated child covers;
            // and a point halfway into the lean the caster's own bottom edge is painted at, which its layout
            // box never covers.
            var onSeatedChild = new Vector2(box.xMin + tab.xMax + 1f, box.yMin + tab.center.y);
            var nearBottom = box.height - 2f;
            var onLeanedFace = new Vector2(
                box.xMax + (((nearBottom - (box.height * 0.5f)) * tan) * 0.5f), box.yMin + nearBottom);

            // Assert
            Assert.That(
                (caster.panel.Pick(onSeatedChild) == child, caster.panel.Pick(onLeanedFace) == caster),
                Is.EqualTo((true, false)));
        }
    }
}

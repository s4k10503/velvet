using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What a panel makes of a skewed element that also carries a rotate or a scale: its descendants are drawn under the
    /// composite rather than seated, <c>origin-*</c> names the pivot, and a pointer picks the drawn shape.
    /// </summary>
    internal sealed class SkewCompositePanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private VisualElement Mount(string className, string childClassName = "w-[20px] h-[20px] mt-[10px]")
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "caster", className: className,
                    children: new VNode?[] { V.Div(name: "tab", className: childClassName) }));
            var caster = _window.rootVisualElement.Q<VisualElement>("caster");
            ForcePanelUpdate(caster.panel);
            ForcePanelUpdate(caster.panel);
            return caster;
        }

        [Test]
        public void Given_ASkewBesideARotate_When_ItsChildIsLaidOut_Then_TheChildIsDrawnUnderTheComposite()
        {
            // Arrange
            Mount("w-[200px] h-[48px] skew-x-12 rotate-45");
            var child = _window.rootVisualElement.Q<VisualElement>("tab");

            // Act
            var world = child.worldTransform;

            // Assert
            Assert.That(new[] { world.m00, world.m01, world.m10, world.m11 },
                Is.EqualTo(SkewCompositeMath.Composite(45f, 1f, 1f, 12f, 0f)).Within(1e-3f));
        }

        [Test]
        public void Given_ASkewBesideAScale_When_ItsChildIsLaidOut_Then_TheChildIsNotSeatedByATranslate()
        {
            // Arrange
            Mount("w-[200px] h-[48px] skew-x-12 scale-90");
            var child = _window.rootVisualElement.Q<VisualElement>("tab");

            // Act
            var translate = child.style.translate.keyword;

            // Assert
            Assert.That(translate, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_OriginNamesTheTopLeft_Then_ThePivotIsThatCorner()
        {
            // Arrange
            var caster = Mount("w-[200px] h-[48px] skew-x-12 rotate-45 origin-top-left");

            // Act
            var origin = caster.resolvedStyle.transformOrigin;

            // Assert
            Assert.That(new[] { origin.x.value, origin.y.value }, Is.EqualTo(new[] { 0f, 0f }));
        }

        [Test]
        public void Given_ASkewBesideARotate_When_PickedJustInsideAndJustOutsideADrawnEdge_Then_OnlyTheInsideIsPicked()
        {
            // Arrange — the probes sit three pixels either side of the middle of the top drawn edge, whose corners are
            // the layout corners taken to where the element is drawn; the axis-aligned box of the drawn shape holds both.
            var caster = Mount("w-[200px] h-[48px] skew-x-12 rotate-[30deg]");
            var width = caster.layout.width;
            Vector2 World(float x, float y)
            {
                var drawn = caster.worldTransform.MultiplyPoint3x4(new Vector3(x, y, 0f));
                return new Vector2(drawn.x, drawn.y);
            }
            var middle = (World(0f, 0f) + World(width, 0f)) * 0.5f;
            var centre = (World(0f, 0f) + World(width, 0f) + World(width, caster.layout.height)
                + World(0f, caster.layout.height)) * 0.25f;
            var outward = (middle - centre).normalized;

            // Act
            var inside = caster.panel.Pick(middle - (outward * 3f));
            var outside = caster.panel.Pick(middle + (outward * 3f));

            // Assert
            Assert.That((inside == caster, outside == caster), Is.EqualTo((true, false)));
        }
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where an <c>absolute</c> element lands on a real panel when an axis has no inset, against the
    /// static position css-flexbox gives an absolutely-positioned child of a flex container: across the
    /// container's content edges, and along its main axis where the child would sit as the container's sole flex
    /// item, so a preceding in-flow sibling does not move it. Every Velvet container is a flex container, so
    /// the block-flow position after preceding siblings never applies.
    /// </summary>
    /// <remarks>
    /// The utility classes <c>relative</c>, <c>flex</c>, <c>flex-col</c>, <c>absolute</c>, <c>justify-*</c> and
    /// <c>items-*</c> come from the bundled stylesheet, so it is attached; sizes and offsets are arbitrary values.
    /// Each case reads the child's layout rect, which is relative to its parent.
    /// </remarks>
    [TestFixture]
    internal sealed class AbsoluteStaticPositionPanelTests : PanelTestBase
    {
        private const float Tolerance = 0.01f;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // A 100x100 parent holding an in-flow 20x30 sibling, then a 10x10 absolute child carrying `childClass`.
        private float[] LayOutChild(string parentClass, string childClass)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "parent", className: parentClass + " w-[100px] h-[100px]", children: new VNode?[]
                {
                    V.Div(className: "w-[20px] h-[30px]"),
                    V.Div(name: "child", className: "absolute w-[10px] h-[10px] " + childClass),
                }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
            var layout = _window.rootVisualElement.Q<VisualElement>("child").layout;
            return new[] { layout.x, layout.y };
        }

        // GREEN_ON_BASE(characterization): the engine places the child as css-flexbox's sole flex item, not after its sibling.
        [Test]
        public void Given_AColumnFlexParentWithAPrecedingSibling_When_AnAbsoluteChildHasNoInsets_Then_ItSitsAtTheContentStart()
        {
            // Act
            var position = LayOutChild("relative flex flex-col", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 0f, 0f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the engine places the child as css-flexbox's sole flex item, not after its sibling.
        [Test]
        public void Given_ARowFlexParentWithAPrecedingSibling_When_AnAbsoluteChildHasNoInsets_Then_ItSitsAtTheContentStart()
        {
            // Act
            var position = LayOutChild("relative flex", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 0f, 0f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): css-flexbox's static-position rectangle is the content box, so padding offsets the child on both axes.
        [Test]
        public void Given_APaddedColumnFlexParent_When_AnAbsoluteChildHasNoInsets_Then_ItSitsAtTheContentStart()
        {
            // Act
            var position = LayOutChild("relative flex flex-col p-[10px]", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 10f, 10f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): css-flexbox's static-position rectangle is the content box, so padding offsets the child on both axes.
        [Test]
        public void Given_APaddedRowFlexParent_When_AnAbsoluteChildHasNoInsets_Then_ItSitsAtTheContentStart()
        {
            // Act
            var position = LayOutChild("relative flex p-[10px]", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 10f, 10f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the main-axis static position follows justify-content, as for a sole flex item.
        [Test]
        public void Given_AColumnFlexParentJustifyingCenter_When_AnAbsoluteChildHasNoInsets_Then_ItIsCenteredAlongTheColumn()
        {
            // Act
            var position = LayOutChild("relative flex flex-col justify-center", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 0f, 45f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the main-axis static position follows justify-content, as for a sole flex item.
        [Test]
        public void Given_AColumnFlexParentJustifyingEnd_When_AnAbsoluteChildHasNoInsets_Then_ItSitsAtTheColumnsEnd()
        {
            // Act
            var position = LayOutChild("relative flex flex-col justify-end", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 0f, 90f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): the cross-axis static position follows align-items.
        [Test]
        public void Given_AColumnFlexParentAligningItemsCenter_When_AnAbsoluteChildHasNoInsets_Then_ItIsCenteredAcrossTheColumn()
        {
            // Act
            var position = LayOutChild("relative flex flex-col items-center", "");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 45f, 0f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): an inset on one axis leaves the other axis at its static position.
        [Test]
        public void Given_APaddedColumnFlexParent_When_AnAbsoluteChildSetsOnlyTop_Then_ItTakesTheStaticPositionHorizontally()
        {
            // Act
            var position = LayOutChild("relative flex flex-col p-[10px]", "top-[5px]");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 10f, 5f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): an inset on one axis leaves the other axis at its static position.
        [Test]
        public void Given_APaddedRowFlexParent_When_AnAbsoluteChildSetsOnlyLeft_Then_ItTakesTheStaticPositionVertically()
        {
            // Act
            var position = LayOutChild("relative flex p-[10px]", "left-[5px]");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 5f, 10f }).Within(Tolerance));
        }

        // GREEN_ON_BASE(characterization): top-auto and left-auto reset to the unset offset, which is the static position.
        [Test]
        public void Given_APaddedColumnFlexParent_When_AnAbsoluteChildSetsTopAutoAndLeftAuto_Then_ItSitsAtTheStaticPosition()
        {
            // Act
            var position = LayOutChild("relative flex flex-col p-[10px]", "top-auto left-auto");

            // Assert
            Assert.That(position, Is.EqualTo(new[] { 10f, 10f }).Within(Tolerance));
        }
    }
}

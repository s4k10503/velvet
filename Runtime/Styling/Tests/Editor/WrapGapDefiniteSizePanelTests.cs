using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins a wrapping gap container whose width it declares itself against where CSS lays it out: a line holds
    /// as many items as fit with one gap between each pair, and the container takes its declared width among its
    /// siblings. The wrap path's negative margin leaves a declared width a gap short of that room unless the gap
    /// grows it. A real panel is needed, since the line breaks and the positions come from a layout pass. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class WrapGapDefiniteSizePanelTests : PanelTestBase
    {
        // gap-4 is 16px. Two 66px items and the gap between them come to 148px, which a 150px line holds; with a
        // gap's width missing from it the line holds 134px, and the second item wraps.
        private const float Gap = 16f;
        private const float ItemWidth = 66f;
        private const float DeclaredWidth = 150f;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private void Mount()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "flex flex-row items-start", children: new VNode[]
            {
                V.Div(name: "wrap", className: "flex flex-row flex-wrap gap-4 w-[150px]", children: new VNode[]
                {
                    V.Div(name: "a", className: "w-[66px] h-[10px]"),
                    V.Div(name: "b", className: "w-[66px] h-[10px]"),
                }),
                V.Div(name: "sibling", className: "w-[10px] h-[10px]"),
            }));
            ForcePanelUpdate(_window.rootVisualElement.panel);
        }

        [Test]
        public void Given_AWrappingGapRowOfDeclaredWidth_When_TwoItemsAndTheirGapFitIt_Then_TheSecondSitsOneGapAfterTheFirst()
        {
            // Arrange / Act
            Mount();
            var a = _window.rootVisualElement.Q("a").layout;
            var b = _window.rootVisualElement.Q("b").layout;

            // Assert — the line a wrap puts the second item on starts where the first does, a whole gap and item away.
            Assert.That(b.x - a.x, Is.EqualTo(ItemWidth + Gap).Within(0.5f));
        }

        [Test]
        public void Given_AWrappingGapRowOfDeclaredWidth_When_ASiblingFollowsIt_Then_TheSiblingStartsAtTheDeclaredWidth()
        {
            // Arrange / Act
            Mount();

            // Assert — the row's parent starts at 0; the container's outer width is its declared width, not a gap less.
            Assert.That(_window.rootVisualElement.Q("sibling").layout.x, Is.EqualTo(DeclaredWidth).Within(0.5f));
        }
    }
}

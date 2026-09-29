using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins flex-1 to Tailwind's <c>flex: 1</c>, whose basis is 0% rather than 0px, in a column that sizes to
    /// its content. A real panel with the bundled stylesheets is required: flex-1 is a USS-only rule and the
    /// difference is a layout one.
    /// </summary>
    [TestFixture]
    internal sealed class FlexOneBasisPanelTests : PanelTestBase
    {
        private const float ContentHeight = 40f;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Test]
        public void Given_AFlexOneChildInAColumnSizedByItsContent_When_LaidOut_Then_TheChildKeepsItsContentHeight()
        {
            // Arrange — the absolute column takes its height from its content, which leaves a percent basis
            // nothing to resolve against.
            var node = V.Div(className: "absolute", children: new VNode[]
            {
                V.Div(name: "grower", className: "flex-1", children: new VNode[]
                {
                    V.Div(className: "h-[40px]"),
                }),
            });

            // Act
            _mounted = V.Mount(_window.rootVisualElement, node);
            var grower = _window.rootVisualElement.Q("grower");
            ForcePanelUpdate(grower.panel);

            // Assert
            Assert.That(grower.layout.height, Is.EqualTo(ContentHeight));
        }
    }
}

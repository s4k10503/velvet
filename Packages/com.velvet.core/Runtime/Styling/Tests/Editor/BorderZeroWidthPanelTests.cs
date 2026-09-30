using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the zero-width border utilities and where the bundled sheet declares them: two utilities writing
    /// one edge resolve to the one Tailwind emits later. A real panel with the bundled stylesheets is
    /// required, since these are USS-only rules.
    /// </summary>
    [TestFixture]
    internal sealed class BorderZeroWidthPanelTests : PanelTestBase
    {
        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        // The resolved widths as "top right bottom left".
        private string ResolvedWidths(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: className));
            var box = _window.rootVisualElement.Q("box");
            ForcePanelUpdate(box.panel);
            var style = box.resolvedStyle;
            return $"{style.borderTopWidth} {style.borderRightWidth} {style.borderBottomWidth} {style.borderLeftWidth}";
        }

        [TestCase("border border-t-0", "0 1 1 1")]
        [TestCase("border border-r-0", "1 0 1 1")]
        [TestCase("border border-b-0", "1 1 0 1")]
        [TestCase("border border-l-0", "1 1 1 0")]
        [TestCase("border border-x-0", "1 0 1 0")]
        [TestCase("border border-y-0", "0 1 0 1")]
        [TestCase("border-0 border", "0 0 0 0")]
        public void Given_ABorderWithAZeroWidthUtility_When_LaidOut_Then_TheLaterUtilityInTailwindsOrderSetsTheEdge(
            string className, string expected)
        {
            // Arrange / Act
            var widths = ResolvedWidths(className);

            // Assert
            Assert.That(widths, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): the base has no border-r-0, so border-r-2 sets the edge there too.
        // What reddens it is declaring border-r-0 after border-r-2 in _borders.uss.
        [Test]
        public void Given_ARightBorderOfTwoBesideARightBorderOfZero_When_LaidOut_Then_TheWidthOfTwoWins()
        {
            // Arrange / Act — Tailwind orders border-r-0 before border-r-2.
            var widths = ResolvedWidths("border-r-2 border-r-0");

            // Assert
            Assert.That(widths, Is.EqualTo("0 2 0 0"));
        }
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Panel-backed coverage for the column WIDTH that <see cref="StyleGridManipulator"/> computes — the one
    /// part of the grid contract that needs a resolved row width (the off-panel <see cref="GridParityTests"/>
    /// cover the gap-margin placement and wiring). A <c>grid grid-cols-3 gap-4</c> row of a known width must
    /// size each column to <c>(rowWidth - (N-1)*columnGap) / N</c>: the manipulator reads
    /// <c>contentRect.width</c> on a GeometryChangedEvent and writes the inline width.
    /// </summary>
    [TestFixture]
    internal sealed class GridSizingPanelTests : PanelTestBase
    {
        private const float Space4 = 16f;

        [Test]
        public void Given_GridCols3OnA300pxRow_When_LaidOut_Then_EachColumnFillsAThirdMinusTheGaps()
        {
            // Arrange — 3 children in a 300px-wide grid-cols-3 gap-4 row (all inline, so no stylesheet needed).
            var children = new VNode[3];
            for (var i = 0; i < 3; i++)
            {
                children[i] = V.Div(className: "child");
            }
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "grid", className: "grid grid-cols-3 gap-4 w-[300px]", children: children));
            var container = _window.rootVisualElement.Q<VisualElement>("grid");

            // Act — resolve the 300px row width, then deliver a GeometryChangedEvent so the manipulator reads
            // contentRect.width and sizes the columns.
            ForcePanelUpdate(container.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                container.SimulateEvent(evt);
            }
            Assume.That(container.contentRect.width, Is.EqualTo(300f).Within(1f),
                "Precondition: the row resolved to its 300px width");

            // Assert — each column ≈ (300 - 2*16) / 3 (minus the manipulator's sub-pixel wrap-safety shave).
            var expected = (300f - 2f * Space4) / 3f;
            Assert.That(container[0].style.width.value.value, Is.EqualTo(expected).Within(2f));
        }
    }
}

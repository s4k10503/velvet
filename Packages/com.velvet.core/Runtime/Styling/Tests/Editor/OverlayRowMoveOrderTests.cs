using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the overlay a row hosts beside itself (a <c>backdrop-*</c> layer before it, a <c>ring-*</c> band
    /// after it) stands in that place once keyed rows on a live panel have been reordered so that several of them move.
    /// </summary>
    internal sealed class OverlayRowMoveOrderTests : PanelTestBase
    {
        private static string s_className = "";
        private static StateUpdater<bool> s_setReversed;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Component(Compiler = false)]
        private static VNode Rows()
        {
            var (reversed, setReversed) = Hooks.UseState(false);
            s_setReversed = setReversed;
            var order = new List<int> { 0, 1, 2, 3 };
            if (reversed) order.Reverse();
            return V.Div(name: "list", children: order
                .Select(i => (VNode)V.Div(name: "r" + i, className: s_className, key: "k" + i)).ToArray());
        }

        private string ReverseAndRead()
        {
            using var mounted = V.Mount(_window.rootVisualElement, V.Component(Rows));
            var list = _window.rootVisualElement.Q<VisualElement>("list");
            ForcePanelUpdate(list.panel);
            s_setReversed.Invoke(true);
            mounted.FlushStateForTest();
            return string.Join(",", list.Children().Select(child => child.name.StartsWith("r") ? child.name : "overlay"));
        }

        // GREEN_ON_BASE(refactor): the base moves each row at once, so the attach that ends each move places the
        // overlay against the list as it then stands, which is already final for it.
        [Test]
        public void Given_FourKeyedRowsWithABackdropLayer_When_TheyAreReversed_Then_EachLayerStandsBeforeItsRow()
        {
            // Arrange
            s_className = "w-[40px] h-[40px] backdrop-blur-sm";

            // Act
            var order = ReverseAndRead();

            // Assert
            Assert.That(order, Is.EqualTo("overlay,r3,overlay,r2,overlay,r1,overlay,r0"));
        }

        // GREEN_ON_BASE(refactor): as the backdrop case above, for the band a ring hosts after its row.
        [Test]
        public void Given_FourKeyedRowsWithARing_When_TheyAreReversed_Then_EachBandStandsAfterItsRow()
        {
            // Arrange
            s_className = "w-[40px] h-[40px] ring-2";

            // Act
            var order = ReverseAndRead();

            // Assert
            Assert.That(order, Is.EqualTo("r3,overlay,r2,overlay,r1,overlay,r0,overlay"));
        }
    }
}

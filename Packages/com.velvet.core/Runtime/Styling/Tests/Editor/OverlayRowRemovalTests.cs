using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that the overlay a row hosts beside itself (a <c>backdrop-*</c> layer before it, a <c>ring-*</c> band
    /// after it) leaves with the row when keyed rows on a live panel are removed.
    /// </summary>
    internal sealed class OverlayRowRemovalTests : PanelTestBase
    {
        private static string s_className = "";
        private static StateUpdater<bool> s_setTrimmed;

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Component(Compiler = false)]
        private static VNode Rows()
        {
            var (trimmed, setTrimmed) = Hooks.UseState(false);
            s_setTrimmed = setTrimmed;
            var kept = trimmed ? new[] { 0, 3 } : new[] { 0, 1, 2, 3 };
            return V.Div(name: "list", children: new List<int>(kept)
                .Select(i => (VNode)V.Div(name: "r" + i, className: s_className, key: "k" + i)).ToArray());
        }

        private string TrimAndRead()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Rows));
            var list = _window.rootVisualElement.Q<VisualElement>("list");
            ForcePanelUpdate(list.panel);
            s_setTrimmed.Invoke(true);
            _mounted.FlushStateForTest();
            return string.Join(",", list.Children().Select(child => child.name.StartsWith("r") ? child.name : "overlay"));
        }

        // GREEN_ON_BASE(characterization): the base removes each row and its overlay as the walk reaches them; what the
        // case pins is that no overlay of a removed row is left once the reconcile returns, where the row's removal waits
        // for the end of the pass.
        [Test]
        public void Given_FourKeyedRowsWithARing_When_TwoOfThemAreRemoved_Then_OnlyTheBandsOfTheKeptRowsRemain()
        {
            // Arrange
            s_className = "w-[40px] h-[40px] ring-2";

            // Act
            var order = TrimAndRead();

            // Assert
            Assert.That(order, Is.EqualTo("r0,overlay,r3,overlay"));
        }

        // GREEN_ON_BASE(characterization): as the ring case above, for the layer a backdrop hosts before its row.
        [Test]
        public void Given_FourKeyedRowsWithABackdropLayer_When_TwoOfThemAreRemoved_Then_OnlyTheLayersOfTheKeptRowsRemain()
        {
            // Arrange
            s_className = "w-[40px] h-[40px] backdrop-blur-sm";

            // Act
            var order = TrimAndRead();

            // Assert
            Assert.That(order, Is.EqualTo("overlay,r0,overlay,r3"));
        }
    }
}

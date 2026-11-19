using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that rows added to a list on a live panel keep their declared order when each carries a
    /// <c>backdrop-*</c> utility, whose layer is hosted before the row as the row attaches to the panel.
    /// </summary>
    internal sealed class BackdropRowInsertOrderTests : PanelTestBase
    {
        private const string LayerClass = "velvet-backdrop-layer";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private static bool s_keyed;
        private static StateUpdater<int> s_setCount;

        [Component(Compiler = false)]
        private static VNode Rows()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            var children = new List<VNode>();
            for (var i = 0; i < count; i++)
            {
                var className = "w-[40px] h-[40px] backdrop-blur-sm";
                children.Add(s_keyed
                    ? V.Div(name: "r" + i, className: className, key: "k" + i)
                    : V.Div(name: "r" + i, className: className));
            }
            return V.Div(name: "list", children: children.ToArray());
        }

        // GREEN_ON_BASE(characterization): the base resolves each insert from the first child, so the layers the attach
        // hosts ahead of a row never move the slot the next row is placed at; this holds the cursor to that.
        [TestCase(true)]
        [TestCase(false)]
        public void Given_AListOfBackdropRows_When_ItGrowsFromNoneOnAPanel_Then_TheRowsStandInDeclaredOrder(bool keyed)
        {
            // Arrange
            s_keyed = keyed;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(Rows));
            var list = _window.rootVisualElement.Q<VisualElement>("list");
            ForcePanelUpdate(list.panel);

            // Act
            s_setCount.Invoke(3);
            _mounted.FlushStateForTest();

            // Assert
            var rows = list.Children().Where(child => !child.ClassListContains(LayerClass)).Select(child => child.name);
            Assert.That(string.Join(",", rows), Is.EqualTo("r0,r1,r2"));
        }
    }
}

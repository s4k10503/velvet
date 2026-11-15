using System.Collections.Generic;
using System.Reflection;
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
        private static int[] s_before = { 0, 1, 2, 3 };
        private static int[] s_after = { 3, 2, 1, 0 };
        private static string s_hostClass = "";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        [Component(Compiler = false)]
        private static VNode Rows()
        {
            var (reversed, setReversed) = Hooks.UseState(false);
            s_setReversed = setReversed;
            var order = new List<int>(reversed ? s_after : s_before);
            return V.Div(name: "list", className: s_hostClass, children: order
                .Select(i => (VNode)V.Div(name: "r" + i, className: s_className, key: "k" + i)).ToArray());
        }

        [SetUp]
        public void ResetOrders()
        {
            s_before = new[] { 0, 1, 2, 3 };
            s_after = new[] { 3, 2, 1, 0 };
            s_hostClass = "";
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

        // GREEN_ON_BASE(refactor): the base moves each row at once, so each band is placed against a list that already
        // holds the rows ahead of it.
        [Test]
        public void Given_RingRowsWhoseReorderAddsTwoRowsAheadOfThem_When_TheyReorder_Then_TheBandOfTheMovedRowStandsDirectlyAfterIt()
        {
            // Arrange
            s_className = "w-[40px] h-[40px] ring-2";
            s_before = new[] { 0, 1, 2 };
            s_after = new[] { 3, 4, 1, 2, 0 };
            using var mounted = V.Mount(_window.rootVisualElement, V.Component(Rows));
            var list = _window.rootVisualElement.Q<VisualElement>("list");
            ForcePanelUpdate(list.panel);

            // Act
            s_setReversed.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            var moved = list.Q<VisualElement>("r0");
            Assert.That(list[list.IndexOf(moved) + 1].ClassListContains(RingOverlay.MarkerClass), Is.True);
        }

        // GREEN_ON_BASE(refactor): as the ring case above, for the outline nodes a stacking-root host keeps after its rows.
        [Test]
        public void Given_OutlinedRowsInAHostWhoseReorderAddsTwoRowsAheadOfThem_When_TheyReorder_Then_TheOutlineNodesFollowTheTreeOrderOfTheirRows()
        {
            // Arrange
            s_hostClass = "opacity-90";
            s_className = "w-[40px] h-[40px] outline-2";
            s_before = new[] { 0, 1, 2 };
            s_after = new[] { 3, 4, 1, 2, 0 };
            using var mounted = V.Mount(_window.rootVisualElement, V.Component(Rows));
            var list = _window.rootVisualElement.Q<VisualElement>("list");
            ForcePanelUpdate(list.panel);

            // Act
            s_setReversed.Invoke(true);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", list.Children().Where(child => child.ClassListContains(RingOverlay.OutlineMarkerClass))
                .Select(OwnerOf)), Is.EqualTo("r3,r4,r1,r2,r0"));
        }

        private static string OwnerOf(VisualElement node)
        {
            var table = typeof(OutlineHosting).GetField("s_owner", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var args = new object?[] { node, null };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            return ((VisualElement?)args[1])?.name ?? "none";
        }
    }
}

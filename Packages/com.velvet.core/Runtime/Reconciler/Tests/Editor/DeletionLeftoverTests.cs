using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what one deletion leaves for the next: a later deletion, in another tree, removes only the rows its own
    /// render drops. <c>DeletionCommitStateTests</c> owns what the deletion itself keeps once it has ended.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionLeftoverTests
    {
        private static StateUpdater<bool> s_setShown;
        private static StateUpdater<bool> s_setLeadingShown;

        [Component(Compiler = false)]
        private static VNode NamedRow(string name) => V.Div(name: name);

        // Two rows ahead of one that stays, which a render removes.
        [Component(Compiler = false)]
        private static VNode LeadingRowsRemoved()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setLeadingShown = setShown;
            return V.Fragment(
                shown ? V.Component(NamedRow, "x", key: "x") : null,
                shown ? V.Component(NamedRow, "y", key: "y") : null,
                V.Component(NamedRow, "z", key: "z"));
        }

        // A row after two that stay, which a render removes.
        [Component(Compiler = false)]
        private static VNode TrailingRowRemoved()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Fragment(
                V.Component(NamedRow, "p", key: "p"),
                V.Component(NamedRow, "q", key: "q"),
                shown ? V.Component(NamedRow, "r", key: "r") : null);
        }

        // GREEN_ON_BASE(characterization): the base removes only the rows each render drops.
        [Test]
        public void Given_ATreeThatRemovedTwoLeadingRows_When_AnotherTreeRemovesItsLastRow_Then_ThatTreeKeepsItsOtherRows()
        {
            // Arrange
            using var first = V.Mount(new VisualElement(), V.Component(LeadingRowsRemoved));
            first.DrainForTest();
            s_setLeadingShown.Invoke(false);
            first.DrainForTest();
            var root = new VisualElement();
            using var second = V.Mount(root, V.Component(TrailingRowRemoved));
            second.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            second.DrainForTest();

            // Assert
            Assert.That(string.Join(",", new[] { "p", "q", "r" }.Where(name => root.Q(name) != null)),
                Is.EqualTo("p,q"));
        }
    }
}

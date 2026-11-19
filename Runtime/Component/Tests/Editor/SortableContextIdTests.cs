using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the container id an item of V.SortableContext reads: the id the context was given.
    /// </summary>
    [TestFixture]
    internal sealed class SortableContextIdTests
    {
        private static SortableState s_state;

        [SetUp]
        public void SetUp()
        {
            s_state = default;
        }

        [Component]
        private static VNode Item()
        {
            s_state = Hooks.UseSortable("a");
            return V.Div(key: "item");
        }

        [Component]
        private static VNode Scene() => V.SortableContext(new[] { "a" }, id: "list-x", key: "list", children: new VNode[]
        {
            V.Component(Item, key: "item"),
        });

        // GREEN_ON_BASE(characterization): the base already hands an item the context's own id; no case asserted it before.
        [Test]
        public void Given_ASortableContextGivenAnId_When_AnItemInItRenders_Then_ItReadsThatIdAsItsContainer()
        {
            // Arrange
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Component(Scene, key: "root"));

            // Assert
            Assert.That(s_state.ContainerId, Is.EqualTo("list-x"));
        }
    }
}

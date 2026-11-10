using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a plain component inside the output of a memoized row that rendered nothing renders again when
    /// the node it is reached with is not the one it last rendered from. The case writes that fiber's last node
    /// directly, since it arranges the condition rather than a path to it.
    /// </summary>
    [TestFixture]
    internal sealed class BailedOutputSourceNodeTests
    {
        private static StateUpdater<int> s_setTick;
        private static int s_innerRenders;
        private static ComponentFiber s_innerFiber;

        [SetUp]
        public void SetUp()
        {
            s_setTick = default;
            s_innerRenders = 0;
            s_innerFiber = null;
        }

        [Component(Compiler = false)]
        private static VNode Inner()
        {
            s_innerRenders++;
            s_innerFiber = FiberAmbientStack.Current;
            return V.Label(name: "inner");
        }

        [Component(Memoize = true, Compiler = false)]
        private static VNode Row(int id) => V.Component(Inner);

        [Component(Compiler = false)]
        private static VNode List()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Custom<VisualElement>(name: "box", children: new VNode[] { V.Component(Row, 0, key: "k0") });
        }

        // GREEN_ON_BASE(characterization): the base already renders a component reached with another node here; no case asserted it before.
        [Test]
        public void Given_APlainComponentInABailedRowLastRenderedFromAnotherNode_When_TheParentReRenders_Then_ItRendersAgain()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            s_innerFiber.SourceNode = V.Component(Inner);

            // Act
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_innerRenders, Is.EqualTo(2));
        }
    }
}

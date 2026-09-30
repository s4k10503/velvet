using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the stacked manipulators a relational binding owns: a render that rebuilds the bindings detaches
    /// the old ones' stacked manipulators, and those they gate in turn, rather than leaving them attached, hooked
    /// and gate-closed.
    /// </summary>
    [TestFixture]
    internal sealed class RelationalStackedOwnerTests : PanelTestBase
    {
        private static StateUpdater<string> s_setClass;

        public override void TearDown()
        {
            base.TearDown();
            s_setClass = default;
        }

        [Component]
        private static VNode RenderGroup()
        {
            var (className, setClass) = Hooks.UseState("group-hover:hover:bg-hot p-1");
            s_setClass = setClass;
            return V.Div(name: "group", className: "group", children: new VNode?[]
            {
                V.Div(name: "leaf", className: className),
            });
        }

        [Test]
        public void Given_AGroupHoverHoverLeafInAHoveredGroup_When_ItsClassNameChangesTwice_Then_OneStackedManipulatorStaysAttached()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderGroup));
            var root = _window.rootVisualElement;
            var context = _mounted.Root.Reconciler.Context;
            var built = new HashSet<StyleStackedVariantManipulator>();

            // Act — each render rebuilds the binding; the group is hovered again so the new one opens its gate.
            foreach (var className in new[] { "group-hover:hover:bg-hot p-1", "group-hover:hover:bg-hot p-2",
                         "group-hover:hover:bg-hot p-3" })
            {
                s_setClass.Invoke(className);
                context.BatchScheduler.DrainImmediateForTest();
                using var over = PointerOverEvent.GetPooled();
                root.Q<VisualElement>("group").SimulateEvent(over);
                built.UnionWith(context.StackedVariantManipulators.Values);
            }

            // Assert
            var leaf = root.Q<VisualElement>("leaf");
            Assert.That(built.Count(manipulator => manipulator.target == leaf), Is.EqualTo(1));
        }

        [Test]
        public void Given_AGroupHoverHoverFocusLeafHoveredWithItsGroup_When_ItsClassNameChangesTwice_Then_TwoStackedManipulatorsStayAttached()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderGroup));
            var root = _window.rootVisualElement;
            var context = _mounted.Root.Reconciler.Context;
            var leaf = root.Q<VisualElement>("leaf");
            var built = new HashSet<StyleStackedVariantManipulator>();

            // Act — hovering the leaf too opens the hover: manipulator's gate, which builds its focus: one.
            foreach (var className in new[] { "group-hover:hover:focus:bg-hot p-1",
                         "group-hover:hover:focus:bg-hot p-2", "group-hover:hover:focus:bg-hot p-3" })
            {
                s_setClass.Invoke(className);
                context.BatchScheduler.DrainImmediateForTest();
                using var groupOver = PointerOverEvent.GetPooled();
                root.Q<VisualElement>("group").SimulateEvent(groupOver);
                using var leafOver = PointerOverEvent.GetPooled();
                leaf.SimulateEvent(leafOver);
                built.UnionWith(context.StackedVariantManipulators.Values);
            }

            // Assert
            Assert.That(built.Count(manipulator => manipulator.target == leaf), Is.EqualTo(2));
        }
    }
}

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the seed a stacked element-local inner takes on an element that no variant manipulator tracks —
    /// here a child a container's <c>[&amp;&gt;*]:</c> rule reaches — which is read from UI Toolkit's own
    /// pseudo-states when the container gains the rule while the state already holds.
    /// </summary>
    [TestFixture]
    internal sealed class LiveStateSeedPanelTests : PanelTestBase
    {
        private static StateUpdater<string> s_setParentClass;

        public override void TearDown()
        {
            base.TearDown();
            s_setParentClass = default;
        }

        [Component]
        private static VNode RenderParent()
        {
            var (className, setClassName) = Hooks.UseState("bg-cold");
            s_setParentClass = setClassName;
            return V.Div(name: "parent", className: className, children: new VNode?[]
            {
                V.Div(name: "child"),
            });
        }

        private VisualElement MountChild()
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderParent));
            return _window.rootVisualElement.Q<VisualElement>("child");
        }

        private void GiveParent(string className)
        {
            s_setParentClass.Invoke(className);
            _mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
        }

        // UI Toolkit keeps :hover and :focus behind an internal setter; the test writes the state the engine's own
        // pointer and focus bookkeeping would.
        private static void SetPseudoState(VisualElement element, int flag)
        {
            var property = typeof(VisualElement).GetProperty("pseudoStates",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var current = Convert.ToInt32(property.GetValue(element));
            property.SetValue(element, Enum.ToObject(property.PropertyType, current | flag));
        }

        [Test]
        public void Given_AChildHeldActive_When_ItsParentGainsAChildActiveRule_Then_TheChildIsApplied()
        {
            // Arrange
            var child = MountChild();
            child.SetActivePseudoState(true);

            // Act
            GiveParent("[&>*]:active:bg-hot");

            // Assert
            Assert.That(child.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_AChildUnderThePointer_When_ItsParentGainsAChildHoverRule_Then_TheChildIsApplied()
        {
            // Arrange
            var child = MountChild();
            SetPseudoState(child, 0x2);

            // Act
            GiveParent("[&>*]:hover:bg-hot");

            // Assert
            Assert.That(child.ClassListContains("bg-hot"), Is.True);
        }

        [Test]
        public void Given_AFocusedChild_When_ItsParentGainsAChildFocusRule_Then_TheChildIsApplied()
        {
            // Arrange
            var child = MountChild();
            SetPseudoState(child, 0x40);

            // Act
            GiveParent("[&>*]:focus:bg-hot");

            // Assert
            Assert.That(child.ClassListContains("bg-hot"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base seeds nothing, so the rule stays off there as well; the case
        // pins that the seed reads the state its inner names, not whichever one holds.
        [Test]
        public void Given_AChildHeldActive_When_ItsParentGainsAChildHoverRule_Then_TheChildIsNotApplied()
        {
            // Arrange — the active state is the only one held, so a seed read off the wrong state fails here.
            var child = MountChild();
            child.SetActivePseudoState(true);

            // Act
            GiveParent("[&>*]:hover:bg-hot");

            // Assert
            Assert.That(child.ClassListContains("bg-hot"), Is.False);
        }
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The unset a reader of an unresolvable variable writes moves the panel's version, and is taken as part of the
    /// panel's last layout pass only where it discards a box-shadow, which no later reader's layout depends on.
    /// </summary>
    internal sealed class CustomPropertyUnsetLayoutCurrencyTests : PanelTestBase
    {
        // GREEN_ON_BASE(characterization): the base already leaves the panel not current after a width's unset.
        [Test]
        public void Given_APanelLaidOutAsItsLastPassLeftIt_When_AWidthReaderOfAnUnresolvableVariableWritesItsUnset_Then_TheLayoutIsNoLongerCurrent()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "box", className: "w-[100px]"));
            var box = _window.rootVisualElement.Q<VisualElement>("box");
            ForcePanelUpdate(box.panel);
            FiberLayoutReflow.TryLayOut(box);
            var current = FiberLayoutReflow.IsCurrent(box.panel);

            // Act
            StyleCustomProperties.Toggle(box, "w-[var(--velvet-undeclared)]", true, StyleLayerPriority.Base, 0,
                _mounted.Root.Reconciler.Context);

            // Assert — true where the panel was not current to begin with, which the expected value is not.
            Assert.That(current ? FiberLayoutReflow.IsCurrent(box.panel) : true, Is.False);
        }
    }
}

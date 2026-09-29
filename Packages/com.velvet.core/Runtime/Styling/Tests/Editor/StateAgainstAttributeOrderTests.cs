using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the tie between an element-state variant and a <c>data-</c> / <c>aria-</c> one on the same
    /// property: the attribute variant wins, as it does in Tailwind, which emits has, aria and data after
    /// hover, focus, active and disabled.
    /// </summary>
    [TestFixture]
    internal sealed class StateAgainstAttributeOrderTests : PanelTestBase
    {
        private VisualElement MountLeaf(string className, IReadOnlyDictionary<string, string>? data = null,
            IReadOnlyDictionary<string, string>? aria = null)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(name: "outer", children: new VNode?[]
                {
                    V.Div(name: "leaf", className: className, data: data, aria: aria),
                }));
            return _window.rootVisualElement.Q<VisualElement>("leaf");
        }

        [Test]
        public void Given_HoverAndAriaWidths_When_TheElementIsHovered_Then_TheAriaWidthWins()
        {
            // Arrange
            var leaf = MountLeaf("hover:w-[10px] aria-[busy=true]:w-[20px]",
                aria: new Dictionary<string, string> { ["busy"] = "true" });

            // Act — hovered after the aria width landed, so a shared layer would leave the hover width in place.
            using (var over = PointerOverEvent.GetPooled()) leaf.SimulateEvent(over);

            // Assert
            Assert.That(leaf.style.width.value.value, Is.EqualTo(20f));
        }

        [Test]
        public void Given_ActiveAndDataOpacityClasses_When_TheElementIsPressed_Then_TheDataClassWins()
        {
            // Arrange
            var leaf = MountLeaf("active:opacity-50 data-[state=open]:opacity-75",
                data: new Dictionary<string, string> { ["state"] = "open" });

            // Act
            using (var down = PointerDownEvent.GetPooled()) leaf.SimulateEvent(down);

            // Assert
            Assert.That((leaf.ClassListContains("opacity-50"), leaf.ClassListContains("opacity-75")),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_DisabledAndAriaOpacityClasses_When_TheElementIsDisabled_Then_TheAriaClassWins()
        {
            // Arrange — disabled:bg-gate says the disabled layer actually opened, so the opacity-50 reading is
            // a loss rather than a payload that never arrived.
            var leaf = MountLeaf("disabled:bg-gate disabled:opacity-50 aria-[busy=true]:opacity-75",
                aria: new Dictionary<string, string> { ["busy"] = "true" });

            // Act
            leaf.parent.SetEnabled(false);

            // Assert
            Assert.That((leaf.ClassListContains("bg-gate"), leaf.ClassListContains("opacity-50"),
                    leaf.ClassListContains("opacity-75")),
                Is.EqualTo((true, false, true)));
        }
    }
}

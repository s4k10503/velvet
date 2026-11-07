using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies preflight's border colour on an element a rule gives a border width by its type alone.
    /// </summary>
    internal sealed class BorderPreflightTypeRuleTests : PanelTestBase
    {
        private const string RuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/BorderPreflightTypeRule.uss";

        protected override void LoadStyleSheets()
        {
            var root = _window.rootVisualElement;
            VelvetStyleUtilities.AttachTo(root);
            root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
        }

        [Test]
        public void Given_AToggleWhoseTypeRuleSetsABorderWidth_When_Resolved_Then_ItsBorderIsItsTextColour()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Toggle(name: "toggle"));
            var toggle = _window.rootVisualElement.Q<VisualElement>("toggle");

            // Act
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(toggle.panel);
            }

            // Assert
            Assert.That(toggle.resolvedStyle.borderTopColor, Is.EqualTo(toggle.resolvedStyle.color));
        }

        [Test]
        public void Given_AnUnnamedToggleWhoseTypeRuleSetsABorderWidth_When_Resolved_Then_ItsBorderIsItsTextColour()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div(children: new VNode[] { V.Toggle(), V.Toggle() }));
            var toggle = _window.rootVisualElement.Q<Toggle>();

            // Act
            for (var i = 0; i < 3; i++)
            {
                ForcePanelUpdate(toggle.panel);
            }

            // Assert
            Assert.That(toggle.resolvedStyle.borderTopColor, Is.EqualTo(toggle.resolvedStyle.color));
        }
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the thresholds an element reads from its own cascade on a panel that gives its elements no variable
    /// of their own, where an element outside the class that declares a breakpoint has an empty variable context and
    /// the last declared value must not stand in for it.
    /// </summary>
    internal sealed class ThemeTokenCascadePanelTests
    {
        private const string CompactSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenCompactSheet.uss";

        [TestCase("inside", 480f)]
        [TestCase("outside", 640f)]
        public void Given_ABreakpointDeclaredUnderAClassAloneOnAPanelWithNoVariables_When_ItIsReadAfterTheStylePass_Then_OnlyTheElementsInTheClassHaveIt(
            string leafName, float expected)
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an offscreen panel with a style and layout pass");
            using var host = new RenderTexturePanelHost(nameof(ThemeTokenCascadePanelTests), 200, 200);
            host.Root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(CompactSheet));
            using var mounted = V.Mount(host.Root, V.Fragment(
                V.Div(className: "theme-token-alt", children: new VNode?[] { V.Div(name: "inside") }),
                V.Div(children: new VNode?[] { V.Div(name: "outside") })));
            var leaf = host.Root.Q<VisualElement>(leafName);
            EditorPanelTestHelpers.ForcePanelUpdate(leaf.panel);

            // Act
            float px;
            using (StyleThemeTokens.Use(mounted.Root.Reconciler.Context, leaf))
            {
                px = StyleVariantClass.BreakpointPx(StyleVariantKind.Sm);
            }

            // Assert
            Assert.That(px, Is.EqualTo(expected));
        }
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// The radius watch sheets attached beside a stylesheet are not part of what the theme scopes read the sheets by, so
    /// attaching them is no change to the sheets.
    /// </summary>
    internal sealed class ThemeTokenWatchSheetSignatureTests : PanelTestBase
    {
        private const string RadiusSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";

        // GREEN_ON_BASE(characterization): the base already leaves its own watch sheets out of the signature.
        [Test]
        public void Given_ASheetWithRadiusRulesAttachedAndReadOnce_When_TheScopeIsReadAgain_Then_TheMountIsToldNothingChanged()
        {
            // Arrange
            _mounted = V.Mount(_window.rootVisualElement, V.Div());
            var context = _mounted.Root.Reconciler.Context;
            using (StyleThemeTokens.Pass(context))
            {
            }
            var source = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RadiusSheet);
            _window.rootVisualElement.styleSheets.Add(source);
            using (StyleThemeTokens.Pass(context))
            {
            }
            var watch = RadiusWatchSheets.DerivedOf(source);
            var watchHeld = watch != null && _window.rootVisualElement.styleSheets.Contains(watch);
            var told = 0;
            System.Action<ReconcilerContext> listener = changed =>
            {
                if (ReferenceEquals(changed, context))
                {
                    told++;
                }
            };
            StyleThemeTokens.ScopeChanged += listener;
            try
            {
                // Act
                using (StyleThemeTokens.Pass(context))
                {
                }
            }
            finally
            {
                StyleThemeTokens.ScopeChanged -= listener;
            }

            // Assert — -1 where no watch sheet was attached, which no count equals.
            Assert.That(watchHeld ? told : -1, Is.EqualTo(0));
        }
    }
}

#nullable enable
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the theme tokens a stylesheet writes in <c>rem</c> or in a CSS math function, which UI Toolkit's own
    /// importer rejects or drops: they read as Tailwind v4 reads them, 1rem being 16px.
    /// </summary>
    internal sealed class ThemeTokenRemTests : PanelTestBase
    {
        // Written from this file because the sheet must exist as an asset before Velvet's importer is selected for it.
        private const string SheetPath = "Assets/VelvetThemeTokenRem.uss";

        private const string SheetText =
            ".theme-token-host {\n    --breakpoint-sm: 40rem;\n}\n"
            + ":root {\n    --spacing: 0.25rem;\n    --spacing-remtest: 1.5rem;\n    --spacing-calctest: calc(0.5rem + 4px);\n}\n";

        public override void TearDown()
        {
            try
            {
                base.TearDown();
            }
            finally
            {
                AssetDatabase.DeleteAsset(SheetPath);
            }
        }

        private StyleSheet? Attach()
        {
            var sheet = RemSheetImport.Import(SheetPath, SheetText);
            if (sheet != null)
            {
                _window.rootVisualElement.styleSheets.Add(sheet);
            }
            return sheet;
        }

        private VisualElement Mount(string className)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(className: "theme-token-host", children: new VNode?[]
            {
                V.Div(name: "leaf", className: className),
            }));
            var leaf = _window.rootVisualElement.Q<VisualElement>("leaf");
            ForcePanelUpdate(leaf.panel);
            return leaf;
        }

        [Test]
        public void Given_AStylesheetWithTokensInRemAndAMathFunction_When_ItIsImported_Then_TheImporterReportsNoError()
        {
            // Arrange
            var sheet = RemSheetImport.Import(SheetPath, SheetText);

            // Act
            var failed = sheet == null || sheet.importedWithErrors;

            // Assert
            Assert.That(failed, Is.False);
        }

        [TestCase("p-4", "p-[length:16px]")]
        [TestCase("p-remtest", "p-[length:24px]")]
        [TestCase("p-calctest", "p-[length:12px]")]
        public void Given_ASpacingTokenInRemOrAMathFunction_When_AStepIsRead_Then_ItIsWrittenWithTheLengthInPixels(
            string className, string expected)
        {
            // Arrange
            Attach();
            _mounted = V.Mount(_window.rootVisualElement, V.Div());

            // Act
            string normalized;
            using (StyleThemeTokens.Pass(_mounted.Root.Reconciler.Context))
            {
                normalized = StyleThemeClass.Normalize(className);
            }

            // Assert
            Assert.That(normalized, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ABreakpointInRem_When_ItsThresholdIsReadAfterTheStylePass_Then_ItIsTheLengthAtSixteenPixelsToTheRem()
        {
            // Arrange
            Attach();
            var leaf = Mount("flex");

            // Act
            float px;
            using (StyleThemeTokens.Use(_mounted.Root.Reconciler.Context, leaf))
            {
                px = StyleVariantClass.BreakpointPx(StyleVariantKind.Sm);
            }

            // Assert
            Assert.That(px, Is.EqualTo(640f));
        }
    }
}

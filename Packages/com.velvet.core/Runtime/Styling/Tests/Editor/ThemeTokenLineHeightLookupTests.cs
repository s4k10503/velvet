using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how a mount's scope answers whether a text size has a line height its sheets declare: from the
    /// variables they declare now, for the classes those variables name, and without building anything to ask.
    /// </summary>
    [TestFixture]
    internal sealed class ThemeTokenLineHeightLookupTests
    {
        // Declares `--text-sizetest--line-height` and `--text-sm--line-height` among others, and nothing for text-lg.
        private const string TokenSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenSheet.uss";
        private const string EdgeSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/ThemeTokenLineHeightEdgeSheet.uss";

        private static readonly Action AskWhetherTextLgIsFollowed = () => StyleNamedTextSize.HasThemedLineHeight("text-lg");

        private static readonly Action ParseTextLgLineHeight = () => StyleNamedTextSize.TryParseLineHeight("text-lg", out _, out _);

        private MountedTree? _mounted;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private ReconcilerContext MountUnder(VisualElement root, string sheetPath)
        {
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(sheetPath));
            _mounted = V.Mount(root, V.Div());
            return _mounted.Root.Reconciler.Context;
        }

        private bool IsFollowedInScope(ReconcilerContext context, string className)
        {
            using (StyleThemeTokens.Pass(context))
            {
                return StyleNamedTextSize.HasThemedLineHeight(className);
            }
        }

        [Test, Category("Performance")]
        public void Given_ASheetDeclaringALineHeightForAnotherSize_When_ASizeIsAskedWhetherItIsFollowed_Then_NothingIsAllocated()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), TokenSheet);
            using var scope = StyleThemeTokens.Pass(context);
            AskWhetherTextLgIsFollowed();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(AskWhetherTextLgIsFollowed);

            // Assert — the canary rides along, because a zero says nothing if the probe measured nothing at all.
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? blocks : -1, Is.Zero);
        }

        [Test, Category("Performance")]
        public void Given_ASheetDeclaringALineHeightForAnotherSize_When_ASizesLineHeightIsParsed_Then_NothingIsAllocated()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), TokenSheet);
            using var scope = StyleThemeTokens.Pass(context);
            ParseTextLgLineHeight();

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(ParseTextLgLineHeight);

            // Assert — the canary rides along, because a zero says nothing if the probe measured nothing at all.
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 ? blocks : -1, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base reads text-lg's Tailwind pairing inside a scope too, which asking the scope by class must keep.
        [Test]
        public void Given_ASheetDeclaringALineHeightForAnotherSize_When_ABuiltInSizesLineHeightIsParsed_Then_ItIsTheTailwindPairing()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), TokenSheet);

            // Act
            float read;
            using (StyleThemeTokens.Pass(context))
            {
                StyleNamedTextSize.TryParseLineHeight("text-lg", out var lineHeight, out _);
                read = lineHeight.Value;
            }

            // Assert
            Assert.That(read, Is.EqualTo(1.75f / 1.125f).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base follows no size its sheets declare no line height for, which asking the scope by class must keep.
        [Test]
        public void Given_ASheetDeclaringALineHeightForAnotherSize_When_ASizeIsAskedWhetherItIsFollowed_Then_ItIsNot()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), TokenSheet);

            // Act
            var followed = IsFollowedInScope(context, "text-lg");

            // Assert
            Assert.That(followed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base stops following a size once the sheet declaring its line height is removed, which rebuilding the scope's classes must keep.
        [Test]
        public void Given_TheSheetDeclaringASizesLineHeightRemoved_When_TheSizeIsAskedWhetherItIsFollowed_Then_ItIsNot()
        {
            // Arrange
            var root = new VisualElement();
            var context = MountUnder(root, TokenSheet);
            root.styleSheets.Clear();

            // Act
            var followed = IsFollowedInScope(context, "text-sizetest");

            // Assert
            Assert.That(followed, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base follows the class naming no size where a sheet declares --text---line-height, which deriving classes from variable names must keep.
        [Test]
        public void Given_ASheetDeclaringALineHeightForASizeWithNoName_When_TheClassNamingNoSizeIsAskedWhetherItIsFollowed_Then_ItIs()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), EdgeSheet);

            // Act
            var followed = IsFollowedInScope(context, "text-");

            // Assert
            Assert.That(followed, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base follows no size for another namespace's variable ending in --line-height, which deriving classes from variable names must keep.
        [Test]
        public void Given_AnotherNamespacesVariableEndingInALineHeightSuffix_When_TheSizeItsNameCarriesIsAskedWhetherItIsFollowed_Then_ItIsNot()
        {
            // Arrange
            var context = MountUnder(new VisualElement(), EdgeSheet);

            // Act
            var followed = IsFollowedInScope(context, "text-lg");

            // Assert
            Assert.That(followed, Is.False);
        }
    }
}

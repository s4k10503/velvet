using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the stylesheets BorderPreflight leaves out of what it compares and reads border widths from: the ones Velvet
    /// derives or swaps in itself.
    /// </summary>
    internal sealed class BorderPreflightWatchSheetExclusionTests : PanelTestBase
    {
        private const string OwnSheet = "Packages/com.velvet.core/Runtime/Reconciler/Tests/Editor/LayoutIdTestRules.uss";

        // GREEN_ON_BASE(characterization): the base leaves a watch sheet out of the sheets BorderPreflight compares. Drop `&&
        // !RadiusWatchSheets.IsWatch(sheet)` from SheetsOf and the attached watch sheet moves the count.
        [Test]
        public void Given_AnElementHoldingASource_When_ItsWatchSheetIsAttached_Then_ItsSheetsCompareAsTheyDid()
        {
            // Arrange
            var element = new VisualElement();
            element.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(OwnSheet));
            var sheetsOf = typeof(BorderPreflight).GetMethod("SheetsOf", BindingFlags.NonPublic | BindingFlags.Static)!;
            var before = (int)sheetsOf.Invoke(null, new object[] { element })!;

            // Act
            RadiusWatchSheets.EnsureOn(element);

            // Assert — a watch sheet that did not attach leaves one sheet short, which reads -1.
            var after = (int)sheetsOf.Invoke(null, new object[] { element })!;
            Assert.That(element.styleSheets.count == 2 ? after - before : -1, Is.Zero);
        }

        // GREEN_ON_BASE(characterization): the base reads no class off a snapped sheet. Change the first `||` of the exclusion in WidthNames.Add to
        // `&&` and the snapped sheet is read.
        [Test]
        public void Given_ASnappedBorderSheet_When_ItsBorderWidthNamesAreRead_Then_ItNamesNoClass()
        {
            // Arrange
            var snapped = SnappedBorderSheets.For(1.5f)!;
            var names = typeof(BorderPreflight).GetNestedType("WidthNames", BindingFlags.NonPublic)!;

            // Act
            var read = names.GetMethod("Of", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, new object[] { snapped })!;

            // Assert
            Assert.That(((HashSet<string>)names.GetField("Classes")!.GetValue(read)!).Count, Is.Zero);
        }
    }
}

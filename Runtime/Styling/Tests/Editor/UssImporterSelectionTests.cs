#nullable enable
using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which stylesheets Velvet's importer is selected for, and that the UI Toolkit importer it calls
    /// into is reachable in the editor running.
    /// </summary>
    internal sealed class UssImporterSelectionTests
    {
        private const string SheetPath = "Assets/VelvetImporterSelection.uss";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(SheetPath);

        [TestCase(":root { --spacing: 0.25rem; }", typeof(VelvetStyleSheetImporter))]
        [TestCase(":root { --spacing: calc(2px * 2); }", typeof(VelvetStyleSheetImporter))]
        [TestCase(":root { --spacing: 4px; }", null)]
        public void Given_AStylesheetAndItsCustomProperties_When_TheImporterIsEnsured_Then_VelvetsImporterIsSelectedOnlyWhereTheStockOneCannotHoldThem(
            string uss, Type? expected)
        {
            // Arrange
            RemSheetImport.Import(SheetPath, uss);

            // Act
            var selected = AssetDatabase.GetImporterOverride(SheetPath);

            // Assert
            Assert.That(selected, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AFileImportedWithATokenInRem_When_TheEditorPostprocessesTheImport_Then_VelvetsImporterIsSelected()
        {
            // Arrange
            RemSheetImport.Import(SheetPath, ":root { --spacing: 4px; }");
            System.IO.File.WriteAllText(SheetPath, ":root { --spacing: 1rem; }");
            var postprocess = typeof(UssImporterSelection).GetMethod("OnPostprocessAllAssets", BindingFlags.NonPublic | BindingFlags.Static);

            // Act
            postprocess?.Invoke(null, new object[] { new[] { SheetPath }, new string[0], new string[0], new string[0] });

            // Assert
            Assert.That(AssetDatabase.GetImporterOverride(SheetPath), Is.EqualTo(typeof(VelvetStyleSheetImporter)));
        }

        private static System.Collections.Generic.IEnumerable<TestCaseData> StockMembers()
            => typeof(StockStyleSheetImport)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(field => typeof(MemberInfo).IsAssignableFrom(field.FieldType))
                .Select(field => new TestCaseData(field.GetValue(null)).SetName(
                    "Given_AMemberOfUIToolkitsImporter_When_ItIsResolvedByName_Then_ItIsFound(" + field.Name + ")"));

        [TestCaseSource(nameof(StockMembers))]
        public void Given_AMemberOfUIToolkitsImporter_When_ItIsResolvedByName_Then_ItIsFound(object? member)
        {
            // Arrange
            // Act
            var found = member != null;

            // Assert
            Assert.That(found, Is.True);
        }
    }
}

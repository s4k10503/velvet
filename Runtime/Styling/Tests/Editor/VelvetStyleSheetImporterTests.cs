#nullable enable
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The asset Velvet's stylesheet importer makes of a file, and what it reports when the file cannot be read.
    /// </summary>
    internal sealed class VelvetStyleSheetImporterTests
    {
        private const string SheetPath = "Assets/VelvetStyleSheetImporter.uss";
        private const string RemSheet = ":root { --spacing: 0.25rem; }";

        [TearDown]
        public void TearDown() => AssetDatabase.DeleteAsset(SheetPath);

        // GREEN_ON_BASE(characterization): pins that Unity marks an imported object not editable, which the importer relies on.
        [Test]
        public void Given_AStylesheetVelvetsImporterImports_When_ItIsLoaded_Then_UnityHasMarkedItNotEditable()
        {
            // Arrange
            var sheet = RemSheetImport.Import(SheetPath, RemSheet);

            // Act
            var notEditable = sheet!.hideFlags & HideFlags.NotEditable;

            // Assert
            Assert.That(notEditable, Is.EqualTo(HideFlags.NotEditable));
        }

        // GREEN_ON_BASE(characterization): the base already makes the sheet the main asset of the file it imports.
        [Test]
        public void Given_AStylesheetVelvetsImporterImports_When_ItsMainAssetIsLoaded_Then_ItIsTheSheet()
        {
            // Arrange
            RemSheetImport.Import(SheetPath, RemSheet);

            // Act
            var main = AssetDatabase.LoadMainAssetAtPath(SheetPath);

            // Assert
            Assert.That(main, Is.InstanceOf<StyleSheet>());
        }

        // GREEN_ON_BASE(characterization): the base already logs a file it cannot read as an import error.
        [Test]
        public void Given_AStylesheetAnotherHandleHoldsExclusively_When_VelvetsImporterImportsIt_Then_TheReadFailureIsLogged()
        {
            // Arrange
            RemSheetImport.Import(SheetPath, RemSheet);
            LogAssert.Expect(LogType.Error, new Regex("IOException : "));
            using var held = File.Open(SheetPath, FileMode.Open, FileAccess.Read, FileShare.None);

            // Act
            AssetDatabase.ImportAsset(SheetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

            // Assert — LogAssert.Expect verifies the error reached the console
        }
    }
}

#nullable enable
using System;
using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor
{
    /// <summary>
    /// Imports a USS file as UI Toolkit's importer does, after <see cref="UssCustomPropertyText"/> has made the custom
    /// properties it cannot hold importable. A file selects it as its importer override;
    /// <see cref="UssImporterSelection"/> does that for a file that needs it.
    /// </summary>
    [ScriptedImporter(1, new string[0], new[] { "uss" }, 1100)]
    internal sealed class VelvetStyleSheetImporter : ScriptedImporter
    {
        /// <summary>Mirrors the setting of the same name on UI Toolkit's importer.</summary>
        public bool disableValidation;

        /// <summary>Mirrors the setting of the same name on UI Toolkit's importer: 0 an error, 1 a warning, 2 ignored.</summary>
        public int unsupportedSelectorAction;

        // The name and signature UI Toolkit's importer declares for its @imports.
        private static string[] GatherDependenciesFromSourceFile(string assetPath)
        {
            try
            {
                return StockStyleSheetImport.Dependencies(assetPath);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var uss = string.Empty;
            try
            {
                uss = File.ReadAllText(ctx.assetPath);
            }
            catch (IOException exception)
            {
                ctx.LogImportError("IOException : " + exception.Message);
            }
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            sheet.hideFlags = HideFlags.NotEditable;
            try
            {
                if (!string.IsNullOrEmpty(uss))
                {
                    StockStyleSheetImport.Run(ctx, sheet, UssCustomPropertyText.Rewrite(uss), disableValidation, unsupportedSelectorAction);
                }
            }
            finally
            {
                ctx.AddObjectToAsset("stylesheet", sheet);
                // MUTANT_SURVIVES(equivalent, line removed): the sheet is the only object added, so the import makes it the main one anyway;
                // VelvetStyleSheetImporterTests' main-asset case reads that.
                ctx.SetMainObject(sheet);
            }
        }
    }
}

#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Velvet.Editor
{
    /// <summary>
    /// Selects <see cref="VelvetStyleSheetImporter"/> for a USS file that declares a custom property UI Toolkit's own
    /// importer cannot hold, once that importer has imported it: the file is then imported again, with the
    /// selection written to its meta file.
    /// </summary>
    internal sealed class UssImporterSelection : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in imported)
            {
                if (path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                {
                    Ensure(path);
                }
            }
        }

        /// <summary>Selects the importer for the file at path where it needs it.</summary>
        internal static void Ensure(string path)
        {
            if (AssetDatabase.GetImporterOverride(path) == typeof(VelvetStyleSheetImporter) || IsImmutable(path) || !Needs(path))
            {
                return;
            }
            AssetDatabase.SetImporterOverride<VelvetStyleSheetImporter>(path);
        }

        private static bool IsImmutable(string path)
            => UnityEditor.PackageManager.PackageInfo.FindForAssetPath(path)?.source is UnityEditor.PackageManager.PackageSource.Registry or UnityEditor.PackageManager.PackageSource.BuiltIn or UnityEditor.PackageManager.PackageSource.Git or UnityEditor.PackageManager.PackageSource.LocalTarball;

        private static string? ReadOrNull(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static bool Needs(string path)
        {
            var uss = ReadOrNull(path);
            return uss != null && !string.Equals(UssCustomPropertyText.Rewrite(uss), uss, StringComparison.Ordinal);
        }
    }
}

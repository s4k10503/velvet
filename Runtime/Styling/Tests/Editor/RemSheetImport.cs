#nullable enable
using System.IO;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Imports a stylesheet whose text UI Toolkit's own importer rejects, the way a project's file reaches Velvet's:
    /// the file exists as an asset first, then holds the text, and the selection is made before the import that
    /// reads it.
    /// </summary>
    internal static class RemSheetImport
    {
        internal static StyleSheet? Import(string path, string uss)
        {
            File.WriteAllText(path, ":root { --placeholder: 1px; }\n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            File.WriteAllText(path, uss);
            UssImporterSelection.Ensure(path);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
        }
    }
}

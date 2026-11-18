#nullable enable
using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine.UIElements;

namespace Velvet.Editor
{
    /// <summary>
    /// UI Toolkit's own USS import, which is internal to the editor and reached by name.
    /// <c>UssImporterSelectionTests</c> resolves every member so a rename names itself.
    /// </summary>
    internal static class StockStyleSheetImport
    {
        private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private const string ImplType = "UnityEditor.UIElements.StyleSheets.StyleSheetImporterImpl";

        internal static readonly Type? Impl = typeof(AssetDatabase).Assembly.GetType(ImplType);

        internal static readonly ConstructorInfo? Constructor = Impl?.GetConstructor(Any, null, new[] { typeof(AssetImportContext) }, null);

        internal static readonly MethodInfo? Import = Impl?.GetMethod("Import", Any, null, new[] { typeof(StyleSheet), typeof(string) }, null);

        internal static readonly PropertyInfo? DisableValidation = Impl?.GetProperty("disableValidation", Any);

        internal static readonly PropertyInfo? ImportErrors = Impl?.GetProperty("importErrors", Any);

        internal static readonly PropertyInfo? UnsupportedSelectorAction =
            ImportErrors?.PropertyType.GetProperty("unsupportedSelectorAction", Any);

        internal static readonly MethodInfo? PopulateDependencies = Impl?.GetMethod("PopulateDependencies", Any, null, new[] { typeof(string) }, null);

        internal static bool Resolved => Constructor != null && Import != null && DisableValidation != null
            && UnsupportedSelectorAction != null && PopulateDependencies != null;

        /// <summary>Imports uss into sheet as the stock importer does, with its two settings.</summary>
        internal static void Run(AssetImportContext context, StyleSheet sheet, string uss, bool disableValidation, int unsupportedSelectorAction)
        {
            if (!Resolved)
            {
                throw new MissingMemberException("UI Toolkit's USS importer (" + ImplType + ") is not reachable in this editor.");
            }
            var impl = Constructor!.Invoke(new object[] { context });
            DisableValidation!.SetValue(impl, disableValidation);
            var errors = ImportErrors!.GetValue(impl);
            UnsupportedSelectorAction!.SetValue(errors, Enum.ToObject(UnsupportedSelectorAction.PropertyType, unsupportedSelectorAction));
            Import!.Invoke(impl, new object[] { sheet, uss });
        }

        internal static string[] Dependencies(string assetPath)
            => Resolved ? (string[])PopulateDependencies!.Invoke(null, new object[] { assetPath }) : Array.Empty<string>();
    }
}

using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
#if UNITY_EDITOR
    /// <summary>
    /// Loads Velvet's bundled utility stylesheet onto a test panel by its known package path. Editor-only:
    /// <c>AssetDatabase</c> lives in the UnityEditor assembly, unavailable in player builds, while this
    /// TestUtilities asmdef compiles for every platform.
    /// </summary>
    public static class StyleUtilitiesTestLoader
    {
        private const string BundledStyleUtilitiesPath =
            "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        /// <summary>
        /// Loads the bundled <c>StyleUtilities.uss</c> and attaches it to <paramref name="root"/>'s
        /// <see cref="VisualElement.styleSheets"/>. Test-only. Must not be used from production code.
        /// </summary>
        public static void LoadBundledStyleUtilitiesForTest(this VisualElement root)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            var sheet = UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(BundledStyleUtilitiesPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            root.styleSheets.Add(sheet);
        }
    }
#endif
}

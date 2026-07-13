using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Parses utility-style arbitrary-value background-image className syntax and applies the
    /// result as an inline <see cref="IStyle.backgroundImage"/>.
    /// </summary>
    /// <remarks>
    /// Supports the <c>bg-[addr:&lt;key&gt;]</c> syntax, which loads a Texture2D from the
    /// Addressables system synchronously via <see cref="AddressableAssetCache"/>.
    /// This mirrors a CSS <c>background-image: url('./icon.png')</c> expressed as a className — at
    /// runtime in UI Toolkit the equivalent has to go through Unity's asset loading, hence the
    /// <c>addr:</c> prefix to make the lookup mechanism explicit.
    /// </remarks>
    public static class StyleBackgroundImageResolver
    {
        private const string Prefix = "bg-[addr:";
        private const string Suffix = "]";

        // Cache resolved Texture2D by Addressable key. Each LoadAssetAsync call increments an
        // internal refcount; without an explicit Release, repeated calls accumulate refcounts
        // and stall reconcile on every re-render. Caching pins the asset for the panel lifetime
        // (typical for icon-class assets) and amortizes the WaitForCompletion to once per key.
        private static readonly Dictionary<string, Texture2D?> _cache = new();

        /// <summary>
        /// True when any class is a <c>bg-[addr:&lt;key&gt;]</c> background-image utility (a cheap prefix
        /// check, no Addressable load). Lets another owner of the inline background-image (the gradient
        /// background) avoid clearing it when this resolver owns the image.
        /// </summary>
        public static bool HasBackgroundImageClass(string[] classNames)
        {
            if (classNames == null)
            {
                return false;
            }
            foreach (var cls in classNames)
            {
                if (!string.IsNullOrEmpty(cls) && cls.StartsWith(Prefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Returns true when <paramref name="className"/> matches <c>bg-[addr:&lt;key&gt;]</c> and
        /// the Addressable key successfully resolved to a Texture2D.
        /// </summary>
        public static bool TryParse(string className, out Texture2D? texture)
        {
            texture = null;
            if (className == null) return false;
            if (!className.StartsWith(Prefix)) return false;
            if (!className.EndsWith(Suffix)) return false;

            var keyStart = Prefix.Length;
            var keyLength = className.Length - Prefix.Length - Suffix.Length;
            if (keyLength <= 0) return false;

            var key = className.Substring(keyStart, keyLength);
            return AddressableAssetCache.TryLoad(key, _cache, "StyleBackgroundImageResolver", out texture);
        }

        /// <summary>
        /// Sets the element's inline <c>backgroundImage</c> from the given Texture2D, through the
        /// SceneView ownership gate (a live camera feed keeps the slot and defers this value).
        /// </summary>
        public static void Apply(VisualElement element, Texture2D? texture)
        {
            SceneViewElement.WriteBackground(element, new StyleBackground(texture));
        }

        /// <summary>Reverts the inline background-image to the USS default (same gate as Apply).</summary>
        public static void Clear(VisualElement element)
        {
            SceneViewElement.WriteBackground(element, new StyleBackground(StyleKeyword.Null));
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine.TextCore.Text;

namespace Velvet
{
    /// <summary>
    /// Outcome of resolving a (<c>family</c>, <c>weight</c>, <c>italic</c>) request against the
    /// <see cref="VelvetFonts"/> registry. <see cref="HasAsset"/> tells the caller whether a Font Asset
    /// was found; <see cref="ResidualBold"/> / <see cref="ResidualItalic"/> are the parts of the request
    /// that the chosen asset does NOT already satisfy and therefore must be emulated through
    /// <c>-unity-font-style</c> (faux bold / faux italic).
    /// </summary>
    public readonly struct ResolvedFont
    {
        public readonly FontAsset? Asset;
        public readonly bool HasAsset;
        public readonly bool ResidualBold;
        public readonly bool ResidualItalic;

        public ResolvedFont(FontAsset? asset, bool residualBold, bool residualItalic)
        {
            Asset = asset;
            HasAsset = asset != null;
            ResidualBold = residualBold;
            ResidualItalic = residualItalic;
        }
    }

    /// <summary>
    /// Application-wide font registry backing the <c>font-&lt;name&gt;</c> / <c>font-&lt;weight&gt;</c>
    /// utilities. Mirrors <see cref="VelvetTheme"/>'s "global state + change event" shape: register
    /// families once at startup and swap the active set at runtime (e.g. on a locale change) —
    /// <see cref="FontsChanged"/> lets the app trigger a re-render so mounted elements re-resolve.
    /// <para/>
    /// The registry is independent of how the master data is stored. It only consumes
    /// <see cref="VelvetFontFamily"/> values via <see cref="Register(VelvetFontFamily)"/> /
    /// <see cref="Register(IEnumerable{VelvetFontFamily}, string)"/>; producing those from CSV,
    /// MasterMemory, a ScriptableObject, or plain code is an adapter concern that lives outside this
    /// class.
    /// <para/>
    /// Font Assets may be supplied directly or by Addressables key; keyed assets are loaded
    /// synchronously and cached on first use, the same mechanism <see cref="StyleBackgroundImageResolver"/>
    /// uses for <c>bg-[addr:…]</c>.
    /// </summary>
    public static class VelvetFonts
    {
        // Ordinal-keyed: utility class names are lowercase ASCII, so a registered family should use the
        // same casing as the class suffix (font-sans → "sans").
        private static readonly Dictionary<string, VelvetFontFamily> _families = new(StringComparer.Ordinal);

        // Addressable key → FontAsset. Pins resolved assets for the registry's lifetime so repeated
        // reconciles do not re-issue (and re-refcount) the load, matching StyleBackgroundImageResolver.
        private static readonly Dictionary<string, FontAsset?> _addrCache = new(StringComparer.Ordinal);

        private static string? _defaultFamily;

        /// <summary>Raised whenever the registry changes (register / unregister / config swap / default).</summary>
        public static event Action? FontsChanged;

        /// <summary>
        /// Family applied to elements that specify a weight/style but no explicit <c>font-&lt;name&gt;</c>.
        /// Null or empty means "inherit the panel's default font".
        /// </summary>
        public static string? DefaultFamily
        {
            get => _defaultFamily;
            set
            {
                if (_defaultFamily == value)
                {
                    return;
                }

                _defaultFamily = value;
                FontsChanged?.Invoke();
            }
        }

        /// <summary>Registers (or replaces) a single family by its <see cref="VelvetFontFamily.name"/>.</summary>
        public static void Register(VelvetFontFamily? family)
        {
            if (family == null || string.IsNullOrEmpty(family.name))
            {
                return;
            }

            _families[family.name] = family;
            FontsChanged?.Invoke();
        }

        /// <summary>
        /// Registers (or replaces) a batch of families and, optionally, the <see cref="DefaultFamily"/>,
        /// raising <see cref="FontsChanged"/> once for the whole batch. This is the
        /// representation-agnostic entry point: the families can come from anywhere — built in code, a
        /// CSV importer, MasterMemory, a ScriptableObject, etc. The registry itself has no knowledge of
        /// how the master data is stored.
        /// </summary>
        public static void Register(IEnumerable<VelvetFontFamily>? families, string? defaultFamily = null)
        {
            if (families == null && string.IsNullOrEmpty(defaultFamily))
            {
                return;
            }

            if (families != null)
            {
                foreach (var family in families)
                {
                    if (family != null && !string.IsNullOrEmpty(family.name))
                    {
                        _families[family.name] = family;
                    }
                }
            }

            if (!string.IsNullOrEmpty(defaultFamily))
            {
                _defaultFamily = defaultFamily;
            }

            FontsChanged?.Invoke();
        }

        /// <summary>Removes a family by name. No-op when it is not registered.</summary>
        public static void Unregister(string? familyName)
        {
            if (!NameKeyedRegistry.Unregister(familyName, _families))
            {
                return;
            }

            FontsChanged?.Invoke();
        }

        /// <summary>Clears every registered family, the addressable cache, and the default family.</summary>
        public static void Clear()
        {
            var hadState = _families.Count > 0 || _defaultFamily != null;
            _families.Clear();
            _addrCache.Clear();
            _defaultFamily = null;
            if (hadState)
            {
                FontsChanged?.Invoke();
            }
        }

        /// <summary>True when a family with the given name is registered.</summary>
        public static bool IsRegistered(string? familyName) => NameKeyedRegistry.IsRegistered(familyName, _families);

        /// <summary>
        /// Resolves a font request to a <see cref="ResolvedFont"/>. When <paramref name="family"/> is null
        /// the <see cref="DefaultFamily"/> is used. Returns a result with <see cref="ResolvedFont.HasAsset"/>
        /// false (but meaningful residual flags) when no asset is registered for the request — the caller
        /// then falls back to the binary <c>-unity-font-style</c> threshold.
        /// </summary>
        public static ResolvedFont Resolve(string? family, VelvetFontWeight weight, bool italic)
        {
            var familyName = string.IsNullOrEmpty(family) ? _defaultFamily : family;

            // Shared fallback: no usable asset, so the whole request is emulated via -unity-font-style.
            var fallback = new ResolvedFont(null, residualBold: (int)weight >= 600, residualItalic: italic);

            if (string.IsNullOrEmpty(familyName) || !_families.TryGetValue(familyName, out var def))
            {
                return fallback;
            }

            var entry = def.FindClosestWeight(weight);
            if (entry == null)
            {
                return fallback;
            }

            FontAsset? asset = null;
            var bakedItalic = false;

            if (italic && TryGetAsset(entry.italic, entry.italicAddress, out var italicAsset))
            {
                asset = italicAsset;
                bakedItalic = true;
            }

            if (asset == null && TryGetAsset(entry.upright, entry.uprightAddress, out var uprightAsset))
            {
                asset = uprightAsset;
            }

            if (asset == null)
            {
                return fallback;
            }

            // Faux bold only when a heavy weight was asked for but the matched asset is a light one.
            var residualBold = (int)weight >= 600 && (int)entry.weight < 600;
            var residualItalic = italic && !bakedItalic;
            return new ResolvedFont(asset, residualBold, residualItalic);
        }

        /// <summary>
        /// Loads a Font Asset by Addressables key (for the <c>font-[addr:&lt;key&gt;]</c> arbitrary form),
        /// caching the result. Returns false and logs a warning when the key does not resolve.
        /// </summary>
        public static bool TryLoadAddressableFont(string? key, out FontAsset? asset)
        {
            if (string.IsNullOrEmpty(key))
            {
                asset = null;
                return false;
            }

            return AddressableAssetCache.TryLoad(key, _addrCache, "VelvetFonts", out asset);
        }

        // A direct asset reference wins; otherwise fall back to the addressable key.
        private static bool TryGetAsset(FontAsset? direct, string? address, out FontAsset? asset)
        {
            if (direct != null)
            {
                asset = direct;
                return true;
            }

            return TryLoadAddressableFont(address, out asset);
        }
    }
}

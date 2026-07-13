#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Registry for user-defined UI Toolkit custom filters consumed by the <c>filter-[name:args]</c>
    /// utility. Register a <see cref="FilterFunctionDefinition"/> under a name at startup (before the
    /// consuming tree mounts), then reference it from any class string:
    /// <code>
    /// VelvetFilters.Register("dissolve", dissolveDefinition);
    /// V.Div(className: "filter-[dissolve:0.4]");
    /// </code>
    /// Arguments after the name are colon-separated and fill the definition's declared parameters in
    /// order, parsed by each slot's declared type — floats (<c>filter-[dissolve:0.4]</c>, sign allowed)
    /// for float slots and colors (<c>filter-[glow:#ff0000:2]</c>) for color slots; a missing tail is
    /// padded from the declaration's defaults, so a bare name (<c>filter-[dissolve]</c>) applies the
    /// declared defaults outright. Custom functions
    /// compose into the same inline <c>filter</c> list as the built-in <c>blur-*</c>/<c>contrast-*</c>/…
    /// utilities: built-ins first (canonical CSS order), then customs in class order.
    /// Registration is not reactive: a class resolved before its name was registered stays inert until
    /// the element's class list changes again.
    /// Not thread-safe (main thread only).
    /// </summary>
    public static class VelvetFilters
    {
        // The built-in filter utility families (single-sourced from the parser that owns them); reserved
        // so filter-[blur:..] can never mean something different from the blur-* utilities that already
        // exist. Case-insensitive: the reservation is about the family name, not one spelling of it.
        private static readonly HashSet<string> s_reserved =
            new(StyleFilterValueParser.BuiltInFamilyNames, System.StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, FilterFunctionDefinition> s_definitions = new();

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields() => s_definitions.Clear();
#endif

        /// <summary>
        /// Registers <paramref name="definition"/> under <paramref name="name"/> for
        /// <c>filter-[name:args]</c> resolution. Re-registering a live name logs a warning and
        /// overwrites; re-registering the identical definition under the same name is a silent no-op.
        /// </summary>
        /// <param name="name">Filter name as written inside the class brackets. Must be non-empty and
        /// must not contain whitespace, <c>:</c>, <c>[</c> or <c>]</c> (they would break the class token);
        /// the built-in filter family names (blur, brightness, contrast, grayscale, hue-rotate, invert,
        /// saturate, sepia) are reserved case-insensitively. Invalid names are rejected with a warning.</param>
        /// <param name="definition">The custom filter definition applied when the class resolves.
        /// Null (or a destroyed asset) is rejected with a warning, as is a definition declaring more than
        /// the 4 parameters a filter function can carry.</param>
        public static void Register(string name, FilterFunctionDefinition? definition)
        {
            if (string.IsNullOrEmpty(name) || !HasValidNameChars(name))
            {
                Debug.LogWarning($"[VelvetFilters] Cannot register \"{name}\": a filter name must be non-empty and free of whitespace, ':', '[' and ']'.");
                return;
            }

            if (s_reserved.Contains(name))
            {
                Debug.LogWarning($"[VelvetFilters] Cannot register \"{name}\": the name is reserved by the built-in {name.ToLowerInvariant()}-* utilities.");
                return;
            }

            if (definition == null)
            {
                Debug.LogWarning($"[VelvetFilters] Cannot register null definition for \"{name}\".");
                return;
            }

            // A filter function carries at most 4 parameters (a fixed buffer that throws past its cap), so
            // a definition declaring more could never compose — fail at the API boundary instead.
            if (definition.parameters != null && definition.parameters.Length > 4)
            {
                Debug.LogWarning($"[VelvetFilters] Cannot register \"{name}\": it declares {definition.parameters.Length} parameters, but a filter function supports at most 4.");
                return;
            }

            // The exact same registration again is a true no-op, not a conflict worth a warning.
            if (NameKeyedRegistry.Set(name, definition!, s_definitions, ReferenceEquals))
            {
                Debug.LogWarning($"[VelvetFilters] \"{name}\" is already registered; overwriting.");
            }
        }

        /// <summary>Removes a registration. Returns true when the name was registered.</summary>
        public static bool Unregister(string name) => NameKeyedRegistry.Unregister(name, s_definitions);

        // Parse-time lookup for the filter-[name:args] resolver branch. A destroyed (fake-null) asset
        // fails the lookup so the token falls through instead of applying a dead definition.
        internal static bool TryGet(string name, out FilterFunctionDefinition definition)
        {
            if (s_definitions.TryGetValue(name, out definition!) && definition != null)
            {
                return true;
            }
            definition = null!;
            return false;
        }

        private static bool HasValidNameChars(string name)
        {
            foreach (var c in name)
            {
                if (char.IsWhiteSpace(c) || c == ':' || c == '[' || c == ']')
                {
                    return false;
                }
            }
            return true;
        }
    }
}

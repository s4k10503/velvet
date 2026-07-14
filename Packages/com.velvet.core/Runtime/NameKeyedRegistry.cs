#nullable enable
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Mechanics shared by the name-keyed static registries (<see cref="VelvetFilters"/>,
    /// <see cref="VelvetFonts"/>, <see cref="FiberPortalRegistry"/>): each owns its own
    /// <see cref="Dictionary{TKey,TValue}"/> and value type, and calls into these helpers for the parts
    /// of Register/Unregister that are identical no matter what the registry stores. Validation that
    /// differs per registry — name character rules, reserved names, value-shape checks, and whether a
    /// re-registration warns at all — stays in the registry itself.
    /// </summary>
    internal static class NameKeyedRegistry
    {
        /// <summary>
        /// Inserts <paramref name="value"/> under <paramref name="key"/> in <paramref name="store"/>,
        /// overwriting any existing entry. Returns <c>false</c> for a fresh key (single-probe fast path)
        /// and <c>true</c> when a key already registered was overwritten, so the caller can warn about it.
        /// A registry that needs to recognize a re-registration as a no-op (e.g. the same reference
        /// restated) checks that itself before calling <see cref="Set{TValue}"/>, per this type's own
        /// principle that per-registry validation stays in the registry.
        /// </summary>
        public static bool Set<TValue>(string key, TValue value, Dictionary<string, TValue> store)
        {
            if (store.TryAdd(key, value))
            {
                return false;
            }

            store[key] = value;
            return true;
        }

        /// <summary>
        /// Removes <paramref name="key"/> from <paramref name="store"/>. A null/empty key, or a key that
        /// is not registered, is a no-op. Returns whether an entry was actually removed.
        /// </summary>
        public static bool Unregister<TValue>(string? key, Dictionary<string, TValue> store) =>
            !string.IsNullOrEmpty(key) && store.Remove(key);

        /// <summary>
        /// Returns whether <paramref name="key"/> is currently registered in <paramref name="store"/>. A
        /// null/empty key always returns <c>false</c>.
        /// </summary>
        public static bool IsRegistered<TValue>(string? key, Dictionary<string, TValue> store) =>
            !string.IsNullOrEmpty(key) && store.ContainsKey(key);
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// Updates the current location's query. Navigation defaults to <see cref="NavigationMode.Push"/>.
    /// </summary>
    public sealed class SearchParamsSetter
    {
        internal static readonly SearchParamsSetter Shared = new();
        private SearchParamsSetter() { }

        /// <summary>Replaces the complete query parameter set before navigating.</summary>
        public void Invoke(ISearchParams next, NavigationMode mode = NavigationMode.Push)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            Apply(_ => next, mode);
        }

        /// <summary>
        /// Applies <paramref name="updater"/> to the current query parameters before navigating.
        /// </summary>
        public void Invoke(Func<ISearchParams, ISearchParams> updater, NavigationMode mode = NavigationMode.Push)
        {
            if (updater == null) throw new ArgumentNullException(nameof(updater));
            Apply(updater, mode);
        }

        private static void Apply(Func<ISearchParams, ISearchParams> updater, NavigationMode mode)
        {
            var router = Router.Current;
            if (router == null) return;
            var currentPath = router.CurrentLocation?.Path ?? string.Empty;
            var next = updater(RouteQuery.ParseQuery(currentPath));
            var basePath = RouteQuery.StripQuery(currentPath);
            router.NavigateAsync(basePath + RouteQuery.BuildQuery(next), mode).Forget();
        }
    }

    /// <summary>
    /// Read-only view over URL query parameters that preserves every value of a repeated key. Enumeration
    /// yields distinct keys in insertion order.
    /// </summary>
    public interface ISearchParams : IEnumerable<string>
    {
        /// <summary>Number of distinct keys.</summary>
        int Count { get; }

        /// <summary>Distinct keys in insertion order.</summary>
        IReadOnlyList<string> Keys { get; }

        bool Has(string key);

        /// <summary>Returns the first value for a key, or <c>null</c> when the key is absent.</summary>
        string? Get(string key);

        /// <summary>Returns every value for a key in insertion order, or an empty list when absent.</summary>
        IReadOnlyList<string> GetAll(string key);
    }

    /// <summary>
    /// Mutable <see cref="ISearchParams"/> implementation that retains key insertion order.
    /// </summary>
    public sealed class SearchParams : ISearchParams
    {
        private readonly List<string> _keys = new();
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        public static readonly SearchParams Empty = new();

        /// <inheritdoc />
        public int Count => _keys.Count;

        /// <inheritdoc />
        public IReadOnlyList<string> Keys => _keys;

        /// <summary>
        /// Appends a value for a key, preserving any value already stored under the same key.
        /// </summary>
        public void Append(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            if (!_values.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _values[key] = list;
                _keys.Add(key);
            }

            list.Add(value ?? string.Empty);
        }

        /// <inheritdoc />
        public bool Has(string key) => key != null && _values.ContainsKey(key);

        /// <inheritdoc />
        public string? Get(string key)
            => key != null && _values.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;

        /// <inheritdoc />
        public IReadOnlyList<string> GetAll(string key)
            => key != null && _values.TryGetValue(key, out var list) ? list : Array.Empty<string>();

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator() => _keys.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

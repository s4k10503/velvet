using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    /// <summary>
    /// The setter returned by <see cref="Hooks.UseSearchParams"/>. It accepts either the next params
    /// directly or a functional updater <c>(prev) =&gt; next</c>, and defaults to a PUSH navigation (so Back
    /// returns to the previous query) — pass <see cref="NavigationMode.Replace"/> to overwrite the current
    /// entry instead.
    /// </summary>
    /// <remarks>Equivalent to React Router's <c>setSearchParams(nextInit, navigateOptions)</c> for users migrating from React Router.</remarks>
    public sealed class SearchParamsSetter
    {
        internal static readonly SearchParamsSetter Shared = new();
        private SearchParamsSetter() { }

        /// <summary>Navigates to the current path with <paramref name="next"/> as the query string.</summary>
        public void Invoke(ISearchParams next, NavigationMode mode = NavigationMode.Push)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            Apply(_ => next, mode);
        }

        /// <summary>
        /// Navigates to the current path with the query produced by applying <paramref name="updater"/> to the
        /// CURRENT params — the functional form, for editing existing query state without rebuilding it by hand.
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
    /// Read-only view over URL query parameters that preserves every value of a repeated key.
    /// </summary>
    /// <remarks>
    /// <see cref="Get"/> returns the first value for a key, while
    /// <see cref="GetAll"/> returns every value in insertion order.
    /// </remarks>
    public interface ISearchParams : IEnumerable<string>
    {
        /// <summary>Number of distinct keys.</summary>
        int Count { get; }

        /// <summary>Distinct keys in insertion order.</summary>
        IReadOnlyList<string> Keys { get; }

        /// <summary>Returns whether the given key is present.</summary>
        bool Has(string key);

        /// <summary>Returns the first value for a key, or <c>null</c> when the key is absent.</summary>
        string Get(string key);

        /// <summary>Returns every value for a key in insertion order, or an empty list when absent.</summary>
        IReadOnlyList<string> GetAll(string key);
    }

    /// <summary>
    /// Default <see cref="ISearchParams"/> implementation backed by an ordered multi-value map. Build one
    /// with <see cref="Append"/> to pass to the search-params setter; the parser produces these from a query
    /// string. Enumerating an instance yields its distinct keys in insertion order.
    /// </summary>
    public sealed class SearchParams : ISearchParams
    {
        private readonly List<string> _keys = new();
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        /// <summary>An empty instance.</summary>
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
        public string Get(string key)
            => key != null && _values.TryGetValue(key, out var list) && list.Count > 0 ? list[0] : null;

        /// <inheritdoc />
        public IReadOnlyList<string> GetAll(string key)
            => key != null && _values.TryGetValue(key, out var list) ? list : Array.Empty<string>();

        /// <inheritdoc />
        public IEnumerator<string> GetEnumerator() => _keys.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

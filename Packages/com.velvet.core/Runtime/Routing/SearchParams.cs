using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Velvet
{
    /// <summary>
    /// Updates the current location's query. Navigation defaults to <see cref="NavigationMode.Push"/>.
    /// </summary>
    public sealed class SearchParamsSetter
    {
        private readonly Router _router;

        internal SearchParamsSetter(Router router) => _router = router;

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

        private void Apply(Func<ISearchParams, ISearchParams> updater, NavigationMode mode)
        {
            var currentPath = _router.CurrentLocation?.Path ?? string.Empty;
            var next = updater(RouteQuery.ParseQuery(currentPath));
            var basePath = RouteQuery.StripQuery(currentPath);
            _router.NavigateAsync(basePath + RouteQuery.BuildQuery(next), mode).Forget();
        }
    }

    /// <summary>
    /// Read-only view over URL query parameters, the web's <c>URLSearchParams</c>: an ordered list of name/value
    /// entries in which a repeated name keeps each entry where it was added. Enumeration yields the entries in
    /// order, as <c>searchParams.entries()</c> does.
    /// </summary>
    public interface ISearchParams : IEnumerable<KeyValuePair<string, string>>
    {
        /// <summary>The number of entries, as <c>searchParams.size</c>.</summary>
        int Size { get; }

        /// <summary>Every entry's name, in order and with repeats, as <c>searchParams.keys()</c>.</summary>
        IReadOnlyList<string> Keys { get; }

        /// <summary>Every entry's value, in order, as <c>searchParams.values()</c>.</summary>
        IReadOnlyList<string> Values { get; }

        /// <summary>
        /// Whether an entry is named <paramref name="key"/>, and holds <paramref name="value"/> when one is
        /// given, as <c>searchParams.has(name, value)</c>.
        /// </summary>
        bool Has(string key, string? value = null);

        /// <summary>Returns the first value for a key, or <c>null</c> when the key is absent.</summary>
        string? Get(string key);

        /// <summary>Returns every value for a key in entry order, or an empty list when absent.</summary>
        IReadOnlyList<string> GetAll(string key);
    }

    /// <summary>
    /// Mutable <see cref="ISearchParams"/> with the methods of <c>URLSearchParams</c>. <see cref="ToString"/>
    /// serializes it as <c>searchParams.toString()</c> does.
    /// </summary>
    public sealed class SearchParams : ISearchParams
    {
        private List<KeyValuePair<string, string>> _entries = new();

        /// <summary>A new empty instance on each read, so <see cref="Append"/> on one reaches no other reader.</summary>
        public static SearchParams Empty => new();

        /// <inheritdoc />
        public int Size => _entries.Count;

        /// <inheritdoc />
        public IReadOnlyList<string> Keys => _entries.ConvertAll(entry => entry.Key);

        /// <inheritdoc />
        public IReadOnlyList<string> Values => _entries.ConvertAll(entry => entry.Value);

        /// <summary>Adds an entry after every other, as <c>searchParams.append(name, value)</c>.</summary>
        public void Append(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            _entries.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
        }

        /// <summary>
        /// Removes every entry named <paramref name="key"/>, or only those holding <paramref name="value"/> when
        /// one is given, as <c>searchParams.delete(name, value)</c>.
        /// </summary>
        public void Delete(string key, string? value = null)
            => _entries.RemoveAll(entry => entry.Key == key && (value == null || entry.Value == value));

        /// <summary>
        /// Puts <paramref name="value"/> in the first entry named <paramref name="key"/> and removes the others
        /// of that name, or appends an entry when there is none, as <c>searchParams.set(name, value)</c>.
        /// </summary>
        public void Set(string key, string value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            var first = _entries.FindIndex(entry => entry.Key == key);
            if (first < 0)
            {
                Append(key, value);
                return;
            }
            var kept = new List<KeyValuePair<string, string>>(_entries.Count);
            for (var index = 0; index < _entries.Count; index++)
            {
                if (index == first)
                {
                    kept.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
                }
                else if (_entries[index].Key != key)
                {
                    kept.Add(_entries[index]);
                }
            }
            _entries = kept;
        }

        /// <summary>
        /// Orders the entries by name, comparing UTF-16 code units and keeping the entries of one name in their
        /// order, as <c>searchParams.sort()</c>.
        /// </summary>
        public void Sort() => _entries = _entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).ToList();

        /// <inheritdoc />
        public bool Has(string key, string? value = null)
            => _entries.Exists(entry => entry.Key == key && (value == null || entry.Value == value));

        /// <inheritdoc />
        public string? Get(string key)
        {
            var index = _entries.FindIndex(entry => entry.Key == key);
            return index < 0 ? null : _entries[index].Value;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> GetAll(string key)
            => _entries.FindAll(entry => entry.Key == key).ConvertAll(entry => entry.Value);

        /// <summary>The entries as <c>application/x-www-form-urlencoded</c>, with no leading <c>?</c>.</summary>
        public override string ToString() => RouteQuery.FormUrlEncode(_entries);

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

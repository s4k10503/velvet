#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// An ordered list of name/value entries, the web's <c>FormData</c>: what a submission encoded as
    /// <c>application/x-www-form-urlencoded</c> or <c>multipart/form-data</c> carries, read through
    /// <see cref="RouteRequest.FormData"/> and <see cref="NavigationState.FormData"/>. Unlike
    /// <see cref="SearchParams"/>, a repeated name keeps each entry where it was appended. Enumeration yields
    /// the entries in order, as <c>formData.entries()</c> does.
    /// </summary>
    public sealed class FormData : IEnumerable<KeyValuePair<string, string>>
    {
        private readonly List<KeyValuePair<string, string>> _entries = new();

        /// <summary>An empty list, as <c>new FormData()</c>.</summary>
        public FormData()
        {
        }

        /// <summary>A list holding <paramref name="entries"/> in order.</summary>
        public FormData(IEnumerable<KeyValuePair<string, string>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            foreach (var entry in entries)
            {
                Append(entry.Key, entry.Value);
            }
        }

        /// <summary>Every entry's name, in order and with repeats, as <c>formData.keys()</c>.</summary>
        public IEnumerable<string> Keys
        {
            get
            {
                foreach (var entry in _entries)
                {
                    yield return entry.Key;
                }
            }
        }

        /// <summary>Every entry's value, in order, as <c>formData.values()</c>.</summary>
        public IEnumerable<string> Values
        {
            get
            {
                foreach (var entry in _entries)
                {
                    yield return entry.Value;
                }
            }
        }

        /// <summary>Adds an entry after every other, as <c>formData.append(name, value)</c>.</summary>
        public void Append(string name, string value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            _entries.Add(new KeyValuePair<string, string>(name, value ?? string.Empty));
        }

        /// <summary>Removes every entry named <paramref name="name"/>, as <c>formData.delete(name)</c>.</summary>
        public void Delete(string name) => _entries.RemoveAll(entry => entry.Key == name);

        /// <summary>The value of the first entry named <paramref name="name"/>, or null when there is none.</summary>
        public string? Get(string name)
        {
            foreach (var entry in _entries)
            {
                if (entry.Key == name)
                {
                    return entry.Value;
                }
            }
            return null;
        }

        /// <summary>The values of every entry named <paramref name="name"/>, in order.</summary>
        public IReadOnlyList<string> GetAll(string name)
        {
            var values = new List<string>();
            foreach (var entry in _entries)
            {
                if (entry.Key == name)
                {
                    values.Add(entry.Value);
                }
            }
            return values;
        }

        /// <summary>Whether an entry is named <paramref name="name"/>.</summary>
        public bool Has(string name) => Get(name) != null;

        /// <summary>
        /// Puts <paramref name="value"/> in the first entry named <paramref name="name"/> and removes the rest of
        /// them, or appends an entry when there is none, as <c>formData.set(name, value)</c>.
        /// </summary>
        public void Set(string name, string value)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            var first = _entries.FindIndex(entry => entry.Key == name);
            if (first < 0)
            {
                Append(name, value);
                return;
            }
            _entries[first] = new KeyValuePair<string, string>(name, value ?? string.Empty);
            for (var index = _entries.Count - 1; index > first; index--)
            {
                if (_entries[index].Key == name)
                {
                    _entries.RemoveAt(index);
                }
            }
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _entries.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

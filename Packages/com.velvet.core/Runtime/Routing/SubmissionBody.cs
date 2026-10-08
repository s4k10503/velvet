using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace Velvet
{
    // A get submission's body as React Router's normalizeNavigateOptions encodes it with
    // new URLSearchParams(body), in the order the body holds its pairs.
    internal static class SubmissionBody
    {
        // False for a body URLSearchParams throws on: a sequence whose entries are not name/value pairs.
        internal static bool TryEncode(object? body, out List<KeyValuePair<string, string>> pairs)
        {
            pairs = new List<KeyValuePair<string, string>>();
            switch (body)
            {
                case null:
                    return true;
                case ISearchParams searchParams:
                    AddAll(pairs, searchParams);
                    return true;
                case string text:
                    pairs = RouteQuery.ParsePairs(text.StartsWith('?') ? text : "?" + text);
                    return true;
                case IDictionary dictionary:
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        pairs.Add(Pair(entry.Key, entry.Value));
                    }
                    return true;
                case IEnumerable sequence:
                    return TryAddSequence(pairs, sequence);
                default:
                    AddProperties(pairs, body);
                    return true;
            }
        }

        private static void AddAll(List<KeyValuePair<string, string>> pairs, ISearchParams searchParams)
        {
            foreach (var key in searchParams.Keys)
            {
                foreach (var value in searchParams.GetAll(key))
                {
                    pairs.Add(new KeyValuePair<string, string>(key, value ?? string.Empty));
                }
            }
        }

        // A plain object's own enumerable properties: a public instance property of an anonymous type or a POCO.
        // A primitive is the one body that converts to a string instead, which parses as a query string.
        private static void AddProperties(List<KeyValuePair<string, string>> pairs, object body)
        {
            if (body.GetType().IsPrimitive || body is Enum)
            {
                pairs.AddRange(RouteQuery.ParsePairs("?" + Text(body)));
                return;
            }
            foreach (var property in body.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                {
                    pairs.Add(Pair(property.Name, property.GetValue(body)));
                }
            }
        }

        private static bool TryAddSequence(List<KeyValuePair<string, string>> pairs, IEnumerable sequence)
        {
            foreach (var entry in sequence)
            {
                if (!TryPair(entry, out var pair))
                {
                    return false;
                }
                pairs.Add(pair);
            }
            return true;
        }

        // A key/value pair of any generic arguments, or a two-element list.
        private static bool TryPair(object? entry, out KeyValuePair<string, string> pair)
        {
            pair = default;
            var type = entry?.GetType();
            if (type != null && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                pair = Pair(type.GetProperty("Key")!.GetValue(entry), type.GetProperty("Value")!.GetValue(entry));
                return true;
            }
            if (entry is IList { Count: 2 } list && entry is not string)
            {
                pair = Pair(list[0], list[1]);
                return true;
            }
            return false;
        }

        private static KeyValuePair<string, string> Pair(object? key, object? value)
            => new(Text(key), Text(value));

        // JavaScript's String(value): null is "null", a boolean is lower-case, a number has no culture.
        private static string Text(object? value) => value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}

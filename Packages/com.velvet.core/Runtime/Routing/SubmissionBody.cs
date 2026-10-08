using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Velvet
{
    // A get submission's body as React Router's normalizeNavigateOptions encodes it with
    // new URLSearchParams(body), in the order the body holds its pairs.
    internal static class SubmissionBody
    {
        // False for a body URLSearchParams throws on: a sequence whose entries are not name/value pairs, or
        // anything the encoding itself throws, as React Router's try around new URLSearchParams has it.
        internal static bool TryEncode(object? body, out List<KeyValuePair<string, string>> pairs)
        {
            var encoded = new List<KeyValuePair<string, string>>();
            try
            {
                var ok = Encode(body, ref encoded);
                pairs = ok ? encoded : new List<KeyValuePair<string, string>>();
                return ok;
            }
            catch (Exception)
            {
                pairs = new List<KeyValuePair<string, string>>();
                return false;
            }
        }

        private static bool Encode(object? body, ref List<KeyValuePair<string, string>> pairs)
        {
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

        // A plain object's own enumerable properties: the public members a type declares itself, in declaration
        // order, read by reflection as ComponentPropsComparer reads a props bag. A number-like primitive is the one body that
        // converts to a string instead, which parses as a query string.
        private static void AddProperties(List<KeyValuePair<string, string>> pairs, object body)
        {
            if (body.GetType().IsPrimitive || body is Enum || body is decimal || body is BigInteger)
            {
                pairs.AddRange(RouteQuery.ParsePairs("?" + Text(body)));
                return;
            }
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            var type = body.GetType();
            foreach (var property in type.GetProperties(flags).OrderBy(member => member.MetadataToken))
            {
                if (property.GetIndexParameters().Length == 0 && property.CanRead)
                {
                    pairs.Add(Pair(property.Name, property.GetValue(body)));
                }
            }
            foreach (var field in type.GetFields(flags).OrderBy(member => member.MetadataToken))
            {
                pairs.Add(Pair(field.Name, field.GetValue(body)));
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
            if (entry is ITuple { Length: 2 } tuple)
            {
                pair = Pair(tuple[0], tuple[1]);
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

        // JavaScript's String(value): null is "null", a boolean is lower-case, a number has no culture, and a
        // list is its elements joined by "," with a null one empty, as Array.prototype.toString has it.
        private static string Text(object? value) => value switch
        {
            null => "null",
            bool flag => flag ? "true" : "false",
            double number => JsNumber(number),
            float number => JsNumber(double.Parse(number.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)),
            decimal number => (number / 1.0000000000000000000000000000m).ToString(CultureInfo.InvariantCulture),
            string text => text,
            IDictionary => "[object Object]",
            IEnumerable list => string.Join(",", list.Cast<object?>().Select(item => item == null ? string.Empty : Text(item))),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

        // Number::toString: the shortest round-trip digits, positional between 1e-6 and 1e21 and otherwise
        // with a lower-case signed exponent; both zeros are "0".
        private static string JsNumber(double number)
        {
            if (number == 0)
            {
                return "0";
            }
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return double.IsNaN(number) ? "NaN" : number > 0 ? "Infinity" : "-Infinity";
            }
            var sign = number < 0 ? "-" : string.Empty;
            var roundTrip = Math.Abs(number).ToString("R", CultureInfo.InvariantCulture);
            var mantissa = roundTrip;
            var exponent = 0;
            var e = roundTrip.IndexOf('E');
            if (e >= 0)
            {
                mantissa = roundTrip.Substring(0, e);
                exponent = int.Parse(roundTrip.Substring(e + 1), CultureInfo.InvariantCulture);
            }
            var dot = mantissa.IndexOf('.');
            var digits = mantissa.Replace(".", string.Empty);
            var point = (dot < 0 ? mantissa.Length : dot) + exponent;
            var lead = digits.Length - digits.TrimStart('0').Length;
            digits = digits.TrimStart('0').TrimEnd('0');
            point -= lead;
            return sign + Layout(digits, point);
        }

        // digits with the decimal point after the first `point` of them, as Number::toString lays them out.
        private static string Layout(string digits, int point)
        {
            if (point >= digits.Length && point <= 21)
            {
                return digits + new string('0', point - digits.Length);
            }
            if (point > 0 && point <= 21)
            {
                return digits.Substring(0, point) + "." + digits.Substring(point);
            }
            if (point > -6 && point <= 0)
            {
                return "0." + new string('0', -point) + digits;
            }
            var exponent = point - 1;
            var tail = digits.Length > 1 ? "." + digits.Substring(1) : string.Empty;
            return digits[0] + tail + "e" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString(CultureInfo.InvariantCulture);
        }
    }
}

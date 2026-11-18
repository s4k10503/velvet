#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Velvet
{
    // JavaScript's JSON.parse and JSON.stringify over the values a submission body holds. A parsed object is a
    // Dictionary<string, object?> in a JavaScript object's key order, an array a List<object?>, a number a
    // double. A C# object is stringified as the plain object SubmissionBody reads it as.
    internal static class JsonText
    {
        internal static object? Parse(string text)
        {
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var value = reader.ReadValue();
            reader.SkipWhitespace();
            if (!reader.AtEnd)
            {
                throw reader.Unexpected();
            }
            return value;
        }

        // Null where JSON.stringify returns undefined: a delegate, which stands for a function.
        internal static string? Stringify(object? value)
        {
            var builder = new StringBuilder();
            return Write(builder, value, new List<object>()) ? builder.ToString() : null;
        }

        // False where the value is undefined to JSON.stringify, which leaves it out of an object.
        private static bool Write(StringBuilder builder, object? value, List<object> stack)
        {
            if (value is Delegate)
            {
                return false;
            }
            if (!TryWriteScalar(builder, value))
            {
                WriteContainer(builder, value!, stack);
            }
            return true;
        }

        // False for a value JSON writes as an object or an array.
        private static bool TryWriteScalar(StringBuilder builder, object? value)
        {
            switch (value)
            {
                case null:
                    builder.Append("null");
                    return true;
                case bool flag:
                    builder.Append(flag ? "true" : "false");
                    return true;
                case string text:
                    Quote(builder, text);
                    return true;
                case char character:
                    Quote(builder, character.ToString());
                    return true;
                case BigInteger:
                    throw new InvalidOperationException("Do not know how to serialize a BigInt");
                case Enum:
                    Quote(builder, SubmissionBody.Text(value));
                    return true;
            }

            if (TryNumber(value, out var number))
            {
                builder.Append(double.IsNaN(number) || double.IsInfinity(number) ? "null" : SubmissionBody.Text(number));
                return true;
            }

            if (value is IFormattable)
            {
                Quote(builder, SubmissionBody.Text(value));
                return true;
            }
            return false;
        }

        private static void WriteContainer(StringBuilder builder, object value, List<object> stack)
        {
            foreach (var open in stack)
            {
                if (ReferenceEquals(open, value))
                {
                    throw new InvalidOperationException("Converting circular structure to JSON");
                }
            }
            stack.Add(value);
            switch (value)
            {
                case FormData:
                case ISearchParams:
                    // No own enumerable property, as JSON.stringify(new FormData()) is "{}".
                    builder.Append("{}");
                    break;
                case IDictionary dictionary:
                    var entries = new List<KeyValuePair<string, object?>>();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        entries.Add(new KeyValuePair<string, object?>(SubmissionBody.Text(entry.Key), entry.Value));
                    }
                    WriteObject(builder, entries, stack);
                    break;
                case IEnumerable sequence:
                    WriteArray(builder, sequence, stack);
                    break;
                default:
                    WriteObject(builder, SubmissionBody.OwnProperties(value), stack);
                    break;
            }
            stack.RemoveAt(stack.Count - 1);
        }

        private static void WriteArray(StringBuilder builder, IEnumerable sequence, List<object> stack)
        {
            builder.Append('[');
            var first = true;
            foreach (var item in sequence)
            {
                if (!first)
                {
                    builder.Append(',');
                }
                first = false;
                if (!Write(builder, item, stack))
                {
                    builder.Append("null");
                }
            }
            builder.Append(']');
        }

        private static void WriteObject(
            StringBuilder builder, IEnumerable<KeyValuePair<string, object?>> entries, List<object> stack)
        {
            builder.Append('{');
            var first = true;
            foreach (var entry in InObjectOrder(entries))
            {
                var mark = builder.Length;
                if (!first)
                {
                    builder.Append(',');
                }
                Quote(builder, entry.Key);
                builder.Append(':');
                if (Write(builder, entry.Value, stack))
                {
                    first = false;
                }
                else
                {
                    // An undefined member is left out.
                    builder.Length = mark;
                }
            }
            builder.Append('}');
        }

        // A JavaScript object's own key order: array indexes ascending, then every other key as first set, a key
        // set twice keeping its first place and its last value.
        private static List<KeyValuePair<string, object?>> InObjectOrder(IEnumerable<KeyValuePair<string, object?>> entries)
        {
            var indexes = new List<KeyValuePair<string, object?>>();
            var names = new List<KeyValuePair<string, object?>>();
            var places = new Dictionary<string, (bool Index, int Place)>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (places.TryGetValue(entry.Key, out var place))
                {
                    var list = place.Index ? indexes : names;
                    list[place.Place] = entry;
                    continue;
                }
                var isIndex = IsArrayIndex(entry.Key);
                var target = isIndex ? indexes : names;
                places[entry.Key] = (isIndex, target.Count);
                target.Add(entry);
            }
            indexes.Sort((a, b) => uint.Parse(a.Key, CultureInfo.InvariantCulture)
                .CompareTo(uint.Parse(b.Key, CultureInfo.InvariantCulture)));
            indexes.AddRange(names);
            return indexes;
        }

        // A canonical integer string below 2^32 - 1: one that reads back as the text it was parsed from.
        private static bool IsArrayIndex(string key)
        {
            if (!uint.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                return false;
            }
            return index != uint.MaxValue && index.ToString(CultureInfo.InvariantCulture) == key;
        }

        private static bool TryNumber(object value, out double number)
        {
            switch (value)
            {
                case double d:
                    number = d;
                    return true;
                case float f:
                    number = double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                    return true;
                case decimal m:
                    number = (double)m;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return true;
                default:
                    number = 0;
                    return false;
            }
        }

        // JSON.stringify's QuoteJSONString.
        private static void Quote(StringBuilder builder, string text)
        {
            builder.Append('"');
            for (var index = 0; index < text.Length; index++)
            {
                var c = text[index];
                switch (c)
                {
                    case '"':
                        builder.Append("\\\"");
                        continue;
                    case '\\':
                        builder.Append("\\\\");
                        continue;
                    case '\b':
                        builder.Append("\\b");
                        continue;
                    case '\f':
                        builder.Append("\\f");
                        continue;
                    case '\n':
                        builder.Append("\\n");
                        continue;
                    case '\r':
                        builder.Append("\\r");
                        continue;
                    case '\t':
                        builder.Append("\\t");
                        continue;
                }
                var paired = char.IsHighSurrogate(c)
                    ? index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                    : char.IsLowSurrogate(c) && index > 0 && char.IsHighSurrogate(text[index - 1]);
                if (c < ' ' || (char.IsSurrogate(c) && !paired))
                {
                    builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    continue;
                }
                builder.Append(c);
            }
            builder.Append('"');
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _at;

            internal Reader(string text) => _text = text;

            internal bool AtEnd => _at >= _text.Length;

            internal FormatException Unexpected() => AtEnd
                ? new FormatException("Unexpected end of JSON input")
                : new FormatException($"Unexpected token '{_text[_at]}' in JSON at position {_at}");

            internal void SkipWhitespace()
            {
                while (!AtEnd && IsWhitespace(_text[_at]))
                {
                    _at++;
                }
            }

            private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\r';

            internal object? ReadValue()
            {
                if (AtEnd)
                {
                    throw Unexpected();
                }
                switch (_text[_at])
                {
                    case '{':
                        return ReadObject();
                    case '[':
                        return ReadArray();
                    case '"':
                        return ReadString();
                    case 't':
                        ReadWord("true");
                        return true;
                    case 'f':
                        ReadWord("false");
                        return false;
                    case 'n':
                        ReadWord("null");
                        return null;
                    default:
                        return ReadNumber();
                }
            }

            private Dictionary<string, object?> ReadObject()
            {
                _at++;
                var entries = new List<KeyValuePair<string, object?>>();
                SkipWhitespace();
                if (!AtEnd && _text[_at] == '}')
                {
                    _at++;
                    return ToDictionary(entries);
                }
                while (true)
                {
                    SkipWhitespace();
                    if (AtEnd || _text[_at] != '"')
                    {
                        throw Unexpected();
                    }
                    var key = ReadString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    entries.Add(new KeyValuePair<string, object?>(key, ReadValue()));
                    SkipWhitespace();
                    if (!AtEnd && _text[_at] == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect('}');
                    return ToDictionary(entries);
                }
            }

            private static Dictionary<string, object?> ToDictionary(List<KeyValuePair<string, object?>> entries)
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var entry in InObjectOrder(entries))
                {
                    result.Add(entry.Key, entry.Value);
                }
                return result;
            }

            private List<object?> ReadArray()
            {
                _at++;
                var items = new List<object?>();
                SkipWhitespace();
                if (!AtEnd && _text[_at] == ']')
                {
                    _at++;
                    return items;
                }
                while (true)
                {
                    SkipWhitespace();
                    items.Add(ReadValue());
                    SkipWhitespace();
                    if (!AtEnd && _text[_at] == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect(']');
                    return items;
                }
            }

            private string ReadString()
            {
                _at++;
                var builder = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Unexpected();
                    }
                    var c = _text[_at];
                    if (c == '"')
                    {
                        _at++;
                        return builder.ToString();
                    }
                    if (c < ' ')
                    {
                        throw Unexpected();
                    }
                    if (c != '\\')
                    {
                        builder.Append(c);
                        _at++;
                        continue;
                    }
                    _at++;
                    if (AtEnd)
                    {
                        throw Unexpected();
                    }
                    var escape = _text[_at];
                    _at++;
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'n': builder.Append('\n'); break;
                        case 'r': builder.Append('\r'); break;
                        case 't': builder.Append('\t'); break;
                        case 'u':
                            var hex = _at + 4 <= _text.Length ? _text.Substring(_at, 4) : string.Empty;
                            if (!int.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            {
                                throw Unexpected();
                            }
                            builder.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            _at--;
                            throw Unexpected();
                    }
                }
            }

            private double ReadNumber()
            {
                var start = _at;
                if (!AtEnd && _text[_at] == '-')
                {
                    _at++;
                }
                if (AtEnd || !IsDigit(_text[_at]))
                {
                    throw Unexpected();
                }
                if (_text[_at] == '0')
                {
                    _at++;
                }
                else
                {
                    SkipDigits();
                }
                if (!AtEnd && _text[_at] == '.')
                {
                    _at++;
                    if (AtEnd || !IsDigit(_text[_at]))
                    {
                        throw Unexpected();
                    }
                    SkipDigits();
                }
                if (!AtEnd && _text[_at] is 'e' or 'E')
                {
                    _at++;
                    if (!AtEnd && _text[_at] is '+' or '-')
                    {
                        _at++;
                    }
                    if (AtEnd || !IsDigit(_text[_at]))
                    {
                        throw Unexpected();
                    }
                    SkipDigits();
                }
                var literal = _text.Substring(start, _at - start);
                try
                {
                    return double.Parse(literal, NumberStyles.Float, CultureInfo.InvariantCulture);
                }
                catch (OverflowException)
                {
                    // A runtime that refuses a literal past double's range: JSON.parse makes it an infinity.
                    return literal[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
                }
            }

            private void SkipDigits()
            {
                while (!AtEnd && IsDigit(_text[_at]))
                {
                    _at++;
                }
            }

            private static bool IsDigit(char c) => c >= '0' && c <= '9';

            private void ReadWord(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                {
                    throw Unexpected();
                }
                _at += word.Length;
            }

            private void Expect(char c)
            {
                if (AtEnd || _text[_at] != c)
                {
                    throw Unexpected();
                }
                _at++;
            }
        }
    }
}

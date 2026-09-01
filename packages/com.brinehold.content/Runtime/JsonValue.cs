using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Brinehold.Content
{
    /// <summary>
    /// A small JSON reader, written rather than taken from a package.
    ///
    /// Content is loaded once, outside the tick, so parsing speed is irrelevant; what matters is
    /// having no third-party dependency to reconcile between the .NET server build and the Unity
    /// import, and being able to report a problem with a line number a designer can act on.
    ///
    /// It reads exactly what the content files need — objects, arrays, strings, numbers, booleans
    /// and null — and rejects anything else rather than guessing.
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind Type { get; private set; } = Kind.Null;

        private bool _bool;
        private double _number;          // authoring-time only; converted to fixed point on read
        private string _string = string.Empty;
        private List<JsonValue>? _array;
        private Dictionary<string, JsonValue>? _object;

        public bool AsBool => Type == Kind.Bool && _bool;
        public string AsString => Type == Kind.String ? _string : string.Empty;
        public int AsInt => Type == Kind.Number ? (int)System.Math.Round(_number) : 0;
        public IReadOnlyList<JsonValue> AsArray => _array ?? (IReadOnlyList<JsonValue>)Array.Empty<JsonValue>();

        /// <summary>
        /// A number as thousandths, which is how fixed-point values are authored: 1.4 becomes 1400
        /// and is turned into <c>Fix64.FromMilli(1400)</c>. Authoring in decimal and converting at a
        /// single point keeps the content files readable without letting a double anywhere near the
        /// simulation.
        /// </summary>
        public int AsMilli => Type == Kind.Number ? (int)System.Math.Round(_number * 1000.0) : 0;

        public bool Has(string key) => _object != null && _object.ContainsKey(key);

        public JsonValue this[string key]
            => _object != null && _object.TryGetValue(key, out JsonValue? value) ? value : new JsonValue();

        public IEnumerable<string> Keys => _object?.Keys ?? (IEnumerable<string>)Array.Empty<string>();

        public int GetInt(string key, int fallback = 0) => Has(key) ? this[key].AsInt : fallback;
        public int GetMilli(string key, int fallback = 0) => Has(key) ? this[key].AsMilli : fallback;
        public bool GetBool(string key, bool fallback = false) => Has(key) ? this[key].AsBool : fallback;
        public string GetString(string key, string fallback = "") => Has(key) ? this[key].AsString : fallback;

        public static bool TryParse(string text, out JsonValue value, out string error)
        {
            var parser = new Parser(text);
            try
            {
                value = parser.ParseValue();
                parser.SkipWhitespace();
                if (!parser.AtEnd)
                {
                    error = $"unexpected trailing content at line {parser.Line}";
                    return false;
                }
                error = string.Empty;
                return true;
            }
            catch (FormatException exception)
            {
                value = new JsonValue();
                error = exception.Message;
                return false;
            }
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _index;
            public int Line = 1;

            public Parser(string text) => _text = text;

            public bool AtEnd => _index >= _text.Length;

            public void SkipWhitespace()
            {
                while (_index < _text.Length)
                {
                    char c = _text[_index];
                    if (c == '\n') { Line++; _index++; continue; }
                    if (char.IsWhiteSpace(c)) { _index++; continue; }

                    // Line comments are not standard JSON, but content files benefit from being able
                    // to explain a number to the next designer who reads it.
                    if (c == '/' && _index + 1 < _text.Length && _text[_index + 1] == '/')
                    {
                        while (_index < _text.Length && _text[_index] != '\n') _index++;
                        continue;
                    }
                    break;
                }
            }

            private char Peek()
            {
                if (AtEnd) throw new FormatException($"unexpected end of file at line {Line}");
                return _text[_index];
            }

            private char Next()
            {
                char c = Peek();
                _index++;
                if (c == '\n') Line++;
                return c;
            }

            private void Expect(char expected)
            {
                char c = Next();
                if (c != expected) throw new FormatException($"expected '{expected}' but found '{c}' at line {Line}");
            }

            public JsonValue ParseValue()
            {
                SkipWhitespace();
                char c = Peek();
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return new JsonValue { Type = Kind.String, _string = ParseString() };
                    case 't': ExpectWord("true"); return new JsonValue { Type = Kind.Bool, _bool = true };
                    case 'f': ExpectWord("false"); return new JsonValue { Type = Kind.Bool, _bool = false };
                    case 'n': ExpectWord("null"); return new JsonValue { Type = Kind.Null };
                    default: return ParseNumber();
                }
            }

            private void ExpectWord(string word)
            {
                foreach (char expected in word) Expect(expected);
            }

            private JsonValue ParseObject()
            {
                Expect('{');
                var result = new JsonValue { Type = Kind.Object, _object = new Dictionary<string, JsonValue>() };

                SkipWhitespace();
                if (Peek() == '}') { Next(); return result; }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    result._object![key] = ParseValue();

                    SkipWhitespace();
                    char c = Next();
                    if (c == '}') return result;
                    if (c != ',') throw new FormatException($"expected ',' or '}}' at line {Line}");
                }
            }

            private JsonValue ParseArray()
            {
                Expect('[');
                var result = new JsonValue { Type = Kind.Array, _array = new List<JsonValue>() };

                SkipWhitespace();
                if (Peek() == ']') { Next(); return result; }

                while (true)
                {
                    result._array!.Add(ParseValue());
                    SkipWhitespace();
                    char c = Next();
                    if (c == ']') return result;
                    if (c != ',') throw new FormatException($"expected ',' or ']' at line {Line}");
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();

                while (true)
                {
                    char c = Next();
                    if (c == '"') return builder.ToString();

                    if (c != '\\') { builder.Append(c); continue; }

                    char escape = Next();
                    switch (escape)
                    {
                        case '"': builder.Append('"'); break;
                        case '\\': builder.Append('\\'); break;
                        case '/': builder.Append('/'); break;
                        case 'n': builder.Append('\n'); break;
                        case 't': builder.Append('\t'); break;
                        case 'r': builder.Append('\r'); break;
                        case 'b': builder.Append('\b'); break;
                        case 'f': builder.Append('\f'); break;
                        case 'u':
                        {
                            var hex = new char[4];
                            for (int i = 0; i < 4; i++) hex[i] = Next();
                            builder.Append((char)Convert.ToInt32(new string(hex), 16));
                            break;
                        }
                        default: throw new FormatException($"unknown escape '\\{escape}' at line {Line}");
                    }
                }
            }

            private JsonValue ParseNumber()
            {
                int start = _index;
                if (Peek() == '-') Next();

                while (!AtEnd)
                {
                    char c = _text[_index];
                    if (char.IsDigit(c) || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { _index++; continue; }
                    break;
                }

                string slice = _text.Substring(start, _index - start);
                if (!double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                    throw new FormatException($"'{slice}' is not a number, at line {Line}");

                return new JsonValue { Type = Kind.Number, _number = parsed };
            }
        }
    }
}

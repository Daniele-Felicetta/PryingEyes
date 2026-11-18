using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Production.Saves
{
    /// <summary>The kinds of value JSON has.</summary>
    public enum JsonKind { Null, Bool, Number, String, Array, Object }

    /// <summary>Text that is not valid JSON. Carries the 1-based line and column of the first bad character.</summary>
    public sealed class JsonSyntaxException : FormatException
    {
        public int Line { get; }
        public int Column { get; }
        public JsonSyntaxException(string message, int line, int column) : base(message + " (line " + line + ", column " + column + ")") { Line = line; Column = column; }
    }

    /// <summary>One value of a JSON document: the tree a migration works on. A save is read into this tree, upgraded step by step
    /// and only then handed to typed code, so an old file never has to fit the classes of the current version.
    /// <para>Objects keep the order their fields were written in. A number keeps its text, so an integer above 2^53 (a seed, a
    /// timestamp) comes back exactly. Reading a missing or mistyped field gives the fallback you pass, never an exception: a migration
    /// decides what an absent field means.</para></summary>
    public sealed class JsonItem
    {
        /// <summary>Nesting deeper than this is refused (a hand-made file must not overflow the stack of the reader).</summary>
        public const int MaxDepth = 64;

        static readonly JsonItem NullItem = new JsonItem(JsonKind.Null, null), TrueItem = new JsonItem(true), FalseItem = new JsonItem(false);

        readonly JsonKind kind;
        readonly string text;                       // the value of a string, or the text of a number
        readonly bool flag;
        readonly List<JsonItem> list;               // arrays
        readonly List<string> order;                // objects: the keys in the order they were first set
        readonly Dictionary<string, JsonItem> map;

        JsonItem(bool value) { kind = JsonKind.Bool; flag = value; }
        JsonItem(JsonKind kind, string text)
        {
            this.kind = kind; this.text = text;
            if (kind == JsonKind.Array) list = new List<JsonItem>();
            else if (kind == JsonKind.Object) { order = new List<string>(); map = new Dictionary<string, JsonItem>(StringComparer.Ordinal); }
        }

        // ------------------------------------------------------------------ making values
        public static JsonItem Null => NullItem;
        public static JsonItem Object() => new JsonItem(JsonKind.Object, null);
        public static JsonItem Array() => new JsonItem(JsonKind.Array, null);
        public static JsonItem From(bool value) => value ? TrueItem : FalseItem;
        public static JsonItem From(string value) => value == null ? NullItem : new JsonItem(JsonKind.String, value);
        public static JsonItem From(long value) => new JsonItem(JsonKind.Number, value.ToString(CultureInfo.InvariantCulture));
        public static JsonItem From(int value) => From((long)value);
        public static JsonItem From(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentException("JSON cannot hold NaN or infinity");
            return new JsonItem(JsonKind.Number, value.ToString("R", CultureInfo.InvariantCulture));
        }
        public static JsonItem From(float value) => From((double)value);

        // ------------------------------------------------------------------ kind
        public JsonKind Kind => kind;
        public bool IsNull => kind == JsonKind.Null;
        public bool IsBool => kind == JsonKind.Bool;
        public bool IsNumber => kind == JsonKind.Number;
        public bool IsString => kind == JsonKind.String;
        public bool IsArray => kind == JsonKind.Array;
        public bool IsObject => kind == JsonKind.Object;

        // ------------------------------------------------------------------ reading a scalar (a wrong kind gives the fallback)
        public string AsString(string fallback = "") => kind == JsonKind.String ? text : fallback;
        public bool AsBool(bool fallback = false) => kind == JsonKind.Bool ? flag : fallback;
        public double AsDouble(double fallback = 0)
        {
            if (kind != JsonKind.Number) return fallback;
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;
        }
        public long AsLong(long fallback = 0)
        {
            if (kind != JsonKind.Number) return fallback;
            if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
            double d = AsDouble(double.NaN);
            if (double.IsNaN(d)) return fallback;
            if (d >= long.MaxValue) return long.MaxValue;
            if (d <= long.MinValue) return long.MinValue;
            return (long)Math.Truncate(d);
        }
        public int AsInt(int fallback = 0)
        {
            long l = AsLong(fallback);
            return l > int.MaxValue ? int.MaxValue : l < int.MinValue ? int.MinValue : (int)l;
        }
        /// <summary>The text a number was written with ("1e3", "007" cannot occur: the reader refuses it), or null for other kinds.</summary>
        public string NumberText => kind == JsonKind.Number ? text : null;

        // ------------------------------------------------------------------ objects
        /// <summary>The number of fields (object) or items (array); zero for a scalar.</summary>
        public int Count => kind == JsonKind.Object ? order.Count : kind == JsonKind.Array ? list.Count : 0;
        /// <summary>The field names in order, as a copy: a migration may rename or remove fields while it walks them.</summary>
        public IReadOnlyList<string> Keys => kind == JsonKind.Object ? order.ToArray() : System.Array.Empty<string>();
        public bool Has(string key) => kind == JsonKind.Object && key != null && map.ContainsKey(key);

        /// <summary>The field, or C# null when this is not an object or has no such field (a field holding JSON null is JsonItem.Null).</summary>
        public JsonItem Get(string key) => kind == JsonKind.Object && key != null && map.TryGetValue(key, out var v) ? v : null;
        public JsonItem this[string key] { get => Get(key); set => Set(key, value); }

        /// <summary>A field deep in the tree: Path("cathedral", "bank") or Path("weapons", "0"); array steps are decimal indices. Null when any step is missing.</summary>
        public JsonItem Path(params string[] steps)
        {
            var at = this;
            foreach (string step in steps)
            {
                if (at == null) return null;
                if (at.kind == JsonKind.Object) at = at.Get(step);
                else if (at.kind == JsonKind.Array && int.TryParse(step, NumberStyles.None, CultureInfo.InvariantCulture, out int i) && i < at.list.Count) at = at.list[i];
                else return null;
            }
            return at;
        }

        public string Str(string key, string fallback = "") => Get(key)?.AsString(fallback) ?? fallback;
        public int Int(string key, int fallback = 0) => Get(key)?.AsInt(fallback) ?? fallback;
        public long Long(string key, long fallback = 0) => Get(key)?.AsLong(fallback) ?? fallback;
        public double Num(string key, double fallback = 0) => Get(key)?.AsDouble(fallback) ?? fallback;
        public bool Bool(string key, bool fallback = false) => Get(key)?.AsBool(fallback) ?? fallback;

        /// <summary>Sets a field (a new key goes last, an existing one keeps its place). Only on objects. A C# null stores JSON null.</summary>
        public JsonItem Set(string key, JsonItem value)
        {
            if (kind != JsonKind.Object) throw new InvalidOperationException("Set needs an object");
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("a field needs a name");
            if (!map.ContainsKey(key)) order.Add(key);
            map[key] = value ?? NullItem;
            return this;
        }
        public JsonItem Set(string key, string value) => Set(key, From(value));
        public JsonItem Set(string key, long value) => Set(key, From(value));
        public JsonItem Set(string key, int value) => Set(key, From(value));
        public JsonItem Set(string key, double value) => Set(key, From(value));
        public JsonItem Set(string key, bool value) => Set(key, From(value));

        /// <summary>Sets a field only when it is absent: how a migration gives an old save the default of something new.</summary>
        public JsonItem SetDefault(string key, JsonItem value) => Has(key) ? this : Set(key, value);
        public JsonItem SetDefault(string key, long value) => Has(key) ? this : Set(key, value);
        public JsonItem SetDefault(string key, string value) => Has(key) ? this : Set(key, value);
        public JsonItem SetDefault(string key, bool value) => Has(key) ? this : Set(key, value);

        public bool Remove(string key)
        {
            if (kind != JsonKind.Object || key == null || !map.Remove(key)) return false;
            order.Remove(key);
            return true;
        }

        /// <summary>Gives a field another name, in the same place. A field already called 'to' is replaced. False when 'from' is absent.</summary>
        public bool Rename(string from, string to)
        {
            if (kind != JsonKind.Object || from == null || string.IsNullOrEmpty(to) || !map.TryGetValue(from, out var value)) return false;
            if (from == to) return true;
            if (map.ContainsKey(to)) { map.Remove(to); order.Remove(to); }
            order[order.IndexOf(from)] = to;
            map.Remove(from);
            map[to] = value;
            return true;
        }

        /// <summary>The object under 'key', created (and attached) if it is absent or not an object.</summary>
        public JsonItem ObjectAt(string key)
        {
            var existing = Get(key);
            if (existing != null && existing.kind == JsonKind.Object) return existing;
            var made = Object();
            Set(key, made);
            return made;
        }

        /// <summary>The array under 'key', created (and attached) if it is absent or not an array.</summary>
        public JsonItem ArrayAt(string key)
        {
            var existing = Get(key);
            if (existing != null && existing.kind == JsonKind.Array) return existing;
            var made = Array();
            Set(key, made);
            return made;
        }

        // ------------------------------------------------------------------ arrays
        /// <summary>The items in order, as a copy (adding while walking is safe).</summary>
        public IReadOnlyList<JsonItem> Items => kind == JsonKind.Array ? list.ToArray() : System.Array.Empty<JsonItem>();
        public JsonItem this[int index] => kind == JsonKind.Array && index >= 0 && index < list.Count ? list[index] : null;
        public JsonItem Add(JsonItem value)
        {
            if (kind != JsonKind.Array) throw new InvalidOperationException("Add needs an array");
            list.Add(value ?? NullItem);
            return this;
        }
        public JsonItem Add(string value) => Add(From(value));
        public JsonItem Add(long value) => Add(From(value));
        public JsonItem Clear()
        {
            if (kind == JsonKind.Array) list.Clear();
            else if (kind == JsonKind.Object) { order.Clear(); map.Clear(); }
            return this;
        }

        // ------------------------------------------------------------------ copies and comparison
        public JsonItem DeepClone()
        {
            switch (kind)
            {
                case JsonKind.Object: { var c = Object(); foreach (string k in order) c.Set(k, map[k].DeepClone()); return c; }
                case JsonKind.Array: { var c = Array(); foreach (var v in list) c.Add(v.DeepClone()); return c; }
                case JsonKind.String: return new JsonItem(JsonKind.String, text);
                case JsonKind.Number: return new JsonItem(JsonKind.Number, text);
                default: return this;           // null, true and false never change
            }
        }

        /// <summary>Same content: object fields compare whatever their order, numbers by value (1 equals 1.0), arrays in order.</summary>
        public static bool DeepEquals(JsonItem a, JsonItem b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.kind != b.kind) return false;
            switch (a.kind)
            {
                case JsonKind.Null: return true;
                case JsonKind.Bool: return a.flag == b.flag;
                case JsonKind.String: return string.Equals(a.text, b.text, StringComparison.Ordinal);
                case JsonKind.Number:
                    if (a.text == b.text) return true;
                    if (long.TryParse(a.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long la) &&
                        long.TryParse(b.text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long lb)) return la == lb;
                    return a.AsDouble() == b.AsDouble();
                case JsonKind.Array:
                    if (a.list.Count != b.list.Count) return false;
                    for (int i = 0; i < a.list.Count; i++) if (!DeepEquals(a.list[i], b.list[i])) return false;
                    return true;
                default:
                    if (a.order.Count != b.order.Count) return false;
                    foreach (string k in a.order) if (!b.map.TryGetValue(k, out var other) || !DeepEquals(a.map[k], other)) return false;
                    return true;
            }
        }

        // ------------------------------------------------------------------ writing
        /// <summary>The JSON text. Always valid UTF-8 once encoded: a lone surrogate is written as an escape, never as a broken character.</summary>
        public string ToJson(bool indented = false)
        {
            var sb = new StringBuilder();
            Write(sb, indented, 0);
            return sb.ToString();
        }
        public override string ToString() => ToJson(false);

        void Write(StringBuilder sb, bool pretty, int depth)
        {
            switch (kind)
            {
                case JsonKind.Null: sb.Append("null"); break;
                case JsonKind.Bool: sb.Append(flag ? "true" : "false"); break;
                case JsonKind.Number: sb.Append(text); break;
                case JsonKind.String: WriteString(sb, text); break;
                case JsonKind.Array:
                    if (list.Count == 0) { sb.Append("[]"); break; }
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); sb.Append(' ', (depth + 1) * 2); }
                        list[i].Write(sb, pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); sb.Append(' ', depth * 2); }
                    sb.Append(']');
                    break;
                default:
                    if (order.Count == 0) { sb.Append("{}"); break; }
                    sb.Append('{');
                    for (int i = 0; i < order.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        if (pretty) { sb.Append('\n'); sb.Append(' ', (depth + 1) * 2); }
                        WriteString(sb, order[i]);
                        sb.Append(pretty ? ": " : ":");
                        map[order[i]].Write(sb, pretty, depth + 1);
                    }
                    if (pretty) { sb.Append('\n'); sb.Append(' ', depth * 2); }
                    sb.Append('}');
                    break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        bool lone = char.IsHighSurrogate(c) ? !(i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) : char.IsLowSurrogate(c) && !(i > 0 && char.IsHighSurrogate(s[i - 1]));
                        if (c < 0x20 || lone) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ------------------------------------------------------------------ reading
        /// <summary>Reads one JSON value. Strict: no comments, no trailing commas, no NaN, no leading zeros, nothing after the value.
        /// A field written twice keeps its first place and its last value. A leading byte-order mark is skipped.</summary>
        public static JsonItem Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            var reader = new Reader(json);
            return reader.ReadDocument();
        }

        public static bool TryParse(string json, out JsonItem item)
        {
            try { item = Parse(json); return true; }
            catch (JsonSyntaxException) { item = null; return false; }
        }

        /// <summary>The JSON grammar of a number, so a number never carries text that would break the file.</summary>
        public static bool IsNumberText(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            int i = 0;
            if (s[i] == '-') i++;
            if (i >= s.Length) return false;
            if (s[i] == '0') i++;
            else if (s[i] >= '1' && s[i] <= '9') { while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
            else return false;
            if (i < s.Length && s[i] == '.')
            {
                i++;
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                if (digits == 0) return false;
            }
            if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
            {
                i++;
                if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                int digits = 0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                if (digits == 0) return false;
            }
            return i == s.Length;
        }

        /// <summary>A number made from text already written the JSON way (null when the text is not a JSON number).</summary>
        public static JsonItem FromNumberText(string s) => IsNumberText(s) ? new JsonItem(JsonKind.Number, s) : null;

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string s) { this.s = s; i = s.Length > 0 && s[0] == '﻿' ? 1 : 0; }

            JsonSyntaxException Fail(string message)
            {
                int line = 1, column = 1;
                for (int k = 0; k < i && k < s.Length; k++) { if (s[k] == '\n') { line++; column = 1; } else column++; }
                return new JsonSyntaxException(message, line, column);
            }

            void SkipSpace() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }

            public JsonItem ReadDocument()
            {
                SkipSpace();
                if (i >= s.Length) throw Fail("the document is empty");
                var item = ReadValue(0);
                SkipSpace();
                if (i < s.Length) throw Fail("unexpected text after the value");
                return item;
            }

            JsonItem ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Fail("nested deeper than " + MaxDepth + " levels");
                SkipSpace();
                if (i >= s.Length) throw Fail("the value is missing");
                char c = s[i];
                if (c == '{') return ReadObject(depth);
                if (c == '[') return ReadArray(depth);
                if (c == '"') return new JsonItem(JsonKind.String, ReadString());
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Literal("true")) return TrueItem;
                if (Literal("false")) return FalseItem;
                if (Literal("null")) return NullItem;
                throw Fail("unexpected character '" + c + "'");
            }

            bool Literal(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            JsonItem ReadObject(int depth)
            {
                var obj = Object();
                i++;                                        // {
                SkipSpace();
                if (i < s.Length && s[i] == '}') { i++; return obj; }
                while (true)
                {
                    SkipSpace();
                    if (i >= s.Length || s[i] != '"') throw Fail("a field name in quotes was expected");
                    string key = ReadString();
                    if (key.Length == 0) throw Fail("a field needs a name");
                    SkipSpace();
                    if (i >= s.Length || s[i] != ':') throw Fail("':' was expected after the field name");
                    i++;
                    var value = ReadValue(depth + 1);
                    obj.Set(key, value);
                    SkipSpace();
                    if (i >= s.Length) throw Fail("the object is not closed");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return obj; }
                    throw Fail("',' or '}' was expected");
                }
            }

            JsonItem ReadArray(int depth)
            {
                var arr = Array();
                i++;                                        // [
                SkipSpace();
                if (i < s.Length && s[i] == ']') { i++; return arr; }
                while (true)
                {
                    arr.Add(ReadValue(depth + 1));
                    SkipSpace();
                    if (i >= s.Length) throw Fail("the array is not closed");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return arr; }
                    throw Fail("',' or ']' was expected");
                }
            }

            JsonItem ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                if (i >= s.Length) throw Fail("a digit was expected");
                if (s[i] == '0') { i++; if (i < s.Length && s[i] >= '0' && s[i] <= '9') throw Fail("a number cannot start with 0"); }
                else if (s[i] >= '1' && s[i] <= '9') { while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++; }
                else throw Fail("a digit was expected");
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    int digits = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                    if (digits == 0) throw Fail("a digit was expected after the decimal point");
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    int digits = 0;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { i++; digits++; }
                    if (digits == 0) throw Fail("a digit was expected in the exponent");
                }
                return new JsonItem(JsonKind.Number, s.Substring(start, i - start));
            }

            string ReadString()
            {
                i++;                                        // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Fail("the string is not closed");
                    char c = s[i];
                    if (c == '"') { i++; return sb.ToString(); }
                    if (c < 0x20) throw Fail("a control character must be escaped inside a string");
                    if (c != '\\') { sb.Append(c); i++; continue; }
                    i++;
                    if (i >= s.Length) throw Fail("the string is not closed");
                    char e = s[i];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                        {
                            if (i + 4 >= s.Length) { i = s.Length; throw Fail("an escape \\u needs four hex digits"); }
                            int code = 0;
                            for (int k = 1; k <= 4; k++)
                            {
                                int d = HexValue(s[i + k]);
                                if (d < 0) { i += k; throw Fail("an escape \\u needs four hex digits"); }
                                code = code * 16 + d;
                            }
                            sb.Append((char)code);
                            i += 4;
                            break;
                        }
                        default: throw Fail("unknown escape '\\" + e + "'");
                    }
                    i++;
                }
            }

            static int HexValue(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
        }
    }
}

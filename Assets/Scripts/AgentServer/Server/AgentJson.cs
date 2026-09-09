using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace YumJump.Agent
{
    /// <summary>
    /// Minimal JSON reader/writer. The project has no JSON dependency and JsonUtility cannot do
    /// dictionaries or ragged arrays, so the protocol carries its own - a few hundred lines is
    /// cheaper than a package dependency and keeps the wire format exactly as documented.
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind { Null, Bool, Number, String, Array, Object }

        public Kind ValueKind { get; private set; }
        private bool boolValue;
        private double numberValue;
        private string stringValue;
        private List<JsonValue> arrayValue;
        private Dictionary<string, JsonValue> objectValue;

        public static readonly JsonValue Null = new JsonValue { ValueKind = Kind.Null };

        public bool IsNull => ValueKind == Kind.Null;
        public bool AsBool => ValueKind == Kind.Bool ? boolValue : ValueKind == Kind.Number && numberValue != 0;
        public double AsDouble => ValueKind == Kind.Number ? numberValue : 0;
        public float AsFloat => (float)AsDouble;
        public int AsInt => ValueKind == Kind.Number ? (int)Math.Round(numberValue) : 0;
        public string AsString => ValueKind == Kind.String ? stringValue : null;
        public List<JsonValue> AsArray => arrayValue ?? new List<JsonValue>();

        public JsonValue this[string key]
        {
            get
            {
                if (ValueKind == Kind.Object && objectValue.TryGetValue(key, out JsonValue v)) return v;
                return Null;
            }
        }

        public JsonValue this[int index] =>
            ValueKind == Kind.Array && index >= 0 && index < arrayValue.Count ? arrayValue[index] : Null;

        public bool Has(string key) => ValueKind == Kind.Object && objectValue.ContainsKey(key);

        public int Count => ValueKind == Kind.Array ? arrayValue.Count : (ValueKind == Kind.Object ? objectValue.Count : 0);

        // ------------------------------------------------------------------ parsing

        public static JsonValue Parse(string text)
        {
            int i = 0;
            JsonValue v = ParseValue(text, ref i);
            return v;
        }

        private static JsonValue ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) return Null;

            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return new JsonValue { ValueKind = Kind.String, stringValue = ParseString(s, ref i) };
                case 't':
                    i += 4;
                    return new JsonValue { ValueKind = Kind.Bool, boolValue = true };
                case 'f':
                    i += 5;
                    return new JsonValue { ValueKind = Kind.Bool, boolValue = false };
                case 'n':
                    i += 4;
                    return Null;
                default: return ParseNumber(s, ref i);
            }
        }

        private static JsonValue ParseObject(string s, ref int i)
        {
            var result = new JsonValue { ValueKind = Kind.Object, objectValue = new Dictionary<string, JsonValue>() };
            i++; // {
            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; break; }

                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ':') i++;
                JsonValue value = ParseValue(s, ref i);
                result.objectValue[key] = value;

                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
            }
            return result;
        }

        private static JsonValue ParseArray(string s, ref int i)
        {
            var result = new JsonValue { ValueKind = Kind.Array, arrayValue = new List<JsonValue>() };
            i++; // [
            while (i < s.Length)
            {
                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; break; }

                result.arrayValue.Add(ParseValue(s, ref i));

                SkipWhitespace(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
            }
            return result;
        }

        private static string ParseString(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length || s[i] != '"') return string.Empty;
            i++;

            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }

                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 <= s.Length)
                        {
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(e); break;
                }
            }
            return sb.ToString();
        }

        private static JsonValue ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
                i++;

            double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d);
            return new JsonValue { ValueKind = Kind.Number, numberValue = d };
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }
    }

    /// <summary>Insertion-ordered object literal, so responses read the same way every time.</summary>
    public sealed class JsonObj : IEnumerable<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> fields = new List<KeyValuePair<string, object>>();

        public JsonObj Add(string key, object value)
        {
            fields.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => fields.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public static class Json
    {
        /// <summary>Decimals kept for world-space floats. 4 is ~0.1 mm at this scale.</summary>
        public const int FloatDigits = 4;

        public static string Write(object value)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, value);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null:
                    sb.Append("null");
                    return;
                case string s:
                    WriteString(sb, s);
                    return;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    return;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    return;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    return;
                case float f:
                    WriteFloat(sb, f);
                    return;
                case double d:
                    WriteFloat(sb, (float)d);
                    return;
                case Vector2 v2:
                    sb.Append('[');
                    WriteFloat(sb, v2.x);
                    sb.Append(',');
                    WriteFloat(sb, v2.y);
                    sb.Append(']');
                    return;
                case Vector3 v3:
                    sb.Append('[');
                    WriteFloat(sb, v3.x);
                    sb.Append(',');
                    WriteFloat(sb, v3.y);
                    sb.Append(']');
                    return;
                case JsonObj obj:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in obj)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case IDictionary dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (DictionaryEntry kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key.ToString());
                        sb.Append(':');
                        WriteValue(sb, kv.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                default:
                    WriteString(sb, v.ToString());
                    return;
            }
        }

        private static void WriteFloat(StringBuilder sb, float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) { sb.Append('0'); return; }
            float rounded = (float)Math.Round(f, FloatDigits);
            sb.Append(rounded.ToString("0.####", CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}

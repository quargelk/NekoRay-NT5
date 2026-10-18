using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NekoRayNT5.Services
{
    // .NET 2.0 has no JSON support, so this is a small writer and reader of our own.
    // Values: JsonObject, JsonArray, string, int, long, double, bool, null.

    internal sealed class JsonObject
    {
        private readonly List<KeyValuePair<string, object>> _items = new List<KeyValuePair<string, object>>();

        public JsonObject Add(string key, object value)
        {
            _items.Add(new KeyValuePair<string, object>(key, value));
            return this;
        }

        public IList<KeyValuePair<string, object>> Items
        {
            get { return _items; }
        }

        public object Get(string key)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Key == key)
                {
                    return _items[i].Value;
                }
            }
            return null;
        }

        // Text form of a scalar member, "" when it is missing or not a scalar.
        public string GetString(string key)
        {
            return Json.ScalarToString(Get(key));
        }
    }

    internal sealed class JsonArray : List<object>
    {
        public static JsonArray Of(params object[] values)
        {
            JsonArray a = new JsonArray();
            a.AddRange(values);
            return a;
        }
    }

    internal static class Json
    {
        public static string Serialize(object value)
        {
            StringBuilder sb = new StringBuilder();
            Write(sb, value, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        public static string ScalarToString(object value)
        {
            if (value is string)
            {
                return (string)value;
            }
            if (value is bool)
            {
                return (bool)value ? "true" : "false";
            }
            if (value is double)
            {
                return ((double)value).ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is int || value is long)
            {
                return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
            return "";
        }

        private static void Write(StringBuilder sb, object value, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
            }
            else if (value is string)
            {
                WriteString(sb, (string)value);
            }
            else if (value is bool || value is int || value is long || value is double)
            {
                sb.Append(ScalarToString(value));
            }
            else if (value is JsonObject)
            {
                IList<KeyValuePair<string, object>> items = ((JsonObject)value).Items;
                if (items.Count == 0)
                {
                    sb.Append("{}");
                    return;
                }
                sb.Append("{");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(i == 0 ? "\r\n" : ",\r\n").Append(' ', (depth + 1) * 2);
                    WriteString(sb, items[i].Key);
                    sb.Append(": ");
                    Write(sb, items[i].Value, depth + 1);
                }
                sb.Append("\r\n").Append(' ', depth * 2).Append("}");
            }
            else if (value is JsonArray)
            {
                JsonArray array = (JsonArray)value;
                if (array.Count == 0)
                {
                    sb.Append("[]");
                    return;
                }
                sb.Append("[");
                for (int i = 0; i < array.Count; i++)
                {
                    sb.Append(i == 0 ? "\r\n" : ",\r\n").Append(' ', (depth + 1) * 2);
                    Write(sb, array[i], depth + 1);
                }
                sb.Append("\r\n").Append(' ', depth * 2).Append("]");
            }
            else
            {
                throw new ArgumentException("Unsupported JSON value type: " + value.GetType().Name);
            }
        }

        private static void WriteString(StringBuilder sb, string s)
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
                    default:
                        if (c < 0x20 || c > 0x7E)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        // Throws FormatException on malformed input.
        public static object Parse(string text)
        {
            int pos = 0;
            object value = ReadValue(text, ref pos, 0);
            SkipSpace(text, ref pos);
            if (pos != text.Length)
            {
                throw new FormatException("Unexpected data after JSON value.");
            }
            return value;
        }

        private static void SkipSpace(string s, ref int pos)
        {
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == '\r' || s[pos] == '\n'))
            {
                pos++;
            }
        }

        private static object ReadValue(string s, ref int pos, int depth)
        {
            if (depth > 32)
            {
                throw new FormatException("JSON is nested too deeply.");
            }
            SkipSpace(s, ref pos);
            if (pos >= s.Length)
            {
                throw new FormatException("Unexpected end of JSON.");
            }
            char c = s[pos];
            if (c == '{')
            {
                pos++;
                JsonObject obj = new JsonObject();
                SkipSpace(s, ref pos);
                if (pos < s.Length && s[pos] == '}')
                {
                    pos++;
                    return obj;
                }
                while (true)
                {
                    SkipSpace(s, ref pos);
                    string key = ReadString(s, ref pos);
                    SkipSpace(s, ref pos);
                    Expect(s, ref pos, ':');
                    obj.Add(key, ReadValue(s, ref pos, depth + 1));
                    SkipSpace(s, ref pos);
                    if (pos < s.Length && s[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    Expect(s, ref pos, '}');
                    return obj;
                }
            }
            if (c == '[')
            {
                pos++;
                JsonArray array = new JsonArray();
                SkipSpace(s, ref pos);
                if (pos < s.Length && s[pos] == ']')
                {
                    pos++;
                    return array;
                }
                while (true)
                {
                    array.Add(ReadValue(s, ref pos, depth + 1));
                    SkipSpace(s, ref pos);
                    if (pos < s.Length && s[pos] == ',')
                    {
                        pos++;
                        continue;
                    }
                    Expect(s, ref pos, ']');
                    return array;
                }
            }
            if (c == '"')
            {
                return ReadString(s, ref pos);
            }
            if (Match(s, ref pos, "true"))
            {
                return true;
            }
            if (Match(s, ref pos, "false"))
            {
                return false;
            }
            if (Match(s, ref pos, "null"))
            {
                return null;
            }

            int start = pos;
            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0)
            {
                pos++;
            }
            double number;
            if (pos == start || !double.TryParse(s.Substring(start, pos - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out number))
            {
                throw new FormatException("Invalid JSON value.");
            }
            return number;
        }

        private static bool Match(string s, ref int pos, string word)
        {
            if (string.CompareOrdinal(s, pos, word, 0, word.Length) == 0)
            {
                pos += word.Length;
                return true;
            }
            return false;
        }

        private static void Expect(string s, ref int pos, char c)
        {
            if (pos >= s.Length || s[pos] != c)
            {
                throw new FormatException("Invalid JSON: '" + c + "' expected.");
            }
            pos++;
        }

        private static string ReadString(string s, ref int pos)
        {
            Expect(s, ref pos, '"');
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                if (pos >= s.Length)
                {
                    throw new FormatException("Unterminated JSON string.");
                }
                char c = s[pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (pos >= s.Length)
                {
                    throw new FormatException("Unterminated JSON string.");
                }
                char e = s[pos++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        int code;
                        if (pos + 4 > s.Length || !int.TryParse(s.Substring(pos, 4), NumberStyles.AllowHexSpecifier,
                                CultureInfo.InvariantCulture, out code))
                        {
                            throw new FormatException("Invalid \\u escape in JSON string.");
                        }
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }
    }
}

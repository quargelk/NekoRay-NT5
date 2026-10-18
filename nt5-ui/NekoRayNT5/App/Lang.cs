using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace NekoRayNT5.App
{
    // Interface language. The English text in the code is the key; other languages are tables embedded
    // in the program (Lang\ru.txt, an EmbeddedResource), so there are no satellite assemblies and the
    // program stays one file. A text without a translation is shown in English.
    //
    // Table format (UTF-8): an "en: " line with the English text, then a "ru: " line with the translation.
    // "\n" is a line break, "\t" a tab, "\\" a backslash; {0}, {1}... are values inserted by the program
    // and must stay in the translation. Lines starting with "#" and empty lines are ignored.
    internal static class Lang
    {
        public const string English = "en";
        public const string Russian = "ru";

        // Codes and names as shown in Settings (each in its own language).
        public static readonly string[] Codes = new string[] { English, Russian };
        public static readonly string[] Names = new string[] { "English", "\u0420\u0443\u0441\u0441\u043a\u0438\u0439" };

        private const string ResourcePrefix = "NekoRayNT5.Lang.";

        private static Dictionary<string, string> _table;
        private static string _current = English;

        public static string Current
        {
            get { return _current; }
        }

        // Switches the language; an unknown code or a missing table means English. Returns a problem
        // with the table (for the log), or null.
        public static string Set(string code)
        {
            _table = null;
            _current = English;
            if (code != Russian)
            {
                return null;
            }
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourcePrefix + code + ".txt"))
                {
                    if (s == null)
                    {
                        return "the translation table \"" + code + "\" is not in the program.";
                    }
                    List<string> problems = new List<string>();
                    _table = Parse(new StreamReader(s, new UTF8Encoding(false, true)), problems);
                    _current = code;
                    return problems.Count > 0 ? problems[0] + (problems.Count > 1 ? " (+" + (problems.Count - 1) + " more)" : "") : null;
                }
            }
            catch (Exception ex)
            {
                _table = null;
                _current = English;
                return "the translation table \"" + code + "\" cannot be read: " + ex.Message;
            }
        }

        // The text in the current language.
        public static string T(string english)
        {
            string text;
            if (_table != null && english != null && _table.TryGetValue(english, out text))
            {
                return text;
            }
            return english;
        }

        // The text in the current language with the values put in ({0}, {1}...).
        public static string F(string english, params object[] args)
        {
            string format = T(english);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, format, args);
            }
            catch (FormatException)
            {
                // a broken translation must not break the program: fall back to English
                return string.Format(CultureInfo.InvariantCulture, english, args);
            }
        }

        // Reads a table; problems receives "line N: ..." for entries that were skipped.
        public static Dictionary<string, string> Parse(TextReader reader, List<string> problems)
        {
            Dictionary<string, string> table = new Dictionary<string, string>();
            string key = null;
            int keyLine = 0;
            int number = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }
                if (line.StartsWith("en: "))
                {
                    if (key != null)
                    {
                        problems.Add("line " + keyLine + ": no translation");
                    }
                    key = Unescape(line.Substring(4));
                    keyLine = number;
                }
                else if (line.StartsWith("ru: ") && key != null)
                {
                    string value = Unescape(line.Substring(4));
                    if (table.ContainsKey(key))
                    {
                        problems.Add("line " + keyLine + ": the same English text twice");
                    }
                    else if (!SamePlaceholders(key, value))
                    {
                        problems.Add("line " + number + ": the values {0}, {1}... differ from the English text");
                    }
                    else
                    {
                        table.Add(key, value);
                    }
                    key = null;
                }
                else
                {
                    problems.Add("line " + number + ": not understood");
                }
            }
            if (key != null)
            {
                problems.Add("line " + keyLine + ": no translation");
            }
            return table;
        }

        public static string Unescape(string text)
        {
            if (text.IndexOf('\\') < 0)
            {
                return text;
            }
            StringBuilder sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\\' && i + 1 < text.Length)
                {
                    char n = text[++i];
                    sb.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                }
                else
                {
                    sb.Append(c);
                }
            }
            // the code uses CR LF for line breaks shown in Windows controls
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        public static string Escape(string text)
        {
            return text.Replace("\\", "\\\\").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        // The placeholders {0}, {1}... used in both texts must be the same set.
        public static bool SamePlaceholders(string a, string b)
        {
            return Placeholders(a) == Placeholders(b);
        }

        private static string Placeholders(string text)
        {
            List<string> found = new List<string>();
            for (int i = 0; i + 2 < text.Length; i++)
            {
                if (text[i] == '{' && char.IsDigit(text[i + 1]))
                {
                    int end = text.IndexOf('}', i);
                    if (end > i)
                    {
                        string p = text.Substring(i, end - i + 1);
                        int colon = p.IndexOf(':');
                        if (colon > 0)
                        {
                            p = p.Substring(0, colon) + "}";
                        }
                        if (!found.Contains(p))
                        {
                            found.Add(p);
                        }
                    }
                }
            }
            found.Sort(StringComparer.Ordinal);
            return string.Join(",", found.ToArray());
        }
    }
}

// Checks of the interface language (0.9.6): the Russian table that is embedded in the program, its
// coverage of every text of the program, placeholders, fallback to English and the saved choice.
//
// Coverage: the sources (NT5_SRC, set by run-tests.cmd) are scanned for Lang.T(...) and Lang.F(...).
// The first argument is read as C# tokens: string literals joined by "+" form one key, and each branch
// of "a ? b : c" is a key of its own, so concatenated texts and conditional variants are checked too.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NekoRayNT5.App;
using NekoRayNT5.Model;
using NekoRayNT5.Services;

internal static partial class SelfTest
{
    private static void TestTranslation()
    {
        Section("Interface language: Russian table, coverage, placeholders, fallback");
        try
        {
            Check("English is the default", Lang.Current == Lang.English && new AppSettings().Language == Lang.English);
            string problem = Lang.Set(Lang.Russian);
            Check("the Russian table is embedded and reads without problems" + (problem == null ? "" : " (" + problem + ")"),
                problem == null && Lang.Current == Lang.Russian);
            Check("Russian text (Settings, Connect)", Lang.T("Settings") == "\u041d\u0430\u0441\u0442\u0440\u043e\u0439\u043a\u0438"
                && Lang.T("Connect") == "\u041f\u043e\u0434\u043a\u043b\u044e\u0447\u0438\u0442\u044c");
            Check("values are put into the translation", Lang.F("Servers found: {0}", 7).IndexOf("7") >= 0
                && Lang.F("Servers found: {0}", 7) != "Servers found: 7");
            Check("a text without a translation stays English", Lang.T("synthetic text that is not translated") == "synthetic text that is not translated");
            Check("line breaks become CR LF", Lang.T("The core cannot run on this system:\r\n\r\n{0}").IndexOf("\r\n\r\n") > 0);

            List<string> problems = new List<string>();
            Dictionary<string, string> table = Lang.Parse(new StringReader(
                "# c\nen: A {0}\nru: B {0}\nen: A {0}\nru: C {0}\nen: D {0}\nru: E {1}\nen: F\nbroken\n"), problems);
            Check("table check: duplicate, placeholder mismatch, missing translation and junk are reported", problems.Count == 4
                && table.Count == 1 && table["A {0}"] == "B {0}");
            Check("placeholder comparison", Lang.SamePlaceholders("a {0} {1}", "{1} b {0}") && !Lang.SamePlaceholders("a {0}", "a {0} {1}")
                && Lang.SamePlaceholders("{0:0.0}", "{0}"));

            string src = Environment.GetEnvironmentVariable("NT5_SRC");
            if (src == null || !Directory.Exists(src))
            {
                _skipped++;
                Console.WriteLine("SKIPPED  translation coverage: NT5_SRC is not set (run tests\\run-tests.cmd)");
            }
            else
            {
                Dictionary<string, string> ru;
                using (StreamReader r = new StreamReader(Path.Combine(src, "Lang\\ru.txt"), new UTF8Encoding(false, true)))
                {
                    problems.Clear();
                    ru = Lang.Parse(r, problems);
                }
                Check("Lang\\ru.txt: no duplicates, placeholder mismatches or broken lines", problems.Count == 0);
                List<string> errors = new List<string>();
                Dictionary<string, string> used = new Dictionary<string, string>();
                foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
                {
                    if (file.IndexOf("\\obj\\") >= 0 || file.IndexOf("\\bin\\") >= 0)
                    {
                        continue;
                    }
                    ExtractKeys(file, used, errors);
                }
                List<string> missing = new List<string>();
                foreach (string key in used.Keys)
                {
                    if (!ru.ContainsKey(key))
                    {
                        missing.Add(used[key] + ": " + key);
                    }
                }
                List<string> unused = new List<string>();
                foreach (string key in ru.Keys)
                {
                    if (!used.ContainsKey(key))
                    {
                        unused.Add(key);
                    }
                }
                foreach (string e in errors) { Console.WriteLine("      " + e); }
                for (int i = 0; i < missing.Count && i < 15; i++) { Console.WriteLine("      missing: " + missing[i]); }
                for (int i = 0; i < unused.Count && i < 15; i++) { Console.WriteLine("      unused: " + unused[i]); }
                Console.WriteLine("      texts of the program: " + used.Count + ", translations: " + ru.Count);
                Check("every Lang.T/Lang.F text is a constant (no variables inside the key)", errors.Count == 0);
                Check("every text of the program has a Russian translation", missing.Count == 0 && used.Count > 500);
                Check("no translation without a text in the program", unused.Count == 0);
                int same = 0;
                foreach (KeyValuePair<string, string> pair in ru)
                {
                    if (pair.Key == pair.Value && pair.Key.Length > 12)
                    {
                        same++;
                    }
                }
                Check("no long text is left untranslated (copied English)", same == 0);
            }

            // the saved choice
            string dir = Path.Combine(_work, "lang");
            Directory.CreateDirectory(dir);
            string file2 = Path.Combine(dir, "settings.xml");
            SettingsStore store = new SettingsStore(file2);
            AppSettings s = new AppSettings();
            s.Language = Lang.Russian;
            store.Save(s);
            string warning;
            Check("the language is saved and read back", store.Load(out warning).Language == Lang.Russian && warning == null);
            File.WriteAllText(file2, File.ReadAllText(file2).Replace("<language>ru</language>", "<language>xx</language>"));
            Check("an unknown language in the settings means English", store.Load(out warning).Language == Lang.English);
            Check("an unknown language code switches to English", Lang.Set("xx") == null && Lang.Current == Lang.English
                && Lang.T("Settings") == "Settings");
        }
        finally
        {
            Lang.Set(Lang.English);
        }
    }

    // ---- key extraction (C# tokens) ----

    private sealed class SrcToken
    {
        public char Kind; // 's' string, 'i' identifier, 'o' operator
        public string Text;
        public int Line;
    }

    private static List<SrcToken> Tokenize(string src)
    {
        List<SrcToken> list = new List<SrcToken>();
        int i = 0;
        int line = 1;
        while (i < src.Length)
        {
            char c = src[i];
            if (c == '\n') { line++; i++; continue; }
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '/')
            {
                while (i < src.Length && src[i] != '\n') { i++; }
                continue;
            }
            if (c == '/' && i + 1 < src.Length && src[i + 1] == '*')
            {
                int end = src.IndexOf("*/", i + 2);
                end = end < 0 ? src.Length : end + 2;
                for (int k = i; k < end; k++) { if (src[k] == '\n') { line++; } }
                i = end;
                continue;
            }
            SrcToken t = new SrcToken();
            t.Line = line;
            if (c == '@' && i + 1 < src.Length && src[i + 1] == '"')
            {
                StringBuilder sb = new StringBuilder();
                int j = i + 2;
                while (j < src.Length)
                {
                    if (src[j] == '"' && j + 1 < src.Length && src[j + 1] == '"') { sb.Append('"'); j += 2; continue; }
                    if (src[j] == '"') { break; }
                    if (src[j] == '\n') { line++; }
                    sb.Append(src[j]);
                    j++;
                }
                t.Kind = 's';
                t.Text = sb.ToString();
                i = j + 1;
            }
            else if (c == '"')
            {
                StringBuilder sb = new StringBuilder();
                int j = i + 1;
                while (j < src.Length && src[j] != '"')
                {
                    if (src[j] == '\\')
                    {
                        char e = src[j + 1];
                        if (e == 'u')
                        {
                            sb.Append((char)Convert.ToInt32(src.Substring(j + 2, 4), 16));
                            j += 6;
                            continue;
                        }
                        sb.Append(e == 'n' ? '\n' : e == 'r' ? '\r' : e == 't' ? '\t' : e == '0' ? '\0' : e);
                        j += 2;
                        continue;
                    }
                    sb.Append(src[j]);
                    j++;
                }
                t.Kind = 's';
                t.Text = sb.ToString();
                i = j + 1;
            }
            else if (c == '\'')
            {
                int j = i + 1;
                if (src[j] == '\\') { j += 2; }
                else { j++; }
                while (src[j] != '\'') { j++; }
                t.Kind = 'o';
                t.Text = "'";
                i = j + 1;
            }
            else if (char.IsLetterOrDigit(c) || c == '_')
            {
                int j = i;
                while (j < src.Length && (char.IsLetterOrDigit(src[j]) || src[j] == '_')) { j++; }
                t.Kind = 'i';
                t.Text = src.Substring(i, j - i);
                i = j;
            }
            else
            {
                t.Kind = 'o';
                t.Text = c.ToString();
                i++;
            }
            list.Add(t);
        }
        return list;
    }

    private static void ExtractKeys(string file, Dictionary<string, string> used, List<string> errors)
    {
        string name = Path.GetFileName(file);
        List<SrcToken> t = Tokenize(File.ReadAllText(file));
        for (int k = 0; k + 3 < t.Count; k++)
        {
            if (!(t[k].Kind == 'i' && t[k].Text == "Lang" && t[k + 1].Text == "." && (t[k + 2].Text == "T" || t[k + 2].Text == "F")
                  && t[k + 3].Text == "("))
            {
                continue;
            }
            int depth = 0;
            List<SrcToken> arg = new List<SrcToken>();
            for (int j = k + 4; j < t.Count; j++)
            {
                string x = t[j].Kind == 'o' ? t[j].Text : null;
                if (x == "(" || x == "[" || x == "{") { depth++; }
                else if (x == ")" || x == "]" || x == "}") { if (depth == 0) { break; } depth--; }
                else if (x == "," && depth == 0) { break; }
                arg.Add(t[j]);
            }
            StringBuilder current = null;
            string prev = null;
            int found = 0;
            for (int j = 0; j < arg.Count; j++)
            {
                SrcToken a = arg[j];
                if (a.Kind == 's')
                {
                    if (current != null && prev == "+")
                    {
                        current.Append(a.Text);
                    }
                    else
                    {
                        if (current != null) { AddKey(used, current.ToString(), name, a.Line); found++; }
                        current = new StringBuilder(a.Text);
                    }
                    prev = "s";
                }
                else if (a.Kind == 'o' && a.Text == "+")
                {
                    if (prev != "s" || j + 1 >= arg.Count || arg[j + 1].Kind != 's')
                    {
                        errors.Add(name + ":" + a.Line + ": the text of Lang." + t[k + 2].Text + " is not constant");
                    }
                    prev = "+";
                }
                else
                {
                    if (current != null) { AddKey(used, current.ToString(), name, a.Line); found++; current = null; }
                    prev = a.Text;
                }
            }
            if (current != null) { AddKey(used, current.ToString(), name, t[k].Line); found++; }
            if (found == 0)
            {
                errors.Add(name + ":" + t[k].Line + ": Lang." + t[k + 2].Text + " without a text");
            }
        }
    }

    private static void AddKey(Dictionary<string, string> used, string key, string file, int line)
    {
        // the table uses CR LF for every line break
        string normal = key.Replace("\r\n", "\n").Replace("\n", "\r\n");
        if (!used.ContainsKey(normal))
        {
            used.Add(normal, file + ":" + line);
        }
    }
}

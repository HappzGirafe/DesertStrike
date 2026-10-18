using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// The game's languages: the website's six (English, Ukrainian, German, French, Italian, Spanish). The game is written
/// in English, and GameUI translates whatever it draws, so the rest of the code keeps writing English.
///
/// Resources/Localization/&lt;code&gt;.json maps English texts to the language's. A text is looked up whole first;
/// then line by line, and by its "  ·  " parts; then against the templates, the keys with {0}, {1}... in them, where
/// each {n} stands for anything (a name, a number) and is translated in turn. Whatever is not found stays English.
/// The choice is saved; the first time, the system's language is used if the game has it.
/// </summary>
public static class Loc
{
    public static readonly (string Code, string Name)[] Languages =
    {
        ("en", "English"), ("ua", "Українська"), ("de", "Deutsch"), ("fr", "Français"), ("it", "Italiano"), ("es", "Español"),
    };

    const string LanguageKey = "DesertStrike.language";
    const string Separator = "  ·  ";

    public static string Language { get; private set; } = "en";
    public static bool English => Language == "en";

    static bool ready;
    static readonly Dictionary<string, string> exact = new Dictionary<string, string>();
    static readonly List<(Regex Pattern, string Translation)> templates = new List<(Regex, string)>();
    static readonly Dictionary<string, string> cache = new Dictionary<string, string>();

    /// <summary>The text in the chosen language.</summary>
    public static string T(string text)
    {
        if (!ready) Init();
        if (Language == "en" || string.IsNullOrEmpty(text)) return text;
        if (cache.TryGetValue(text, out string done)) return done;
        string result = Translate(text, 0);
        if (cache.Count > 4000) cache.Clear();   // texts with changing numbers in them pile up
        cache[text] = result;
        return result;
    }

    /// <summary>Switches language (saved), at once.</summary>
    public static void Set(string code)
    {
        if (!ready) Init();
        Load(code);
        PlayerPrefs.SetString(LanguageKey, Language);
        Debug.Log("[Loc] Language: " + Language);
    }

    static void Init()
    {
        ready = true;
        string code = PlayerPrefs.HasKey(LanguageKey) ? PlayerPrefs.GetString(LanguageKey) : FromSystem();
        var args = Environment.GetCommandLineArgs();
        int at = Array.IndexOf(args, "-ds-language");   // -ds-language <en|ua|de|fr|it|es>: this run only (tests)
        if (at >= 0 && at + 1 < args.Length) code = args[at + 1];
        Load(code);
    }

    static string FromSystem()
    {
        switch (Application.systemLanguage)
        {
            case SystemLanguage.Ukrainian: return "ua";
            case SystemLanguage.German: return "de";
            case SystemLanguage.French: return "fr";
            case SystemLanguage.Italian: return "it";
            case SystemLanguage.Spanish: return "es";
            default: return "en";
        }
    }

    static void Load(string code)
    {
        exact.Clear();
        templates.Clear();
        cache.Clear();
        Language = Array.Exists(Languages, l => l.Code == code) ? code : "en";
        if (Language == "en") return;
        var file = Resources.Load<TextAsset>("Localization/" + Language);
        if (file == null)
        {
            Debug.LogWarning("[Loc] No translation file for " + Language);
            Language = "en";
            return;
        }
        Dictionary<string, string> entries;
        try
        {
            entries = ParseObject(file.text);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Loc] {Language}.json is broken: {e.Message}");
            Language = "en";
            return;
        }
        var placeholder = new Regex(@"\{(\d)\}");
        var withLength = new List<(Regex, string, int)>();
        foreach (var entry in entries)
        {
            if (entry.Key.StartsWith("_") || entry.Value.Length == 0) continue;   // "_about": notes for translators
            if (!placeholder.IsMatch(entry.Key))
            {
                exact[entry.Key] = entry.Value;
                continue;
            }
            // "Hold {0} to plant the bomb on {1}" -> ^Hold\ (?<p0>.*)\ to\ plant\ the\ bomb\ on\ (?<p1>.*?)$: the first
            // part takes all it can, so "Bob  (owner)  (you)" is "{0}  ({1})" with {0} "Bob  (owner)", and that again.
            var pattern = new StringBuilder("^");
            int last = 0;
            foreach (Match m in placeholder.Matches(entry.Key))
            {
                pattern.Append(Regex.Escape(entry.Key.Substring(last, m.Index - last)));
                pattern.Append($"(?<p{m.Groups[1].Value}>.*{(last == 0 ? "" : "?")})");
                last = m.Index + m.Length;
            }
            pattern.Append(Regex.Escape(entry.Key.Substring(last))).Append('$');
            int literal = placeholder.Replace(entry.Key, "").Length;
            withLength.Add((new Regex(pattern.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant), entry.Value, literal));
        }
        // The most specific first: "{0} players online" before "{0} online".
        withLength.Sort((a, b) => b.Item3.CompareTo(a.Item3));
        foreach (var (pattern, translation, _) in withLength) templates.Add((pattern, translation));
        Debug.Log($"[Loc] {Language}: {exact.Count} texts, {templates.Count} templates");
    }

    static string Translate(string text, int depth)
    {
        if (exact.TryGetValue(text, out string found)) return found;
        if (depth > 4 || text.Trim().Length == 0) return text;
        string trimmed = text.Trim();
        if (trimmed.Length != text.Length)
            return text.Substring(0, text.IndexOf(trimmed, StringComparison.Ordinal)) + Translate(trimmed, depth + 1)
                 + text.Substring(text.IndexOf(trimmed, StringComparison.Ordinal) + trimmed.Length);
        if (text.IndexOf('\n') >= 0)
            return string.Join("\n", Array.ConvertAll(text.Split('\n'), line => Translate(line, depth + 1)));
        if (text.Contains(Separator))
            return string.Join(Separator, Array.ConvertAll(text.Split(new[] { Separator }, StringSplitOptions.None), part => Translate(part, depth + 1)));
        foreach (var (pattern, translation) in templates)
        {
            var m = pattern.Match(text);
            if (!m.Success) continue;
            var result = new StringBuilder(translation);
            for (int i = 0; i < 10; i++)
            {
                var group = m.Groups["p" + i];
                if (group.Success) result.Replace("{" + i + "}", Translate(group.Value, depth + 1));
            }
            return result.ToString();
        }
        return text;
    }

    // A flat JSON object of strings: { "English": "Translation", ... }.
    static Dictionary<string, string> ParseObject(string json)
    {
        var result = new Dictionary<string, string>();
        int i = 0;
        void Space()
        {
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        }
        void Expect(char c)
        {
            Space();
            if (i >= json.Length || json[i] != c) throw new FormatException($"'{c}' expected at {i}");
            i++;
        }
        string ReadString()
        {
            Expect('"');
            var text = new StringBuilder();
            while (i < json.Length && json[i] != '"')
            {
                char c = json[i++];
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }
                char e = json[i++];
                switch (e)
                {
                    case 'n': text.Append('\n'); break;
                    case 't': text.Append('\t'); break;
                    case 'r': text.Append('\r'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'u':
                        text.Append((char)Convert.ToInt32(json.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: text.Append(e); break;   // \" \\ \/
                }
            }
            Expect('"');
            return text.ToString();
        }

        if (json.Length > 0 && json[0] == '﻿') i = 1;
        Expect('{');
        Space();
        while (i < json.Length && json[i] != '}')
        {
            string key = ReadString();
            Expect(':');
            result[key] = ReadString();
            Space();
            if (i < json.Length && json[i] == ',') i++;
            Space();
        }
        return result;
    }
}

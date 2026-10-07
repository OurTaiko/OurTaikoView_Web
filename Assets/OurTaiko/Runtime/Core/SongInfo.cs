using System;
using System.Collections.Generic;
using System.Globalization;

namespace OurTaiko
{
    // Difficulty columns of the original song select (src/libs/global_data.h Difficulty);
    // Edit is the Ura course that shares the Oni column.
    public enum Difficulty { Back = -3, Modifier = -2, Neiro = -1, Easy = 0, Normal, Hard, Oni, Ura }

    public sealed class CourseInfo
    {
        public Difficulty Difficulty;
        public string Course; // The TJA's own COURSE value, passed back to TjaParser.Parse.
        public int Level;
        public bool IsBranching;
    }

    // Song-select metadata of a TJA file: the header and every course with a complete #START/#END.
    public sealed class SongInfo
    {
        public string Title = "Untitled", Subtitle = "", Genre = "";
        public double Bpm = 120, DemoStart;
        public readonly List<CourseInfo> Courses = new List<CourseInfo>();

        static readonly string[] CourseNames = { "Easy", "Normal", "Hard", "Oni", "Edit" };

        public static string CourseName(Difficulty difficulty) => CourseNames[(int)difficulty];

        public static Difficulty? DifficultyOf(string course)
        {
            string value = course.Trim().Split('_')[0];
            if (int.TryParse(value, out int n)) return n >= 0 && n < CourseNames.Length ? (Difficulty)n : (Difficulty?)null;
            if (value.Equals("Ura", StringComparison.OrdinalIgnoreCase)) return Difficulty.Ura;
            for (int i = 0; i < CourseNames.Length; i++)
                if (value.Equals(CourseNames[i], StringComparison.OrdinalIgnoreCase)) return (Difficulty)i;
            return null;
        }

        public CourseInfo Course(Difficulty difficulty) => Courses.Find(c => c.Difficulty == difficulty);
        public bool Has(Difficulty difficulty) => Course(difficulty) != null;

        public static SongInfo Read(string text, string language = null)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("The TJA chart is empty.");
            var info = new SongInfo();
            var titles = new Dictionary<string, string>();
            var subtitles = new Dictionary<string, string>();
            string course = "Oni";
            int level = 0;
            CourseInfo reading = null;
            foreach (string raw in text.TrimStart('﻿').Replace("\r", "").Split('\n'))
            {
                string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase))
                {
                    var difficulty = DifficultyOf(course);
                    reading = difficulty == null ? null : new CourseInfo { Difficulty = difficulty.Value, Course = course, Level = level };
                    continue;
                }
                if (line.Equals("#END", StringComparison.OrdinalIgnoreCase))
                {
                    // A later block of the same course replaces the earlier one, like the reference parser.
                    if (reading != null) { info.Courses.RemoveAll(c => c.Difficulty == reading.Difficulty); info.Courses.Add(reading); }
                    reading = null;
                    continue;
                }
                if (reading != null)
                {
                    if (line.StartsWith("#BRANCHSTART", StringComparison.OrdinalIgnoreCase)) reading.IsBranching = true;
                    continue;
                }
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpperInvariant(), value = line.Substring(colon + 1).Trim();
                switch (key)
                {
                    case "TITLE": info.Title = value; titles["en"] = value; break;
                    case "SUBTITLE": info.Subtitle = value.TrimStart('-', '+'); subtitles["en"] = info.Subtitle; break;
                    case "GENRE": info.Genre = value; break;
                    case "BPM": info.Bpm = Number(value, info.Bpm); break;
                    case "DEMOSTART": info.DemoStart = Number(value, 0); break;
                    case "COURSE": course = value; level = 0; break;
                    case "LEVEL": level = (int)Number(value, 0); break;
                    default:
                        bool subtitle = key.StartsWith("SUBTITLE", StringComparison.Ordinal);
                        string prefix = subtitle ? "SUBTITLE" : "TITLE";
                        if (!key.StartsWith(prefix, StringComparison.Ordinal)) break;
                        string locale = TitleLanguage(key.Substring(prefix.Length));
                        if (locale != null) (subtitle ? subtitles : titles)[locale] = subtitle ? value.TrimStart('-', '+') : value;
                        break;
                }
            }
            if (language != null)
            {
                info.Title = Translated(titles, language, info.Title);
                info.Subtitle = Translated(subtitles, language, info.Subtitle);
            }
            info.Courses.Sort((a, b) => a.Difficulty.CompareTo(b.Difficulty));
            return info;
        }

        // Display names: the chosen language's translation, else the chart's original TITLE/SUBTITLE.
        // Online metadata uses the same rule.
        internal static string Translated(Dictionary<string, string> values, string language, string original)
            => values.TryGetValue(language, out string text) && !string.IsNullOrWhiteSpace(text) ? text : original;

        static string TitleLanguage(string suffix) => suffix switch
        {
            "EN" => "en", "JA" or "JP" => "ja", "KO" => "ko",
            "ZH" or "CN" or "ZH_CN" or "ZH-CN" => "zh",
            "TW" or "ZH_TW" or "ZH-TW" => "zh_tw",
            _ => null,
        };

        static double Number(string value, double fallback)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : fallback;
    }
}

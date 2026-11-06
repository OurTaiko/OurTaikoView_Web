using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace OurTaiko
{
    // A song of a course. Local courses name the chart by its file's SHA-256 (title and subtitle as a
    // fallback) and are played as 1P reads them; a server's courses name the song's id on that
    // server and the block's side, and tell whether the block is still the one the course was set
    // up on (Available: valid and published).
    public sealed class DanSongRef
    {
        public string Sha256 = "", Id = "", Side = "", Title = "", Subtitle = "";
        public bool Available = true;
        public Difficulty Difficulty = Difficulty.Oni;
        // The arcade's ？？？: the title stays hidden on the select screen and the curtain.
        public bool Hidden;
    }

    // A 段位 as an .otdan.json file defines it (Documentation/DanDojo.md in OurTaikoLogs). Rewards
    // only matter to a server's courses and are kept as written.
    public sealed class DanCourse
    {
        public const int FormatVersion = 1, MaxSongs = 3;

        public string Id = "";
        // Null for a local course; otherwise the base URL of the server that hands it out.
        public string Server;
        public readonly Dictionary<string, string> Titles = new Dictionary<string, string>();
        // The Nijiiro dan box colour.
        public int Color;
        // A server's course: whether an administrator marked it official (the lists show only
        // those), and its six-character code (the 外伝 entry takes it).
        public bool Official;
        public string Code = "";
        public readonly List<DanSongRef> Songs = new List<DanSongRef>();
        public readonly List<DanExam> Exams = new List<DanExam>();
        public JToken Rewards;
        public readonly List<string> Warnings = new List<string>();

        public bool IsLocal => Server == null;

        // The rank a server's course names in rewards.dan (五級 6 .. 24 達人; 0..5 are not offered), or
        // -1. The result's 昇段 text shows it; nothing is granted here (the online stage does that).
        public int DanIndex => !IsLocal && Rewards is JObject rewards && rewards["dan"] is JValue { Type: JTokenType.Integer } dan
            && (long)dan >= PlayerInfo.DanFirst && (long)dan < PlayerInfo.DanCount ? (int)(long)dan : -1;

        public string Title(string language)
        {
            if (language != null && Titles.TryGetValue(language, out string t) && t.Length > 0) return t;
            if (Titles.TryGetValue("en", out t) && t.Length > 0) return t;
            return Titles.Values.FirstOrDefault(v => v.Length > 0) ?? Id;
        }

        // Throws FormatException naming what is wrong; tolerated oddities go to Warnings.
        public static DanCourse Parse(string json, string server = null)
        {
            JObject root;
            try
            {
                using var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None };
                root = JObject.Load(reader);
            }
            catch (JsonException error) { throw new FormatException("Not a JSON object: " + error.Message); }
            return From(root, server);
        }

        // Reads the JSON .otdan.json or a server's /dans holds. A value of the wrong JSON type is a
        // FormatException too, so a broken course is listed with its reason instead of failing the list.
        public static DanCourse From(JObject root, string server = null)
        {
            try { return Read(root, server); }
            catch (Exception error) when (error is InvalidCastException || error is OverflowException || error is ArgumentException)
            { throw new FormatException("A value has the wrong type: " + error.Message, error); }
        }

        static DanCourse Read(JObject root, string server)
        {
            if (root.Value<int?>("format") is int format && format != FormatVersion)
                throw new FormatException($"Unsupported format {format}.");
            var course = new DanCourse { Server = server, Id = Text(root, "id") };
            if (course.Id.Length == 0) throw new FormatException("Missing id.");

            switch (root["title"])
            {
                case JValue value when value.Type == JTokenType.String: course.Titles[""] = (string)value; break;
                case JObject titles:
                    foreach (var p in titles.Properties())
                        if (p.Value.Type == JTokenType.String) course.Titles[p.Name] = (string)p.Value;
                    break;
            }
            if (!course.Titles.Values.Any(t => t.Length > 0)) throw new FormatException("Missing title.");
            course.Color = root.Value<int?>("color") ?? 0;
            course.Official = root.Value<bool?>("official") ?? false;
            course.Code = Text(root, "code").ToUpperInvariant();

            if (!(root["songs"] is JArray songs) || songs.Count == 0) throw new FormatException("A course needs songs.");
            if (songs.Count > MaxSongs) throw new FormatException($"A course has at most {MaxSongs} songs, not {songs.Count}.");
            foreach (var entry in songs)
            {
                if (!(entry is JObject song) || !(song["chart"] is JObject chart)) throw new FormatException("A song needs a chart.");
                var reference = new DanSongRef
                {
                    Sha256 = Text(chart, "sha256").ToLowerInvariant(), Id = Text(chart, "id", numbers: true), Side = Text(chart, "side").ToLowerInvariant(),
                    Title = Text(song, "title"), Subtitle = Text(song, "subtitle"), Hidden = song.Value<bool?>("hidden") ?? false,
                    Available = song.Value<bool?>("available") ?? true,
                };
                string name = Text(chart, "course", numbers: true);
                var difficulty = name.Length == 0 ? Difficulty.Oni : SongInfo.DifficultyOf(name);
                if (!difficulty.HasValue || difficulty.Value < Difficulty.Easy) throw new FormatException($"Unknown course '{name}'.");
                reference.Difficulty = difficulty.Value;
                if (server == null ? reference.Sha256.Length == 0 && reference.Title.Length == 0 : reference.Id.Length == 0)
                    throw new FormatException(server == null ? "A local song needs a sha256 or a title." : "A server's song needs an id.");
                course.Songs.Add(reference);
            }

            if (root["exams"] is JArray exams)
                foreach (var entry in exams) course.Exams.Add(ParseExam(entry as JObject ?? throw new FormatException("An exam must be an object."), course.Songs.Count));
            if (course.Exams.Count == 0) course.Warnings.Add("No exams: the course cannot be passed.");

            course.Rewards = root["rewards"];
            if (course.IsLocal && course.Rewards != null && course.Rewards.Type != JTokenType.Null)
                course.Warnings.Add("Local courses grant no rewards; 'rewards' is ignored.");
            return course;
        }

        static DanExam ParseExam(JObject entry, int songCount)
        {
            string type = Text(entry, "type").ToLowerInvariant();
            if (!DanExam.TypeNames.TryGetValue(type, out var examType)) throw new FormatException($"Unknown exam type '{type}'.");
            string range = Text(entry, "range").ToLowerInvariant();
            if (range != "more" && range != "less") throw new FormatException($"Exam range must be 'more' or 'less', not '{range}'.");
            var exam = new DanExam { Type = examType, Range = range == "less" ? DanExamRange.Less : DanExamRange.More };
            if (!(entry["value"] is JArray value) || value.Count == 0) throw new FormatException($"Exam '{type}' needs a value.");
            if (value.Any(v => v.Type == JTokenType.Array || v.Type == JTokenType.Null))
            {
                if (value.Count != songCount) throw new FormatException($"Exam '{type}' has {value.Count} per-song values for {songCount} songs.");
                // The soul gauge is the course's: one value for every song makes no sense.
                if (examType == DanExamType.Gauge) throw new FormatException("A gauge exam takes one [red, gold] for the course.");
                exam.Songs = value.Select(v => v.Type == JTokenType.Null ? (DanBorder?)null : Pair(exam, v as JArray, type)).ToArray();
                if (exam.Songs.All(s => s == null)) throw new FormatException($"Exam '{type}' has no border for any song.");
                exam.Course = exam.Songs.First(s => s != null).Value;
            }
            else exam.Course = Pair(exam, value, type);
            return exam;
        }

        static DanBorder Pair(DanExam exam, JArray pair, string type)
        {
            if (pair == null || pair.Count < 1 || pair.Count > 2 || pair.Any(v => v.Type != JTokenType.Integer))
                throw new FormatException($"Exam '{type}' borders must be [red] or [red, gold] integers.");
            int red = (int)pair[0];
            if (red < 0) throw new FormatException($"Exam '{type}' has a negative border.");
            // Nothing is under 0: a less exam on 0 would fail before the first note.
            if (red == 0 && exam.Range == DanExamRange.Less) throw new FormatException($"Exam '{type}' (less) needs a border of at least 1.");
            return exam.Sane(red, pair.Count > 1 ? (int)pair[1] : (int?)null);
        }

        // A string value, trimmed; with numbers, an integer is read as its digits (ids, course 0-4).
        static string Text(JObject o, string key, bool numbers = false)
        {
            var v = o[key];
            if (v?.Type == JTokenType.String) return ((string)v).Trim();
            return numbers && v?.Type == JTokenType.Integer ? v.ToString(Formatting.None) : "";
        }
    }
}

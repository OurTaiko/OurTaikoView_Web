using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // Thrown with OurTaikoPlayer's error codes (API_*, NETWORK_*, HTTP_<status>, DOWNLOAD_*), which
    // the login and loading screens show as they are.
    public class FanmadeException : Exception
    {
        public FanmadeException(string code, Exception inner = null) : base(code, inner) { }
    }

    public sealed class HttpStatusException : FanmadeException
    {
        public readonly int Status;
        public HttpStatusException(int status) : base("HTTP_" + status) { Status = status; }
    }

    public sealed class FanmadeDifficulty
    {
        // Course as the API names it: Easy..Edit, with _1p/_2p on a DOUBLE chart.
        public string Course = "", Player = "", Maker = "";
        public int Level;
        // The block has #BRANCHSTART (Fanmade migration 031).
        public bool Branching;
    }

    // A published chart as /api/v1/game/categories/{id}/charts and /api/v1/charts/{id} return it.
    public sealed class FanmadeChart
    {
        public static readonly string[] Courses = { "Easy", "Normal", "Hard", "Oni", "Edit" };

        public string Server = "", Id = "", Title = "", Subtitle = "", Maker = "";
        public string TjaHash = "", AudioHash = "", AudioName = "";
        public string Category = "", Genre = "";
        public bool IsSingle = true;
        public readonly Dictionary<string, string> Titles = new Dictionary<string, string>(), Subtitles = new Dictionary<string, string>();
        public double Bpm = 120, DemoStart;
        // One slot per Easy..Edit; a DOUBLE chart fills them per player through ForPlayer.
        public FanmadeDifficulty[] Difficulties = new FanmadeDifficulty[5];
        public readonly List<FanmadeDifficulty> Blocks = new List<FanmadeDifficulty>();

        public bool IsPlayable => Difficulties.Any(d => d != null);

        public static FanmadeChart From(JToken v, string server)
        {
            if (v["isSingle"]?.Type != JTokenType.Boolean) throw new FanmadeException("API_DIFFICULTY_INVALID");
            var c = new FanmadeChart
            {
                Server = server, IsSingle = (bool)v["isSingle"],
                Id = Json.Str(v, "id"), Title = Json.Str(v, "title"),
                Subtitle = Json.Str(v, "subtitle"), Maker = Json.Str(v, "maker"), TjaHash = Json.Str(v, "tjaHash"),
                AudioHash = Json.Str(v, "audioHash"), AudioName = Json.Str(v, "audioName"),
            };
            if (!Json.HexId(c.Id, 32) || !Json.HexId(c.TjaHash, 64) || !Json.HexId(c.AudioHash, 64))
                throw new FanmadeException("API_ID_INVALID");
            foreach (var (key, target) in new[] { ("titleTranslations", c.Titles), ("subtitleTranslations", c.Subtitles) })
            {
                if (!(v[key] is JObject translations)) throw new FanmadeException("API_TRANSLATIONS_INVALID");
                foreach (var pair in translations)
                    if (pair.Value.Type == JTokenType.String) target[pair.Key] = (string)pair.Value;
            }
            if (!Json.IsNumber(v["bpm"]) || !Json.IsNumber(v["demoStart"])) throw new FanmadeException("API_METADATA_INVALID");
            c.Bpm = (double)v["bpm"]; c.DemoStart = (double)v["demoStart"];
            if (!(v["difficulties"] is JArray difficulties)) throw new FanmadeException("API_DIFFICULTIES_INVALID");
            foreach (var d in difficulties)
            {
                string name = Json.Str(d, "course");
                string player = name.EndsWith("_1p") ? "P1" : name.EndsWith("_2p") ? "P2" : "";
                int slot = Array.IndexOf(Courses, player.Length > 0 ? name.Substring(0, name.Length - 3) : name);
                if (slot < 0) continue;  // Tower/Dan are not playable here.
                long level = Json.Number(d, "level");
                if (level > 100 || d["branching"]?.Type != JTokenType.Boolean) throw new FanmadeException("API_DIFFICULTY_INVALID");
                var difficulty = new FanmadeDifficulty
                {
                    Course = name, Level = (int)level, Player = player,
                    Maker = Json.Str(d, "maker"), Branching = (bool)d["branching"],
                };
                if (c.Blocks.Any(x => x.Course == name) || c.IsSingle != (player.Length == 0))
                    throw new FanmadeException("API_DIFFICULTY_INVALID");
                c.Blocks.Add(difficulty);
                c.Difficulties[slot] ??= difficulty;
            }
            return c;
        }

        public string SelectedPlayer = "";
        public FanmadeChart ForPlayer(string player)
        {
            var copy = (FanmadeChart)MemberwiseClone();
            copy.SelectedPlayer = player;
            copy.Difficulties = new FanmadeDifficulty[5];
            foreach (var d in Blocks)
            {
                if (d.Player != player) continue;
                string basic = d.Course.Split('_')[0];
                int slot = Array.IndexOf(Courses, basic);
                if (slot >= 0) copy.Difficulties[slot] = d;
            }
            return copy;
        }

        // Song-select metadata straight from the API; the chart itself is only downloaded to play.
        // Names use the chosen language's translation, else the chart's original title/subtitle.
        public SongInfo ToSongInfo(string language)
        {
            language = language == "zh-Hans" ? "zh" : language ?? "en";
            var info = new SongInfo
            {
                Title = SongInfo.Translated(Titles, language, Title) + (SelectedPlayer.Length > 0 ? " " + SelectedPlayer : ""),
                Subtitle = SongInfo.Translated(Subtitles, language, Subtitle).TrimStart('-', '+'),
                Genre = Genre, Bpm = Bpm, DemoStart = DemoStart,
            };
            for (int i = 0; i < Difficulties.Length; i++)
                if (Difficulties[i] != null)
                    info.Courses.Add(new CourseInfo
                    {
                        Difficulty = (Difficulty)i, Course = Difficulties[i].Course, Level = Difficulties[i].Level,
                        IsBranching = Difficulties[i].Branching,
                    });
            return info;
        }

        // The verified audio keeps its real container in the cache name; anything else is refused.
        public string CachedAudioName
        {
            get
            {
                string extension = System.IO.Path.GetExtension(AudioName).ToUpperInvariant();
                if (extension == ".MP3") return "audio.mp3";
                if (extension == ".OGG") return "audio.ogg";
                throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            }
        }

        // MAKER and the TITLE/SUBTITLE translations the playable copy uses.
        public string TitleHeaders()
        {
            var output = new System.Text.StringBuilder("MAKER:" + LineText(Maker) + "\n");
            output.Append("TITLE:").Append(LineText(Title)).Append("\nSUBTITLE:").Append(LineText(Subtitle)).Append("\n");
            foreach (var (key, values) in new[] { ("TITLE", Titles), ("SUBTITLE", Subtitles) })
                foreach (var pair in values.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (pair.Key != "en" && pair.Key != "ja" && pair.Key != "zh" && pair.Key != "ko") continue;
                    output.Append(key).Append(pair.Key.ToUpperInvariant()).Append(':').Append(LineText(pair.Value)).Append('\n');
                }
            return output.ToString();
        }

        internal static string LineText(string s) => (s ?? "").Replace('\n', ' ').Replace('\r', ' ').Replace('\0', ' ');
    }

    // One server category in bootstrap order, with its charts in API order. A chart listed in
    // several categories is the same FanmadeChart (by id) in each.
    public sealed class FanmadeCategory
    {
        public string Server = "", ServerName = "", Id = "", Title = "", Genre = "";
        public readonly List<string> ChartIds = new List<string>();
    }

    public sealed class FanmadeScore
    {
        public string Id = "", Song = "", Difficulty = "";
        public long Good, Ok, Bad, Score, Drumroll, MaxCombo, ClearStatus;

        public static FanmadeScore From(JToken v) => new FanmadeScore
        {
            Id = Json.Str(v, "id"), Song = Json.Str(v, "songId"), Difficulty = Json.Str(v, "difficulty"),
            Good = Json.Number(v, "good"), Ok = Json.Number(v, "ok"), Bad = Json.Number(v, "bad"), Score = Json.Number(v, "score"),
            Drumroll = Json.Number(v, "drumroll"), MaxCombo = Json.Number(v, "max_combo"),
            ClearStatus = Json.Number(v, "ClearStatus"),
        };
    }

    // Recorded input sent with every score (scoreReplayVersion 1).
    // Types: 0 left ka, 1 left don, 2 right don, 3 right ka; times are judged game time in ms.
    public sealed class PlayRecord
    {
        public const int MaxInputs = 100000;
        public const double MaxTimeMs = 86400000;
        public int AudioOffsetMs, VisualOffsetMs;
        public readonly List<(double Ms, int Type)> Inputs = new List<(double, int)>();

        public static int TypeOf(bool isKa, bool right) => isKa ? (right ? 3 : 0) : (right ? 2 : 1);

        // null (sent as JSON null) when the record is too long or holds an invalid event.
        public JToken ToJson()
        {
            if (Inputs.Count > MaxInputs) return JValue.CreateNull();
            var inputs = new JArray();
            foreach (var (ms, type) in Inputs)
            {
                if (double.IsNaN(ms) || double.IsInfinity(ms) || Math.Abs(ms) > MaxTimeMs || type < 0 || type > 3) return JValue.CreateNull();
                inputs.Add(new JArray(ms, type));
            }
            return new JObject
            {
                ["version"] = 1, ["audio_offset_ms"] = AudioOffsetMs, ["visual_offset_ms"] = VisualOffsetMs, ["inputs"] = inputs,
            };
        }
    }

    public sealed class FileProgress
    {
        public enum State { Waiting, Downloading, Verifying, Cached, Complete }
        public State Status;
        public long Received;
        public long Total;  // 0: the server sent no length.
        public FileProgress Clone() => (FileProgress)MemberwiseClone();
    }

    public sealed class DownloadProgress
    {
        public enum Stage { Checking, Files, Preparing, Ready }
        public Stage Step;
        public FileProgress Chart = new FileProgress(), Audio = new FileProgress();
        public DownloadProgress Clone() => new DownloadProgress { Step = Step, Chart = Chart.Clone(), Audio = Audio.Clone() };
    }

    static class Json
    {
        public static JObject Parse(string text)
        {
            try
            {
                using var reader = new Newtonsoft.Json.JsonTextReader(new System.IO.StringReader(text))
                    { DateParseHandling = Newtonsoft.Json.DateParseHandling.None };
                return JToken.ReadFrom(reader) as JObject ?? throw new FanmadeException("API_JSON_INVALID");
            }
            catch (Newtonsoft.Json.JsonException error) { throw new FanmadeException("API_JSON_INVALID", error); }
        }

        public static string Str(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.String) throw new FanmadeException("API_STRING_INVALID");
            return (string)value;
        }

        public static long Number(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value == null || value.Type != JTokenType.Integer || (long)value < 0) throw new FanmadeException("API_NUMBER_INVALID");
            return (long)value;
        }

        public static bool IsNumber(JToken v) => v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float);

        public static bool HexId(string s, int length) => s.Length == length && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}

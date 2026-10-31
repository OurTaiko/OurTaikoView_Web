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

    // One chart block: a course (Easy..Edit) on one side, as the arcade keeps a single, a 1P and a
    // 2P chart per course. The side is the block's #START: plain single, #START P1 p1, #START P2 p2.
    public sealed class FanmadeDifficulty
    {
        public string Course = "", Side = "single", Maker = "";
        // The block's id (chartSlotVersion 1).
        public long Id;
        public int Level;
        // The block has #BRANCHSTART (Fanmade migration 031).
        public bool Branching;
        // The server's content hash of the block as played ("v1:" + 64 hex, TJAParser): the block's
        // identity for local records and queued scores. Empty while the server has not computed it.
        public string ChartHash = "";
        // The key of the block's scores: Oni, Oni_1p, Oni_2p.
        public string Name => Course + (Side == "p1" ? "_1p" : Side == "p2" ? "_2p" : "");
    }

    // A published chart as /api/v1/game/categories/{id}/charts and /api/v1/charts/{id} return it.
    public sealed class FanmadeChart
    {
        public string Server = "", Id = "", Title = "", Subtitle = "", Maker = "";
        // The audio file's hash (the download check); the chart itself is identified by its blocks.
        public string AudioHash = "", AudioName = "";
        public string Category = "", Genre = "";
        public readonly Dictionary<string, string> Titles = new Dictionary<string, string>(), Subtitles = new Dictionary<string, string>();
        public double Bpm = 120, DemoStart, Offset;
        // MOVIEOFFSET in seconds (0 without one), and the cached background movie once PrepareAsync
        // has downloaded it (null when the chart has none, or it was not wanted or failed).
        public double MovieOffset;
        public string MoviePath;
        // The block the 1P seat reads for each of Easy..Edit; Block gives either seat's.
        public FanmadeDifficulty[] Difficulties = new FanmadeDifficulty[5];
        public readonly List<FanmadeDifficulty> Blocks = new List<FanmadeDifficulty>();

        public bool IsPlayable => Difficulties.Any(d => d != null);
        // Every block with its content hash: the same text for the same playable chart, whatever
        // the file or its headers (replaces the TJA file hash as the cache identity).
        public string BlocksKey => string.Join(",", Blocks.Select(b => b.Course + "/" + b.Side + "=" + b.ChartHash));

        public static FanmadeChart From(JToken v, string server)
        {
            var c = new FanmadeChart
            {
                Server = server,
                Id = Json.Id(v, "id"), Title = Json.Str(v, "title"),
                Subtitle = Json.Str(v, "subtitle"), Maker = Json.Str(v, "maker"),
                AudioHash = Json.Str(v, "audioHash"), AudioName = Json.Str(v, "audioName"),
            };
            if (!Json.ChartId(c.Id) || !Json.HexId(c.AudioHash, 64))
                throw new FanmadeException("API_ID_INVALID");
            foreach (var (key, target) in new[] { ("titleTranslations", c.Titles), ("subtitleTranslations", c.Subtitles) })
            {
                if (!(v[key] is JObject translations)) throw new FanmadeException("API_TRANSLATIONS_INVALID");
                foreach (var pair in translations)
                    if (pair.Value.Type == JTokenType.String) target[pair.Key] = (string)pair.Value;
            }
            if (!Json.IsNumber(v["bpm"]) || !Json.IsNumber(v["demoStart"]) || !Json.IsNumber(v["offset"]))
                throw new FanmadeException("API_METADATA_INVALID");
            c.Bpm = (double)v["bpm"]; c.DemoStart = (double)v["demoStart"]; c.Offset = (double)v["offset"];
            if (double.IsNaN(c.Offset) || double.IsInfinity(c.Offset)) throw new FanmadeException("API_METADATA_INVALID");
            // Optional: servers without background movies leave it out.
            if (Json.IsNumber(v["movieOffset"])) c.MovieOffset = (double)v["movieOffset"];
            if (double.IsNaN(c.MovieOffset) || double.IsInfinity(c.MovieOffset)) c.MovieOffset = 0;
            if (!(v["difficulties"] is JArray difficulties)) throw new FanmadeException("API_DIFFICULTIES_INVALID");
            foreach (var d in difficulties)
            {
                string name = Json.Str(d, "course"), side = Json.Str(d, "side");
                long id = Json.Number(d, "id");
                if (Array.IndexOf(ChartSide.All, side) < 0 || id <= 0) throw new FanmadeException("API_DIFFICULTY_INVALID");
                int slot = SongInfo.CourseNames.IndexOf(name);
                if (slot < 0) continue;  // Tower/Dan are not playable here.
                long level = Json.Number(d, "level");
                if (level > 100 || d["branching"]?.Type != JTokenType.Boolean) throw new FanmadeException("API_DIFFICULTY_INVALID");
                string chartHash = Json.Str(d, "chartHash");
                if (chartHash.Length > 0 && !ChartHashPattern.IsMatch(chartHash)) throw new FanmadeException("API_DIFFICULTY_INVALID");
                var difficulty = new FanmadeDifficulty
                {
                    Id = id, Course = name, Side = side, Level = (int)level,
                    Maker = Json.Str(d, "maker"), Branching = (bool)d["branching"], ChartHash = chartHash,
                };
                if (c.Blocks.Any(x => x.Course == name && x.Side == side)) throw new FanmadeException("API_DIFFICULTY_INVALID");
                c.Blocks.Add(difficulty);
            }
            for (int i = 0; i < c.Difficulties.Length; i++) c.Difficulties[i] = c.Block(i);
            return c;
        }

        // 32 hex digits on Fanmade, a positive whole number on OurTaiko Database.
        public static bool ValidId(string id) => Json.ChartId(id);
        static readonly System.Text.RegularExpressions.Regex ChartHashPattern = new System.Text.RegularExpressions.Regex("^v[0-9]+:[0-9a-f]{64}$");

        // The block on one side of a course; null when the course has none there.
        public FanmadeDifficulty BlockOn(int course, string side)
            => Blocks.Find(x => x.Course == SongInfo.CourseNames[course] && x.Side == side);

        // The block a seat ("p1" or "p2") plays on a course (0..4 for Easy..Edit), alone or with
        // two playing together (SeatBlocks); null when it has none.
        public FanmadeDifficulty Block(int course, string seat = "p1", bool together = false)
        {
            foreach (string side in SeatBlocks.Reads(seat, together))
            {
                var block = Blocks.Find(x => x.Course == SongInfo.CourseNames[course] && x.Side == side);
                if (block != null) return block;
            }
            return null;
        }

        // Song-select metadata straight from the API; the chart itself is only downloaded to play.
        // Names use the chosen language's translation, else the chart's original title/subtitle.
        public SongInfo ToSongInfo(string language)
        {
            language = language == "zh-Hans" ? "zh" : language ?? "en";
            // The server hands over the whole title dictionaries; the game picks by language (SongInfo).
            var info = new SongInfo { Language = language, Genre = Genre, Bpm = Bpm, DemoStart = DemoStart, Maker = (Maker ?? "").Trim() };
            info.Titles["origin"] = Title;
            info.Subtitles["origin"] = Subtitle;
            foreach (var pair in Titles) if (!string.IsNullOrWhiteSpace(pair.Value)) info.Titles[pair.Key] = pair.Value;
            foreach (var pair in Subtitles) if (!string.IsNullOrWhiteSpace(pair.Value)) info.Subtitles[pair.Key] = pair.Value;
            for (int i = 0; i < Difficulties.Length; i++)
                if (Difficulties[i] != null)
                {
                    var block = new TjaBlock
                    {
                        Difficulty = (Difficulty)i, Course = Difficulties[i].Course, Level = Difficulties[i].Level,
                        IsBranching = Difficulties[i].Branching,
                    };
                    info.Blocks.Add(block);
                }
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

    // One server category in catalog order, with its charts in API order. A chart listed in
    // several categories is the same FanmadeChart (by id) in each.
    public sealed class FanmadeCategory
    {
        public string Server = "", ServerName = "", Id = "", Title = "", Genre = "";
        public readonly List<string> ChartIds = new List<string>();
    }

    public sealed class FanmadeScore
    {
        // Difficulty is the played block's name (FanmadeDifficulty.Name).
        public string Id = "", Song = "", Difficulty = "";
        public long Good, Ok, Bad, Score, Drumroll, MaxCombo, ClearStatus;
        // False once the song's files were replaced after the play.
        public bool IsLatest = true;

        public static FanmadeScore From(JToken v) => new FanmadeScore
        {
            Id = Json.Str(v, "id"), Song = Json.Id(v, "songId"),
            Difficulty = new FanmadeDifficulty { Course = Json.Str(v, "course"), Side = Json.Str(v, "side") }.Name,
            IsLatest = v["isLatest"]?.Type != JTokenType.Boolean || (bool)v["isLatest"],
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
        // Only when the background movie is being fetched.
        public FileProgress Movie;
        public DownloadProgress Clone() => new DownloadProgress { Step = Step, Chart = Chart.Clone(), Audio = Audio.Clone(), Movie = Movie?.Clone() };
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

        // An ID a server gives as a string or as a whole number, as text.
        public static string Id(JToken v, string key)
        {
            var value = v is JObject o ? o[key] : null;
            if (value?.Type == JTokenType.Integer && (long)value > 0) return ((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return Str(v, key);
        }

        // Fanmade charts have 32 hex digits; OurTaiko Database charts are positive whole numbers.
        public static bool ChartId(string s) => HexId(s, 32)
            || (s.Length > 0 && s.Length <= 18 && s[0] != '0' && s.All(c => c >= '0' && c <= '9'));

        public static bool HexId(string s, int length) => s.Length == length && s.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
    }
}

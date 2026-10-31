using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OurTaiko.Online
{
    // fanmade.cpp playable_tja/to_utf8: the downloaded original stays byte-for-byte intact for the hash
    // check; the playable copy is UTF-8 and takes its titles and offset from the API. Every block the
    // API lists must be in the file (matched by COURSE and the #START player: plain single, P1, P2);
    // the copy keeps exactly those blocks with their #START player, so TjaParser and SongInfo pick
    // the seat's block by the same ChartSide rule FanmadeChart.Block gives the score's block id by.
    public static class PlayableTja
    {
        public static string ToUtf8(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException error) { throw new FanmadeException("TJA_ENCODING_INVALID", error); }
        }

        public static string Build(string utf8, FanmadeChart chart)
        {
            var output = new StringBuilder(chart.TitleHeaders() + "WAVE:" + chart.CachedAudioName + "\n");
            output.Append("OFFSET:").Append(chart.Offset.ToString("R", CultureInfo.InvariantCulture)).Append('\n');
            var globals = new List<string>();
            var headers = new List<string>();
            var body = new StringBuilder();
            bool inBlock = false, seenCourse = false;
            int block = -1;
            string course = "Oni";
            var wanted = new SortedDictionary<int, FanmadeDifficulty>();
            var found = new HashSet<int>();
            foreach (var tja in TjaLines.Read(utf8))
            {
                string line = tja.Text;
                if (tja.Kind == TjaLineKind.Start)
                {
                    inBlock = true; block++; body.Clear();
                    string side = ChartSide.OfStart(tja.Value);
                    var d = chart.Blocks.Find(x => x.Course == course && x.Side == side);
                    if (d != null)
                    {
                        if (wanted.ContainsValue(d)) throw new FanmadeException("TJA_BLOCK_MISMATCH");
                        wanted[block] = d;
                    }
                    continue;
                }
                if (tja.Kind == TjaLineKind.End)
                {
                    if (inBlock && wanted.TryGetValue(block, out var d))
                    {
                        found.Add(block);
                        output.Append("COURSE:").Append(d.Course).Append("\nLEVEL:").Append(d.Level).Append('\n');
                        foreach (string h in globals) if (KeptHeader(h)) output.Append(h).Append('\n');
                        foreach (string h in headers) if (KeptHeader(h)) output.Append(h).Append('\n');
                        output.Append(d.Side == "p1" ? "#START P1\n" : d.Side == "p2" ? "#START P2\n" : "#START\n").Append(body).Append("#END\n");
                    }
                    inBlock = false;
                    continue;
                }
                if (inBlock) { body.Append(line).Append('\n'); continue; }
                if (tja.Key == "COURSE")
                {
                    var difficulty = SongInfo.DifficultyOf(tja.Value);
                    course = difficulty.HasValue ? SongInfo.CourseNames[(int)difficulty.Value] : tja.Value;
                    seenCourse = true; headers.Clear(); continue;
                }
                (seenCourse ? headers : globals).Add(line);
            }
            if (found.Count != chart.Blocks.Count || inBlock) throw new FanmadeException("TJA_BLOCK_MISMATCH");
            return output.ToString();
        }

        static string Key(string line)
        {
            int colon = line.IndexOf(':');
            return (colon >= 0 ? line.Substring(0, colon) : line).Trim().ToUpperInvariant();
        }

        // API metadata replaces these headers; retain no original OFFSET, even inside course headers.
        static bool KeptHeader(string line)
        {
            string key = Key(line);
            return !key.StartsWith("TITLE", StringComparison.Ordinal) && !key.StartsWith("SUBTITLE", StringComparison.Ordinal)
                && key != "OFFSET" && key != "MAKER" && key != "WAVE" && key != "BGMOVIE" && key != "PREIMAGE" && key != "COURSE" && key != "LEVEL" && key != "STYLE";
        }
    }
}

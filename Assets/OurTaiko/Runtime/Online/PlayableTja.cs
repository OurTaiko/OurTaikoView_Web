using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

namespace OurTaiko.Online
{
    // fanmade.cpp playable_tja/to_utf8: the downloaded original stays byte-for-byte intact for the hash
    // check; the playable copy is UTF-8, keeps exactly the API's courses (matched by COURSE and the
    // #START player, so Oni_1p is the Oni block started with P1) and takes its titles from the API.
    public static class PlayableTja
    {
        public static string ToUtf8(byte[] bytes)
        {
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException error) { throw new FanmadeException("TJA_ENCODING_INVALID", error); }
        }

        public static string Build(string utf8, FanmadeChart chart)
        {
            if (utf8.StartsWith("﻿", StringComparison.Ordinal)) utf8 = utf8.Substring(1);
            var output = new StringBuilder(chart.TitleHeaders() + "WAVE:" + chart.CachedAudioName + "\n");
            var globals = new List<string>();
            var headers = new List<string>();
            var body = new StringBuilder();
            bool inBlock = false, seenCourse = false;
            int block = -1;
            string course = "Oni";
            var wanted = new SortedDictionary<int, FanmadeDifficulty>();
            var found = new HashSet<int>();
            foreach (string raw in utf8.Split('\n'))
            {
                int comment = raw.IndexOf("//", StringComparison.Ordinal);
                string line = Trim(comment >= 0 ? raw.Substring(0, comment) : raw);
                if (line.Length == 0) continue;
                string key = Key(line);
                if (line.StartsWith("#START", StringComparison.Ordinal))
                {
                    inBlock = true; block++; body.Clear();
                    string player = line.Substring(6).Trim().ToUpperInvariant();
                    string name = course + (player == "P1" ? "_1p" : player == "P2" ? "_2p" : "");
                    var d = chart.Blocks.Find(x => x.Course == name);
                    if (d != null)
                    {
                        if (wanted.Values.Any(x => x.Course == name)) throw new FanmadeException("TJA_BLOCK_MISMATCH");
                        wanted[block] = d;
                    }
                    continue;
                }
                if (line == "#END")
                {
                    if (inBlock && wanted.TryGetValue(block, out var d))
                    {
                        output.Append("COURSE:").Append(d.Course).Append("\nLEVEL:").Append(d.Level)
                            .Append("\nSTYLE:").Append(string.IsNullOrEmpty(d.Player) ? "Single" : "Double").Append('\n');
                        foreach (string h in globals) if (KeptHeader(h)) output.Append(h).Append('\n');
                        foreach (string h in headers) if (KeptHeader(h)) output.Append(h).Append('\n');
                        output.Append("#START").Append(string.IsNullOrEmpty(d.Player) ? "" : " " + d.Player).Append('\n')
                            .Append(body).Append("#END\n");
                        found.Add(block);
                    }
                    inBlock = false;
                    continue;
                }
                if (inBlock) { body.Append(line).Append('\n'); continue; }
                if (key == "COURSE")
                {
                    string value = line.Substring(line.IndexOf(':') + 1).Trim();
                    var difficulty = SongInfo.DifficultyOf(value);
                    course = difficulty.HasValue ? FanmadeChart.Courses[(int)difficulty.Value] : value;
                    seenCourse = true; headers.Clear(); continue;
                }
                (seenCourse ? headers : globals).Add(line);
            }
            if (found.Count != chart.Blocks.Count || inBlock) throw new FanmadeException("TJA_BLOCK_MISMATCH");
            return output.ToString();
        }

        static string Trim(string s) => s.Trim(' ', '\t', '\r', '\n');
        static string Key(string line)
        {
            int colon = line.IndexOf(':');
            return Trim(colon >= 0 ? line.Substring(0, colon) : line).ToUpperInvariant();
        }

        // Titles, audio, images/video and the course fields come from the API, not the original.
        static bool KeptHeader(string line)
        {
            string key = Key(line);
            return !key.StartsWith("TITLE", StringComparison.Ordinal) && !key.StartsWith("SUBTITLE", StringComparison.Ordinal)
                && key != "MAKER" && key != "WAVE" && key != "BGMOVIE" && key != "PREIMAGE" && key != "COURSE" && key != "LEVEL" && key != "STYLE";
        }
    }
}

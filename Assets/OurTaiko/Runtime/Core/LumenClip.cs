using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    // Reads the exported Nijiiro Scripts/anim/*.lua tables (kept verbatim as .txt TextAssets) and
    // samples them like anim/sampler.lua: rows are {frame, fields...}, linear between rows, clamped
    // to the first/last row. Frames are arcade frames at the table's own fps (60 for every table).
    public sealed class LumenClip
    {
        public static readonly LumenClip Empty = new LumenClip();

        readonly Dictionary<string, int> fieldIndex = new Dictionary<string, int>();
        readonly Dictionary<string, double[][]> tracks = new Dictionary<string, double[][]>();
        readonly Dictionary<string, double> labels = new Dictionary<string, double>();

        public double Fps { get; private set; } = 60;
        public double First { get; private set; }
        public double Last { get; private set; }
        public bool IsEmpty => tracks.Count == 0;

        static readonly Regex Fields = new Regex(@"fields\s*=\s*\{([^}]*)\}");
        static readonly Regex Range = new Regex(@"range\s*=\s*\{\s*([-\d.]+)\s*,\s*([-\d.]+)\s*\}");
        static readonly Regex FpsField = new Regex(@"\bfps\s*=\s*([\d.]+)");
        static readonly Regex Quoted = new Regex("\"([^\"]*)\"");
        static readonly Regex Entry = new Regex("\\[\"([^\"]+)\"\\]\\s*=\\s*");
        static readonly Regex Row = new Regex(@"\{\s*([-\d.eE+]+(?:\s*,\s*[-\d.eE+]+)*)\s*\}");

        public static LumenClip Parse(string text)
        {
            var clip = new LumenClip();
            if (string.IsNullOrEmpty(text)) return clip;
            // Comments may contain braces; drop them before reading the table.
            text = Regex.Replace(text, @"--[^\n]*", "");
            var fields = Fields.Match(text);
            if (!fields.Success) throw new FormatException("Animation table has no fields list.");
            int index = 0;
            foreach (Match name in Quoted.Matches(fields.Groups[1].Value)) clip.fieldIndex[name.Groups[1].Value] = index++;
            var range = Range.Match(text);
            if (range.Success) { clip.First = Number(range.Groups[1].Value); clip.Last = Number(range.Groups[2].Value); }
            var fps = FpsField.Match(text);
            if (fps.Success) clip.Fps = Number(fps.Groups[1].Value);

            int labelsAt = text.IndexOf("labels", StringComparison.Ordinal);
            int tracksAt = text.IndexOf("tracks", StringComparison.Ordinal);
            if (labelsAt >= 0)
            {
                string block = Block(text, text.IndexOf('{', labelsAt));
                foreach (Match label in new Regex("\\[\"([^\"]+)\"\\]\\s*=\\s*([-\\d.]+)").Matches(block))
                    clip.labels[label.Groups[1].Value] = Number(label.Groups[2].Value);
            }
            if (tracksAt < 0) throw new FormatException("Animation table has no tracks.");
            string all = Block(text, text.IndexOf('{', tracksAt));
            for (var entry = Entry.Match(all); entry.Success; entry = entry.NextMatch())
            {
                string body = Block(all, all.IndexOf('{', entry.Index + entry.Length));
                var rows = new List<double[]>();
                foreach (Match row in Row.Matches(body))
                {
                    var values = row.Groups[1].Value.Split(',');
                    var parsed = new double[values.Length];
                    for (int i = 0; i < values.Length; i++) parsed[i] = Number(values[i].Trim());
                    rows.Add(parsed);
                }
                clip.tracks[entry.Groups[1].Value] = rows.ToArray();
            }
            return clip;
        }

        static double Number(string value) => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);

        // The substring of the balanced {...} block starting at `open` (exclusive of the braces).
        static string Block(string text, int open)
        {
            if (open < 0) throw new FormatException("Unbalanced animation table.");
            int depth = 0;
            for (int i = open; i < text.Length; i++)
            {
                if (text[i] == '{') depth++;
                else if (text[i] == '}' && --depth == 0) return text.Substring(open + 1, i - open - 1);
            }
            throw new FormatException("Unbalanced animation table.");
        }

        public bool Has(string track) => tracks.ContainsKey(track);
        public double? Label(string name) => labels.TryGetValue(name, out double frame) ? frame : (double?)null;
        public double Frames(double ms) => ms * Fps / 1000.0;

        // First and last frame a track has rows for (the leaf's display window).
        public bool Window(string track, out double first, out double last)
        {
            first = last = 0;
            if (!tracks.TryGetValue(track, out var rows) || rows.Length == 0) return false;
            first = rows[0][0]; last = rows[rows.Length - 1][0];
            return true;
        }

        public double? Get(string track, double frame, string field)
        {
            if (!fieldIndex.TryGetValue(field, out int column)) return null;
            if (!tracks.TryGetValue(track, out var rows) || rows.Length == 0) return null;
            int i = Seek(rows, frame);
            var a = rows[i];
            if (i >= rows.Length - 1 || frame <= a[0]) return a[column + 1];
            var b = rows[i + 1];
            double u = (frame - a[0]) / (b[0] - a[0]);
            return a[column + 1] + (b[column + 1] - a[column + 1]) * u;
        }

        public double Get(string track, double frame, string field, double fallback) => Get(track, frame, field) ?? fallback;

        // The last row whose frame is <= f.
        static int Seek(double[][] rows, double f)
        {
            int lo = 0, hi = rows.Length - 1;
            if (f <= rows[lo][0]) return lo;
            if (f >= rows[hi][0]) return hi;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (rows[mid][0] <= f) lo = mid; else hi = mid - 1;
            }
            return lo;
        }
    }
}

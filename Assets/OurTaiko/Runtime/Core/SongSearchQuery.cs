using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OurTaiko
{
    public enum SongSearchOrder { Default, UnFullCombo, UnPerfect }

    // Immutable snapshot: edits in the dialog cannot change an in-flight request.
    public sealed class SongSearchQuery
    {
        public readonly string Keyword;
        public readonly Difficulty? Difficulty;
        public readonly int Level;
        public readonly SongSearchOrder Order;
        readonly Regex keywordPattern;
        public string ApiOrder => Order == SongSearchOrder.UnFullCombo ? "unfc" : Order == SongSearchOrder.UnPerfect ? "unperfect" : "default";
        public SongSearchQuery(string keyword = "", Difficulty? difficulty = null, int level = 0, SongSearchOrder order = SongSearchOrder.Default)
        {
            Keyword = (keyword ?? "").Trim(); Difficulty = difficulty; Level = level; Order = order;
            if (Encoding.UTF8.GetByteCount(Keyword) > 200) throw new ArgumentException("Keyword must be at most 200 UTF-8 bytes.");
            if (difficulty.HasValue && ((int)difficulty < 0 || (int)difficulty > 4) || level < 0 || level > 10 || !Enum.IsDefined(typeof(SongSearchOrder), order))
                throw new ArgumentOutOfRangeException(nameof(difficulty));
            keywordPattern = CreatePattern();
        }
        public bool Matches(CourseInfo course) => (!Difficulty.HasValue || course.Difficulty == Difficulty) && (Level == 0 || course.Level == Level);
        public bool Matches(SongInfo info) => info.Courses.Any(Matches);

        // Match the API's ILIKE substring semantics, including % and _ wildcards and backslash escapes.
        public bool MatchesText(string text) => keywordPattern.IsMatch(text ?? "");
        Regex CreatePattern()
        {
            var pattern = new StringBuilder(@"\A");
            string like = "%" + Keyword + "%";
            for (int i = 0; i < like.Length; i++)
            {
                char c = like[i];
                if (c == '\\' && i + 1 < like.Length) pattern.Append(Regex.Escape(like[++i].ToString()));
                else pattern.Append(c == '%' ? ".*" : c == '_' ? "." : Regex.Escape(c.ToString()));
            }
            return new Regex(pattern.Append(@"\z").ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromMilliseconds(100));
        }
        public bool MatchesLocal(SongDefinition song)
        {
            if (!Matches(song.ReadDisplayInfo())) return false;
            if (Keyword.Length == 0) return true;
            bool inChart = false;
            foreach (var raw in song.chart.text.Replace("\r", "").Split('\n'))
            {
                string line = raw.Split(new[] { "//" }, StringSplitOptions.None)[0].Trim().TrimStart('\uFEFF');
                if (line.StartsWith("#START", StringComparison.OrdinalIgnoreCase)) { inChart = true; continue; }
                if (line.Equals("#END", StringComparison.OrdinalIgnoreCase)) { inChart = false; continue; }
                if (inChart) continue;
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                string key = line.Substring(0, colon).Trim().ToUpperInvariant();
                if ((key.StartsWith("TITLE") || key.StartsWith("SUBTITLE") || key == "MAKER") && MatchesText(line.Substring(colon + 1).Trim())) return true;
            }
            return false;
        }
    }
}

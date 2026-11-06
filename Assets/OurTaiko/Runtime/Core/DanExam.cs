using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // The values a 合格条件 counts (YataiDON's exam types): gauge %, 良, 可, 不可, hits (良+可+rolls),
    // score, max combo, drumroll hits.
    public enum DanExamType { Gauge, Good, Ok, Bad, Hits, Score, Combo, Rolls }
    // more: pass at the border or above (以上); less: pass below it (未満).
    public enum DanExamRange { More, Less }

    public readonly struct DanBorder
    {
        public readonly int Red, Gold;
        public DanBorder(int red, int gold) { Red = red; Gold = gold; }
    }

    // One 合格条件, as YataiDON's Exam: one [red, gold] pair for the whole course, or one pair per
    // song, where a song without a pair has no border.
    public sealed class DanExam
    {
        // A gold on the wrong side of its red: resolved to the perfect value (YataiDON GOLD_FULL).
        public const int GoldFull = -1;

        public DanExamType Type;
        public DanExamRange Range;
        public DanBorder Course;
        // Per song, null for a song with no border; null altogether for a course-wide exam.
        public DanBorder?[] Songs;
        public bool PerSong => Songs != null;

        public static readonly IReadOnlyDictionary<string, DanExamType> TypeNames = new Dictionary<string, DanExamType>
        {
            ["gauge"] = DanExamType.Gauge, ["good"] = DanExamType.Good, ["ok"] = DanExamType.Ok, ["bad"] = DanExamType.Bad,
            ["hits"] = DanExamType.Hits, ["score"] = DanExamType.Score, ["combo"] = DanExamType.Combo, ["rolls"] = DanExamType.Rolls,
        };

        // The border song `song` is judged by (-1: the course), or null when that song has none.
        public DanBorder? BorderFor(int song) => !PerSong || song < 0 ? Course : song < Songs.Length ? Songs[song] : null;

        // A pair as written: a lone value is both borders, and a gold on the wrong side is none.
        public DanBorder Sane(int red, int? gold)
        {
            int g = gold ?? red;
            bool wrongSide = Range == DanExamRange.Less ? g > red : g < red;
            return new DanBorder(red, wrongSide ? GoldFull : g);
        }

        // YataiDON exam_for: an unset gold is 0 (none at all) for less, a full gauge, every note for
        // 良/hits/combo, and the red itself for the rest.
        public DanBorder Resolve(DanBorder border, int notes)
        {
            if (border.Gold != GoldFull) return border;
            int gold;
            if (Range == DanExamRange.Less) gold = 1;
            else if (Type == DanExamType.Gauge) gold = 100;
            else if (Type == DanExamType.Good || Type == DanExamType.Hits || Type == DanExamType.Combo) gold = Math.Max(border.Red, notes);
            else gold = border.Red;
            return new DanBorder(border.Red, gold);
        }

        // 0 failed, 1 red, 2 gold. Unlike YataiDON's exam_tier, a gold equal to its red is reached
        // with it: there a lone [100] gauge capped every course at red.
        public int Tier(DanBorder border, int value)
        {
            if (Range == DanExamRange.Less) return value >= border.Red ? 0 : value < border.Gold ? 2 : 1;
            return value < border.Red ? 0 : value >= border.Gold ? 2 : 1;
        }

        // YataiDON dan_bar_state: the art state of a bar at `value` (live = during play, when a less
        // exam never shows max; nearEnd/justBeforeEnd = under 10% / 5% of the notes remain).
        public string BarState(DanBorder border, int value, bool live = false, bool nearEnd = false, bool justBeforeEnd = false)
        {
            bool down = Range == DanExamRange.Less;
            if (border.Gold > 0 && (down ? value < border.Gold : value >= border.Gold))
            {
                if (!live || !down) return "max";
                if (justBeforeEnd) return "max_soon2";
                if (nearEnd) return "max_soon";
            }
            int gauge = border.Red > 0 ? (int)Math.Floor(100.0 * value / border.Red) : 100;
            gauge = Math.Max(0, Math.Min(100, gauge));
            if (down) gauge = 100 - gauge;
            if (gauge <= 0) return "empty";
            if (!down) return gauge <= 49 ? "up_50" : gauge <= 99 ? "up_80" : "up_100";
            int good = border.Red > 0 && border.Gold > 0 ? 100 - (int)Math.Floor(100.0 * border.Gold / border.Red) : 100;
            if (gauge < 30) return "down_80";
            if (gauge <= good) return "up_80";
            return "down_100";
        }
    }
}

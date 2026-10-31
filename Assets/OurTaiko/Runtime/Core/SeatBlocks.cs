using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Which of a file's blocks a player plays. The arcade stores every course three times (single,
    // 1P, 2P); a player always sits at the 1P or 2P seat, alone or not. Alone, the player reads
    // single first and the seat's own side when the course lacks it; two together each read their
    // seat's side first, falling back to single (user rule R036 2026-10-15, changed by R055
    // 2026-10-30): alone 1P single → p1, 2P single → p2 → p1; together 1P p1 → single, 2P
    // p2 → single → p1. The TJAParser package only says what blocks a file has (SongInfo.Blocks);
    // this is the game's choice among them.
    public static class SeatBlocks
    {
        public static readonly string[] Seats = { "p1", "p2" };

        static readonly Dictionary<string, string[]> TogetherOrder = new Dictionary<string, string[]>
        {
            ["p1"] = new[] { "p1", "single" }, ["p2"] = new[] { "p2", "single", "p1" },
        };
        static readonly Dictionary<string, string[]> AloneOrder = new Dictionary<string, string[]>
        {
            ["p1"] = new[] { "single", "p1" }, ["p2"] = new[] { "single", "p2", "p1" },
        };

        // The sides a seat ("p1" or "p2") plays, best first; together is whether two play.
        public static IReadOnlyList<string> Reads(string seat, bool together = false)
            => (together ? TogetherOrder : AloneOrder).TryGetValue(seat ?? "p1", out var order) ? order : throw new ArgumentException($"Unknown seat '{seat}'.", nameof(seat));

        // The block a seat plays on a difficulty; null when the file has none for it.
        public static TjaBlock PlayedBlock(this SongInfo info, Difficulty difficulty, string seat = "p1", bool together = false)
        {
            foreach (string side in Reads(seat, together))
                if (info.Block(difficulty, side) is TjaBlock block) return block;
            return null;
        }

        public static TjaBlock Course(this SongInfo info, Difficulty difficulty, string seat = "p1", bool together = false) => info.PlayedBlock(difficulty, seat, together);
        public static bool Has(this SongInfo info, Difficulty difficulty, string seat = "p1", bool together = false) => info.PlayedBlock(difficulty, seat, together) != null;

        // The block the seat plays on each difficulty the file has, by difficulty: the song select's list.
        public static List<TjaBlock> Courses(this SongInfo info, string seat = "p1", bool together = false)
        {
            var courses = new List<TjaBlock>();
            foreach (var difficulty in info.Blocks.Select(b => b.Difficulty).Distinct().OrderBy(d => d))
                if (info.PlayedBlock(difficulty, seat, together) is TjaBlock block) courses.Add(block);
            return courses;
        }
    }
}

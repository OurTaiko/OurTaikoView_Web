using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Which of a file's blocks a player plays. The arcade stores every course three times (single,
    // 1P, 2P); a player always sits at the 1P or 2P seat, alone or not, and reads that seat's side
    // first, falling back where a course lacks it (user rule, 2026-10-13/15): 1P reads p1, then
    // single; 2P reads p2, then single, then p1. The TJAParser package only says what blocks a
    // file has (SongInfo.Blocks); this is the game's choice among them.
    public static class SeatBlocks
    {
        public static readonly string[] Seats = { "p1", "p2" };

        static readonly Dictionary<string, string[]> ReadOrder = new Dictionary<string, string[]>
        {
            ["p1"] = new[] { "p1", "single" }, ["p2"] = new[] { "p2", "single", "p1" },
        };

        // The sides a seat ("p1" or "p2") plays, best first.
        public static IReadOnlyList<string> Reads(string seat)
            => ReadOrder.TryGetValue(seat ?? "p1", out var order) ? order : throw new ArgumentException($"Unknown seat '{seat}'.", nameof(seat));

        // The block a seat plays on a difficulty; null when the file has none for it.
        public static TjaBlock PlayedBlock(this SongInfo info, Difficulty difficulty, string seat = "p1")
        {
            foreach (string side in Reads(seat))
                if (info.Block(difficulty, side) is TjaBlock block) return block;
            return null;
        }

        public static TjaBlock Course(this SongInfo info, Difficulty difficulty, string seat = "p1") => info.PlayedBlock(difficulty, seat);
        public static bool Has(this SongInfo info, Difficulty difficulty, string seat = "p1") => info.PlayedBlock(difficulty, seat) != null;

        // The block the seat plays on each difficulty the file has, by difficulty: the song select's list.
        public static List<TjaBlock> Courses(this SongInfo info, string seat = "p1")
        {
            var courses = new List<TjaBlock>();
            foreach (var difficulty in info.Blocks.Select(b => b.Difficulty).Distinct().OrderBy(d => d))
                if (info.PlayedBlock(difficulty, seat) is TjaBlock block) courses.Add(block);
            return courses;
        }
    }
}

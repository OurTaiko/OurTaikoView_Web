using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // The arcade stores every course three times: single, 1P and 2P. In a TJA a block's side is
    // #START's player (`#START P1`, `#START P2`, none for single); online charts list it per block.
    // A player always sits at the 1P or 2P seat, alone or not, and reads that seat's side first;
    // where a course lacks it the simulator falls back (user rule, 2026-10-13/15): 1P reads p1,
    // then single; 2P reads p2, then single, then p1. Servers only list the blocks a file has.
    public static class ChartSide
    {
        public static readonly string[] All = { "single", "p1", "p2" };
        public static readonly string[] Seats = { "p1", "p2" };

        static readonly Dictionary<string, string[]> ReadOrder = new Dictionary<string, string[]>
        {
            ["p1"] = new[] { "p1", "single" }, ["p2"] = new[] { "p2", "single", "p1" },
        };

        // The side of a block whose #START names this player (TjaLine.Value: "", "P1", "P2", …).
        public static string OfStart(string player) => player == "P1" ? "p1" : player == "P2" ? "p2" : "single";

        // The sides a seat ("p1" or "p2") plays, best first.
        public static IReadOnlyList<string> Reads(string seat)
            => ReadOrder.TryGetValue(seat ?? "p1", out var order) ? order : throw new ArgumentException($"Unknown seat '{seat}'.", nameof(seat));

        // The side a seat plays among a course's blocks, or null when it plays none of them.
        public static string Pick(string seat, ICollection<string> present)
        {
            foreach (string side in Reads(seat)) if (present.Contains(side)) return side;
            return null;
        }
    }
}

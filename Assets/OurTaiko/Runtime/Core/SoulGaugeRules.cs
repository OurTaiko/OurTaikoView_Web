using System;

namespace OurTaiko
{
    // Table copied from OurTaikoPlayer's gauge.h; its unconfirmed entries remain source values.
    public readonly struct SoulGaugeRules
    {
        public double SoulPercent { get; }
        public double OkMultiplier { get; }
        public double BadMultiplier { get; }
        public int ClearPoints { get; }

        SoulGaugeRules(double soulPercent, double okMultiplier, double badMultiplier, int clearPoints)
        {
            SoulPercent = soulPercent; OkMultiplier = okMultiplier;
            BadMultiplier = badMultiplier; ClearPoints = clearPoints;
        }

        static readonly double[][] SoulPercentTable = {
            new[] { 60.0, 63.333, 63.333, 73.333, 73.333, 0, 0, 0, 0, 0 },
            new[] { 65.6, 65.6, 69.5, 70.3, 75.0, 75.0, 75.0, 0, 0, 0 },
            new[] { 77.6, 77.6, 72.5, 69.15, 67.5, 68.74, 68.74, 68.74, 0, 0 },
            new[] { 70.75, 70.75, 70.75, 70.75, 70.75, 70.75, 70.75, 70.0, 76.75, 76.75 }
        };
        static readonly double[][] BadMultiplierTable = {
            new[] { -0.5, -0.5, -0.5, -0.5, -0.5, 0, 0, 0, 0, 0 },
            new[] { -0.5, -0.5, -0.5, -0.75, -1.0, -1.0, -1.0, 0, 0, 0 },
            new[] { -0.75, -0.75, -1.0, -1.17, -1.25, -1.25, -1.25, -1.25, 0, 0 },
            new[] { -1.6, -1.6, -1.6, -1.6, -1.6, -1.6, -1.6, -2.0, -2.0, -2.0 }
        };

        public static SoulGaugeRules ForChart(string course, int level)
        {
            int difficulty;
            switch (course?.ToLowerInvariant())
            {
                case "easy": difficulty = 0; break;
                case "normal": difficulty = 1; break;
                case "hard": difficulty = 2; break;
                case "oni": case "edit": difficulty = 3; break;
                default: throw new NotSupportedException("Unsupported soul gauge course: " + course);
            }
            // Player::reset_chart treats absent/zero LEVEL as Oni 10, including the clear tier.
            if (level == 0) { difficulty = 3; level = 10; }
            int row = Math.Max(1, Math.Min(10, level)) - 1;
            double soul = SoulPercentTable[difficulty][row];
            double ok = soul == 0 ? 0 : difficulty == 3 ? 0.5 : 0.75;
            int clear = difficulty == 0 ? 6000 : difficulty < 3 ? 7000 : 8000;
            return new SoulGaugeRules(soul, ok, BadMultiplierTable[difficulty][row], clear);
        }
    }
}

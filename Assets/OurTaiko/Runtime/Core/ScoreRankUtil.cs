using System;

namespace OurTaiko
{
    // Shared by song select, gameplay and results. A score alone cannot distinguish
    // Miyabi from Kiwami: that requires the ceiling of the matching chart/course.
    public static class ScoreRankUtil
    {
        public const int Count = 7;
        static readonly int[] Thresholds = { 500000, 600000, 700000, 800000, 900000, 950000 };

        public static int KiwamiThreshold(TaikoChart chart) => new ShinuchiScore(new ChartStatistics(chart)).CeilingScore;

        // While an online chart is loading, hide an unresolved million-plus rank
        // rather than briefly showing a wrong icon. Lower ranks need no chart data.
        public static ScoreRank FromScore(int score, int? kiwamiThreshold = null)
        {
            if (score >= 1000000)
            {
                if (!kiwamiThreshold.HasValue) return ScoreRank.None;
                if (score >= Math.Max(1000000, kiwamiThreshold.Value)) return ScoreRank.Kiwami;
            }
            for (int i = Thresholds.Length - 1; i >= 0; i--)
                if (score >= Thresholds[i]) return (ScoreRank)(i + 1);
            return ScoreRank.None;
        }
    }
}

namespace OurTaiko
{
    // Nijiiro result_player.lua / rank_of. Derived from scores, never a second persisted value.
    public static class ScoreRank
    {
        public const int Count = 7;
        static readonly int[] Thresholds = { 500000, 600000, 700000, 800000, 900000, 950000, 1000000 };
        public static int FromScore(int score)
        {
            for (int i = Thresholds.Length - 1; i >= 0; i--)
                if (score >= Thresholds[i]) return i + 1;
            return 0;
        }
    }
}

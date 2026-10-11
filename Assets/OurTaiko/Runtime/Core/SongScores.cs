using System;

namespace OurTaiko
{
    // Server bests are session data loaded at login, never a fallback to the local database.
    public static class SongScores
    {
        public static bool IsOnlineKey(string key) => key != null && key.StartsWith("fanmade/", StringComparison.Ordinal);
        public static bool IsOnline(SongDefinition song) => song != null &&
            (IsOnlineKey(song.name) || (Online.OnlineManager.Instance != null && Online.OnlineManager.Instance.IsOnline(song)));

        public static ScoreStore.Record Get(SongDefinition song, Difficulty difficulty)
        {
            if (song == null || difficulty < Difficulty.Easy || difficulty > Difficulty.Ura) return null;
            if (!IsOnline(song)) return ScoreStore.Shared.Get(song.name, difficulty);
            var manager = Online.OnlineManager.Instance;
            var best = manager?.Client.Best(manager.ChartOf(song), (int)difficulty);
            if (best == null) return null;
            // The server's ClearStatus is authoritative: 0 none, 1 silver, 2 gold, 3 rainbow.
            // Missing (old server/history) or unknown values never imply a crown from judgments.
            var crown = best.ClearStatus >= 0 && best.ClearStatus <= 3 ? (Crown)best.ClearStatus : Crown.None;
            return new ScoreStore.Record { key = ScoreStore.Key(song.name, difficulty), score = Clamp(best.Score),
                good = Clamp(best.Good), ok = Clamp(best.Ok), bad = Clamp(best.Bad), maxCombo = Clamp(best.MaxCombo),
                rolls = Clamp(best.Drumroll), crown = crown };
        }

        static int Clamp(long value) => (int)Math.Clamp(value, 0, int.MaxValue);

        public static ScoreRank Rank(SongDefinition song, Difficulty difficulty)
        {
            int score = Get(song, difficulty)?.score ?? 0;
            if (score < 1000000) return ScoreRankUtil.FromScore(score);
            int? threshold = song.RankThreshold(difficulty);
            if (!threshold.HasValue && IsOnline(song))
            {
                var manager = Online.OnlineManager.Instance;
                threshold = manager?.Client.RankThreshold(manager.ChartOf(song), (int)difficulty);
            }
            return ScoreRankUtil.FromScore(score, threshold);
        }
    }
}

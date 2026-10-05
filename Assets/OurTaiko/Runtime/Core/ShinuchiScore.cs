using System;

namespace OurTaiko
{
    // Pure scoring policy. Timing, combos, GOGO, note size and rendering belong to other modules.
    public sealed class ShinuchiScore
    {
        // Keep the float literal used in tja.cpp::calculate_base_score, including its precision.
        public const float EstimatedRollHitsPerSecond = 16.920079999994086f;
        public const int LongHitPoints = 100;
        public int BaseScore { get; }
        public int Total { get; private set; }

        public ShinuchiScore(ChartStatistics statistics)
        {
            if (statistics == null) throw new ArgumentNullException(nameof(statistics));
            if (statistics.JudgeableNotes == 0) { BaseScore = 1000000; return; }
            float budget = 1000000f - statistics.BalloonHitBudget * 100f;
            double perTen = (budget - EstimatedRollHitsPerSecond * statistics.DrumrollMilliseconds / 1000f * 100f)
                / statistics.JudgeableNotes / 10f;
            BaseScore = (int)Math.Ceiling(perTen) * 10;
        }

        public int ApplyJudgment(Judgment judgment)
        {
            int points = judgment == Judgment.Good ? BaseScore
                : judgment == Judgment.Ok ? BaseScore / 2 / 10 * 10 : 0;
            Total += points;
            return points;
        }

        public void AddLongHit() => Total += LongHitPoints;
    }
}

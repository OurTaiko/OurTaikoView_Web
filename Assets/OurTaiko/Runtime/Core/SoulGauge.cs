using System;

namespace OurTaiko
{
    // Numeric gauge only; SoulGaugeView owns cells, skin art and animations.
    public sealed class SoulGauge
    {
        public const int MaximumPoints = 10000;
        public const double PointsEpsilon = 1e-6;
        public double Points { get; private set; }
        public double Value => Points / MaximumPoints;
        public int Percent => (int)Math.Floor((Points + PointsEpsilon) / MaximumPoints * 100);
        public int ClearPoints { get; }
        public double ClearThreshold => (double)ClearPoints / MaximumPoints;
        public bool IsClear => Points >= ClearPoints;
        public bool IsFull => Points >= MaximumPoints;
        public double GoodPoints { get; }
        public double OkPoints { get; }
        public double BadPoints { get; }

        public SoulGauge(int judgeableNotes, string course, int level)
        {
            var rules = SoulGaugeRules.ForChart(course, level);
            ClearPoints = rules.ClearPoints;
            double denominator = Math.Max(1, judgeableNotes) * rules.SoulPercent;
            GoodPoints = denominator > 0 ? 1000000.0 / denominator : 0;
            OkPoints = GoodPoints * rules.OkMultiplier;
            BadPoints = GoodPoints * rules.BadMultiplier;
        }

        public void ApplyJudgment(Judgment judgment)
        {
            double delta;
            switch (judgment)
            {
                case Judgment.Good: delta = GoodPoints; break;
                case Judgment.Ok: delta = OkPoints; break;
                case Judgment.Bad: delta = BadPoints; break;
                default: return;
            }
            Points = Math.Max(0, Math.Min(MaximumPoints, Points + delta));
            // Gauge::apply_points_clamped snaps clear/full boundaries before state comparisons.
            if (Math.Abs(Points - MaximumPoints) < PointsEpsilon) Points = MaximumPoints;
            if (Math.Abs(Points - ClearPoints) < PointsEpsilon) Points = ClearPoints;
        }
    }
}

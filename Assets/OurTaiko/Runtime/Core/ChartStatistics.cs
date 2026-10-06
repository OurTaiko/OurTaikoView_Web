using System;

namespace OurTaiko
{
    // Fixed chart totals used by the reference player, regardless of the route selected in play.
    public sealed class ChartStatistics
    {
        public int JudgeableNotes { get; }
        public int BalloonHitBudget { get; }
        public double DrumrollMilliseconds { get; }

        public ChartStatistics(TaikoChart chart)
        {
            if (chart == null) throw new ArgumentNullException(nameof(chart));
            int judgeableNotes = 0, balloonHitBudget = 0;
            double drumrollMilliseconds = 0;
            // Player::reset_chart aggregates the common list followed by every Master section.
            // Preserve that order for the source's double accumulation of roll durations.
            Accumulate(-1);
            for (int branch = 0; branch < chart.Branches.Count; branch++) Accumulate(branch);
            JudgeableNotes = judgeableNotes;
            BalloonHitBudget = balloonHitBudget;
            DrumrollMilliseconds = drumrollMilliseconds;

            void Accumulate(int branchId)
            {
                foreach (var note in chart.Notes)
                {
                    // A branch without #M counts the route drawn in its place.
                    if (note.BranchId != branchId || (branchId >= 0 && note.Route != chart.Branches[branchId].ResolveRoute(BranchRoute.Master))) continue;
                    if (note.Kind >= NoteKind.Don && note.Kind <= NoteKind.BigKa) judgeableNotes++;
                    else if (note.IsBalloon) balloonHitBudget += Math.Min(100, note.BalloonHits);
                    else if (note.Kind == NoteKind.Roll || note.Kind == NoteKind.BigRoll)
                        drumrollMilliseconds += (note.EndTime - note.Time) * 1000;
                }
            }
        }
    }
}

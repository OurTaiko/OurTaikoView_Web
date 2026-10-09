using System;

namespace OurTaiko
{
    public static class NoteExpression
    {
        const double BeatTolerance = 1e-7;

        public static double StartBeat(double noteBeat) => Math.Ceiling(noteBeat - BeatTolerance);

        // The first change is on the threshold note's beat, rounded up to a whole beat.
        // Until the faster tier starts, keep the eighth-note animation running.
        public static int Frame(double beat, int combo, double eighthStart, double sixteenthStart)
        {
            if (combo < 50 || double.IsNaN(beat) || double.IsInfinity(beat) || beat + BeatTolerance < eighthStart) return 0;
            bool fast = combo >= 150 && beat + BeatTolerance >= sixteenthStart;
            double start = fast ? sixteenthStart : eighthStart;
            double step = Math.Max(0, Math.Floor((beat - start + BeatTolerance) * (fast ? 4 : 2)));
            return 1 - (int)(step % 2);
        }
    }
}

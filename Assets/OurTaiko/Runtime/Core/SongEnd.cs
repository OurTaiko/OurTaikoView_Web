using System;

namespace OurTaiko
{
    // When a play ends, in chart seconds: two seconds after its last note (a roll at its end), as
    // OpenTaiko's chart end (CTja.InsertEndOfChartChips) but decided by the chart alone (user
    // request): neither #END nor the music's end cuts it short. The ending starts there and the
    // music fades out under it. A chart without notes ends at #END.
    public static class SongEnd
    {
        public const double LastNoteDelay = 2;

        // lateJudge (a positive judge offset) moves it later, as the notes are judged that much later.
        public static double Of(TaikoChart chart, double lateJudge = 0)
        {
            double late = Math.Max(0, lateJudge);
            double last = double.NegativeInfinity;
            foreach (var note in chart.Notes) last = Math.Max(last, note.IsLong ? note.EndTime : note.Time);
            return (double.IsNegativeInfinity(last) ? chart.Duration : last + LastNoteDelay) + late;
        }

        // The music's volume (1 to 0, linear) this long after the ending started.
        public static float Fade(double elapsed, double duration) =>
            duration <= 0 ? 0 : (float)Math.Max(0, Math.Min(1, 1 - elapsed / duration));
    }
}

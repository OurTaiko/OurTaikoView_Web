using System;

namespace OurTaiko
{
    // NoteArc: a hit note flies from the judge circle to the soul badge. Nijiiro's skin_config
    // authors the arcade sweep (note_arc_pivot, note_arc_duration): the note centre runs a circle
    // about the pivot at a constant angular rate instead of the PyTaikoGreen Bezier.
    // Lane-local design units with Y down, note centres (192-pixel sprites).
    public static class NoteArcPath
    {
        // NoteArc::update counts 16.67 ms frames; note_arc_duration.x = 30.
        public const double FrameSeconds = 0.01667;
        public const int DurationFrames = 30;
        public const double Duration = DurationFrames * FrameSeconds;

        // notes.y=14 + note_arc_start_x_offset 522 and gauge_hit_effect_note (1738,-126), plus half a note.
        public const double StartX = 522 + 96, StartY = 14 + 96;
        public const double EndX = 1738 + 96, EndY = -126 + 96;
        public const double PivotX = 1269.22, PivotY = 415.14;

        public static readonly double Radius, StartAngle, EndAngle;

        static NoteArcPath()
        {
            // set_circle: the mean of both radii, the short way round between the two angles.
            Radius = (Math.Sqrt(Sq(StartX - PivotX) + Sq(StartY - PivotY)) + Math.Sqrt(Sq(EndX - PivotX) + Sq(EndY - PivotY))) / 2;
            StartAngle = Math.Atan2(StartY - PivotY, StartX - PivotX);
            EndAngle = Math.Atan2(EndY - PivotY, EndX - PivotX);
            while (EndAngle - StartAngle > Math.PI) EndAngle -= 2 * Math.PI;
            while (EndAngle - StartAngle < -Math.PI) EndAngle += 2 * Math.PI;
        }

        static double Sq(double v) => v * v;

        public static double Progress(double elapsed) => Math.Max(0, Math.Min(1, elapsed / Duration));

        // is_finished: the arc is erased on the update that reaches the badge, so it is never drawn there.
        public static bool IsFinished(double elapsed) => Progress(elapsed) >= 1;

        public static void Position(double progress, out double x, out double y)
        {
            double angle = StartAngle + (EndAngle - StartAngle) * progress;
            x = PivotX + Radius * Math.Cos(angle);
            y = PivotY + Radius * Math.Sin(angle);
        }
    }
}

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
        // 2P's gauge sits under its lane: NoteArc adds gauge_hit_effect_note.height (324) to the
        // landing and sweeps about note_arc_pivot_2p, curving down.
        public const double EndY2P = EndY + 324, PivotX2P = 1282.19, PivotY2P = -167.83;

        public static readonly double Radius, StartAngle, EndAngle;
        static readonly double[] radius = new double[2], startAngle = new double[2], endAngle = new double[2];

        static NoteArcPath()
        {
            Circle(0, EndY, PivotX, PivotY);
            Circle(1, EndY2P, PivotX2P, PivotY2P);
            Radius = radius[0]; StartAngle = startAngle[0]; EndAngle = endAngle[0];
        }

        // set_circle: the mean of both radii, the short way round between the two angles.
        static void Circle(int seat, double endY, double pivotX, double pivotY)
        {
            radius[seat] = (Math.Sqrt(Sq(StartX - pivotX) + Sq(StartY - pivotY)) + Math.Sqrt(Sq(EndX - pivotX) + Sq(endY - pivotY))) / 2;
            double start = Math.Atan2(StartY - pivotY, StartX - pivotX), end = Math.Atan2(endY - pivotY, EndX - pivotX);
            while (end - start > Math.PI) end -= 2 * Math.PI;
            while (end - start < -Math.PI) end += 2 * Math.PI;
            startAngle[seat] = start; endAngle[seat] = end;
        }

        static double Sq(double v) => v * v;

        public static double Progress(double elapsed) => Math.Max(0, Math.Min(1, elapsed / Duration));

        // is_finished: the arc is erased on the update that reaches the badge, so it is never drawn there.
        public static bool IsFinished(double elapsed) => Progress(elapsed) >= 1;

        public static void Position(double progress, out double x, out double y) => Position(progress, 0, out x, out y);

        public static void Position(double progress, int seat, out double x, out double y)
        {
            double angle = startAngle[seat] + (endAngle[seat] - startAngle[seat]) * progress;
            x = (seat == 1 ? PivotX2P : PivotX) + radius[seat] * Math.Cos(angle);
            y = (seat == 1 ? PivotY2P : PivotY) + radius[seat] * Math.Sin(angle);
        }

        // The same sweep from a judge #JPOSSCROLL has moved by (startDx, startDy): the arc leaves
        // the moved judge and still lands on the badge, the displacement fading out along the way.
        public static void Position(double progress, double startDx, double startDy, out double x, out double y)
            => Position(progress, startDx, startDy, 0, out x, out y);

        public static void Position(double progress, double startDx, double startDy, int seat, out double x, out double y)
        {
            Position(progress, seat, out x, out y);
            x += startDx * (1 - progress);
            y += startDy * (1 - progress);
        }
    }
}

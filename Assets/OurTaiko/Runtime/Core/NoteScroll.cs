namespace OurTaiko
{
    public static class NoteScroll
    {
        // Nijiiro's native 1920-wide field: judge X=618 and 192-pixel notes.
        public const double DefaultTravelDistance = 1302, NoteHalfWidth = 96;

        // Same entry boundary as Player::get_load_time, including the sprite radius.
        public static double LoadTime(ChartNote note)
        {
            double scroll = System.Math.Abs(note.ScrollX);
            if (scroll == 0) scroll = System.Math.Abs(note.ScrollY);
            double speed = note.Bpm / 240.0 * scroll * DefaultTravelDistance;
            return speed == 0 ? note.Time : note.Time - (DefaultTravelDistance + NoteHalfWidth) / speed;
        }

        // OurTaikoPlayer Player::get_position_x/y uses BPM / 240000 in milliseconds.
        // Our clock is seconds: at SCROLL 1, four beats span the right edge-to-judge distance.
        public static double DistanceFromJudge(double hitTime, double currentTime, double bpm,
            double scroll, double travelDistance)
        {
            return (hitTime - currentTime) * bpm / 240.0 * scroll * travelDistance;
        }

        // A roll is a rigid body: its tail inherits the head's BPM and SCROLL, even
        // when chart commands change before the tail. Its length stays constant.
        public static double RollLength(ChartNote note, double travelDistance)
        {
            return DistanceFromJudge(note.EndTime, note.Time, note.Bpm, note.ScrollX, travelDistance);
        }
    }
}

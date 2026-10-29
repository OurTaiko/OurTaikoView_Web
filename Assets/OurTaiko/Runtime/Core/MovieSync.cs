using System;

namespace OurTaiko
{
    // Keeps the background movie on the song clock. MajdataPlay starts its video once and leaves it
    // on its own clock, and OpenTaiko starts it when the chart reaches its chip; here every frame
    // compares the movie with where the song is: a small drift is pulled in by playing the movie a
    // little faster or slower (as SongClock pulls in the audio), a large one (a pause, a stall, the
    // start) seeks. The movie is shown from its first frame at MOVIEOFFSET and keeps its last frame
    // after it ends; before it starts the screen stays black.
    public static class MovieSync
    {
        // Seek beyond this drift (in movie seconds at rate 1); below it the rate catches up.
        public const double SeekDrift = 0.25;
        // Drift ignored, about a frame at 60 fps.
        public const double Deadband = 0.012;
        // A drift is pulled in over this long, at most MaxNudge faster or slower.
        public const double CatchUpSeconds = 1, MaxNudge = 0.1;
        // Playback rates are set in these steps, so a steady drift does not re-anchor every frame.
        public const double RateStep = 0.01;

        public struct Player
        {
            public bool Prepared, Seeking, Playing, Ended;
            public double Position, Length;
        }

        public struct Plan
        {
            // Whether a frame of the movie is on screen (false: black).
            public bool Visible;
            // Seek here first (playback resumes after it when Play is true).
            public double? SeekTo;
            public bool Play, Pause;
            public double Rate;
        }

        // target: the movie position the song is at (song time - MOVIEOFFSET); rate: the song's
        // playback rate; running: whether the song clock is moving.
        public static Plan Decide(double target, double rate, bool running, Player player)
        {
            if (double.IsNaN(target) || double.IsInfinity(target)) throw new ArgumentOutOfRangeException(nameof(target));
            if (double.IsNaN(rate) || rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            var plan = new Plan { Visible = player.Prepared && target >= 0, Rate = rate };
            if (!player.Prepared || player.Seeking) return plan;
            bool hasLength = player.Length > 0;
            double wanted = Math.Max(0, hasLength ? Math.Min(target, player.Length) : target);
            double drift = player.Position - wanted;
            bool far = Math.Abs(drift) > SeekDrift * rate;
            bool playing = running && target >= 0 && (!hasLength || target < player.Length);
            if (playing)
            {
                // Ended has rewound nothing: the song went back over the end (a seek) or the movie ran ahead.
                if (far || player.Ended) { plan.SeekTo = wanted; plan.Play = true; return plan; }
                if (!player.Playing) plan.Play = true;
                plan.Rate = Nudge(drift, rate);
                return plan;
            }
            // Past the end the movie plays out by itself and keeps its last frame.
            if (running && hasLength && target >= player.Length) return plan;
            if (player.Playing) plan.Pause = true;
            // Before it starts the first frame waits; a stopped song keeps the movie where it is
            // unless the song was moved (SongClock.Seek).
            if (far && !player.Ended) plan.SeekTo = wanted;
            else if (player.Ended && target < player.Length) plan.SeekTo = wanted;
            return plan;
        }

        // The rate that removes drift (movie ahead: positive) over CatchUpSeconds.
        public static double Nudge(double drift, double rate)
        {
            if (Math.Abs(drift) <= Deadband * rate) return rate;
            double factor = 1 - Math.Max(-MaxNudge, Math.Min(MaxNudge, drift / (CatchUpSeconds * rate)));
            return Math.Round(rate * factor / RateStep) * RateStep;
        }
    }
}

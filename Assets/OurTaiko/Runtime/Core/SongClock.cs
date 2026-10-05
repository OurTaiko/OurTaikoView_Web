using System;

namespace OurTaiko
{
    // Song progress and playback scheduling share one origin in the audio clock's domain.
    // Only Update advances Time, so every judgment and effect in a frame sees the same value.
    public sealed class SongClock
    {
        const double SyncSeconds = 2, SyncStrength = 0.8, ResumeLead = 0.05;
        double origin, syncUntil, runningSince;
        bool paused;
        public bool Started { get; private set; }
        public double Time { get; private set; } = -2;
        public double Rate { get; private set; } = 1;

        public void Start(double now, double countdown)
        {
            Started = true;
            Rate = 1;
            paused = false;
            runningSince = now;
            origin = now + countdown;
            syncUntil = origin + SyncSeconds;
            Time = -countdown;
        }

        public void Update(double now, double? playbackPosition = null)
        {
            // Start/resume can happen after this frame was sampled.
            if (!Started || paused || now < runningSince) return;
            double time = (now - origin) * Rate;
            if (time > 0 && now < syncUntil && playbackPosition > 0)
                origin += (time - playbackPosition.Value) * SyncStrength / Rate;
            Time = (now - origin) * Rate;
        }

        public void Pause() => paused = true;

        // Seeking is deliberately paused; publishing a new position must never judge skipped notes.
        public void Seek(double position, double rate = 1)
        {
            if (double.IsNaN(position) || double.IsInfinity(position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            Started = true; paused = true; Time = position; Rate = rate;
        }

        public void Resume(double now)
        {
            if (!Started || !paused) return;
            runningSince = now;
            origin = now - Time / Rate;
            syncUntil = Math.Max(now, origin) + SyncSeconds;
            paused = false;
        }

        // The clip may start after the countdown or resume with a short scheduling lead.
        public (double At, double Position)? Schedule(double now, double length)
        {
            if (!Started || paused || length <= 0) return null;
            double position = (now - origin) * Rate;
            if (position < 0) return (origin, 0);
            position += ResumeLead * Rate;
            if (position >= length) return null;
            return (now + ResumeLead, Math.Min(length - 1.0 / 48000, position));
        }
    }
}

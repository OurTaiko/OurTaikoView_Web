using System;

namespace OurTaiko
{
    // Song progress shares one origin with the audio clock. Only Update advances Time, so every
    // judgment and effect in a frame sees the same value.
    // Like MajdataPlay's GamePlayManager, the track starts once the clock reaches it, the clock
    // restarts from that moment, and BassSimple pulls 80% of the drift in for two seconds.
    public sealed class SongClock
    {
        const double SyncSeconds = 2, SyncStrength = 0.8;
        double origin, runningSince, trackPosition, trackStart = double.NaN;
        bool paused, trackWaiting;
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
            Time = -countdown;
            WaitForTrack(0);
        }

        public void Update(double now, double? playbackPosition = null)
        {
            // Start/resume can happen after this frame was sampled.
            if (!Started || paused || now < runningSince) return;
            Time = (now - origin) * Rate;
            // MajdataPlay corrects after publishing the frame, so the change shows from the next one.
            double sinceTrackStart = (Time - trackStart) / Rate;
            if (playbackPosition.HasValue && sinceTrackStart >= 0 && sinceTrackStart <= SyncSeconds)
                origin -= (playbackPosition.Value - Time) * SyncStrength / Rate;
        }

        public void Pause() { paused = true; trackWaiting = false; }

        // Seeking is deliberately paused; publishing a new position must never judge skipped notes.
        public void Seek(double position, double rate = 1)
        {
            if (double.IsNaN(position) || double.IsInfinity(position)) throw new ArgumentOutOfRangeException(nameof(position));
            if (double.IsNaN(rate) || double.IsInfinity(rate) || rate <= 0) throw new ArgumentOutOfRangeException(nameof(rate));
            Started = true; paused = true; trackWaiting = false; Time = position; Rate = rate;
        }

        public void Resume(double now)
        {
            if (!Started || !paused) return;
            runningSince = now;
            origin = now - Time / Rate;
            paused = false;
            WaitForTrack(Math.Max(0, Time));
        }

        // The track position to play now, once the clock has reached it.
        public double? TrackDue(double now)
        {
            if (!Started || paused || !trackWaiting || (now - origin) * Rate < trackPosition) return null;
            return trackPosition;
        }

        // MajdataPlay: _audioStartTime = timer - _audioTrackStartAt right after Play().
        public void TrackStarted(double now)
        {
            if (TrackDue(now) == null) return;
            trackWaiting = false;
            trackStart = trackPosition;
            origin = now - trackPosition / Rate;
        }

        // Nothing is left to play (no audio, or the position is past its end): the clock runs on alone.
        public void SkipTrack() { trackWaiting = false; trackStart = double.NaN; }

        void WaitForTrack(double position)
        {
            trackWaiting = true;
            trackPosition = position;
            trackStart = double.NaN;
        }
    }
}

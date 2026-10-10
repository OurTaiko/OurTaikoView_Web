using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Practice menu pages in order; Branch appears only for a chart with branches.
    public enum PracticeStage { Branch, Measure, Speed }

    // Uses parsed measure times, including hidden barlines, BPM changes, delays and branches.
    public sealed class PracticeProgress
    {
        public const double ScrollSeconds = 0.2;
        public const double PreparationSeconds = 2;
        // The cursor remains on the requested bar. Only playback rewinds, so skipped
        // notes stay resolved. Scale by Speed to retain two real seconds at any rate.
        public double PlaybackStart(double audioOffset, double visualOffset, double judgeOffset)
            => Target + audioOffset + visualOffset - PreparationSeconds * Speed
                + Math.Min(0, judgeOffset - visualOffset);
        // Scrolls the lane back to where playback starts, like a measure skip; the cursor stays.
        public void Rewind(double position, double now)
        {
            Update(now);
            from = Position; to = position; began = now; Scrolling = true;
        }
        public bool Scrolling { get; private set; }
        public int SpeedTenths { get; private set; } = 10;
        public double Speed => SpeedTenths / 10.0;
        public double Position { get; private set; }
        public double Target { get; private set; }
        public int Measure { get; private set; }
        public int Count => bars.Length;
        double[] bars = Array.Empty<double>();
        double from, to, began;
        public void SetBars(IEnumerable<double> times)
        {
            bars = times.Distinct().OrderBy(x => x).ToArray();
            if (bars.Length == 0) bars = new[] { 0.0 };
        }
        public void PauseAt(double position, double now)
        {
            Position = Target = from = to = position; began = now; Scrolling = false;
            Measure = Math.Max(0, Array.FindLastIndex(bars, x => x <= position + 1e-7));
        }
        public double First => bars[0];
        public void Move(int direction, double now)
        {
            Update(now);
            int next = direction > 0 ? Array.FindIndex(bars, x => x > Target + 1e-7)
                : Array.FindLastIndex(bars, x => x < Target - 1e-7);
            if (next < 0) return;
            from = Position; Target = to = bars[next]; began = now; Measure = next; Scrolling = true;
        }
        public void Update(double now)
        {
            double t = Math.Max(0, Math.Min(1, (now - began) / ScrollSeconds));
            Position = t < 1 ? from + (to - from) * t : to;
            Scrolling = t < 1;
        }
        // Unity AudioSource supports positive pitch up to 3; keep the same range on every backend.
        public void ChangeSpeed(int direction) => SpeedTenths = Math.Max(1, Math.Min(30, SpeedTenths + direction));
    }
}

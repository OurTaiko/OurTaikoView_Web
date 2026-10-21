using System;

namespace OurTaiko
{
    // YataiDON's KusudamaCounter with the Nijiiro animation.json ids 11, 13-17 and 19-21, in milliseconds.
    // `shown` is the time since the ball appeared (its first hit), `counted` since the count last changed and
    // `popped` since it broke (NaN while whole). The engine's easing: quadratic in t², out t(2 - t).
    public static class KusudamaMotion
    {
        // id 11: the ball drops 1080 in 250 ms (ease out) and settles back to 930 by 333.33 ms (ease in).
        public const double DropMs = 333.33;
        // id 21: the broken ball fades out over 150 ms after 666 ms; the counter is gone after that.
        public const double FadeDelayMs = 666, FadeMs = 150, PoppedMs = FadeDelayMs + FadeMs;
        public const double SettledDrop = 930;

        static double In(double t) => t * t;
        static double Out(double t) => t * (2 - t);
        static double Clamp01(double t) => Math.Max(0, Math.Min(1, t));

        public static double Drop(double shown)
        {
            if (shown <= 0) return 0;
            if (shown < 250) return 1080 * Out(shown / 250);
            if (shown < DropMs) return 1080 + (SettledDrop - 1080) * In((shown - 250) / (DropMs - 250));
            return SettledDrop;
        }

        // ids 13 + 17: 連打!! rises 420 in 116.67 ms after the drop (ease out) and comes back 60 by 233.34 ms
        // (ease out), while bobbing 60 down and up every second (linear, from the moment the ball appeared).
        public static double RendaMove(double shown)
        {
            double t = shown - DropMs;
            double move = t <= 0 ? 0 : t < 116.67 ? -420 * Out(t / 116.67)
                : t < 233.34 ? -420 + 60 * Out((t - 116.67) / (233.34 - 116.67)) : -360;
            double phase = Math.Max(0, shown) % 1000;
            double bob = phase < 500 ? 60 * phase / 500 : 60 * (1000 - phase) / 500;
            return move + bob;
        }

        // ids 14 and 20: fades in over 83.33 ms after the drop; out over 200 ms once broken (the lower wins).
        public static double RendaAlpha(double shown, double popped)
        {
            double fadeIn = Clamp01((shown - DropMs) / 83.33);
            double fadeOut = double.IsNaN(popped) ? 1 : 1 - Clamp01(popped / 200);
            return Math.Min(fadeIn, fadeOut);
        }

        // id 15 (TextStretchAnimation, 50 ms): the digits grow taller from their bottom on every hit.
        public static double Stretch(double counted)
        {
            if (double.IsNaN(counted) || counted < 0) return 0;
            if (counted <= 50) return 2 + 5 * ((int)counted / 25.0);
            if (counted <= 50 + 116) return 2 + 10 - 2 * ((int)((counted - 50) / 16.57) + 1);
            return 0;
        }

        // id 16: the ball swells to 1.05 in 100 ms, holds 100 ms and shrinks back in 100 ms on every hit.
        public static double Breathing(double counted)
        {
            if (double.IsNaN(counted) || counted < 0) return 1;
            if (counted < 100) return 1 + 0.05 * counted / 100;
            if (counted < 200) return 1.05;
            if (counted < 300) return 1.05 - 0.05 * (counted - 200) / 100;
            return 1;
        }

        // id 19: frames 1-4 open the ball, 100 ms each; 0 while whole.
        public static int OpenFrame(double popped)
        {
            if (double.IsNaN(popped) || popped <= 0) return 0;
            return Math.Min(4, (int)Math.Ceiling(popped / 100));
        }

        public static double Alpha(double popped) => double.IsNaN(popped) ? 1 : 1 - Clamp01((popped - FadeDelayMs) / FadeMs);
    }
}

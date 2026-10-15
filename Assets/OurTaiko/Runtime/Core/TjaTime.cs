using System;

namespace OurTaiko
{
    // OpenTaiko keeps chart and play times in whole milliseconds, cut toward zero (CChip.n発声時刻ms,
    // the (long) play clock). Times here stay in seconds, but on whole milliseconds.
    public static class TjaTime
    {
        // A time in whole milliseconds. Rounding to a millionth of a millisecond first removes the
        // floating-point noise of a value that already is on a whole millisecond.
        public static long Ms(double seconds) => (long)Math.Truncate(Math.Round(seconds * 1000, 6));

        public static double Truncate(double seconds) => Ms(seconds) / 1000.0;
    }
}

using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // One #JPOSSCROLL: from Time the judge (and the note origin with it) moves linearly from From
    // by Move over Duration milliseconds. In OpenTaiko's 1280-wide pixels, down positive.
    public sealed class ChartJudgeMove
    {
        public double Time, Duration, MoveX, MoveY, FromX, FromY;
    }

    // Where #JPOSSCROLL has moved the judge at a time (OpenTaiko CActImplLaneTaiko.t判定枠移動 and
    // its per-frame interpolation, GetJPOSCROLLX/Y), in canvas units of the 1920 design, cut to
    // whole units, y down.
    public static class JudgeShift
    {
        public const double Scale = 1920 / 1280.0;

        // OpenTaiko's chart pass: in time order each move starts from the last one's end, and a
        // move still running when the next starts is cut to the time it had, keeping its pace.
        public static void Link(List<ChartJudgeMove> moves)
        {
            ChartJudgeMove last = null;
            foreach (var move in moves)
            {
                if (last != null)
                {
                    if (last.Duration > 0)
                    {
                        double reached = Math.Max(0, TjaTime.Ms(move.Time) - TjaTime.Ms(last.Time));
                        if (reached < last.Duration)
                        {
                            double rate = reached / last.Duration;
                            last.Duration = reached; last.MoveX *= rate; last.MoveY *= rate;
                        }
                    }
                    move.FromX = last.FromX + last.MoveX; move.FromY = last.FromY + last.MoveY;
                }
                last = move;
            }
        }

        public static (double X, double Y) At(IReadOnlyList<ChartJudgeMove> moves, double time)
        {
            long now = TjaTime.Ms(time);
            ChartJudgeMove move = null;
            for (int i = moves.Count - 1; i >= 0; i--)
                if (TjaTime.Ms(moves[i].Time) <= now) { move = moves[i]; break; }
            if (move == null) return (0, 0);
            long start = TjaTime.Ms(move.Time), duration = (long)move.Duration;
            double x = move.FromX + move.MoveX, y = move.FromY + move.MoveY;
            if (now < start + duration)
            {
                double progress = (now - start) / (double)duration;
                x = move.FromX + progress * move.MoveX; y = move.FromY + progress * move.MoveY;
            }
            return (Math.Truncate(x * Scale), Math.Truncate(y * Scale));
        }

        // The farthest the judge goes across, for lane culling.
        public static double MaxAcross(IReadOnlyList<ChartJudgeMove> moves)
        {
            double max = 0;
            foreach (var move in moves)
                max = Math.Max(max, Math.Max(Math.Abs(move.FromX), Math.Abs(move.FromX + move.MoveX)) * Scale);
            return Math.Ceiling(max);
        }
    }
}

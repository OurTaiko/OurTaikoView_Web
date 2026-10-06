using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Lane geometry shared by the renderer and its tests. x is the lane-local centre, 0 at the
    // lane's left edge; an object is drawn while [x + min, x + max] overlaps [0, width].
    public static class LaneCull
    {
        public static bool InLane(double x, double min, double max, double width) => x + max >= 0 && x + min <= width;

        // Horizontal extent of a note's sprites relative to its centre.
        public static void NoteReach(ChartNote note, double width, double height, double length, double tailAspect,
            double balloonFace, out double min, out double max)
        {
            double half = width / 2;
            if (note.Kind == NoteKind.Balloon)
            {
                // The face shifts left by balloonFace of the width; notes/10 follows it.
                double face = width * balloonFace;
                min = -half - face; max = half + width - face; return;
            }
            min = -half; max = half;
            if (!note.IsLong || note.IsBalloon) return;
            double tail = height * tailAspect;
            if (length >= 0) max = Math.Max(half, length + tail);
            else min = Math.Min(-half, length - tail);
        }

        // Text under a note: plain text is its own width, roll text runs from head to tail.
        public static void MojiReach(bool roll, double width, double length, out double min, out double max)
        {
            double half = width / 2;
            min = -half; max = half;
            if (!roll) return;
            min = Math.Min(-half, length - half); max = Math.Max(half, length + half);
        }

        // Render times during which an object whose sprites reach at most extent from its
        // centre can touch the lane. x(t) = judgeX + (hitTime - t) * speed is linear, so the set
        // is one interval; negative speed only swaps its ends. Zero or non-finite speed keeps the
        // object in place (DistanceFromJudge is 0), so it is always a candidate. A balloon stays
        // on the judge after its hit time, so its interval never ends.
        public static void Interval(double hitTime, double bpm, double scroll, double travelDistance, double judgeX,
            double width, double extent, bool holdsAtJudge, out double start, out double end)
        {
            double speed = bpm / 240.0 * scroll * travelDistance;
            start = double.NegativeInfinity; end = double.PositiveInfinity;
            if (speed == 0 || double.IsNaN(speed) || double.IsInfinity(speed) || double.IsNaN(hitTime)) return;
            double enter = hitTime - (width + extent - judgeX) / speed, leave = hitTime - (-extent - judgeX) / speed;
            start = Math.Min(enter, leave); end = Math.Max(enter, leave);
            if (holdsAtJudge) end = double.PositiveInfinity;
            if (double.IsNaN(start)) start = double.NegativeInfinity;
            if (double.IsNaN(end)) end = double.PositiveInfinity;
        }

        // Candidate windows for the renderer. The extent over-covers every sprite: a balloon with
        // its tail reaches 1.5 notes from its centre, a roll its length plus the tail, its text
        // half the text width plus the length; three of the wider sprite plus the length leaves
        // ample margin. Bar lines are a few pixels wide and keep one sprite of margin.
        public static LaneWindow ForNotes(IReadOnlyList<ChartNote> notes, double width, double judgeX, double spriteWidth)
        {
            double travel = width - judgeX;
            double[] start = new double[notes.Count], end = new double[notes.Count];
            for (int i = 0; i < notes.Count; i++)
            {
                var note = notes[i];
                double length = note.IsLong && !note.IsBalloon ? Math.Abs(NoteScroll.RollLength(note, travel)) : 0;
                Interval(note.Time, note.Bpm, note.ScrollX, travel, judgeX, width, 3 * spriteWidth + length,
                    note.IsBalloon, out start[i], out end[i]);
            }
            return new LaneWindow(start, end);
        }

        public static LaneWindow ForBars(IReadOnlyList<ChartNote> bars, double width, double judgeX, double spriteWidth)
        {
            double travel = width - judgeX;
            double[] start = new double[bars.Count], end = new double[bars.Count];
            for (int i = 0; i < bars.Count; i++)
                Interval(bars[i].Time, bars[i].Bpm, bars[i].ScrollX, travel, judgeX, width, spriteWidth, false, out start[i], out end[i]);
            return new LaneWindow(start, end);
        }
    }

    // The objects whose precomputed lane interval contains the render time. Only a filter:
    // callers still cull each candidate exactly, so a generous interval costs a few checks
    // but never hides anything. Moving forward walks the start-sorted order; any step back
    // (seek, rewind, a lane resize) rebuilds the set once.
    public sealed class LaneWindow
    {
        readonly double[] start, end;
        readonly int[] byStart;
        readonly bool[] listed;
        readonly List<int> active = new List<int>();
        int next;
        double last = double.NaN;

        public LaneWindow(double[] start, double[] end)
        {
            this.start = start; this.end = end;
            listed = new bool[start.Length];
            byStart = Enumerable.Range(0, start.Length).OrderBy(i => start[i]).ThenBy(i => i).ToArray();
        }

        public int Count => active.Count;
        public int this[int k] => active[k];
        public bool Contains(int index) => listed[index];

        // Moves to time and adds the indices that left the window to left.
        public void Seek(double time, List<int> left)
        {
            if (!(time >= last))
            {
                foreach (int i in active) listed[i] = false;
                int previous = left.Count;
                left.AddRange(active);
                active.Clear(); next = 0;
                Enter(time);
                // An object still inside after the rebuild has not left.
                int kept = previous;
                for (int k = previous; k < left.Count; k++) if (!listed[left[k]]) left[kept++] = left[k];
                left.RemoveRange(kept, left.Count - kept);
            }
            else
            {
                int kept = 0;
                for (int k = 0; k < active.Count; k++)
                {
                    int i = active[k];
                    if (end[i] >= time) active[kept++] = i;
                    else { listed[i] = false; left.Add(i); }
                }
                active.RemoveRange(kept, active.Count - kept);
                Enter(time);
            }
            last = time;
        }

        void Enter(double time)
        {
            for (; next < byStart.Length && start[byStart[next]] <= time; next++)
            {
                int i = byStart[next];
                if (end[i] >= time && !listed[i]) { listed[i] = true; active.Add(i); }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace OurTaiko.Tests
{
    // The judgment cursor and the lane windows only skip work: compared with the full scans
    // (ReferencePlaySession and an exact cull of every object) they must give identical results.
    public sealed class ChartCursorTests
    {
        const double Frame = 1 / 120.0, JudgeX = 120, NoteSize = 192, MojiWidth = 256, BalloonFace = 12 / 128.0;

        // Negative, zero and complex scroll, every long-note kind, a short-roll pile-up.
        const string Synthetic = "BPM:150\nBALLOON:4,3,6\nCOURSE:Oni\n#START\n" +
            "#SCROLL -1\n1212,\n#SCROLL 0\n1122,\n#SCROLL 2\n5000,\n0008,\n#SCROLL -0.5\n6000,\n8000,\n" +
            "#SCROLL 1\n7000,\n0800,\n9000,\n0008,\n#SCROLL 1-0.5i\n3434,\n#SCROLL 0.05\n12121212,\n" +
            "#SCROLL 1\n50800000,\n#SCROLL -3\n7000,\n0008,\n" +
            "#SCROLL 1\n#GOGOSTART\n11111111,\n#GOGOEND\n#END";

        // BM/HB scroll (OpenTaiko): beat-clock placement with BPM changes, delays, forced and
        // zero, negative and complex SCROLL, and routes with their own clocks.
        const string ClockSynthetic = "BPM:150\nBALLOON:4,3,6\nCOURSE:Oni\n#BMSCROLL\n#START\n" +
            "#SCROLL -1\n1212,\n#BPMCHANGE 300\n1122,\n#HBSCROLL\n#SCROLL 2\n5000,\n0008,\n#DELAY 0.4\n" +
            "#SCROLL -0.5\n6000,\n8000,\n#BPMCHANGE 75\n#SCROLL 1\n7000,\n0800,\n#BMSCROLL\n9000,\n0008,\n" +
            "#HBSCROLL\n#SCROLL 1-0.5i\n3434,\n#SCROLL 0\n1212,\n#NMSCROLL\n#SCROLL 0.5\n12121212,\n#BMSCROLL\n" +
            "#BRANCHSTART p,0,0\n#N\n#BPMCHANGE 200\n1111,\n#E\n#DELAY 0.3\n2222,\n#M\n#HBSCROLL\n#SCROLL -2\n#BPMCHANGE 100\n3333,\n" +
            "#BRANCHEND\n11111111,\n#END";

        // #SUDDEN: appearing late, standing before moving (also on the clock), negative times,
        // a reset, and the hidden-roll quirk.
        const string SuddenSynthetic = "BPM:150\nBALLOON:4,3\nCOURSE:Oni\n#START\n" +
            "#SUDDEN 1 0.6\n1212,\n#SCROLL 2\n5000,\n0008,\n#SUDDEN 0.5 2\n#SCROLL -1\n1122,\n#SUDDEN 3 0\n7000,\n0800,\n" +
            "#SUDDEN 0 0\n#SCROLL 1\n1111,\n#HBSCROLL\n#SUDDEN 2 1\n#SCROLL 1+1i\n12121212,\n#BPMCHANGE 300\n6000,\n0008,\n" +
            "#BMSCROLL\n#SUDDEN -0.2 0.3\n1111,\n#SUDDEN 0.8 -0.4\n9000,\n0008,\n#END";

        // #JPOSSCROLL moving the judge far both ways while notes pass.
        const string JposSynthetic = "BPM:150\nCOURSE:Oni\n#START\n1111,\n#JPOSSCROLL 1 -400 1\n12121212,\n" +
            "#JPOSSCROLL 0.3 600-80i 1\n5000,\n0008,\n#SCROLL 2\n#JPOSSCROLL 0 -350 1\n11221122,\n#BMSCROLL\n1111,\n#END";

        static string Read(string path) => File.ReadAllText(Path.Combine(Application.dataPath, "OurTaiko", path));

        static IEnumerable<TestCaseData> Charts()
        {
            foreach (string course in new[] { "Oni", "Hard", "Normal", "Easy" })
                yield return new TestCaseData("Tests/EditMode/Charts/Donkama2000.txt", course).SetName("{m}(Donkama2000 " + course + ")");
            yield return new TestCaseData("Tests/Shared/Songs~/1 TripleHelix.tja", "Oni").SetName("{m}(TripleHelix Oni)");
            yield return new TestCaseData("Tests/Shared/Songs~/3 BranchTraining.tja", "Oni").SetName("{m}(BranchTraining Oni)");
            yield return new TestCaseData(null, "Oni").SetName("{m}(Synthetic scrolls)");
            yield return new TestCaseData("clock", "Oni").SetName("{m}(Synthetic BM and HB scrolls)");
            yield return new TestCaseData("sudden", "Oni").SetName("{m}(Synthetic SUDDEN)");
            yield return new TestCaseData("jpos", "Oni").SetName("{m}(Synthetic JPOSSCROLL)");
        }

        static TaikoChart Load(string path, string course)
            => TjaParser.Parse(path == null ? Synthetic : path == "clock" ? ClockSynthetic : path == "sudden" ? SuddenSynthetic : path == "jpos" ? JposSynthetic : Read(path), course);

        sealed class Pair
        {
            public PlaySession Fast;
            public ReferencePlaySession Slow;
            public readonly List<string> FastEvents = new List<string>(), SlowEvents = new List<string>();
            public Pair(PlaySession fast, ReferencePlaySession slow)
            {
                Fast = fast; Slow = slow;
                fast.Judged += (i, j) => FastEvents.Add(i + ":" + j);
                fast.BranchSelected += (b, r) => FastEvents.Add("branch" + b.Id + ":" + r);
                slow.Judged += (i, j) => SlowEvents.Add(i + ":" + j);
                slow.BranchSelected += (b, r) => SlowEvents.Add("branch" + b.Id + ":" + r);
            }
            public void Check(string at)
            {
                Assert.That(FastEvents, Is.EqualTo(SlowEvents), at + " events");
                Assert.That(Fast.Resolved, Is.EqualTo(Slow.Resolved), at + " resolved");
                Assert.That(Fast.Missed, Is.EqualTo(Slow.Missed), at + " missed");
                Assert.That(Fast.LongHits, Is.EqualTo(Slow.LongHits), at + " long hits");
                Assert.That((Fast.Score, Fast.Combo, Fast.MaxCombo, Fast.Good, Fast.Ok, Fast.Bad, Fast.Rolls, Fast.CurrentBranch),
                    Is.EqualTo((Slow.Score, Slow.Combo, Slow.MaxCombo, Slow.Good, Slow.Ok, Slow.Bad, Slow.Rolls, Slow.CurrentBranch)), at + " totals");
                Assert.That(Fast.GaugePoints, Is.EqualTo(Slow.GaugePoints), at + " gauge");
            }
        }

        [TestCaseSource(nameof(Charts))]
        public void CursorMatchesFullScan(string path, string course)
        {
            var chart = Load(path, course);
            foreach (double offset in new[] { 0, 0.04, -0.06 })
            {
                Play(chart, offset, auto: true, seed: 1);
                Play(chart, offset, auto: false, seed: 2);
                Play(chart, offset, auto: false, seed: 3, pressChance: 0.9);
                Practice(chart, offset, seed: 4);
            }
        }

        static void Play(TaikoChart chart, double offset, bool auto, int seed, double pressChance = 0.35)
        {
            var random = new System.Random(seed);
            var pair = new Pair(new PlaySession(chart, offset), new ReferencePlaySession(chart, offset));
            for (double t = -3; t < chart.Duration + 3; t += Frame)
            {
                // A clock correction may step slightly back; the cursor must still agree.
                double time = random.NextDouble() < 0.01 ? t - 0.004 : t;
                Step(pair, time, auto, random, pressChance);
                if (pair.FastEvents.Count != pair.SlowEvents.Count) pair.Check($"t={time:F4}");
            }
            pair.Check($"{(auto ? "auto" : "manual")} offset {offset} end");
        }

        static void Step(Pair pair, double time, bool auto, System.Random random, double pressChance)
        {
            pair.Fast.Advance(time, auto); pair.Slow.Advance(time, auto);
            if (auto || random.NextDouble() >= pressChance) return;
            bool ka = random.Next(2) == 0;
            Assert.That(pair.Fast.Hit(ka, time), Is.EqualTo(pair.Slow.Hit(ka, time)), $"hit at {time:F4}");
        }

        // Jumps to random bars in both directions, inside long notes too, and plays on from each.
        static void Practice(TaikoChart chart, double offset, int seed)
        {
            var random = new System.Random(seed);
            PlaySession fast = null; ReferencePlaySession slow = null;
            for (int jump = 0; jump < 12 && chart.Bars.Count > 0; jump++)
            {
                double at = chart.Bars[random.Next(chart.Bars.Count)].Time + (jump % 3 == 0 ? random.NextDouble() : 0);
                fast = PlaySession.PracticeAt(chart, at, fast); slow = ReferencePlaySession.PracticeAt(chart, at, slow);
                var pair = new Pair(fast, slow);
                bool auto = jump % 2 == 0;
                for (double t = at - 2; t < at + 6; t += Frame) Step(pair, t, auto, random, 0.5);
                pair.Check($"practice at {at:F3}");
            }
        }

        [TestCaseSource(nameof(Charts))]
        public void LaneWindowsCoverEveryDrawnObject(string path, string course)
        {
            var chart = Load(path, course);
            var clocks = new ScrollClockTimeline(chart);
            foreach (double width in new[] { 1422.0, 2100.0 })
            {
                // The measure keeps its length on lanes of either width.
                double shift = JudgeShift.MaxAcross(chart.JudgeMoves);
                var notes = LaneCull.ForNotes(chart.Notes, clocks, width, JudgeX, Math.Max(NoteSize, MojiWidth), NoteScroll.MeasureWidth, shift);
                var bars = LaneCull.ForBars(chart.Bars, clocks, width, JudgeX, NoteSize, NoteScroll.MeasureWidth, shift);
                var left = new List<int>();
                var random = new System.Random(5);
                int maxCandidates = 0, maxDrawn = 0;
                void Check(double time)
                {
                    left.Clear(); notes.Seek(time, left);
                    foreach (int i in left) Assert.That(notes.Contains(i), Is.False);
                    left.Clear(); bars.Seek(time, left);
                    int drawn = 0;
                    for (int i = 0; i < chart.Notes.Count; i++)
                        if (NoteDrawn(chart, chart.Notes[i], clocks, time, width))
                        {
                            drawn++;
                            Assert.That(notes.Contains(i), $"note {i} ({chart.Notes[i].Kind} at {chart.Notes[i].Time:F3}) at {time:F4}, width {width}");
                        }
                    for (int i = 0; i < chart.Bars.Count; i++)
                        if (BarDrawn(chart, chart.Bars[i], clocks, time, width))
                            Assert.That(bars.Contains(i), $"bar {i} at {time:F4}, width {width}");
                    maxCandidates = Math.Max(maxCandidates, notes.Count); maxDrawn = Math.Max(maxDrawn, drawn);
                }
                for (double t = -5; t < chart.Duration + 5; t += Frame) Check(t);
                // Practice seeks and the 200 ms bar scroll run backwards as well as forwards.
                for (int jump = 0; jump < 200; jump++)
                {
                    double t = -5 + random.NextDouble() * (chart.Duration + 10);
                    Check(t);
                    for (int k = 0; k < 24; k++) Check(t -= Frame);
                }
                UnityEngine.Debug.Log($"{course} width {width}: {chart.Notes.Count} notes, at most {maxDrawn} drawn, {maxCandidates} candidates");
            }
        }

        // The exact cull PlayScene applies to every candidate, without the alive test (which only hides more).
        static bool NoteDrawn(TaikoChart chart, ChartNote note, ScrollClockTimeline clocks, double time, double width)
        {
            double travel = NoteScroll.MeasureWidth;
            if (!NoteScroll.Appeared(note, time)) return false;
            double judge = JudgeX + JudgeShift.At(chart.JudgeMoves, time).X;
            double x = judge + NoteScroll.Distance(note, time, clocks, travel).X;
            if (note.IsBalloon && time >= note.Time) x = judge;
            double length = note.IsDrumroll ? NoteScroll.TailDistance(note, time, clocks, travel).X - (x - judge) : 0;
            double aspect = note.Kind == NoteKind.BigRoll ? 120 / 192.0 : 80 / 192.0;
            LaneCull.NoteReach(note, NoteSize, NoteSize, length, aspect, BalloonFace, out double min, out double max);
            if (LaneCull.InLane(x, min, max, width)) return true;
            bool roll = note.IsLong && !note.IsBalloon;
            LaneCull.MojiReach(roll, MojiWidth, length, out min, out max);
            return LaneCull.InLane(x, min, max, width);
        }

        static bool BarDrawn(TaikoChart chart, ChartNote bar, ScrollClockTimeline clocks, double time, double width)
        {
            double x = JudgeX + JudgeShift.At(chart.JudgeMoves, time).X + NoteScroll.Distance(bar, time, clocks, NoteScroll.MeasureWidth).X;
            return LaneCull.InLane(x, -3, 3, width);
        }

        [Test]
        public void IntervalsHandleReverseAndStillScroll()
        {
            LaneCull.Interval(10, 120, 1, 1302, 120, 1422, 0, false, out double start, out double end);
            Assert.That(start, Is.EqualTo(10 - 1302 / 651.0).Within(1e-9));
            Assert.That(end, Is.EqualTo(10 + 120 / 651.0).Within(1e-9));
            LaneCull.Interval(10, 120, -1, 1302, 120, 1422, 0, false, out start, out end);
            Assert.That(start, Is.EqualTo(10 - 120 / 651.0).Within(1e-9));
            Assert.That(end, Is.EqualTo(10 + 1302 / 651.0).Within(1e-9));
            LaneCull.Interval(10, 120, 0, 1302, 120, 1422, 0, false, out start, out end);
            Assert.That((start, end), Is.EqualTo((double.NegativeInfinity, double.PositiveInfinity)));
            LaneCull.Interval(10, 120, 1, 1302, 120, 1422, 0, true, out start, out end);
            Assert.That(end, Is.EqualTo(double.PositiveInfinity));
        }

        [Test]
        public void WindowRebuildsOnSeekBackAndReportsLeavers()
        {
            var window = new LaneWindow(new[] { 0.0, 1, 2, double.NegativeInfinity }, new[] { 1.5, 2.5, 3.5, double.PositiveInfinity });
            var left = new List<int>();
            window.Seek(1.2, left);
            Assert.That(Members(window), Is.EquivalentTo(new[] { 0, 1, 3 }));
            window.Seek(3, left);
            Assert.That(Members(window), Is.EquivalentTo(new[] { 2, 3 }));
            Assert.That(left, Is.EquivalentTo(new[] { 0, 1 }));
            left.Clear(); window.Seek(1.2, left);
            Assert.That(Members(window), Is.EquivalentTo(new[] { 0, 1, 3 }));
            Assert.That(left, Is.EquivalentTo(new[] { 2 }));
        }

        static List<int> Members(LaneWindow window)
        {
            var members = new List<int>();
            for (int k = 0; k < window.Count; k++) members.Add(window[k]);
            return members;
        }

        // Not a pass/fail limit: logs the per-frame judgment cost on a long chart for comparison.
        [Test]
        public void ReportsAdvanceCostOnLongChart()
        {
            var body = new System.Text.StringBuilder("BPM:200\nCOURSE:Oni\n#START\n");
            for (int bar = 0; bar < 1000; bar++) body.Append("1212121212121212,\n");
            var chart = TjaParser.Parse(body.Append("#END").ToString());
            var fast = new PlaySession(chart); var slow = new ReferencePlaySession(chart);
            var watch = Stopwatch.StartNew();
            for (double t = 0; t < chart.Duration; t += Frame) fast.Advance(t, true);
            double fastMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            for (double t = 0; t < chart.Duration; t += Frame) slow.Advance(t, true);
            double slowMs = watch.Elapsed.TotalMilliseconds;
            Assert.That(fast.Resolved, Is.EqualTo(slow.Resolved));
            int frames = (int)(chart.Duration / Frame);
            UnityEngine.Debug.Log($"{chart.Notes.Count} notes, {frames} frames: cursor {fastMs / frames * 1000:F2} µs/frame, full scan {slowMs / frames * 1000:F2} µs/frame");
        }
    }
}

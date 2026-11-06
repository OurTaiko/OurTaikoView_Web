using System;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class TimingStatisticsTests
    {
        static PlaySession Session(params NoteKind[] kinds)
        {
            var chart = new TaikoChart();
            for (int i = 0; i < kinds.Length; i++) chart.Notes.Add(new ChartNote
                { Kind = kinds[i], Time = i + 1, EndTime = i + 1.8, Bpm = 120, BalloonHits = 3 });
            return new PlaySession(chart, .03);
        }
        [Test]
        public void SignedTimesUseActualTargetAndJudgeOffsetIncludingHitBad()
        {
            var session = Session(NoteKind.Don, NoteKind.Ka, NoteKind.BigDon, NoteKind.BigKa);
            double? observed = null;
            session.Judged += (index, judgment) => observed = session.TimingOffsetMs(index);
            Assert.That(session.Hit(false, 1.01), Is.EqualTo(Judgment.Good));
            Assert.That(observed, Is.EqualTo(-20).Within(1e-8));
            Assert.That(session.Hit(true, 2.08), Is.EqualTo(Judgment.Ok));
            Assert.That(session.Hit(false, 3.12), Is.EqualTo(Judgment.Bad));
            Assert.That(session.Hit(true, 4.03), Is.EqualTo(Judgment.Good));
            Assert.That(session.HitTimings.Select(t => t.OffsetMs), Is.EqualTo(new[] { -20.0, 50, 90, 0 }).Within(1e-8));
            Assert.That(session.HitTimings.Select(t => t.NoteIndex), Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(session.Hit(false, 4.04), Is.EqualTo(Judgment.None));
            Assert.That(session.HitTimings.Count, Is.EqualTo(4));
        }
        [Test]
        public void MissesRollsBalloonsEmptyHitsAndAutoPlayHaveNoSample()
        {
            var session = Session(NoteKind.Don, NoteKind.Roll, NoteKind.Balloon, NoteKind.Ka);
            session.Hit(true, .5);
            session.Advance(1.5, false);
            session.Hit(false, 2.1); session.Hit(true, 2.2);
            session.Hit(false, 3.1);
            session.Advance(4.1, true);
            var stats = TimingStatistics.From(session);
            Assert.That(stats.Count, Is.Zero);
            Assert.That(stats.Misses, Is.EqualTo(1));
            Assert.That(stats.MeanMs, Is.Null);
            Assert.That(stats.StandardDeviationMs, Is.Null);
            Assert.That(stats.Bins.Sum(), Is.Zero);
        }
        [Test]
        public void MeanDoesNotHideSpreadAndAbsoluteError()
        {
            var stats = Stats(-20, 20);
            Assert.That(stats.MeanMs, Is.Zero);
            Assert.That(stats.StandardDeviationMs, Is.EqualTo(20));
            Assert.That(stats.MeanAbsoluteMs, Is.EqualTo(20));
            Assert.That((stats.Early, stats.Late, stats.Exact), Is.EqualTo((1, 1, 0)));
            stats = Stats(20, 20);
            Assert.That(stats.MeanMs, Is.EqualTo(20));
            Assert.That(stats.StandardDeviationMs, Is.Zero);
        }
        [Test]
        public void HistogramHasCenteredZeroAndConservesAllBoundarySamples()
        {
            var stats = Stats(-125.125, -2.50001, -2.5, 0, 2.49999, 2.5, 125.125);
            Assert.That(stats.Bins[26], Is.EqualTo(3));
            Assert.That(stats.Bins[25], Is.EqualTo(1));
            Assert.That(stats.Bins[27], Is.EqualTo(1));
            Assert.That(stats.Bins.Sum(), Is.EqualTo(stats.Count));
            Assert.That(Stats(.125).MeanMs, Is.EqualTo(.125), "Statistics never use rounded bins.");
            Assert.That(Stats(.125).StandardDeviationMs, Is.Null);
        }
        [Test]
        public void ResultSnapshotAndPracticeAttemptsDoNotShareMutableSamples()
        {
            var session = Session(NoteKind.Don, NoteKind.Ka);
            session.Hit(false, 1.03);
            var result = PlayResult.From(session, "timing", false);
            session.Hit(true, 2.03);
            Assert.That(result.Timing.Count, Is.EqualTo(1));
            var practice = PlaySession.PracticeAt(session.Chart, 1.5, session, BranchRoute.Normal);
            Assert.That(practice.HitTimings.Count, Is.Zero);
            Assert.That(TimingStatistics.From(practice).Misses, Is.Zero);
            Assert.That(practice.TimingOffsetMs(0), Is.Null);
        }
        [TestCase("Easy", 41.7083358764648, 108.441665649414, 125.125)]
        [TestCase("Normal", 41.7083358764648, 108.441665649414, 125.125)]
        [TestCase("Oni", 25.0250015258789, 75.0750045776367, 108.441665649414)]
        public void StatisticsCarryActualCourseWindows(string course, double good, double ok, double bad)
        {
            var data = TimingStatistics.From(new PlaySession(new TaikoChart { Course = course }));
            Assert.That(data.GoodWindowMs, Is.EqualTo(good).Within(1e-8));
            Assert.That(data.OkWindowMs, Is.EqualTo(ok).Within(1e-8));
            Assert.That(data.BadWindowMs, Is.EqualTo(bad).Within(1e-8));
        }
        [Test]
        public void SkippingSettlesImmediatelyAndNeverAutomaticallyLeaves()
        {
            var sequence = new ResultSequence(new PlayResult { Score = 1000000, KiwamiThreshold = 1000000,
                IsClear = true, IsGaugeFull = true, GaugePoints = 10000 });
            sequence.Update(ResultSequence.FadeInEndMs + 1);
            Assert.That(sequence.Skip(), Is.True);
            sequence.Update(ResultSequence.FadeInEndMs + 1);
            Assert.That(sequence.CanAdvance, Is.True);
            Assert.That(sequence.GaugeShown, Is.EqualTo(50));
            double end = sequence.RevealEndMs;
            sequence.Update(1e9);
            Assert.That(sequence.ShouldAutoAdvance, Is.False);
            Assert.That(sequence.RevealEndMs, Is.EqualTo(end));
            Assert.That(sequence.Update(1e9 + 1), Is.Empty, "No replay of reveal cues.");
        }
        static TimingStatistics Stats(params double[] values) => new TimingStatistics(values.Select((value, i) =>
            new HitTiming(i, value, Judgment.Good)), 0, 25.025, 75.075, 108.442);

        [Test]
        public void LaneRetargetingRecordsTheActuallyJudgedNote()
        {
            var chart = new TaikoChart();
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Don, Time = 1, Bpm = 120 });
            chart.Notes.Add(new ChartNote { Kind = NoteKind.Don, Time = 1.1, Bpm = 120 });
            var session = new PlaySession(chart);
            session.Hit(false, 1.09);
            Assert.That(session.HitTimings.Single().NoteIndex, Is.EqualTo(1));
            Assert.That(session.HitTimings.Single().OffsetMs, Is.EqualTo(-10).Within(1e-8));
            session.Advance(1.2, false);
            Assert.That(TimingStatistics.From(session).Misses, Is.EqualTo(1));
        }
        [Test]
        public void UnplayedBranchRoutesDoNotProduceSamplesOrMisses()
        {
            var chart = new TaikoChart();
            chart.Branches.Add(new ChartBranch { Id = 0, Time = 1, EndTime = 2 });
            for (int i = 0; i < 3; i++) chart.Notes.Add(new ChartNote
                { Kind = NoteKind.Don, Time = 1, Bpm = 120, BranchId = 0, Route = (BranchRoute)i });
            var session = new PlaySession(chart, 0, BranchRoute.Expert);
            session.Hit(false, 1);
            session.Advance(2, false);
            Assert.That(session.HitTimings.Single().NoteIndex, Is.EqualTo(1));
            Assert.That(TimingStatistics.From(session).Misses, Is.Zero);
        }
    }
}

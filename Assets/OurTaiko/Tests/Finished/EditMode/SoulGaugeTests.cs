using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class SoulGaugeTests
    {
        // OpenTaiko CAct演奏ゲージ共通.Init over 100 notes: the stars alone pick the rate
        // (★0–7 70.7%, ★8 70%, ★9 and above 75%) and the miss cost (0.625 or 2 良 increments).
        // Expected values come from a verbatim float port of OpenTaiko's code.
        [TestCase("Easy", 1, 141.44, 406.64)]
        [TestCase("Easy", 6, 141.44, 406.64)]
        [TestCase("Easy", 10, 133.33, 199.99)]
        [TestCase("Normal", 7, 141.44, 406.64)]
        [TestCase("Normal", 8, 142.85, 410.69)]
        [TestCase("Hard", 3, 141.44, 406.64)]
        [TestCase("Hard", 9, 133.33, 199.99)]
        [TestCase("Oni", 8, 142.85, 410.69)]
        [TestCase("Oni", 10, 133.33, 199.99)]
        [TestCase("Edit", 13, 133.33, 199.99)]
        public void StarsControlEachJudgment(string course, int level, double firstGood, double mixedTotal)
        {
            var gauge = new SoulGauge(100, course, level);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(firstGood).Within(1e-9));
            gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Good);
            gauge.ApplyJudgment(Judgment.Ok); gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.EqualTo(mixedTotal).Within(1e-9));
            double previous = gauge.Points;
            gauge.ApplyJudgment(Judgment.None); gauge.ApplyJudgment(Judgment.Roll);
            Assert.That(gauge.Points, Is.EqualTo(previous));
        }

        // Stars the official game never gives a course (Hard ★9, Normal ★8, Easy ★6…) once froze
        // the gauge at zero.
        [Test]
        public void EveryCourseAndStarMovesTheGauge()
        {
            foreach (var course in new[] { "Easy", "Normal", "Hard", "Oni", "Edit" })
                for (int level = -1; level <= 13; level++)
                {
                    var gauge = new SoulGauge(100, course, level);
                    gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Good);
                    double raised = gauge.Points;
                    Assert.That(raised, Is.GreaterThan(0), course + " ★" + level);
                    gauge.ApplyJudgment(Judgment.Bad);
                    Assert.That(gauge.Points, Is.LessThan(raised), course + " ★" + level);
                }
        }

        [TestCase("Easy")]
        [TestCase("Normal")]
        [TestCase("Hard")]
        [TestCase("Oni")]
        [TestCase("Edit")]
        public void AbsentLevelKeepsTheOniClearTierAndUsesStarZeroIncrements(string course)
        {
            var session = new PlaySession(new TaikoChart { Course = course });
            Assert.That(session.ClearThreshold, Is.EqualTo(0.8));
            var gauge = new SoulGauge(100, course, 0);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(141.44).Within(1e-9));
        }

        [Test]
        public void RoutesWithoutNotesReuseTheLastFiniteRouteTimesThree()
        {
            var empty = new SoulGauge(new[] { 0, 0, 0 }, "Oni", 10);
            Assert.That(empty.Increment(Judgment.Good), Is.EqualTo(1.2f));
            Assert.That(empty.Increment(Judgment.Ok), Is.EqualTo(0.6f));
            Assert.That(empty.Increment(Judgment.Bad), Is.EqualTo(-2.4f));
            var gap = new SoulGauge(new[] { 10, 0, 20 }, "Oni", 10);
            Assert.That(gap.Increment(Judgment.Good, BranchRoute.Normal), Is.EqualTo(13.3333f));
            Assert.That(gap.Increment(Judgment.Good, BranchRoute.Expert), Is.EqualTo(40f).Within(1e-4));
            Assert.That(gap.Increment(Judgment.Good, BranchRoute.Master), Is.EqualTo(6.6666f));
        }

        [Test]
        public void ClearAndFullComeFromRoundedHitsAndBadJudgmentsClampPerHit()
        {
            var gauge = new SoulGauge(100, "Easy", 1);
            gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.Zero);
            for (int i = 0; i < 42; i++) gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Percent, Is.EqualTo(59));
            Assert.That(gauge.IsClear, Is.False);
            gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(6081.92).Within(1e-9));
            Assert.That(gauge.IsClear, Is.True);
            for (int i = 0; i < 28; i++) gauge.ApplyJudgment(Judgment.Good);
            Assert.That(gauge.Points, Is.EqualTo(10000));
            Assert.That(gauge.IsFull, Is.True);
            gauge.ApplyJudgment(Judgment.Good); gauge.ApplyJudgment(Judgment.Bad);
            Assert.That(gauge.Points, Is.EqualTo(9911.6).Within(1e-9), "An extra full-gauge hit cannot bank progress above 100%.");
            Assert.That(gauge.IsFull, Is.False);
        }

        [Test]
        public void ManualBadAndMissApplyTheSamePenaltyAndRestartClearsTheState()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:8\n#START\n1111,\n#END");
            var session = new PlaySession(chart);
            session.Hit(false, 0); session.Hit(false, 0.5); session.Hit(false, 1.06);
            // Oni ★8 over four notes: 良 35.7142%, 可 17.8571%, 不可 −22.3214% (1.06 s is a 可).
            Assert.That(session.GaugePoints, Is.EqualTo(8928.55).Within(1e-9));
            Assert.That(session.Hit(false, 1.6), Is.EqualTo(Judgment.Bad));
            Assert.That(session.GaugePoints, Is.EqualTo(6696.41).Within(1e-9));
            var missed = new PlaySession(chart);
            missed.Hit(false, 0); missed.Hit(false, 0.5); missed.Hit(false, 1.06);
            missed.Advance(1.7, false);
            Assert.That(missed.GaugePoints, Is.EqualTo(session.GaugePoints));
            Assert.That(missed.GaugePercent, Is.EqualTo(66));
            var restarted = new PlaySession(chart);
            Assert.That(restarted.Score, Is.Zero); Assert.That(restarted.GaugePoints, Is.Zero);
        }

        // OpenTaiko UpdateGauge: a note inside a branch section uses its own route's increments,
        // any other note the route being played.
        [Test]
        public void EachRouteFillsTheGaugeFromItsOwnNoteCount()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:10\n#START\n1000,\n0000,\n0000,\n"
                + "#BRANCHSTART p,101,102\n#N\n1111,\n#E\n1111,\n#M\n11111111,\n#BRANCHEND\n1000,\n#END");
            var statistics = new ChartStatistics(chart);
            Assert.That(statistics.RouteNotes, Is.EqualTo(new[] { 6, 6, 10 }));
            var session = new PlaySession(chart);
            session.Advance(20, true);
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
            Assert.That(session.GaugePoints, Is.EqualTo(10000), "Six 良 on a six-note route fill the gauge.");
            var master = new PlaySession(chart, 0, BranchRoute.Master);
            master.Advance(20, true);
            var expected = new SoulGauge(new[] { 6, 6, 10 }, "Oni", 10);
            // The opening note is played before any branch, on Normal; the closing one after it, on Master.
            expected.ApplyJudgment(Judgment.Good, BranchRoute.Normal);
            for (int i = 0; i < 9; i++) expected.ApplyJudgment(Judgment.Good, BranchRoute.Master);
            Assert.That(master.GaugePoints, Is.EqualTo(expected.Points));
        }
    }
}

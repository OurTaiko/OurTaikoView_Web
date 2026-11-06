using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class ShinuchiTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "BPM:120\nCOURSE:Oni\nLEVEL:8\n" + metadata + "\n#START\n" + body + "\n#END");

        [Test]
        public void OrdinaryJudgmentsUseTenPointRoundingWithoutComboOrGogoMultipliers()
        {
            var chart = Parse("#GOGOSTART\n1340,");
            var session = new PlaySession(chart);
            Assert.That(session.BaseScore, Is.EqualTo(333340));
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(session.Hit(false, 0.56), Is.EqualTo(Judgment.Ok));
            session.Advance(1.2, false);
            Assert.That(session.Score, Is.EqualTo(500010));
            Assert.That(new[] { session.Good, session.Ok, session.Bad }, Is.EqualTo(new[] { 1, 1, 1 }));
            var allGood = new PlaySession(chart);
            allGood.Advance(3, true);
            Assert.That(allGood.Score, Is.EqualTo(1000020), "Rounding can exceed one million; do not cap the final score.");
        }

        [Test]
        public void LongNoteBudgetAndActualHitsFollowShinuchi()
        {
            var chart = Parse("#GOGOSTART\n1234,\n5008,\n7008,\n9008,", "BALLOON:150,2");
            var session = new PlaySession(chart);
            Assert.That(session.BaseScore, Is.EqualTo(246820), "Only the first 100 required hits of each balloon enter the base-score budget.");
            session.Hit(false, 0); session.Hit(true, 0.56);
            session.Hit(false, 1); session.Hit(true, 1.5);
            Assert.That(session.Score, Is.EqualTo(863870));
            double gauge = session.GaugePoints;
            session.Hit(false, 2.1); session.Hit(true, 2.1);
            Assert.That(session.Hit(true, 4.1), Is.EqualTo(Judgment.None));
            for (int i = 0; i < 150; i++) session.Hit(false, 4.1);
            Assert.That(session.Resolved[5], Is.True);
            Assert.That(session.Hit(false, 4.1), Is.EqualTo(Judgment.None));
            session.Hit(false, 6.1); session.Hit(false, 6.1);
            Assert.That(session.Resolved[6], Is.True);
            Assert.That(session.Rolls, Is.EqualTo(154));
            Assert.That(session.Score, Is.EqualTo(879270), "Every long-note hit adds 100, without a pop bonus.");
            Assert.That(session.GaugePoints, Is.EqualTo(gauge), "Long notes never change the soul gauge.");
        }

        [TestCase("5008,", 1.5)]
        [TestCase("5000,\n0080,", 3)]
        [TestCase("#BARLINEOFF\n5000,\n0080,", 3)]
        [TestCase("6000,\n0080,", 3)]
        [TestCase("50\n08,", 1.5)]
        [TestCase("50\n#BPMCHANGE 240\n08,", 1.25)]
        [TestCase("5000,\n#DELAY 0.25\n0080,", 3.25)]
        [TestCase("5000\n,\n#DELAY 0.25\n0080,", 3.25)]
        [TestCase("5000,\n,\n0080,", 5)]
        public void RollBudgetUsesTheLinkedTailAcrossMeasuresAndSourceParts(string body, double duration)
        {
            var chart = Parse(body, "OFFSET:0.5");
            var roll = chart.Notes.Single();
            Assert.That(new ChartStatistics(chart).DrumrollMilliseconds, Is.EqualTo(duration * 1000).Within(1e-9));
            Assert.That(roll.EndTime, Is.EqualTo(duration - 0.5).Within(1e-9));
            Assert.That(NoteScroll.RollLength(roll, 1302), Is.EqualTo(duration * 120 / 240 * 1302).Within(1e-9));
        }

        [TestCase("1000,\n5000,\n0080,\n1000,")]
        [TestCase("#BARLINEOFF\n1000,\n50\n00,\n00\n80,\n1000,")]
        [TestCase("1000,\n5000\n,\n0080,\n1000,")]
        public void FullRollDurationReducesBaseScoreRegardlessOfBarsOrLineBreaks(string body)
        {
            var session = new PlaySession(Parse(body));
            Assert.That(session.BaseScore, Is.EqualTo(497470), "Budget the full three seconds, not only the two seconds to the next bar.");
            session.Hit(false, 0); session.Hit(false, 6);
            Assert.That(session.Score, Is.EqualTo(994940));
        }

        [Test]
        public void CommonAndMasterTotalsStayFixedWhenNormalIsSelected()
        {
            var chart = Parse("1000,\n0000,\n0000,\n#BRANCHSTART p,101,102\n#N\n1111,\n#E\n7008,\n#M\n1100,\n6000,\n0080,\n7008,\n#BRANCHEND\n1000,", "BALLOONMAS:3");
            var statistics = new ChartStatistics(chart);
            Assert.That(statistics.JudgeableNotes, Is.EqualTo(4));
            Assert.That(statistics.BalloonHitBudget, Is.EqualTo(3));
            Assert.That(statistics.DrumrollMilliseconds, Is.EqualTo(3000));
            var session = new PlaySession(chart);
            Assert.That(session.BaseScore, Is.EqualTo(248660));
            session.Advance(20, true);
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
            Assert.That(session.Good, Is.EqualTo(6));
            Assert.That(session.Rolls, Is.Zero, "Unselected Master balloons and rolls enter only the fixed budget.");
            Assert.That(session.Score, Is.EqualTo(1491960));
            Assert.That(session.GaugePoints, Is.EqualTo(10000));
        }

        [Test]
        public void ChartsWithoutOrdinaryNotesUseReferenceFallbackAndMissingBalloonCountsUseFive()
        {
            var session = new PlaySession(Parse("7008,\n9008,"));
            Assert.That(session.BaseScore, Is.EqualTo(1000000));
            Assert.That(session.Chart.Notes.Select(n => n.BalloonHits), Is.EqualTo(new[] { 5, 5 }), "OpenTaiko's default");
            for (int i = 0; i < 5; i++) session.Hit(false, 0.1);
            for (int i = 0; i < 5; i++) session.Hit(false, 2.1);
            Assert.That(session.Resolved.All(resolved => resolved), Is.True);
            Assert.That(session.Score, Is.EqualTo(1000));
            Assert.That(session.GaugePoints, Is.Zero);
            Assert.That(new PlaySession(Parse("0000,")).BaseScore, Is.EqualTo(1000000));
        }

        [Test]
        public void NonpositiveBalloonCountDoesNotCreateAnAutomaticPopBonus()
        {
            var session = new PlaySession(Parse("7008,", "BALLOON:0"));
            session.Hit(false, 0.1); session.Hit(false, 0.1);
            Assert.That(session.Resolved[0], Is.False);
            Assert.That(session.Score, Is.EqualTo(200));
            session.Advance(2, false);
            Assert.That(session.Resolved[0], Is.True);
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.GaugePoints, Is.Zero);
        }
    }
}

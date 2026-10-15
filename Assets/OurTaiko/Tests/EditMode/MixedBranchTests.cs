using System.Collections.Generic;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // p, r and s branches in one chart: every condition counts the same play and reads its own
    // statistic. As in OpenTaiko, counts carry over decisions and #SECTION clears all but s.
    public sealed class MixedBranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Mixed Branch\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";

        // At BPM 120 each measure is 2 s; a branch decides at the bar line before it.
        //   lead      0–6 s   1111 (notes at 0–1.5 s)
        //   p,50,75   6–8 s   普通 1000 / 玄人 and 達人 a drumroll 6–7.5 s     decides at 4 s
        //   r,3,5    12–14 s  普通 1000 / 玄人 1100 / 達人 1111                 decides at 10 s
        //   s        18–20 s  1000 / 2000 / 3000                                decides at 16 s
        static TaikoChart Chain(string score) => Parse(
            "1111,\n0000,\n0000,\n" +
            "#BRANCHSTART p,50,75\n#N\n1000,\n#E\n5008,\n#M\n5008,\n#BRANCHEND\n0000,\n0000,\n" +
            "#BRANCHSTART r,3,5\n#N\n1000,\n#E\n1100,\n#M\n1111,\n#BRANCHEND\n0000,\n0000,\n" +
            "#BRANCHSTART " + score + "\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND\n");

        static List<double> Values(PlaySession session)
        {
            var values = new List<double>();
            session.BranchSelected += (_, __) => values.Add(session.LastBranchValue);
            return values;
        }

        [Test] public void EachBranchJudgesThePlayOnTheRouteBeforeIt()
        {
            var chart = Chain("s,200000,300000");
            var session = new PlaySession(chart);
            var values = Values(session);
            // p: three of four lead notes → 75% → 達人, whose drumroll then follows.
            for (int i = 0; i < 3; i++) session.Hit(false, i * 0.5);
            // r: four drumroll hits → 玄人, whose two notes then follow.
            for (int i = 0; i < 4; i++) session.Hit(i % 2 == 1, 6.1 + i * 0.1);
            // s: the whole score — three lead 良, four drumroll hits and both 玄人 notes.
            session.Hit(false, 12); session.Hit(false, 12.5);
            session.Advance(chart.Branches[2].DecisionTime, false);

            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Expert, BranchRoute.Master }));
            Assert.That(values, Is.EqualTo(new double[] { 75, 4, 5 * session.BaseScore + 400 }));
            Assert.That(session.Score, Is.EqualTo(5 * session.BaseScore + 400));
        }

        [Test] public void AChangedEarlierRouteChangesWhatLaterBranchesCount()
        {
            var chart = Chain("s,200000,300000");
            var session = new PlaySession(chart);
            var values = Values(session);
            // p: 50% → 玄人 (also a drumroll); r: five hits → 達人; s: everything so far.
            session.Hit(false, 0); session.Hit(false, 0.5);
            for (int i = 0; i < 5; i++) session.Hit(false, 6.1 + i * 0.1);
            for (int i = 0; i < 4; i++) session.Hit(false, 12 + i * 0.5);
            session.Advance(chart.Branches[2].DecisionTime, false);

            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Expert, BranchRoute.Master, BranchRoute.Master }));
            Assert.That(values, Is.EqualTo(new double[] { 50, 5, 6 * session.BaseScore + 500 }));
        }

        [Test] public void AutoPlayReachesMasterOnEveryCondition()
        {
            // Autoplay hits drumrolls 15 times a second: 1.5 s gives 23 hits.
            var chart = Chain("s,1,2");
            var session = new PlaySession(chart);
            var values = Values(session);
            session.Advance(chart.Branches[2].DecisionTime, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master, BranchRoute.Master }));
            Assert.That(values, Is.EqualTo(new double[] { 100, 23, session.Score }));
        }

        // The same lead play (two 良, then three drumroll hits) seen by each condition.
        [TestCase("p,50,75", 100.0)]
        [TestCase("r,1,3", 3.0)]
        [TestCase("s,1,2", -1.0)]   // two base scores plus 300
        public void EachConditionReadsItsOwnStatisticFromTheSamePlay(string condition, double expected)
        {
            var chart = Parse("1100,\n5008,\n0000,\n#BRANCHSTART " + condition + Routes);
            var session = new PlaySession(chart);
            session.Hit(false, 0); session.Hit(false, 0.5);
            for (int i = 0; i < 3; i++) session.Hit(false, 2.1 + i * 0.1);
            session.Advance(chart.Branches[0].DecisionTime, false);
            if (expected < 0) expected = 2 * session.BaseScore + 300;
            Assert.That(session.LastBranchValue, Is.EqualTo(expected));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Master));
        }

        [TestCase("p,50,75")]
        [TestCase("r,1,3")]
        [TestCase("s,1,2")]
        public void SectionClearsEveryConditionButScore(string condition)
        {
            // Two 良 and three drumroll hits (drumroll 1–2.5 s) all before #SECTION at 2 s.
            string lead = "1150,\n#SECTION\n0800,\n0000,";
            var withSection = new PlaySession(Parse(lead + "\n#BRANCHSTART " + condition + Routes));
            var without = new PlaySession(Parse(lead.Replace("#SECTION\n", "") + "\n#BRANCHSTART " + condition + Routes));
            foreach (var session in new[] { withSection, without })
            {
                session.Hit(false, 0); session.Hit(false, 0.5);
                for (int i = 0; i < 3; i++) session.Hit(false, 1.1 + i * 0.1);
                session.Advance(session.Chart.Branches[0].DecisionTime, false);
            }
            Assert.That(without.LastBranchValue, Is.GreaterThan(0));
            Assert.That(without.CurrentBranch, Is.Not.EqualTo(BranchRoute.Normal));
            if (condition.StartsWith("s"))
            {
                Assert.That(withSection.LastBranchValue, Is.EqualTo(without.LastBranchValue));
                return;
            }
            Assert.That(withSection.LastBranchValue, Is.Zero);
            Assert.That(withSection.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
        }

        [TestCase(BranchRoute.Normal)]
        [TestCase(BranchRoute.Expert)]
        [TestCase(BranchRoute.Master)]
        public void AFixedRouteIgnoresEveryCondition(BranchRoute route)
        {
            var chart = Chain("s,1,2");
            var session = new PlaySession(chart, 0, route);
            var values = Values(session);
            session.Advance(chart.Branches[2].DecisionTime, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { route, route, route }));
            Assert.That(values, Is.EqualTo(new double[] { 0, 0, 0 }));
        }
    }
}

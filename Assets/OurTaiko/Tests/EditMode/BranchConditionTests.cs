using NUnit.Framework;

namespace OurTaiko.Tests
{
    // OpenTaiko's #BRANCHSTART conditions beyond p, r and s: pp, jp/d, jg, jb, rb, upper-case
    // big-only types, the `l` range and an unreadable condition that keeps the current route.
    // At BPM 120 a branch after three lead measures starts at 6 s.
    public sealed class BranchConditionTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Branch Conditions\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static PlaySession Session(string condition, string lead, string metadata = "") =>
            new PlaySession(Parse(lead + "\n#BRANCHSTART " + condition + Routes, metadata));
        static void Decide(PlaySession session) => session.Advance(session.Chart.Branches[0].DecisionTime, false);
        static ChartBranch Branch(string condition) => Parse("1111,\n#BRANCHSTART " + condition + Routes).Branches[0];

        [TestCase("p,50,80", BranchCondition.Accuracy, false, false)]
        [TestCase("p50,80", BranchCondition.Accuracy, false, false)]
        [TestCase("p 50, 80", BranchCondition.Accuracy, false, false)]
        [TestCase("P,50,80", BranchCondition.Accuracy, true, false)]
        [TestCase("pp,50,80", BranchCondition.PercentPerfect, false, false)]
        [TestCase("jp,1,2", BranchCondition.JudgePerfect, false, false)]
        [TestCase("d,1,2", BranchCondition.JudgePerfect, true, false)]
        [TestCase("JG,1,2,m", BranchCondition.JudgeGood, true, false)]
        [TestCase("jb,2,1,l", BranchCondition.JudgeBad, false, true)]
        [TestCase("rb,1,2", BranchCondition.BalloonHits, false, false)]
        [TestCase("R,1,2", BranchCondition.Drumroll, true, false)]
        [TestCase("S,1,2", BranchCondition.Score, false, false)]
        public void ConditionsAreRead(string condition, BranchCondition expected, bool bigOnly, bool less)
        {
            var branch = Branch(condition);
            Assert.That((branch.Condition, branch.BigOnly, branch.Less), Is.EqualTo((expected, bigOnly, less)));
        }

        [TestCase("")]
        [TestCase("x,1,2")]
        [TestCase("p,50")]
        [TestCase("p,a,80")]
        [TestCase("p,50,80,q")]
        public void UnreadableConditionsKeepTheCurrentRoute(string condition)
        {
            var chart = Parse("1111,\n#BRANCHSTART " + condition + Routes);
            Assert.That(chart.Branches[0].Condition, Is.EqualTo(BranchCondition.None));
            if (condition.Length > 0) Assert.That(chart.Warnings, Has.Some.StartsWith("#BRANCHSTART"));
        }

        [Test] public void AnEmptyConditionStaysOnTheRoutePlayedSoFar()
        {
            var first = Session("", "1111,\n0000,\n0000,");
            first.Advance(first.Chart.Branches[0].DecisionTime, true);
            Assert.That(first.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,0,0" + Routes + "0000,\n0000,\n#BRANCHSTART" + Routes);
            var later = new PlaySession(chart);
            later.Advance(chart.Branches[1].DecisionTime, true);
            Assert.That(later.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
        }

        // Lead notes at 0, 0.5, 1 and 1.5 s: two 良, one 可 (50 ms late), one missed.
        static void Mixed(PlaySession session) { session.Hit(false, 0); session.Hit(false, 0.5); session.Hit(false, 1.05); }

        [TestCase("pp,50,75", 50, BranchRoute.Expert)]
        [TestCase("jp,2,3", 2, BranchRoute.Expert)]
        [TestCase("jg,1,2", 1, BranchRoute.Expert)]
        [TestCase("jb,1,2", 1, BranchRoute.Expert)]
        [TestCase("p,50,62", 62, BranchRoute.Master)]
        public void JudgementConditionsCountTheirResult(string condition, double value, BranchRoute expected)
        {
            var session = Session(condition, "1111,\n0000,\n0000,");
            Mixed(session);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(value));
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
        }

        [TestCase(0, BranchRoute.Master)]
        [TestCase(1, BranchRoute.Expert)]
        [TestCase(2, BranchRoute.Normal)]
        public void LessRangeRewardsFewerBads(int missed, BranchRoute expected)
        {
            var session = Session("jb,2,1,l", "1111,\n0000,\n0000,");
            for (int i = 0; i < 4 - missed; i++) session.Hit(false, i * 0.5);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(missed));
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
        }

        [Test] public void UpperCaseCountsOnlyBigNotes()
        {
            // Big don, big ka, don, ka: both big notes 良, both small ones missed.
            string lead = "3412,\n0000,\n0000,";
            var big = Session("P,50,100", lead);
            var every = Session("p,50,100", lead);
            var perfect = Session("d,1,2", lead);
            foreach (var session in new[] { big, every, perfect })
            {
                session.Hit(false, 0); session.Hit(true, 0.5);
                Decide(session);
            }
            Assert.That(big.LastBranchValue, Is.EqualTo(100)); Assert.That(big.CurrentBranch, Is.EqualTo(BranchRoute.Master));
            Assert.That(every.LastBranchValue, Is.EqualTo(50)); Assert.That(every.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
            Assert.That(perfect.LastBranchValue, Is.EqualTo(2));
        }

        [Test] public void BalloonHitsCountForRbAndKusudamaForUpperCaseRb()
        {
            // Balloon 0–1.5 s (3 hits), kusudama 2–3.5 s (2 hits so far).
            string lead = "7008,\n9008,\n0000,";
            var all = Session("rb,4,5", lead, "BALLOON:3,5");
            var kusudama = Session("RB,1,3", lead, "BALLOON:3,5");
            var rolls = Session("r,1,2", lead, "BALLOON:3,5");
            foreach (var session in new[] { all, kusudama, rolls })
            {
                for (int i = 0; i < 3; i++) session.Hit(false, 0.1 + i * 0.1);
                for (int i = 0; i < 2; i++) session.Hit(false, 2.1 + i * 0.1);
                Decide(session);
            }
            Assert.That(all.LastBranchValue, Is.EqualTo(5)); Assert.That(all.CurrentBranch, Is.EqualTo(BranchRoute.Master));
            Assert.That(kusudama.LastBranchValue, Is.EqualTo(2)); Assert.That(kusudama.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
            Assert.That(rolls.LastBranchValue, Is.EqualTo(5), "r counts balloon hits too");
        }

        [Test] public void UpperCaseDrumrollCountsOnlyBigDrumrollsAndKusudama()
        {
            // Big drumroll 0–1.5 s, small drumroll 2–3.5 s, balloon 4–5.5 s, kusudama 6–7.5 s.
            var session = Session("R,4,6", "6008,\n5008,\n7008,\n9008,\n0000,", "BALLOON:2,5");
            for (int i = 0; i < 3; i++) session.Hit(false, 0.1 + i * 0.1);
            for (int i = 0; i < 4; i++) session.Hit(false, 2.1 + i * 0.1);
            for (int i = 0; i < 2; i++) session.Hit(false, 4.1 + i * 0.1);
            for (int i = 0; i < 2; i++) session.Hit(false, 6.1 + i * 0.1);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(5));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
        }
    }
}

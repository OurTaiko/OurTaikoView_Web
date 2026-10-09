using System;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    // Parsing is identical for every mode; PlaySession decides whether a branch is evaluated
    // (normal play) or fixed to one route (practice).
    public sealed class BranchRouteTests
    {
        static TaikoChart Parse(string body) => TjaParser.Parse("BPM:240\nCOURSE:Oni\nLEVEL:10\n#START\n1111,\n" + body + "\n1111,\n#END");
        const string NoMaster = "#BRANCHSTART p,50,80\n#N\n1000,\n#E\n2020,\n#BRANCHEND";
        const string OnlyNormal = "#BRANCHSTART p,50,80\n#N\n1000,\n#BRANCHEND";
        const string Score = "#BRANCHSTART s,100,200\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND";
        const string Master = "#BRANCHSTART p,-1,0\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND";

        [TestCase(false)]
        [TestCase(true)]
        public void CommonSectionsDisplayNormalWithoutChangingSelectedNotes(bool auto)
        {
            var chart = Parse(Master + "\n1111,\n" + Master.Replace("p,-1,0", "p,0,101"));
            var session = new PlaySession(chart);
            var first = chart.Branches[0];
            var second = chart.Branches[1];
            session.Advance(first.DecisionTime, auto);
            Assert.That(session.SelectedRoute(0), Is.EqualTo(BranchRoute.Master));
            Assert.That(session.DisplayBranchAt(first.Time - 1e-6), Is.EqualTo(BranchRoute.Normal),
                "An early branch decision must not recolour the preceding common measures.");
            session.Advance(second.DecisionTime, auto);
            Assert.That(session.DisplayBranchAt(first.Time), Is.EqualTo(BranchRoute.Master));
            Assert.That(session.DisplayBranchAt(first.EndTime - 1e-6), Is.EqualTo(BranchRoute.Master));
            Assert.That(session.DisplayBranchAt(first.EndTime), Is.EqualTo(BranchRoute.Normal));
            Assert.That(session.DisplayBranchAt(second.Time - 1e-6), Is.EqualTo(BranchRoute.Normal));
            Assert.That(session.DisplayBranchAt(second.Time), Is.EqualTo(BranchRoute.Expert));
            Assert.That(session.DisplayBranchAt(second.EndTime), Is.EqualTo(BranchRoute.Normal));
            Assert.That(session.IsActive(chart.Notes.First(n => n.BranchId == 0 && n.Route == BranchRoute.Master)), Is.True,
                "Changing the lane label must preserve the previous route's remaining notes and judgments.");
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Expert }));
        }

        [TestCase(BranchRoute.Expert)]
        [TestCase(BranchRoute.Master)]
        public void PracticeDisplayFollowsTheSectionWhenSeekingBothWays(BranchRoute route)
        {
            var chart = Parse(Master);
            var branch = chart.Branches[0];
            var session = PlaySession.PracticeAt(chart, branch.EndTime, forcedBranch: route);
            Assert.That(session.DisplayBranchAt(branch.EndTime), Is.EqualTo(BranchRoute.Normal));
            session = PlaySession.PracticeAt(chart, branch.Time, session, route);
            Assert.That(session.DisplayBranchAt(branch.Time), Is.EqualTo(route));
            session = PlaySession.PracticeAt(chart, branch.Time - 1e-6, session, route);
            Assert.That(session.DisplayBranchAt(branch.Time - 1e-6), Is.EqualTo(BranchRoute.Normal));
        }

        [Test]
        public void ConsecutiveBranchesSwitchDirectlyWithoutACommonSection()
        {
            var chart = Parse(Master + "\n" + Master.Replace("p,-1,0", "p,0,101"));
            var session = new PlaySession(chart);
            session.Advance(chart.Duration, true);
            double boundary = chart.Branches[0].EndTime;
            Assert.That(chart.Branches[1].Time, Is.EqualTo(boundary));
            Assert.That(session.DisplayBranchAt(boundary - 1e-6), Is.EqualTo(BranchRoute.Master));
            Assert.That(session.DisplayBranchAt(boundary), Is.EqualTo(BranchRoute.Expert));
        }

        [Test]
        public void ParserRecordsOmittedRoutesAndScoreConditions()
        {
            Assert.That(Parse(NoMaster).Branches[0].Routes, Is.EqualTo(new[] { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Expert }));
            Assert.That(Parse(OnlyNormal).Branches[0].Routes, Is.EqualTo(new[] { BranchRoute.Normal, BranchRoute.Normal, BranchRoute.Normal }));
            var score = Parse(Score).Branches[0];
            Assert.That(score.Condition, Is.EqualTo(BranchCondition.Score));
            Assert.That(score.HasAllRoutes, Is.True);
            Assert.Throws<FormatException>(() => Parse("#BRANCHSTART p,50,80\n#E\n1000,\n#BRANCHEND"), "#N is still required.");
            Assert.Throws<NotSupportedException>(() => Parse("#BRANCHSTART x,1,2\n#N\n1000,\n#BRANCHEND"));
        }

        [TestCase(NoMaster)]
        [TestCase(OnlyNormal)]
        public void NormalPlayRefusesWhatItCannotEvaluate(string body)
        {
            var chart = Parse(body);
            Assert.Throws<NotSupportedException>(() => new PlaySession(chart));
            Assert.Throws<NotSupportedException>(() => PlaySession.PracticeAt(chart, 0));
            Assert.DoesNotThrow(() => new PlaySession(chart, 0, BranchRoute.Master));
        }

        [Test]
        public void NormalPlayEvaluatesScoreBranches()
        {
            var chart = Parse(Score);
            Assert.DoesNotThrow(() => new PlaySession(chart));
            Assert.DoesNotThrow(() => PlaySession.PracticeAt(chart, 0));
        }

        [TestCase(BranchRoute.Normal, BranchRoute.Normal)]
        [TestCase(BranchRoute.Expert, BranchRoute.Expert)]
        [TestCase(BranchRoute.Master, BranchRoute.Expert)]
        public void FixedRouteDrawsTheRouteStandingIn(BranchRoute requested, BranchRoute drawn)
        {
            var chart = Parse(NoMaster);
            var session = new PlaySession(chart, 0, requested);
            Assert.That(chart.Notes.Where(n => n.BranchId >= 0).All(n => session.IsPracticePreviewActive(n) == (n.Route == drawn)), Is.True);
            for (double t = 0; t < 4; t += 1 / 120.0) session.Advance(t, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { drawn }));
            Assert.That(session.Good, Is.EqualTo(8 + chart.Notes.Count(n => n.BranchId >= 0 && n.Route == drawn)));
        }

        // The Shinuchi base and soul gauge count the common part plus the route standing in for #M.
        [Test]
        public void StatisticsCountTheRouteStandingInForMaster()
        {
            var withMaster = Parse("#BRANCHSTART p,50,80\n#N\n1000,\n#E\n2020,\n#M\n2020,\n#BRANCHEND");
            Assert.That(new ChartStatistics(Parse(NoMaster)).JudgeableNotes, Is.EqualTo(new ChartStatistics(withMaster).JudgeableNotes));
            Assert.That(new ChartStatistics(Parse(OnlyNormal)).JudgeableNotes, Is.EqualTo(9));
        }
    }
}

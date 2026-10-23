using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class BranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Branch Test\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static TaikoChart Accuracy(string condition = "p,50,80", string lead = "1111,\n0000,\n0000,", string metadata = "") =>
            Parse(lead + "\n#BRANCHSTART " + condition + Routes, metadata);

        [Test] public void RoutesShareStartAndRestoreTiming()
        {
            var chart = Parse("7008,\n#BRANCHSTART p,50,80\n#N\n#BPMCHANGE 240\n#SCROLL 2\n#MEASURE 3/4\n#DELAY 0.25\n#BARLINEOFF\n7008,\n#E\n7008,\n#M\n7008,\n#BRANCHEND\n7008,", "BALLOON:2,3,4");
            var n = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Normal);
            var e = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Expert);
            var m = chart.Notes.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Master);
            Assert.That(n.Time, Is.EqualTo(2.25)); Assert.That(n.Bpm, Is.EqualTo(240));
            Assert.That(n.ScrollX, Is.EqualTo(2));
            Assert.That(e.Time, Is.EqualTo(2)); Assert.That(e.Bpm, Is.EqualTo(120));
            Assert.That(e.ScrollX, Is.EqualTo(1));
            Assert.That(m.Time, Is.EqualTo(2));
            // BALLOON is the Normal list only; 玄人 and 達人 have none and take the default 5.
            Assert.That(new[] { n.BalloonHits, e.BalloonHits, m.BalloonHits }, Is.EqualTo(new[] { 3, 5, 5 }));
            Assert.That(chart.Notes.Last().Time, Is.EqualTo(4)); Assert.That(chart.Notes.Last().BalloonHits, Is.EqualTo(4));
            Assert.That(chart.Duration, Is.EqualTo(6));
            Assert.That(chart.Bars.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Normal).Display, Is.False);
            Assert.That(chart.Bars.Single(x => x.BranchId == 0 && x.Route == BranchRoute.Expert).Display, Is.True);
        }

        // OpenTaiko: each route reads its own list in order. A common balloon is played with the
        // Normal list's number on every route and moves every route's place past it.
        [Test] public void EachRouteReadsItsOwnBalloonList()
        {
            var chart = Parse("7008,\n#BRANCHSTART p,50,80\n#N\n7008,\n#E\n7008,\n#M\n7008,\n#BRANCHEND\n7008,",
                "BALLOONNOR:10,20,30\nBALLOONEXP:11,21,31\nBALLOONMAS:12,22,32");
            int Route(BranchRoute route) => chart.Notes.Single(x => x.BranchId == 0 && x.Route == route).BalloonHits;
            Assert.That(chart.Notes.First(x => x.BranchId < 0).BalloonHits, Is.EqualTo(10));
            Assert.That(new[] { Route(BranchRoute.Normal), Route(BranchRoute.Expert), Route(BranchRoute.Master) }, Is.EqualTo(new[] { 20, 21, 22 }));
            Assert.That(chart.Notes.Last().BalloonHits, Is.EqualTo(30));
            // The Shinuchi budget counts what is played: common balloons from Normal, branches from Master.
            Assert.That(new ChartStatistics(chart).BalloonHitBudget, Is.EqualTo(10 + 22 + 30));
            // The header order does not matter, and BALLOON is BALLOONNOR.
            var reordered = Parse("7008,\n#BRANCHSTART p,50,80\n#N\n7008,\n#E\n7008,\n#M\n7008,\n#BRANCHEND",
                "BALLOONMAS:12,22\nBALLOONEXP:11,21\nBALLOON:10,20");
            Assert.That(reordered.Notes.Select(x => x.BalloonHits), Is.EqualTo(new[] { 10, 20, 21, 22 }));
        }

        [Test] public void BalloonListsReadAsOpenTaikoDoes()
        {
            int[] Hits(string headers) => Parse("7008,\n7008,\n7008,", headers).Notes.Select(x => x.BalloonHits).ToArray();
            Assert.That(Hits("BALLOON:4,6"), Is.EqualTo(new[] { 4, 6, 5 }), "Past the end: 5.");
            Assert.That(Hits("BALLOON:4,,6"), Is.EqualTo(new[] { 4, 5, 5 }), "Reading stops at an empty item.");
            Assert.That(Hits("BALLOON:4,6\nBALLOON:5.10"), Is.EqualTo(new[] { 4, 6, 5 }), "An unreadable line is ignored.");
            Assert.That(Parse("7008,", "BALLOON:5.10").Warnings, Has.Member("BALLOON:5.10"));
            // Lists before any COURSE are every course's defaults; a course's own list replaces them.
            Assert.That(TjaParser.Parse("BPM:120\nBALLOON:7\nCOURSE:Hard\nBALLOON:8\n#START\n7008,\n#END\nCOURSE:Oni\n#START\n7008,\n#END").Notes[0].BalloonHits, Is.EqualTo(7));
        }

        // OpenTaiko #LEVELHOLD: in a route, choosing that route keeps it for every later branch;
        // elsewhere, the route being played is kept from its time. Branch 2 (p,101,102) would
        // otherwise always fall to 普通.
        [TestCase(BranchRoute.Master, BranchRoute.Master, BranchRoute.Master)]
        [TestCase(BranchRoute.Normal, BranchRoute.Master, BranchRoute.Normal)]
        public void LevelHoldInARouteKeepsItOnceChosen(BranchRoute holding, BranchRoute first, BranchRoute second)
        {
            string routes = Routes.Replace("#" + holding.ToString()[0] + "\n", "#" + holding.ToString()[0] + "\n#LEVELHOLD\n");
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80" + routes + "0000,\n0000,\n#BRANCHSTART p,101,102" + Routes);
            Assert.That(chart.Branches[0].HoldRoutes[(int)holding], Is.True);
            var session = new PlaySession(chart);
            session.Advance(chart.Duration + 1, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { first, second }));
            Assert.That(session.IsLevelHeld, Is.EqualTo(holding == first));
        }

        [Test] public void LevelHoldOutsideARouteKeepsTheRouteBeingPlayed()
        {
            // Before the first judgement (4 s): 普通 is kept although every note is 良.
            var early = Parse("1111,\n#LEVELHOLD\n0000,\n0000,\n#BRANCHSTART p,50,80" + Routes);
            Assert.That(early.LevelHolds, Is.EqualTo(new[] { 2.0 }));
            var session = new PlaySession(early);
            session.Advance(early.Duration + 1, true);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Normal }));
            // At the judgement's own time it still comes first, as in OpenTaiko's chip order.
            var same = new PlaySession(Parse("1111,\n0000,\n#LEVELHOLD\n0000,\n#BRANCHSTART p,50,80" + Routes));
            same.Advance(same.Chart.Duration + 1, true);
            Assert.That(same.BranchHistory, Is.EqualTo(new[] { BranchRoute.Normal }));
            // Between #BRANCHSTART and #N it holds from there: the branch it follows is already judged.
            var after = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#LEVELHOLD" + Routes + "0000,\n0000,\n#BRANCHSTART p,101,102" + Routes);
            var held = new PlaySession(after);
            held.Advance(after.Duration + 1, true);
            Assert.That(held.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
            // A fixed route (practice) is not held.
            var practice = new PlaySession(early, 0, BranchRoute.Expert);
            practice.Advance(early.Duration + 1, true);
            Assert.That(practice.BranchHistory, Is.EqualTo(new[] { BranchRoute.Expert }));
        }

        [Test] public void DecisionIsAtTheLastBarLineBeforeTheBranch()
        {
            // OpenTaiko: the bar line of the measure before #BRANCHSTART, hidden or not.
            var chart = Parse("0000,\n0000,\n#BARLINEOFF\n0000,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n2000,\n#M\n#BARLINEOFF\n#SCROLL 2\n,\n#BRANCHEND");
            var branch = chart.Branches.Single();
            Assert.That(branch.Time, Is.EqualTo(6));
            Assert.That(branch.DecisionTime, Is.EqualTo(4));
            Assert.That(branch.FirstEntries[2].Display, Is.False);
        }

        [Test] public void DecisionFollowsTheLatestRouteOfAPreviousBranchAndTheChartStart()
        {
            // Without a bar line before it, four beats before the chart starts (OFFSET 1 → -1 - 2).
            Assert.That(Parse("#BRANCHSTART p,50,80" + Routes, "OFFSET:1").Branches[0].DecisionTime, Is.EqualTo(-3));
            // Back-to-back branches: the latest bar line of any route, here 達人's second 2/4 measure.
            var chart = Parse("1111,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n2000,\n#M\n#MEASURE 2/4\n3000,\n3000,\n#BRANCHSTART p,50,80" + Routes);
            Assert.That(chart.Branches[1].Time, Is.EqualTo(4));
            Assert.That(chart.Branches[1].DecisionTime, Is.EqualTo(3));
        }

        [Test] public void RollConditionsWaitForARollEndingInTheLastMeasure()
        {
            // Drumroll 4.5–6.5 s ending in the measure before the branch (bar line at 6 s).
            string lead = "1111,\n0000,\n0500,\n0800,";
            Assert.That(Parse(lead + "\n#BRANCHSTART r,1,2" + Routes).Branches[0].DecisionTime, Is.EqualTo(6.5));
            Assert.That(Parse(lead + "\n#BRANCHSTART rb,1,2" + Routes).Branches[0].DecisionTime, Is.EqualTo(6.5));
            Assert.That(Parse(lead + "\n#BRANCHSTART p,1,2" + Routes).Branches[0].DecisionTime, Is.EqualTo(6));
            // A roll that ended before that bar line does not delay it.
            Assert.That(Parse("5008,\n0000,\n0000,\n#BRANCHSTART r,1,2" + Routes).Branches[0].DecisionTime, Is.EqualTo(4));
        }

        [TestCase(0, 1, 6)]
        [TestCase(0, 0, 6)]
        [TestCase(-1, 0, 6)]
        public void LoadBoundaryHandlesVerticalStationaryAndReverseScroll(double x, double y, double time)
        {
            var note = new ChartNote { Time = time, Bpm = 120, ScrollX = x, ScrollY = y };
            // Nijiiro: (1920 - judge X 618 + note radius 96) / (BPM 120 / 240 * 1302).
            Assert.That(NoteScroll.LoadTime(note), Is.EqualTo(x == 0 && y == 0 ? 6 : 6 - 1398.0 / 651).Within(1e-9));
        }

        [TestCase(4, 0, BranchRoute.Master, 100)]
        [TestCase(3, 0, BranchRoute.Expert, 75)]
        [TestCase(1, 2, BranchRoute.Expert, 50)]
        [TestCase(1, 0, BranchRoute.Normal, 25)]
        [TestCase(0, 0, BranchRoute.Normal, 0)]
        public void AccuracyUsesGoodHalfOkAndBad(int good, int ok, BranchRoute expected, int percentage)
        {
            var session = new PlaySession(Accuracy());
            for (int i = 0; i < good + ok; i++) session.Hit(false, i * 0.5 + (i >= good ? 0.05 : 0));
            double at = session.Chart.Branches[0].DecisionTime;
            session.Advance(at - 0.001, false);
            Assert.That(session.SelectedRoute(0), Is.Null);
            session.Advance(at, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
            Assert.That(session.LastBranchValue, Is.EqualTo(percentage));
            Assert.That(session.Chart.Notes.Where(n => n.BranchId == 0 && session.IsActive(n)).All(n => n.Route == expected), Is.True);
        }

        [Test] public void PercentageIsTruncatedAndNegativeExpertThresholdIsReachable()
        {
            var session = new PlaySession(Accuracy("p,66.5,90", "1110,\n0000,\n0000,"));
            session.Hit(false, 0); session.Hit(false, 0.5); session.Advance(4, false);
            Assert.That(session.LastBranchValue, Is.EqualTo(66)); Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
            var forced = new PlaySession(Accuracy("p,-1,0")); forced.Advance(4, false);
            Assert.That(forced.CurrentBranch, Is.EqualTo(BranchRoute.Master));
            // OpenTaiko tBranchJudge compares only `value >= threshold`: with no notes (0%) a
            // negative expert threshold still picks 玄人.
            var empty = new PlaySession(Accuracy("p,-1,100", "0000,\n0000,\n0000,")); empty.Advance(4, false);
            Assert.That(empty.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
        }

        // BRAINIAC MANIAC (MZ72): `p,-1,101` forces 玄人 and `p,101,102` forces 普通, whatever the play.
        [TestCase(true)]
        [TestCase(false)]
        public void UnreachableThresholdsForceARoute(bool auto)
        {
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,-1,101" + Routes + "0000,\n#BRANCHSTART p,101,102" + Routes);
            var session = new PlaySession(chart);
            var routes = new List<BranchRoute>();
            session.BranchSelected += (branch, route) => routes.Add(route);
            for (double t = 0; t <= chart.Duration + 1; t += 1 / 60.0) session.Advance(t, auto);
            Assert.That(routes, Is.EqualTo(new[] { BranchRoute.Expert, BranchRoute.Normal }));
        }

        [TestCase(0, BranchRoute.Normal)]
        [TestCase(3, BranchRoute.Expert)]
        [TestCase(5, BranchRoute.Master)]
        public void DrumrollCountsRepeatedSameFrameHits(int hits, BranchRoute expected)
        {
            var session = new PlaySession(Accuracy("r,3,5", "5008,\n0000,\n0000,"));
            for (int i = 0; i < hits; i++) session.Hit(i % 2 == 0, 0.1);
            session.Advance(4, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(expected)); Assert.That(session.LastBranchValue, Is.EqualTo(hits));
        }

        [Test] public void BalloonHitsCountAsDrumrollBranchHits()
        {
            var session = new PlaySession(Accuracy("r,1,3", "7008,\n0000,\n0000,", "BALLOON:5"));
            for (int i = 0; i < 5; i++) session.Hit(false, 0.1);
            session.Advance(4, false);
            Assert.That(session.Rolls, Is.EqualTo(5)); Assert.That(session.LastBranchValue, Is.EqualTo(5));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Master));
        }

        [Test] public void SectionResetsAccuracyAndDrumrollHits()
        {
            var session = new PlaySession(Accuracy("p,50,80", "1111,\n#SECTION\n1000,\n0000,"));
            for (int i = 0; i < 4; i++) session.Hit(false, i * 0.5);
            session.Advance(4, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal)); Assert.That(session.LastBranchValue, Is.Zero);
            // Unlike the reference player, hits on a drumroll before #SECTION are gone even
            // though the drumroll runs on past it.
            var roll = new PlaySession(Accuracy("r,3,5", "5000,\n#SECTION\n0000,\n0008,"));
            for (int i = 0; i < 5; i++) roll.Hit(false, 0.1);
            roll.Advance(roll.Chart.Branches[0].DecisionTime, false);
            Assert.That(roll.CurrentBranch, Is.EqualTo(BranchRoute.Normal)); Assert.That(roll.LastBranchValue, Is.Zero);
        }

        [Test] public void UnselectedNotesCannotConsumeInputMissOrInflateScore()
        {
            var chart = Accuracy(); var session = new PlaySession(chart);
            session.Advance(4, false); // All misses -> Normal.
            Assert.That(session.Hit(true, 6), Is.EqualTo(Judgment.None));
            Assert.That(session.Hit(false, 6), Is.EqualTo(Judgment.Good));
            int score = session.Score; session.Advance(10, false);
            Assert.That(session.Good, Is.EqualTo(1)); Assert.That(session.Bad, Is.EqualTo(4));
            Assert.That(session.Score, Is.EqualTo(score)); Assert.That(score, Is.EqualTo(125000));
            Assert.That(chart.Notes.Select((n, i) => n.BranchId < 0 || n.Route == BranchRoute.Normal || !session.Resolved[i]).All(x => x), Is.True);
        }

        [Test] public void ConsecutiveBranchesAndCoarseAutoplayMatchSmallSteps()
        {
            string block = "\n#N\n1000,\n1000,\n#E\n2000,\n2000,\n#M\n1100,\n0000,";
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80" + block + "\n#BRANCHSTART p,50,80" + block);
            var coarse = new PlaySession(chart); coarse.Advance(chart.Duration + 1, true);
            var fine = new PlaySession(chart);
            for (double t = -2; t < chart.Duration + 1; t += 1.0 / 120) fine.Advance(t, true);
            Assert.That(coarse.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
            Assert.That(coarse.BranchHistory, Is.EqualTo(fine.BranchHistory));
            Assert.That(coarse.Score, Is.EqualTo(fine.Score)); Assert.That(coarse.Bad, Is.Zero);
            Assert.That(coarse.Good, Is.EqualTo(chart.Notes.Count(n => !n.IsLong && coarse.IsActive(n))));
        }

        [Test] public void DecisionsKeepCountingAndASectionOnAnyRouteResets()
        {
            // OpenTaiko: counts carry over a decision; #SECTION resets them even on an unplayed route.
            var kept = new PlaySession(Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#N\n0000,\n#E\n0000,\n#M\n0000,\n#BRANCHEND\n0000,\n0000,\n#BRANCHSTART p,50,80" + Routes));
            kept.Advance(kept.Chart.Duration + 1, true);
            Assert.That(kept.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Master }));
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,80\n#N\n#SECTION\n0000,\n#E\n0000,\n#M\n1000,\n#BRANCHEND\n0000,\n0000,\n#BRANCHSTART p,50,80" + Routes);
            var reset = new PlaySession(chart); reset.Advance(chart.Duration + 1, true);
            Assert.That(reset.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Normal }),
                "The 達人 note at the #SECTION time counts first, then the 普通 #SECTION clears it.");
        }

        [TestCase("#N\n1000,")]
        [TestCase("#BRANCHSTART p,50,80\n#N\n1000,\n#N\n1000,")]
        [TestCase("10\n#BRANCHSTART p,50,80")]
        public void MalformedBranchIsRejected(string body) => Assert.Throws<FormatException>(() => Parse(body));

        // OpenTaiko keeps the open roll per route: the normal route's roll is not ended by the expert
        // route's 8 (a stray one there, a blank) and runs to #END, with warnings for both.
        [Test] public void ARollStaysOnItsRouteAcrossTheOthers()
        {
            var chart = Parse("#BRANCHSTART p,50,80\n#N\n5000,\n#E\n0008,");
            var roll = chart.Notes.Single(n => n.Kind == NoteKind.Roll);
            Assert.That(roll.Route, Is.EqualTo(BranchRoute.Normal));
            Assert.That(roll.EndTime, Is.EqualTo(chart.Duration));
            Assert.That(chart.Warnings, Has.Some.StartsWith("A stray 8"));
            Assert.That(chart.Warnings, Has.Some.EqualTo("An unended roll is ended by #END"));
        }
    }
}

using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class PracticeTests
    {
        [TestCase(0.1, -0.3)]
        [TestCase(1.0, 0.0)]
        [TestCase(3.0, 0.4)]
        public void PreparationKeepsTwoRealSecondsAndTargetBoundary(double rate, double judgeOffset)
        {
            var progress = new PracticeProgress(); progress.SetBars(new[] { 0.0, 10.0 });
            progress.PauseAt(10, 0);
            while (progress.Speed < rate) progress.ChangeSpeed(1);
            while (progress.Speed > rate) progress.ChangeSpeed(-1);
            var clock = new SongClock();
            double start = progress.PlaybackStart(0.2, 0, judgeOffset);
            clock.Seek(start, rate); clock.Resume(100); clock.Update(102);
            Assert.That(progress.Target, Is.EqualTo(10));
            Assert.That(clock.Time, Is.EqualTo(10.2 + System.Math.Min(0, judgeOffset)).Within(1e-8));
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1111,\n1111,\n#END");
            var session = PlaySession.PracticeAt(chart, 2);
            session.Advance(0, true); session.Advance(1.9, true);
            Assert.That(session.Good + session.Bad, Is.Zero);
            Assert.That(session.Skipped, Is.EqualTo(new[] { true, true, true, true, false, false, false, false }));
            Assert.That(session.Hit(false, 2), Is.EqualTo(Judgment.Good));
        }
        // At any practice speed the windows stay the same real milliseconds: chart time runs at the rate.
        [TestCase(0.5)]
        [TestCase(2.0)]
        public void JudgeWindowsKeepRealMilliseconds(double rate)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1000,\n#END");
            Judgment HitAt(double realSeconds) => PlaySession.PracticeAt(chart, 0, null, null, rate).Hit(false, realSeconds * rate);
            Assert.That(HitAt(PlaySession.GoodWindow - .001), Is.EqualTo(Judgment.Good));
            Assert.That(HitAt(PlaySession.GoodWindow + .001), Is.EqualTo(Judgment.Ok));
            Assert.That(HitAt(PlaySession.OkWindow + .001), Is.EqualTo(Judgment.Bad));
            Assert.That(HitAt(PlaySession.BadWindow + .001), Is.EqualTo(Judgment.None));
            var session = PlaySession.PracticeAt(chart, 0, null, null, rate);
            Assert.That(session.Rate, Is.EqualTo(rate));
            Assert.That(PlaySession.PracticeAt(chart, 0, session).Rate, Is.EqualTo(rate));
            Assert.That(session.GoodWindowMs, Is.EqualTo(PlaySession.GoodWindow * 1000 * rate).Within(1e-8));
        }
        // Custom judge windows are real milliseconds at any practice speed, and later attempts keep them.
        [TestCase(1.0)]
        [TestCase(0.5)]
        [TestCase(2.0)]
        public void CustomWindowsKeepRealMilliseconds(double rate)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Easy\n#START\n1000,\n#END", "Easy");
            var windows = JudgeWindows.FromMs(40, 90, 150);
            Judgment HitAt(double realSeconds) => PlaySession.PracticeAt(chart, 0, null, null, rate, windows).Hit(false, realSeconds * rate);
            Assert.That(HitAt(.039), Is.EqualTo(Judgment.Good));
            Assert.That(HitAt(.041), Is.EqualTo(Judgment.Ok));
            Assert.That(HitAt(.091), Is.EqualTo(Judgment.Bad));
            Assert.That(HitAt(.151), Is.EqualTo(Judgment.None));
            var session = PlaySession.PracticeAt(chart, 0, null, null, rate, windows);
            Assert.That(session.BadWindowMs, Is.EqualTo(150 * rate).Within(1e-8), "Custom windows replace かんたん's own too.");
            Assert.That(PlaySession.PracticeAt(chart, 0, session).CustomJudge, Is.True);
            Assert.That(PlaySession.PracticeAt(chart, 0).CustomJudge, Is.False);
        }
        // The judge offset is real seconds too: 30 ms late input stays centred at any speed.
        [TestCase(0.5)]
        [TestCase(2.0)]
        public void JudgeOffsetKeepsRealMilliseconds(double rate)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n0000,\n1000,\n#END");
            var first = PlaySession.PracticeAt(chart, 0, new PlaySession(chart, .03), null, rate);
            Assert.That(first.JudgeOffset, Is.EqualTo(.03 * rate).Within(1e-12));
            var again = PlaySession.PracticeAt(chart, 0, first, null, 1);
            Assert.That(again.JudgeOffset, Is.EqualTo(.03).Within(1e-12));
            double note = chart.Notes[0].Time;
            Assert.That(first.Hit(false, note + (.03 + PlaySession.GoodWindow - .001) * rate), Is.EqualTo(Judgment.Good));
            Assert.That(PlaySession.PracticeAt(chart, 0, first).Hit(false, note + (.03 + PlaySession.GoodWindow + .001) * rate),
                Is.EqualTo(Judgment.Ok));
        }
        [Test]
        public void PreparationInsideLongNoteDoesNotCountEarlyHits()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n5000,\n0000,\n0008,\n#END");
            var session = PlaySession.PracticeAt(chart, 4);
            session.Advance(2, true);
            Assert.That(session.Hit(false, 3.9), Is.EqualTo(Judgment.None));
            Assert.That(session.Rolls, Is.Zero);
            session.Advance(4, true);
            Assert.That(session.Rolls, Is.EqualTo(1));
        }
        [Test]
        public void ClockRateChangesChartSecondsAndScheduledAudioTogether()
        {
            var clock = new SongClock();
            clock.Seek(10, 0.8); clock.Resume(100);
            Assert.That(clock.TrackDue(100), Is.EqualTo(10));
            clock.TrackStarted(100);
            clock.Update(105);
            Assert.That(clock.Time, Is.EqualTo(14).Within(1e-8));
            clock.Pause(); clock.Update(200);
            Assert.That(clock.Time, Is.EqualTo(14).Within(1e-8));
            clock.Seek(-1, 0.5); clock.Resume(210);
            Assert.That(clock.TrackDue(211.999), Is.Null);
            Assert.That(clock.TrackDue(212), Is.EqualTo(0));
            clock.Update(213);
            Assert.That(clock.Time, Is.EqualTo(0.5).Within(1e-8));
        }
        [Test]
        public void StartScrollsBackToThePreparationWithoutMovingTheCursor()
        {
            var progress = new PracticeProgress(); progress.SetBars(new[] { 0.0, 10.0 });
            progress.PauseAt(10, 0);
            progress.Rewind(progress.PlaybackStart(0, 0, 0), 1);
            Assert.That(progress.Scrolling, Is.True);
            progress.Update(1 + PracticeProgress.ScrollSeconds / 2);
            Assert.That(progress.Position, Is.EqualTo(9).Within(1e-8));
            Assert.That(progress.Scrolling, Is.True);
            progress.Update(2);
            Assert.That(progress.Position, Is.EqualTo(8));
            Assert.That(progress.Scrolling, Is.False);
            Assert.That(progress.Target, Is.EqualTo(10));
            Assert.That(progress.Measure, Is.EqualTo(1));
        }
        [Test]
        public void CursorUsesRealBarsAndAnimatesBothDirections()
        {
            var chart = TjaParser.Parse("BPM:120\nOFFSET:0.5\nCOURSE:Oni\n#START\n1000,\n#BARLINEOFF\n#MEASURE 3/4\n#BPMCHANGE 180\n#DELAY 0.25\n100,\n100,\n#END");
            var progress = new PracticeProgress();
            progress.SetBars(chart.Bars.Select(b => b.Time));
            Assert.That(progress.Count, Is.EqualTo(3));
            progress.PauseAt(-0.5, 0); progress.Move(1, 0);
            Assert.That(progress.Target, Is.EqualTo(1.75).Within(1e-8));
            progress.Update(0.1);
            Assert.That(progress.Position, Is.EqualTo(0.625).Within(1e-8));
            progress.Move(-1, 0.1); progress.Update(0.3);
            Assert.That(progress.Position, Is.EqualTo(-0.5).Within(1e-8));
            progress.ChangeSpeed(-1); progress.ChangeSpeed(-1);
            Assert.That(progress.Speed, Is.EqualTo(0.8));
            for (int i = 0; i < 100; i++) progress.ChangeSpeed(-1);
            Assert.That(progress.Speed, Is.EqualTo(0.1));
            for (int i = 0; i < 100; i++) progress.ChangeSpeed(1);
            Assert.That(progress.Speed, Is.EqualTo(3));
        }
        [Test]
        public void SeekingDoesNotJudgeSkippedNotesAndRewindRestoresThem()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1111,\n1111,\n#END");
            var session = PlaySession.PracticeAt(chart, 2);
            session.Advance(2, false);
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.Score, Is.Zero);
            Assert.That(session.Hit(false, 2), Is.EqualTo(Judgment.Good));
            var rewind = PlaySession.PracticeAt(chart, 0, session);
            Assert.That(rewind.Good, Is.Zero);
            Assert.That(rewind.Hit(false, 0), Is.EqualTo(Judgment.Good));
        }
        [Test]
        public void SeekIntoLongNoteDoesNotAutoplayAllEarlierRollHits()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n5000,\n0000,\n0008,\n#END");
            var session = PlaySession.PracticeAt(chart, 4);
            session.Advance(4, true);
            Assert.That(session.Rolls, Is.EqualTo(1));
            session.Advance(4.2, true);
            Assert.That(session.Rolls, Is.InRange(3, 4));
        }
        [Test]
        public void SeekKeepsPastBranchesWithoutReplayingScoresAndRewindClearsFutureChoices()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1111,\n#BRANCHSTART p,50,80\n#N\n1000,\n#E\n1100,\n#M\n1111,\n#BRANCHEND\n1000,\n#END");
            var original = new PlaySession(chart); original.Advance(3, true);
            var sought = PlaySession.PracticeAt(chart, 3, original);
            Assert.That(sought.SelectedRoute(0), Is.EqualTo(original.SelectedRoute(0)));
            Assert.That(sought.Good + sought.Bad + sought.Rolls, Is.Zero);
            var rewind = PlaySession.PracticeAt(chart, -5, sought);
            Assert.That(rewind.SelectedRoute(0), Is.Null);
        }
        // Practice fixes every branch to the menu's route: misses that would select 普通 and a perfect
        // run that would select 達人 both keep the chosen route.
        [TestCase(BranchRoute.Normal, false)]
        [TestCase(BranchRoute.Expert, false)]
        [TestCase(BranchRoute.Expert, true)]
        [TestCase(BranchRoute.Master, false)]
        public void PracticeForcesTheChosenBranch(BranchRoute route, bool perfect)
        {
            var chart = TjaParser.Parse("BPM:240\nCOURSE:Oni\n#START\n1111,\n#BRANCHSTART p,10,20\n#N\n1111,\n#E\n2222,\n#M\n3333,\n#BRANCHEND\n#BRANCHSTART r,1,2\n#N\n1000,\n#E\n2000,\n#M\n3000,\n#BRANCHEND\n#END");
            var session = PlaySession.PracticeAt(chart, 0, null, route);
            for (double t = 0; t < 4; t += 1 / 120.0)
            {
                session.Advance(t, perfect);
            }
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { route, route }));
            Assert.That(chart.Notes.Where(n => n.BranchId >= 0).All(n => session.IsPracticePreviewActive(n) == (n.Route == route)), Is.True);
            Assert.That(chart.Notes.Where(n => n.BranchId >= 0 && n.Route != route).All(n => !session.Resolved[chart.Notes.IndexOf(n)]), Is.True,
                "Notes of the other routes are never judged.");
        }
    }
}

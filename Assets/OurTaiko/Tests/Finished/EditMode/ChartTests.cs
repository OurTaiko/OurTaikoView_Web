using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class ChartTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse("TITLE:Test\nBPM:120\n" + metadata + "\nCOURSE:Oni\n#START\n" + body + "\n#END");

        // TjaLines: the song list and the parser agree on the block they read, and on case.
        [Test] public void SongInfoListsTheBlockTheParserPlays()
        {
            const string text = "TITLE:Two\nBPM:120\nCOURSE:Oni\nLEVEL:5\n#start\n1,\n#end\nCOURSE:Edit\nLEVEL:9\n#START\n#BRANCHSTART p,50,80\n#N\n1,\n#E\n1,\n#M\n1,\n#END";
            var song = SongInfo.Read(text);
            var info = song.Course(Difficulty.Oni);
            var chart = TjaParser.Parse(text, info.Course);
            Assert.That((info.Level, info.IsBranching), Is.EqualTo((5, false)));
            Assert.That((chart.Level, chart.Branches.Count), Is.EqualTo((5, 0)));
            Assert.That((song.Course(Difficulty.Ura).Level, song.Course(Difficulty.Ura).IsBranching), Is.EqualTo((9, true)));
            // The same course written twice is an error, as on the servers.
            Assert.Throws<FormatException>(() => SongInfo.Read(text.Replace("COURSE:Edit", "COURSE:Oni")));
        }

        // OpenTaiko: a block without #END ends at the next COURSE: (or the file's end), and notes
        // after the last comma are a measure of their own.
        [Test] public void MissingEndAndLastCommaAreTolerated()
        {
            const string text = "BPM:120\nCOURSE:Hard\nLEVEL:5\n#START\n1111,\n2222\nCOURSE:Oni\nLEVEL:9\n#START\n1000,\n10";
            var hard = TjaParser.Parse(text, "Hard");
            Assert.That(hard.Notes.Select(n => n.Time), Is.EqualTo(new[] { 0, 0.5, 1, 1.5, 2, 2.5, 3, 3.5 }));
            Assert.That(hard.Bars.Count, Is.EqualTo(2));
            Assert.That(hard.Warnings, Is.SupersetOf(new[] { "#END missing (line 7)", "Last measure without a comma (line 6)" }));
            var oni = TjaParser.Parse(text, "Oni");
            Assert.That((oni.Level, oni.Notes.Count, oni.Duration), Is.EqualTo((9, 2, 4.0)));
            Assert.That(SongInfo.Read(text).Courses().Select(c => (c.Difficulty, c.Level)),
                Is.EqualTo(new[] { (Difficulty.Hard, 5), (Difficulty.Oni, 9) }));
        }

        // TjaNumber: a comma is a decimal point (OpenTaiko), and a trailing one is dropped.
        [Test] public void CommasInNumbersAreDecimalPointsOrTrailing()
        {
            var chart = TjaParser.Parse("TITLE:Test\nBPM:120,5\nOFFSET:-1,25\nCOURSE:Oni\n#START\n#BPMCHANGE 240,\n#MEASURE 2/4,\n1,\n#END");
            Assert.That(chart.Bpm, Is.EqualTo(120.5));
            Assert.That(chart.Offset, Is.EqualTo(-1.25));
            Assert.That(chart.Notes[0].Bpm, Is.EqualTo(240));
            Assert.That(chart.Duration, Is.EqualTo(1.25 + 0.5));
            Assert.That(SongInfo.Read("BPM:120,5\nCOURSE:Oni\n#START\n1,\n#END").Bpm, Is.EqualTo(120.5));
        }

        [TestCase("Easy", 0.6)]
        [TestCase("Normal", 0.7)]
        [TestCase("Hard", 0.7)]
        [TestCase("Oni", 0.8)]
        [TestCase("Edit", 0.8)]
        public void ClearThresholdMatchesOriginalDifficulty(string course, double threshold)
        {
            var session = new PlaySession(new TaikoChart { Course = course, Level = 1 });
            Assert.That(session.ClearThreshold, Is.EqualTo(threshold));
        }

        [Test] public void OffsetAndEmptyMeasuresKeepAudioTime()
        {
            var chart = Parse("1000,\n,\n2000,", "OFFSET:0.5");
            Assert.That(chart.Notes.Select(n => n.Time), Is.EqualTo(new[] { -0.5, 3.5 }).Within(0.000001));
            Assert.That(chart.Duration, Is.EqualTo(5.5).Within(0.000001));
        }
        [Test] public void CommandsInsideMeasureKeepSubdivisionTiming()
        {
            var chart = Parse("10\n#BPMCHANGE 240\n#SCROLL 1-0.5i\n20,");
            Assert.That(chart.Notes[1].Time, Is.EqualTo(1).Within(0.000001));
            Assert.That(chart.Duration, Is.EqualTo(1.5).Within(0.000001));
            Assert.That(chart.Notes[1].ScrollY, Is.EqualTo(-0.5));
        }
        [Test] public void MeasureAndDelayAreApplied()
        {
            var chart = Parse("#MEASURE 3/4\n#DELAY 0.25\n100,\n200,");
            Assert.That(chart.Notes[0].Time, Is.EqualTo(0.25));
            Assert.That(chart.Notes[1].Time, Is.EqualTo(1.75));
        }
        [Test] public void LongNoteLinksAcrossMeasures()
        {
            var chart = Parse("5000,\n0080,");
            Assert.That(chart.Notes.Count, Is.EqualTo(1));
            Assert.That(chart.Notes[0].EndTime, Is.EqualTo(3));
        }
        [Test] public void CourseSelectionUsesRequestedDifficulty()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Easy\nLEVEL:1\n#START\n1000,\n#END\nCOURSE:3\nLEVEL:9\nBALLOON:3\n#START\n7008,\n#END", "Oni");
            Assert.That(chart.Level, Is.EqualTo(9)); Assert.That(chart.Notes[0].BalloonHits, Is.EqualTo(3));
        }
        // OpenTaiko tolerances, each warned about: notes before the first route marker are common,
        // and a roll still open at #END ends there.
        [Test] public void CommonNotesInABranchAndAnUnendedRollAreToleratedWithWarnings()
        {
            var branched = Parse("#BRANCHSTART p,50,80\n1000,");
            Assert.That(branched.Notes.Single().BranchId, Is.EqualTo(-1));
            Assert.That(branched.Warnings, Has.Some.StartsWith("Notes between #BRANCHSTART"));
            var roll = Parse("5000,");
            Assert.That(roll.Notes.Single().EndTime, Is.EqualTo(roll.Duration));
            Assert.That(roll.Warnings, Has.Some.EqualTo("An unended roll is ended by #END (line 7)"));
        }
        [Test] public void WrongColorDoesNotConsumeNoteAndMissResetsCombo()
        {
            var session = new PlaySession(Parse("1200,"));
            Assert.That(session.Hit(true, 0), Is.EqualTo(Judgment.None));
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(session.Combo, Is.EqualTo(1));
            session.Advance(0.7, false);
            Assert.That(session.Bad, Is.EqualTo(1)); Assert.That(session.Combo, Is.Zero);
        }
        [Test] public void DonAndKaAreSeparateLanes()
        {
            // Don at 0, ka at 62.5 ms: hitting the ka first leaves the don judgeable.
            var session = new PlaySession(Parse("12" + new string('0', 30) + ","));
            Assert.That(session.Hit(true, 0.02), Is.EqualTo(Judgment.Ok));
            Assert.That(session.Resolved, Is.EqualTo(new[] { false, true }));
            Assert.That(session.Hit(false, 0.024), Is.EqualTo(Judgment.Good));
            Assert.That(session.Combo, Is.EqualTo(2));
        }
        [Test] public void LateFrontNoteYieldsToNextNoteOfItsLaneUnlessBlocked()
        {
            // Dons at 0 and 125 ms: 80 ms is past 可 for the first and within 可 for the second.
            var free = new PlaySession(Parse("11" + new string('0', 14) + ","));
            Assert.That(free.Hit(false, 0.08), Is.EqualTo(Judgment.Ok));
            Assert.That(free.Resolved, Is.EqualTo(new[] { false, true }));
            // A pending ka between them keeps the press on the first don.
            var blocked = new PlaySession(Parse("10201" + new string('0', 27) + ","));
            Assert.That(blocked.Hit(false, 0.08), Is.EqualTo(Judgment.Bad));
            Assert.That(blocked.Resolved, Is.EqualTo(new[] { true, false, false }));
        }
        [Test] public void TimingWindowsAndDuplicateInput()
        {
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.02), Is.EqualTo(Judgment.Good));
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.06), Is.EqualTo(Judgment.Ok));
            Assert.That(new PlaySession(Parse("1000,")).Hit(false, 0.1), Is.EqualTo(Judgment.Bad));
            var session = new PlaySession(Parse("1000,")); session.Hit(false, 0); session.Hit(false, 0);
            Assert.That(session.Good, Is.EqualTo(1));
        }
        [Test] public void OnlyTimedOutNotesAreMarkedMissed()
        {
            // Matches play_note_manager vs check_note: a timed-out note stays in
            // draw_note_buffer, any hit (even 不可) removes it.
            var session = new PlaySession(Parse("1100,"));
            Assert.That(session.Hit(false, 0.1), Is.EqualTo(Judgment.Bad));
            session.Advance(1, false);
            Assert.That(session.Resolved, Is.EqualTo(new[] { true, true }));
            Assert.That(session.Missed, Is.EqualTo(new[] { false, true }));
        }
        [Test] public void RollInputsInSameFrameAreNotDropped()
        {
            var session = new PlaySession(Parse("5008,"));
            session.Hit(false, 0.5); session.Hit(true, 0.5);
            Assert.That(session.Rolls, Is.EqualTo(2));
            Assert.That(session.Combo, Is.Zero);
        }
        [Test] public void BalloonOnlyConsumesDonAndPopsOnce()
        {
            var session = new PlaySession(TjaParser.Parse("BPM:120\nCOURSE:Oni\nBALLOON:2\n#START\n7008,\n#END"));
            Assert.That(session.Hit(true, 0.2), Is.EqualTo(Judgment.None));
            session.Hit(false, 0.2); session.Hit(false, 0.2); session.Hit(false, 0.2);
            Assert.That(session.Rolls, Is.EqualTo(2)); Assert.That(session.Score, Is.EqualTo(200)); Assert.That(session.Resolved[0], Is.True);
        }
        [TestCase("1 TripleHelix", "Oni")]
        [TestCase("2 Calibration", "Hard")]
        public void ImportedChartParsesAndAutoPlayCompletesWithoutMisses(string name, string course)
        {
            var chart = TjaParser.Parse(File.ReadAllText("Assets/OurTaiko/Tests/Shared/Songs~/" + name + ".tja"), course);
            Assert.That(chart.Notes.Count, Is.GreaterThan(50));
            var session = new PlaySession(chart);
            for (double t = -2; t < chart.Duration + 2; t += 1.0 / 60) session.Advance(t, true);
            Assert.That(session.Good, Is.EqualTo(chart.Notes.Count(n => !n.IsLong)));
            Assert.That(session.Bad, Is.Zero); Assert.That(session.Resolved.All(x => x), Is.True);
        }
    }
}

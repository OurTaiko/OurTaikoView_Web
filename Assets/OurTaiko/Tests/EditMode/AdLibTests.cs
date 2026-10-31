using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The Ad-Lib (F): hidden, hit with either colour within 可 and then judged like a small note (良/可,
    // combo, gauge), but left out of the note count, so its points are a bonus. Never 不可, never missed.
    public sealed class AdLibTests
    {
        static TaikoChart Chart(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static PlaySession Session(string body) => new PlaySession(Chart(body));
        // 32 slots: a note every 62.5 ms.
        static string Thirtyseconds(string head) => head.PadRight(32, '0') + ",";

        [Test] public void EitherColourJudgesItLikeASmallNote()
        {
            var don = Session("F000,");
            Assert.That(don.Hit(false, 0.01), Is.EqualTo(Judgment.Good));
            Assert.That(don.Good, Is.EqualTo(1));
            Assert.That(don.Combo, Is.EqualTo(1));
            Assert.That(don.Gauge, Is.GreaterThan(0));

            var ka = Session("F000,");
            Assert.That(ka.Hit(true, -0.05), Is.EqualTo(Judgment.Ok));
            Assert.That(ka.Ok, Is.EqualTo(1));
        }

        [Test] public void ItScoresOnTopOfTheNotes()
        {
            var plain = new ChartStatistics(Chart("1000,"));
            var stats = new ChartStatistics(Chart("1F00,"));
            Assert.That(stats.JudgeableNotes, Is.EqualTo(1));
            var session = Session("1F00,");
            Assert.That(session.BaseScore, Is.EqualTo(new ShinuchiScore(plain).BaseScore));
            Assert.That(session.KiwamiThreshold, Is.EqualTo(new ShinuchiScore(plain).CeilingScore));
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(session.Hit(true, 0.5), Is.EqualTo(Judgment.Good));
            Assert.That(session.Score, Is.EqualTo(2 * session.BaseScore));
        }

        [Test] public void LeftAloneItIsNeitherBadNorMissed()
        {
            var session = Session("1F10,");
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            session.Advance(0.7, false);
            Assert.That(session.Hit(false, 1), Is.EqualTo(Judgment.Good));
            session.Advance(2, false);
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.Missed[1], Is.False);
            Assert.That(session.Resolved[1], Is.True);
            Assert.That(session.Combo, Is.EqualTo(2));
        }

        [Test] public void APressInItsBadRangeIgnoresIt()
        {
            var session = Session("F000,");
            Assert.That(session.Hit(false, -0.09), Is.EqualTo(Judgment.None));
            Assert.That(session.Resolved[0], Is.False);
            Assert.That(session.Bad, Is.Zero);

            // A don already 不可, then an Ad-Lib 24 ms ahead: the press goes to the Ad-Lib.
            var late = Session(Thirtyseconds("10F"));
            Assert.That(late.Hit(false, 0.101), Is.EqualTo(Judgment.Good));
            Assert.That(late.Resolved[1], Is.True);
            Assert.That(late.Resolved[0], Is.False);
        }

        // OpenTaiko judges an Ad-Lib from the nearest: a press between it and the next note goes to the nearer.
        [Test] public void APressBetweenItAndTheNextNoteGoesToTheNearer()
        {
            var nearAdLib = Session(Thirtyseconds("F1"));
            Assert.That(nearAdLib.Hit(false, 0.02), Is.EqualTo(Judgment.Good));
            Assert.That(nearAdLib.Resolved[0], Is.True);
            Assert.That(nearAdLib.Resolved[1], Is.False);

            var nearDon = Session(Thirtyseconds("F1"));
            Assert.That(nearDon.Hit(false, 0.04), Is.EqualTo(Judgment.Good));
            Assert.That(nearDon.Resolved[1], Is.True);
            Assert.That(nearDon.Resolved[0], Is.False);
        }

        // A note after the Ad-Lib, already passed and within 可, comes first; one before it is hidden by it.
        [Test] public void PastNotesAroundIt()
        {
            var after = Session(Thirtyseconds("F1"));
            Assert.That(after.Hit(false, 0.07), Is.EqualTo(Judgment.Good));
            Assert.That(after.Resolved[1], Is.True);
            Assert.That(after.Hit(false, 0.072), Is.EqualTo(Judgment.Ok));
            Assert.That(after.Resolved[0], Is.True);

            var before = Session(Thirtyseconds("1F"));
            Assert.That(before.Hit(false, 0.064), Is.EqualTo(Judgment.Good));
            Assert.That(before.Resolved[1], Is.True);
            Assert.That(before.Resolved[0], Is.False);
        }

        [Test] public void AutoplayHitsIt()
        {
            var session = Session("1F2F,");
            Assert.That(new ChartStatistics(session.Chart).JudgeableNotes, Is.EqualTo(2));
            session.Advance(2, true);
            Assert.That(session.Good, Is.EqualTo(4));
            Assert.That(session.MaxCombo, Is.EqualTo(4));
            Assert.That(session.Score, Is.EqualTo(4 * session.BaseScore));
        }
    }
}

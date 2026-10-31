using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The Kadon (G), as OpenTaiko judges its swap note: both colours within PlaySession.KadonWait, in
    // either order, judged at the first hit; a single hit is not enough.
    public sealed class KadonTests
    {
        static PlaySession Session(string body) => new PlaySession(TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END"));

        [Test] public void BothColoursInEitherOrderJudgeAtTheFirstHit()
        {
            var donFirst = Session("G000,");
            Assert.That(donFirst.Hit(false, 0.01), Is.EqualTo(Judgment.None));
            Assert.That(donFirst.AwaitsKadon(true, 0.02), Is.True);
            Assert.That(donFirst.AwaitsKadon(false, 0.02), Is.False);
            // 30 ms after the note would be 可; the first hit at 10 ms makes it 良.
            Assert.That(donFirst.Hit(true, 0.03), Is.EqualTo(Judgment.Good));
            Assert.That(donFirst.Combo, Is.EqualTo(1));

            var kaFirst = Session("G000,");
            Assert.That(kaFirst.Hit(true, -0.04), Is.EqualTo(Judgment.None));
            Assert.That(kaFirst.Hit(false, -0.01), Is.EqualTo(Judgment.Ok));
        }

        [Test] public void OneColourAloneIsMissed()
        {
            var single = Session("G000,");
            Assert.That(single.Hit(false, 0), Is.EqualTo(Judgment.None));
            single.Advance(0.5, false);
            Assert.That(single.Bad, Is.EqualTo(1));
            Assert.That(single.Missed[0], Is.True);

            // The same colour again inside the wait is an empty hit.
            var twice = Session("G000,");
            Assert.That(twice.Hit(false, 0), Is.EqualTo(Judgment.None));
            Assert.That(twice.Hit(false, 0.01), Is.EqualTo(Judgment.None));
            Assert.That(twice.Resolved[0], Is.False);
        }

        [Test] public void AWaitThatRunsOutStartsOver()
        {
            var session = Session("G000,");
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.None));
            // 60 ms later the wait is over: this ka starts a new one instead of completing.
            Assert.That(session.Hit(true, 0.06), Is.EqualTo(Judgment.None));
            Assert.That(session.Resolved[0], Is.False);
            Assert.That(session.Hit(false, 0.07), Is.EqualTo(Judgment.Ok));
        }

        [Test] public void ABadFirstHitIsJudgedAtOnce()
        {
            var session = Session("G000,");
            Assert.That(session.Hit(true, 0.09), Is.EqualTo(Judgment.Bad));
            Assert.That(session.Resolved[0], Is.True);
        }

        [Test] public void ItCountsForScoreAndAutoplayHitsIt()
        {
            var chart = TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n1G2G,\n#END");
            Assert.That(new ChartStatistics(chart).JudgeableNotes, Is.EqualTo(4));
            var session = new PlaySession(chart);
            session.Advance(2, true);
            Assert.That(session.Good, Is.EqualTo(4));
        }
    }
}

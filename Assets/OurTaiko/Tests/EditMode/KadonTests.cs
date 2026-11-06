using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

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
            Assert.That(donFirst.TimingOffsetMs(0), Is.EqualTo(10).Within(1e-6));
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
            Assert.That(session.TimingOffsetMs(0), Is.EqualTo(60).Within(1e-6));
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

        // A real chart with four Kadons an eighth apart at BPM 224 (Charts/IrohaUta.txt, PeepoDrumKit
        // 2025-09-03): every note hit on time, each Kadon with don then ka 10 ms later, is a full combo.
        [Test] public void IrohaUtaFullComboWithItsKadons()
        {
            var chart = TjaParser.Parse(File.ReadAllText(Path.Combine(Application.dataPath, "OurTaiko", "Tests/EditMode/Charts/IrohaUta.txt")), "Oni");
            Assert.That(chart.Notes.Count(n => n.IsKadon), Is.EqualTo(4));
            int judgeable = new ChartStatistics(chart).JudgeableNotes;
            var session = new PlaySession(chart);
            foreach (var note in chart.Notes.Where(n => !n.IsLong))
            {
                if (note.IsKadon)
                {
                    Assert.That(session.Hit(false, note.Time), Is.EqualTo(Judgment.None));
                    Assert.That(session.Hit(true, note.Time + 0.01), Is.EqualTo(Judgment.Good));
                }
                else Assert.That(session.Hit(note.IsKa, note.Time), Is.EqualTo(Judgment.Good));
            }
            session.Advance(chart.Duration + 1, false);
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.Good, Is.EqualTo(judgeable));
            Assert.That(session.MaxCombo, Is.EqualTo(judgeable));

            var auto = new PlaySession(chart);
            auto.Advance(chart.Duration + 1, true);
            Assert.That(auto.Good, Is.EqualTo(judgeable));
        }
    }
}

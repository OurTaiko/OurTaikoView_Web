using NUnit.Framework;

namespace OurTaiko.Tests
{
    // The mine (C): drawn, hit with either colour within 可 like an Ad-Lib, and then it explodes: the
    // combo breaks and the gauge loses SoulGauge.MineDamage, shown as 不可 but not counted as one.
    // Left alone (autoplay always) it costs nothing. It is not in the note count.
    public sealed class MineTests
    {
        static TaikoChart Chart(string body) => TjaParser.Parse("TITLE:Test\nBPM:120\nCOURSE:Oni\n#START\n" + body + "\n#END");
        static PlaySession Session(string body) => new PlaySession(Chart(body));
        // 32 slots: a note every 62.5 ms.
        static string Thirtyseconds(string head) => head.PadRight(32, '0') + ",";

        [Test] public void EitherColourExplodesIt()
        {
            foreach (bool ka in new[] { false, true })
            {
                var session = Session("1C00,");
                Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
                double gauge = session.GaugePoints;
                Assert.That(session.Hit(ka, 0.5 + 0.05), Is.EqualTo(Judgment.Bad));
                Assert.That(session.Exploded[1], Is.True);
                Assert.That(session.Resolved[1], Is.True);
                Assert.That(session.MinesHit, Is.EqualTo(1));
                Assert.That(session.Combo, Is.Zero);
                Assert.That(session.MaxCombo, Is.EqualTo(1));
                // Shown as 不可, not counted as one, and no score lost.
                Assert.That(session.Bad, Is.Zero);
                Assert.That(session.Score, Is.EqualTo(session.BaseScore));
                Assert.That(session.GaugePoints, Is.EqualTo(gauge - SoulGauge.MineDamage * 100).Within(1e-6));
                Assert.That(session.TimingOffsetMs(1), Is.Null);
            }
        }

        // OpenTaiko: a mine hit loses the full combo (nMine > 0), and so the donderful one.
        [Test] public void AHitLosesTheFullCombo()
        {
            var session = Session("1C1C,");
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(session.Hit(false, 0.5), Is.EqualTo(Judgment.Bad));
            Assert.That(session.Hit(false, 1), Is.EqualTo(Judgment.Good));
            session.Advance(3, false);
            var result = PlayResult.From(session, "test", false);
            Assert.That(result.Bad, Is.Zero);
            Assert.That(result.Mines, Is.EqualTo(1));
            Assert.That(result.StoredCrown, Is.Not.EqualTo(Crown.FullCombo).And.Not.EqualTo(Crown.DonderfulCombo));

            var avoided = Session("1C1C,");
            Assert.That(avoided.Hit(false, 0), Is.EqualTo(Judgment.Good));
            Assert.That(avoided.Hit(false, 1), Is.EqualTo(Judgment.Good));
            avoided.Advance(3, false);
            Assert.That(PlayResult.From(avoided, "test", false).StoredCrown, Is.EqualTo(Crown.DonderfulCombo));
        }

        [Test] public void TheGaugeStopsAtZero()
        {
            var session = Session("C000,");
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Bad));
            Assert.That(session.GaugePoints, Is.Zero);
        }

        [Test] public void ItIsNotANote()
        {
            var plain = new ChartStatistics(Chart("1000,"));
            var stats = new ChartStatistics(Chart("1C00,"));
            Assert.That(stats.JudgeableNotes, Is.EqualTo(1));
            Assert.That(stats.RouteNotes, Is.EqualTo(plain.RouteNotes));
            Assert.That(Session("1C00,").BaseScore, Is.EqualTo(new ShinuchiScore(plain).BaseScore));
        }

        [Test] public void LeftAloneItCostsNothing()
        {
            var session = Session("1C10,");
            Assert.That(session.Hit(false, 0), Is.EqualTo(Judgment.Good));
            session.Advance(0.7, false);
            Assert.That(session.Resolved[1], Is.True);
            Assert.That(session.Exploded[1], Is.False);
            Assert.That(session.Missed[1], Is.False);
            Assert.That(session.Hit(false, 1), Is.EqualTo(Judgment.Good));
            Assert.That(session.Combo, Is.EqualTo(2));
            Assert.That(session.Bad, Is.Zero);
            Assert.That(session.MinesHit, Is.Zero);
        }

        [Test] public void APressInItsBadRangeIgnoresIt()
        {
            var session = Session("C000,");
            Assert.That(session.Hit(false, -0.09), Is.EqualTo(Judgment.None));
            Assert.That(session.Resolved[0], Is.False);
            Assert.That(session.MinesHit, Is.Zero);
        }

        // Judged from the nearest, as OpenTaiko: a press between a mine and a don goes to the nearer.
        [Test] public void APressBetweenItAndTheNextNoteGoesToTheNearer()
        {
            var nearMine = Session(Thirtyseconds("C1"));
            Assert.That(nearMine.Hit(false, 0.02), Is.EqualTo(Judgment.Bad));
            Assert.That(nearMine.Exploded[0], Is.True);
            Assert.That(nearMine.Resolved[1], Is.False);

            var nearDon = Session(Thirtyseconds("C1"));
            Assert.That(nearDon.Hit(false, 0.04), Is.EqualTo(Judgment.Good));
            Assert.That(nearDon.Resolved[1], Is.True);
            Assert.That(nearDon.Resolved[0], Is.False);
        }

        // The lanes do not see a mine: a press in its 不可 range does not judge it as a note, nor the don
        // after it, which is beyond its own 不可.
        [Test] public void ItIsNotInTheLanes()
        {
            var session = Session(Thirtyseconds("C1"));
            Assert.That(session.Hit(false, -0.09), Is.EqualTo(Judgment.None));
            Assert.That(session.Resolved[0] || session.Resolved[1], Is.False);
            Assert.That(session.Hit(false, 0.09), Is.EqualTo(Judgment.Ok));
            Assert.That(session.Resolved[1], Is.True);
            Assert.That(session.Exploded[0], Is.False);
            Assert.That(session.Bad, Is.Zero);
        }

        [Test] public void AutoplayLeavesIt()
        {
            var session = Session("1C2C,");
            session.Advance(2, true);
            Assert.That(session.Good, Is.EqualTo(2));
            Assert.That(session.MinesHit, Is.Zero);
            Assert.That(session.Combo, Is.EqualTo(2));
            Assert.That(session.Resolved, Is.All.True);
        }

        // E is a blank: the same session as a 0.
        [Test] public void EIsABlank()
        {
            var session = Session("1E2E,");
            Assert.That(session.Chart.Notes.Count, Is.EqualTo(2));
            Assert.That(session.Chart.Notes[1].Kind, Is.EqualTo(NoteKind.Ka));
        }
    }
}

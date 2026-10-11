using NUnit.Framework;

namespace OurTaiko.Tests
{
    // GOGO follows its #GOGOSTART / #GOGOEND events (handle_gogotime), not the notes around them.
    public sealed class GogoTests
    {
        // BPM 240: one measure per second, the first measure is 0–1 s.
        static TaikoChart Parse(string body) => TjaParser.Parse("BPM:240\nCOURSE:Oni\nLEVEL:10\n#START\n1111,\n" + body + "\n1111,\n#END");

        [Test]
        public void GogoStartsAndEndsAtItsCommandsEvenWithoutNotes()
        {
            // GOGO 1–3 s with its only note at 2.75 s, a long silent stretch before it.
            var session = new PlaySession(Parse("#GOGOSTART\n0,\n0001,\n#GOGOEND\n0,"));
            Assert.That(session.IsGogo(0.999), Is.False, "No early start one measure (or 1 s) before the first GOGO note.");
            Assert.That(session.IsGogo(1), Is.True);
            Assert.That(session.IsGogo(2.5), Is.True);
            Assert.That(session.IsGogo(2.999), Is.True, "Stays on after the last GOGO note has passed.");
            Assert.That(session.IsGogo(3), Is.False);
        }

        [Test]
        public void MidMeasureCommandsTakeTheirSlotTime()
        {
            var chart = Parse("10\n#GOGOSTART\n10\n#GOGOEND\n10,");
            Assert.That(chart.Gogos.Count, Is.EqualTo(2));
            Assert.That(chart.Gogos[0].Time, Is.EqualTo(1 + 1 / 3.0).Within(1e-9));
            Assert.That(chart.Gogos[1].Time, Is.EqualTo(1 + 2 / 3.0).Within(1e-9));
        }

        [Test]
        public void HiddenNotesDoNotTurnGogoOff()
        {
            var chart = Parse("#GOGOSTART\n1111,\n#GOGOEND");
            foreach (var note in chart.Notes) note.Display = false; // ドロン
            Assert.That(new PlaySession(chart).IsGogo(1.5), Is.True);
        }

        [TestCase(BranchRoute.Normal, false)]
        [TestCase(BranchRoute.Master, true)]
        public void OnlyThePlayedRouteSwitchesGogo(BranchRoute route, bool gogo)
        {
            var chart = Parse("#BRANCHSTART p,50,80\n#N\n1111,\n#E\n1111,\n#M\n#GOGOSTART\n1111,\n#BRANCHEND\n1111,\n#GOGOEND");
            var session = new PlaySession(chart, 0, route);
            session.Advance(1.5, true);
            Assert.That(session.IsGogo(1.5), Is.EqualTo(gogo));
            Assert.That(session.IsGogo(1.5, preview: true), Is.EqualTo(gogo));
            Assert.That(session.IsGogo(0.5), Is.False);
        }
    }
}

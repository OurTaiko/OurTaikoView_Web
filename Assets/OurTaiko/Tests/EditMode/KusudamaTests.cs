using System.Linq;
using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class KusudamaTests
    {
        // At BPM 120 a measure is 2 s: the kusudama of "9008," runs 0-1.5 s.
        static TaikoChart Parse(string body, string metadata = "BALLOON:4,4") => TjaParser.Parse(
            "BPM:120\nCOURSE:Oni\nLEVEL:8\n" + metadata + "\n#START\n" + body + "\n#END");

        [Test]
        public void AloneItBreaksAtItsOwnCountWithDonsOnly()
        {
            var session = new PlaySession(Parse("9008,"));
            var ball = session.KusudamaOf(0);
            Assert.That(ball, Is.Not.Null);
            Assert.That(ball.Required, Is.EqualTo(4));
            Assert.That(session.Hit(true, 0.1), Is.EqualTo(Judgment.None), "Ka does not count, as for balloons.");
            for (int i = 0; i < 3; i++) session.Hit(false, 0.2);
            Assert.That(ball.Remaining, Is.EqualTo(1));
            Assert.That(ball.IsRolling(session, 0.2), Is.True);
            session.Hit(false, 0.3);
            Assert.That(ball.IsBroken, Is.True);
            Assert.That(ball.BrokenBy, Is.SameAs(session));
            Assert.That(session.Resolved[0], Is.True);
            Assert.That(session.Rolls, Is.EqualTo(4));
            Assert.That(session.Score, Is.EqualTo(400), "Each hit is a 100-point roll hit; breaking adds nothing (shinuchi).");
        }

        [Test]
        public void TwoSeatsShareOneBallAndItsSummedCount()
        {
            var first = new PlaySession(Parse("9008,", "BALLOON:4"));
            var second = new PlaySession(Parse("9008,", "BALLOON:6"));
            Kusudama.Share(first, second);
            var ball = first.KusudamaOf(0);
            Assert.That(second.KusudamaOf(0), Is.SameAs(ball));
            Assert.That(ball.Required, Is.EqualTo(10), "OpenTaiko adds every linked seat's count.");
            int version = second.Version;
            for (int i = 0; i < 7; i++) first.Hit(false, 0.2);
            Assert.That(second.Version, Is.GreaterThan(version), "The other seat redraws its shared count.");
            for (int i = 0; i < 2; i++) second.Hit(false, 0.2);
            Assert.That(ball.Remaining, Is.EqualTo(1));
            second.Hit(false, 0.3);
            Assert.That(ball.IsBroken, Is.True);
            Assert.That(ball.BrokenBy, Is.SameAs(second));
            Assert.That(first.Resolved[0] && second.Resolved[0], Is.True, "One hit breaks it for both seats.");
            Assert.That(new[] { first.Rolls, second.Rolls }, Is.EqualTo(new[] { 7, 3 }), "Each seat counts its own hits.");
            Assert.That(new[] { first.Score, second.Score }, Is.EqualTo(new[] { 700, 300 }));
            Assert.That(first.Hit(false, 0.4), Is.EqualTo(Judgment.None));
        }

        [Test]
        public void UnbrokenBallEndsAtItsTail()
        {
            var first = new PlaySession(Parse("9008,"));
            var second = new PlaySession(Parse("9008,"));
            Kusudama.Share(first, second);
            first.Hit(false, 0.2);
            first.Advance(1.6, false); second.Advance(1.6, false);
            var ball = first.KusudamaOf(0);
            Assert.That(ball.IsBroken, Is.False);
            Assert.That(first.Resolved[0] && second.Resolved[0], Is.True);
            Assert.That(ball.IsRolling(first, 1.6), Is.False);
            Assert.That(first.Bad, Is.Zero, "A missed kusudama is no judgment.");
        }

        [Test]
        public void KusudamaWithoutAPartnerPlaysAsABalloon()
        {
            // 2P's ball starts 0.5 s later: no match within 100 ms, so each seat's is a balloon.
            var first = new PlaySession(Parse("9008,\n9008,"));
            var second = new PlaySession(Parse("09008000,\n9008,"));
            Kusudama.Share(first, second);
            Assert.That(first.Chart.Notes[0].Kind, Is.EqualTo(NoteKind.Balloon));
            Assert.That(first.Chart.Notes[0].Moji, Is.EqualTo(NoteMoji.Balloon));
            Assert.That(second.Chart.Notes[0].Kind, Is.EqualTo(NoteKind.Balloon));
            Assert.That(first.KusudamaOf(0), Is.Null);
            Assert.That(first.KusudamaOf(1), Is.SameAs(second.KusudamaOf(1)), "The second measure still matches.");
            for (int i = 0; i < 4; i++) first.Hit(false, 0.2);
            Assert.That(first.Resolved[0], Is.True, "Popped on its own count.");
        }

        [Test]
        public void OnlyTheRoutePlayedCountsTowardTheBall()
        {
            const string branched = "0000,\n#BRANCHSTART p,101,102\n#N\n9008,\n#E\n9008,\n#M\n9008,\n#BRANCHEND";
            var first = new PlaySession(Parse(branched, "BALLOON:2\nBALLOONEXP:5\nBALLOONMAS:9"));
            var second = new PlaySession(Parse("0000,\n9008,", "BALLOON:3"));
            Kusudama.Share(first, second);
            first.Advance(2, false); second.Advance(2, false);
            var ball = second.KusudamaOf(0);
            Assert.That(first.Chart.Notes.Where(n => n.Kind == NoteKind.Kusudama).Count(), Is.EqualTo(3));
            Assert.That(ball.Members.Count, Is.EqualTo(4));
            Assert.That(ball.Required, Is.EqualTo(5), "Normal (2) plus 2P's 3; the unplayed routes add nothing.");
        }

        [Test]
        public void RepeatedRollHeadIsABlank()
        {
            // TaikoJiro's 9…9…8: the second 9 marks the bonus border and is no note.
            var chart = Parse("9009,\n0008,");
            Assert.That(chart.Notes.Count, Is.EqualTo(1));
            Assert.That(chart.Notes[0].EndTime, Is.EqualTo(3.5).Within(1e-9));
            Assert.That(Parse("5050,\n8000,").Notes.Count, Is.EqualTo(1));
        }

        [Test]
        public void MotionFollowsTheNijiiroAnimations()
        {
            Assert.That(KusudamaMotion.Drop(0), Is.Zero);
            Assert.That(KusudamaMotion.Drop(125), Is.EqualTo(810).Within(1e-9));
            Assert.That(KusudamaMotion.Drop(250), Is.EqualTo(1080));
            Assert.That(KusudamaMotion.Drop(1000), Is.EqualTo(930));
            Assert.That(KusudamaMotion.RendaAlpha(KusudamaMotion.DropMs, double.NaN), Is.Zero);
            Assert.That(KusudamaMotion.RendaAlpha(500, double.NaN), Is.EqualTo(1));
            Assert.That(KusudamaMotion.RendaAlpha(500, 100), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(KusudamaMotion.RendaMove(1000), Is.EqualTo(-360), "Settled, at the bottom of its bob.");
            Assert.That(KusudamaMotion.Stretch(0), Is.EqualTo(2));
            Assert.That(KusudamaMotion.Stretch(50), Is.EqualTo(12));
            Assert.That(KusudamaMotion.Stretch(200), Is.Zero);
            Assert.That(KusudamaMotion.Breathing(150), Is.EqualTo(1.05));
            Assert.That(KusudamaMotion.Breathing(400), Is.EqualTo(1));
            Assert.That(Enumerable.Range(0, 6).Select(i => KusudamaMotion.OpenFrame(i * 100)), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 4 }));
            Assert.That(KusudamaMotion.Alpha(700), Is.EqualTo(1 - 34 / 150.0).Within(1e-9));
            Assert.That(KusudamaMotion.Alpha(KusudamaMotion.PoppedMs), Is.Zero);
        }
    }
}

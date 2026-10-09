using NUnit.Framework;

namespace OurTaiko.Tests
{
    public sealed class NoteExpressionTests
    {
        static TaikoChart Parse(string body, double offset = 0) => TjaParser.Parse(
            "BPM:120\nOFFSET:" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "\nCOURSE:Oni\nLEVEL:1\n#START\n" + body + "\n#END");

        [TestCase(49, 4, 0)]
        [TestCase(50, 3.999, 0)]
        [TestCase(50, 4, 1)]
        [TestCase(50, 4.499, 1)]
        [TestCase(50, 4.5, 0)]
        [TestCase(149, 5, 1)]
        [TestCase(150, 5.25, 1)] // Wait for beat 6 before switching to sixteenths.
        [TestCase(150, 5.99999999, 1)]
        [TestCase(150, 6, 1)]
        [TestCase(150, 6.249, 1)]
        [TestCase(150, 6.25, 0)]
        [TestCase(151, 6.5, 1)]
        [TestCase(200, 6.75, 0)]
        public void ThresholdsSwitchOnWholeBeats(int combo, double beat, int expected)
            => Assert.That(NoteExpression.Frame(beat, combo, 4, 6), Is.EqualTo(expected));

        [TestCase(1.75, 2)] // A in the reference image.
        [TestCase(4, 4)]
        [TestCase(4.00000000001, 4)]
        public void ThresholdStartRoundsUpToTheAuthoredWholeBeat(double beat, double expected)
            => Assert.That(NoteExpression.StartBeat(beat), Is.EqualTo(expected));

        static PlaySession ReachCombo(string body, int combo, double hitOffset = 0)
        {
            var session = new PlaySession(Parse(body));
            for (int i = 0; i < combo; i++)
                Assert.That(session.Hit(false, session.Chart.Notes[i].Time + hitOffset), Is.EqualTo(Judgment.Good));
            return session;
        }

        [TestCase(-0.01)]
        [TestCase(0.01)]
        public void FiftyOnAnOffbeatWaitsForTheNextWholeBeat(double hitOffset)
        {
            var session = ReachCombo(new string('1', 49) + ",\n01000000,\n1,", 50, hitOffset);
            Assert.That(session.NoteExpressionFrame(2.25), Is.Zero);
            Assert.That(session.NoteExpressionFrame(2.499), Is.Zero);
            Assert.That(session.NoteExpressionFrame(2.5), Is.EqualTo(1));
            Assert.That(session.NoteExpressionFrame(2.75), Is.Zero);
        }

        [TestCase(-0.01)]
        [TestCase(0.01)]
        public void FiftyOnAWholeBeatStartsOnThatBeat(double hitOffset)
        {
            var session = ReachCombo(new string('1', 49) + ",\n1,", 50, hitOffset);
            Assert.That(session.NoteExpressionFrame(2), Is.EqualTo(1));
            Assert.That(session.NoteExpressionFrame(2.25), Is.Zero);
        }

        [Test]
        public void FastTierWaitsForWholeBeatAndMissResetsBothTiers()
        {
            var session = ReachCombo(new string('1', 149) + ",\n01000000,\n1,", 150);
            Assert.That(session.NoteExpressionFrame(2.375), Is.Zero, "Keep eighths before beat 5.");
            Assert.That(session.NoteExpressionFrame(2.5), Is.EqualTo(1));
            Assert.That(session.NoteExpressionFrame(2.625), Is.Zero);
            Assert.That(session.NoteExpressionFrame(2.75), Is.EqualTo(1));
            session.Advance(4.2, false);
            Assert.That(session.Combo, Is.Zero);
            Assert.That(session.NoteExpressionFrame(4.5), Is.Zero);
            Assert.That(PlaySession.PracticeAt(session.Chart, 2, session).NoteExpressionFrame(2.5), Is.Zero);
        }

        [Test]
        public void BeatPhaseUsesOffsetTempoAndDelay()
        {
            var session = new PlaySession(Parse("100\n#BPMCHANGE 180\n1,\n#DELAY 0.3\n1,", 0.37));
            Assert.That(session.BeatAt(-0.37), Is.EqualTo(0).Within(1e-8));
            Assert.That(session.BeatAt(1.13), Is.EqualTo(3).Within(1e-8));
            double end = 1.13 + 1.0 / 3;
            Assert.That(session.BeatAt(end + 0.1), Is.EqualTo(4).Within(1e-8));
            Assert.That(session.BeatAt(end + 0.3), Is.EqualTo(4).Within(1e-8));
            Assert.That(session.BeatAt(end + 0.3 + 1.0 / 6), Is.EqualTo(4.5).Within(1e-8));
            Assert.That(session.BeatAt(-0.12), Is.EqualTo(0.5).Within(1e-8));
        }

        [Test]
        public void SilentTempoCommandsUseTheirExactTimeAndSupportBackwardSeeking()
        {
            var session = new PlaySession(Parse("0,\n#BPMCHANGE 240\n#DELAY 1\n0001,", 0.5));
            Assert.That(session.BpmAt(1.499), Is.EqualTo(120));
            Assert.That(session.BpmAt(1.5), Is.EqualTo(240));
            Assert.That(session.BpmAt(2.4), Is.EqualTo(240), "No note or bar is needed to apply BPMCHANGE.");
            Assert.That(session.BpmAt(-1), Is.EqualTo(120), "A backward seek must restore the earlier tempo.");
        }

        [Test]
        public void MidMeasureAndSameTimeChangesKeepSourceOrderAndBeatPhase()
        {
            var session = new PlaySession(Parse("10\n#BPMCHANGE 180\n#BPMCHANGE 240\n01,"));
            Assert.That(session.Chart.Tempos[0].Time, Is.EqualTo(1));
            Assert.That(session.BpmAt(0.999), Is.EqualTo(120));
            Assert.That(session.BpmAt(1), Is.EqualTo(240));
            Assert.That(session.BeatAt(1.13), Is.EqualTo(2.52).Within(1e-8));
        }

        [TestCase(BranchRoute.Normal, 120)]
        [TestCase(BranchRoute.Expert, 180)]
        [TestCase(BranchRoute.Master, 240)]
        public void TempoFollowsOnlyTheSelectedRouteIncludingPracticePreview(BranchRoute route, double bpm)
        {
            var chart = Parse("1,\n#BRANCHSTART p,50,80\n#N\n0,\n#E\n#BPMCHANGE 180\n0,\n#M\n#BPMCHANGE 240\n0,\n#BRANCHEND\n#BPMCHANGE 150\n0,");
            var session = new PlaySession(chart, 0, route);
            Assert.That(session.BpmAt(2.1, preview: true), Is.EqualTo(bpm));
            Assert.That(session.BeatAt(2.1, preview: true), Is.EqualTo(4 + 0.1 * bpm / 60).Within(1e-8));
            session.Advance(2.1, true);
            Assert.That(session.BpmAt(2.1), Is.EqualTo(bpm));
            Assert.That(session.BpmAt(1.9), Is.EqualTo(120));
            Assert.That(session.BpmAt(3), Is.EqualTo(150));
        }
    }
}

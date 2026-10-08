using NUnit.Framework;

namespace OurTaiko.Tests
{
    // p branches: truncated percentage of normal notes judged since the last reset,
    // 良 = 1, 可 = 0.5, 不可 and misses = 0. At BPM 120 the branch below starts at 6 s and is
    // decided when its first object loads (about 3.85 s), after every lead note is settled.
    public sealed class AccuracyBranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Accuracy Branch\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static PlaySession Session(string condition, string lead, string metadata = "") =>
            new PlaySession(Parse(lead + "\n#BRANCHSTART " + condition + Routes, metadata));
        static void Decide(PlaySession session) => session.Advance(session.Chart.Branches[0].DecisionTime, false);

        // Lead notes at 0, 0.5, 1 and 1.5 s: the first `good` are hit on time, the next `ok`
        // 50 ms late, and the rest are left to time out.
        static void Play(PlaySession session, int good, int ok)
        {
            for (int i = 0; i < good + ok; i++) session.Hit(false, i * 0.5 + (i >= good ? 0.05 : 0));
        }

        [TestCase(0, 0, 0, BranchRoute.Normal)]
        [TestCase(1, 1, 37, BranchRoute.Normal)]
        [TestCase(2, 0, 50, BranchRoute.Expert)]   // equal to the expert threshold
        [TestCase(2, 1, 62, BranchRoute.Expert)]
        [TestCase(3, 0, 75, BranchRoute.Master)]   // equal to the master threshold
        [TestCase(3, 1, 87, BranchRoute.Master)]
        [TestCase(4, 0, 100, BranchRoute.Master)]
        public void ThresholdsAreInclusiveAndThePercentageIsTruncated(int good, int ok, int value, BranchRoute expected)
        {
            var session = Session("p,50,75", "1111,\n0000,\n0000,");
            Play(session, good, ok);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(value));
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
        }

        [Test] public void BadHitsAndTimeoutsBothCountAsZero()
        {
            var bad = Session("p,50,75", "1111,\n0000,\n0000,");
            bad.Hit(false, 0); bad.Hit(false, 0.5); bad.Hit(false, 1.09); bad.Hit(false, 1.59);
            Decide(bad);
            Assert.That(bad.Bad, Is.EqualTo(2));
            var missed = Session("p,50,75", "1111,\n0000,\n0000,");
            Play(missed, 2, 0);
            Decide(missed);
            Assert.That(missed.Bad, Is.EqualTo(2));
            Assert.That(bad.LastBranchValue, Is.EqualTo(50));
            Assert.That(missed.LastBranchValue, Is.EqualTo(50));
        }

        [Test] public void BigNotesWeighTheSameAsSmallOnes()
        {
            var session = Session("p,50,75", "3344,\n0000,\n0000,");
            session.Hit(false, 0); session.Hit(false, 0.5);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(50));
        }

        [Test] public void DrumrollAndBalloonHitsDoNotMoveThePercentage()
        {
            // Drumroll 2–3.5 s, balloon 4–5.5 s; the branch moves to 8 s and decides at about 5.85 s.
            var session = Session("p,50,75", "1100,\n5008,\n7008,\n0000,", "BALLOON:2");
            session.Hit(false, 0);
            for (int i = 0; i < 8; i++) session.Hit(i % 2 == 0, 2.1 + i * 0.1);
            session.Hit(false, 4.1); session.Hit(false, 4.2);
            Decide(session);
            Assert.That(session.Rolls, Is.EqualTo(10));
            Assert.That(session.LastBranchValue, Is.EqualTo(50), "one 良 out of two notes");
        }

        [Test] public void AutoPlayIsAllGood()
        {
            var session = Session("p,50,75", "1111,\n0000,\n0000,");
            session.Advance(session.Chart.Branches[0].DecisionTime, true);
            Assert.That(session.LastBranchValue, Is.EqualTo(100));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Master));
        }

        [Test] public void NoNotesIsZeroPercent()
        {
            var zeroExpert = Session("p,0,75", "0000,\n0000,\n0000,");
            Decide(zeroExpert);
            Assert.That(zeroExpert.LastBranchValue, Is.Zero);
            Assert.That(zeroExpert.CurrentBranch, Is.EqualTo(BranchRoute.Expert), "0 reaches a threshold of 0");
            var positive = Session("p,1,75", "0000,\n0000,\n0000,");
            Decide(positive);
            Assert.That(positive.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
        }

        [Test] public void SectionStartsANewCount()
        {
            // The four notes before #SECTION are missed; the four after it are all 良.
            var session = Session("p,50,75", "1111,\n#SECTION\n1111,\n0000,");
            for (int i = 0; i < 4; i++) session.Hit(false, 2 + i * 0.5);
            Decide(session);
            Assert.That(session.Bad, Is.EqualTo(4));
            Assert.That(session.LastBranchValue, Is.EqualTo(100));
        }

        [Test] public void TheNextBranchCountsOnlyThePlayedRoute()
        {
            // The lead chooses 達人 (3434 at 6–7.5 s); half of it is hit before the second
            // branch decides at about 9.85 s. 普通 (1000) would have had one note.
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART p,50,75" + Routes + "0000,\n0000,\n#BRANCHSTART p,50,75" + Routes);
            var session = new PlaySession(chart);
            Play(session, 4, 0);
            session.Hit(false, 6); session.Hit(true, 6.5);
            session.Advance(chart.Branches[1].DecisionTime, false);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Expert }));
            Assert.That(session.LastBranchValue, Is.EqualTo(50));
        }
    }
}

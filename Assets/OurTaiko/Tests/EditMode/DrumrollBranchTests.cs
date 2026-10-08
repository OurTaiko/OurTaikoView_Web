using NUnit.Framework;

namespace OurTaiko.Tests
{
    // r branches: drumroll (5/6) hits since the last reset, or all hits on a drumroll still
    // running at the decision if that is more. At BPM 120 a branch after three lead measures
    // starts at 6 s and decides at about 3.85 s.
    public sealed class DrumrollBranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Drumroll Branch\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static PlaySession Session(string condition, string lead, string metadata = "") =>
            new PlaySession(Parse(lead + "\n#BRANCHSTART " + condition + Routes, metadata));
        static void Decide(PlaySession session) => session.Advance(session.Chart.Branches[0].DecisionTime, false);

        // Drumroll from 0 to 1.5 s.
        const string Roll = "5008,\n0000,\n0000,";

        [TestCase(0, BranchRoute.Normal)]
        [TestCase(2, BranchRoute.Normal)]
        [TestCase(3, BranchRoute.Expert)]   // equal to the expert threshold
        [TestCase(4, BranchRoute.Expert)]
        [TestCase(5, BranchRoute.Master)]   // equal to the master threshold
        [TestCase(12, BranchRoute.Master)]
        public void ThresholdsAreInclusive(int hits, BranchRoute expected)
        {
            var session = Session("r,3,5", Roll);
            for (int i = 0; i < hits; i++) session.Hit(false, 0.1 + i * 0.1);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(hits));
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
        }

        [Test] public void DonKaAndBigDrumrollsAllCount()
        {
            // Big drumroll 0–1.5 s, small drumroll 2–3.5 s.
            var session = Session("r,3,5", "6008,\n5008,\n0000,");
            for (int i = 0; i < 3; i++) session.Hit(true, 0.1 + i * 0.1);
            for (int i = 0; i < 3; i++) session.Hit(false, 2.1 + i * 0.1);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(6));
        }

        [Test] public void BalloonAndKusudamaHitsDoNotCount()
        {
            // Balloon 0–1.5 s, kusudama 2–3.5 s.
            var session = Session("r,1,3", "7008,\n9008,\n0000,", "BALLOON:3,3");
            for (int i = 0; i < 3; i++) session.Hit(false, 0.1 + i * 0.1);
            for (int i = 0; i < 3; i++) session.Hit(false, 2.1 + i * 0.1);
            Decide(session);
            Assert.That(session.Rolls, Is.EqualTo(6));
            Assert.That(session.LastBranchValue, Is.Zero);
        }

        [Test] public void NoteJudgmentsAndHitsOutsideADrumrollDoNotCount()
        {
            var session = Session("r,1,3", "1111,\n0000,\n0000,");
            for (int i = 0; i < 4; i++) session.Hit(false, i * 0.5);
            session.Hit(false, 2.5); session.Hit(true, 3);
            Decide(session);
            Assert.That(session.Good, Is.EqualTo(4));
            Assert.That(session.LastBranchValue, Is.Zero);
        }

        [Test] public void ADrumrollAcrossTheDecisionCountsAllItsHits()
        {
            // Drumroll 0–7.5 s across #SECTION at 2 s and the decision at about 5.85 s: two hits
            // before the reset and two after. The section count is 2 but the drumroll has 4.
            var session = Session("r,3,5", "5000,\n#SECTION\n0000,\n0000,\n0008,");
            session.Hit(false, 0.1); session.Hit(false, 0.2);
            session.Hit(false, 3); session.Hit(false, 3.1);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(4));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
        }

        [Test] public void ADrumrollEndedBeforeTheDecisionOnlyCountsSinceTheReset()
        {
            // Same hits, but the drumroll ends at 3.5 s, before the decision at about 5.85 s.
            var session = Session("r,3,5", "5000,\n#SECTION\n0008,\n0000,\n0000,");
            session.Hit(false, 0.1); session.Hit(false, 0.2);
            session.Hit(false, 3); session.Hit(false, 3.1);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(2));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
        }

        [Test] public void HitsAfterADecisionCountForTheNextBranch()
        {
            // The lead decides 玄人; its drumroll at 6–7.5 s feeds only the second branch,
            // which decides at about 9.85 s.
            const string RollRoutes = "\n#N\n5008,\n#E\n5008,\n#M\n5008,\n#BRANCHEND\n";
            var chart = Parse(Roll + "\n#BRANCHSTART r,3,5" + RollRoutes + "0000,\n0000,\n#BRANCHSTART r,3,5" + Routes);
            var session = new PlaySession(chart);
            for (int i = 0; i < 3; i++) session.Hit(false, 0.1 + i * 0.1);
            for (int i = 0; i < 5; i++) session.Hit(false, 6.1 + i * 0.1);
            session.Advance(chart.Branches[1].DecisionTime, false);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Expert, BranchRoute.Master }));
            Assert.That(session.LastBranchValue, Is.EqualTo(5));
        }
    }
}

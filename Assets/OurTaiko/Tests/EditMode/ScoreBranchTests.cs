using NUnit.Framework;

namespace OurTaiko.Tests
{
    // s branches: Shinuchi points scored since the last reset, from notes, drumrolls and
    // balloons alike. At BPM 120 a branch after three lead measures starts at 6 s and decides
    // at about 3.85 s. With four lead notes and four 達人 notes the base score is 125000.
    public sealed class ScoreBranchTests
    {
        static TaikoChart Parse(string body, string metadata = "") => TjaParser.Parse(
            "TITLE:Score Branch\nBPM:120\nCOURSE:Oni\n" + metadata + "\n#START\n" + body + "\n#END");
        const string Routes = "\n#N\n1000,\n#E\n2200,\n#M\n3434,\n#BRANCHEND\n";
        static PlaySession Session(string condition, string lead, string metadata = "") =>
            new PlaySession(Parse(lead + "\n#BRANCHSTART " + condition + Routes, metadata));
        static void Decide(PlaySession session) => session.Advance(session.Chart.Branches[0].DecisionTime, false);

        [TestCase(0, 0, 0, BranchRoute.Normal)]
        [TestCase(1, 0, 125000, BranchRoute.Normal)]
        [TestCase(1, 2, 250000, BranchRoute.Expert)]   // 可 is half of 良
        [TestCase(2, 0, 250000, BranchRoute.Expert)]   // equal to the expert threshold
        [TestCase(3, 1, 437500, BranchRoute.Expert)]
        [TestCase(4, 0, 500000, BranchRoute.Master)]   // equal to the master threshold
        public void ScoreComparesPointsScoredBeforeTheDecision(int good, int ok, int points, BranchRoute expected)
        {
            var session = Session("s,250000,500000", "1111,\n0000,\n0000,");
            Assert.That(session.BaseScore, Is.EqualTo(125000));
            for (int i = 0; i < good + ok; i++) session.Hit(false, i * 0.5 + (i >= good ? 0.05 : 0));
            double at = session.Chart.Branches[0].DecisionTime;
            session.Advance(at - 0.001, false);
            Assert.That(session.SelectedRoute(0), Is.Null);
            session.Advance(at, false);
            Assert.That(session.CurrentBranch, Is.EqualTo(expected));
            Assert.That(session.LastBranchValue, Is.EqualTo(points));
            Assert.That(session.Score, Is.EqualTo(points));
        }

        [Test] public void BadHitsAndMissesScoreNothing()
        {
            var session = Session("s,250000,500000", "1111,\n0000,\n0000,");
            session.Hit(false, 0); session.Hit(false, 0.59);
            Decide(session);
            Assert.That(session.Bad, Is.EqualTo(3));
            Assert.That(session.LastBranchValue, Is.EqualTo(125000));
        }

        [Test] public void BigNotesHaveNoMultiplier()
        {
            var session = Session("s,250000,500000", "3344,\n0000,\n0000,");
            session.Hit(false, 0); session.Hit(false, 0.5); session.Hit(true, 1); session.Hit(true, 1.5);
            Decide(session);
            Assert.That(session.LastBranchValue, Is.EqualTo(4 * session.BaseScore));
        }

        [Test] public void AutoPlayScoresEveryNoteAsGood()
        {
            var session = Session("s,250000,500000", "1111,\n0000,\n0000,");
            session.Advance(session.Chart.Branches[0].DecisionTime, true);
            Assert.That(session.LastBranchValue, Is.EqualTo(500000));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Master));
        }

        [Test] public void DrumrollAndBalloonHitsScoreToo()
        {
            // Drumroll 0–1.5 s, balloon 2–3.5 s popped on its third hit; 100 points per hit.
            var session = Session("s,500,800", "5008,\n7008,\n0000,", "BALLOON:3");
            for (int i = 0; i < 4; i++) session.Hit(i % 2 == 0, 0.1);
            for (int i = 0; i < 3; i++) session.Hit(false, 2.1);
            Decide(session);
            Assert.That(session.Rolls, Is.EqualTo(7));
            Assert.That(session.LastBranchValue, Is.EqualTo(700));
            Assert.That(session.CurrentBranch, Is.EqualTo(BranchRoute.Expert));
        }

        [Test] public void SectionStartsANewCount()
        {
            var section = new PlaySession(Parse("1111,\n#SECTION\n1000,\n0000,\n#BRANCHSTART s,1,2" + Routes));
            for (int i = 0; i < 4; i++) section.Hit(false, i * 0.5);
            Decide(section);
            Assert.That(section.Score, Is.EqualTo(4 * section.BaseScore));
            Assert.That(section.LastBranchValue, Is.Zero);
            Assert.That(section.CurrentBranch, Is.EqualTo(BranchRoute.Normal));
        }

        [Test] public void EachDecisionStartsANewCount()
        {
            // Four 良 at a base of 83340 (12 notes) reach 300000. After the first decision the
            // 達人 notes at 6–7.5 s are missed, so the second branch (about 9.85 s) sees 0.
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART s,1,300000" + Routes + "0000,\n0000,\n#BRANCHSTART s,1,300000" + Routes);
            var twice = new PlaySession(chart);
            for (int i = 0; i < 4; i++) twice.Hit(false, i * 0.5);
            twice.Advance(chart.Branches[1].DecisionTime, false);
            Assert.That(twice.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Normal }));
            Assert.That(twice.LastBranchValue, Is.Zero);
        }

        [Test] public void PointsAfterTheDecisionAreLeftForTheNextBranch()
        {
            // The 達人 notes at 6 and 6.5 s are hit after the first decision and before the second.
            var chart = Parse("1111,\n0000,\n0000,\n#BRANCHSTART s,1,300000" + Routes + "0000,\n0000,\n#BRANCHSTART s,1,300000" + Routes);
            var session = new PlaySession(chart);
            for (int i = 0; i < 4; i++) session.Hit(false, i * 0.5);
            session.Hit(false, 6); session.Hit(true, 6.5);
            session.Advance(chart.Branches[1].DecisionTime, false);
            Assert.That(session.BranchHistory, Is.EqualTo(new[] { BranchRoute.Master, BranchRoute.Expert }));
            Assert.That(session.LastBranchValue, Is.EqualTo(2 * session.BaseScore));
        }
    }
}

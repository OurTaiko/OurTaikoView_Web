using NUnit.Framework;

namespace OurTaiko.Tests
{
    // #LANE_ALPHA as the play session reads it: the common commands always, a route's once its
    // branch is decided (and in a practice preview, the route it would show).
    public sealed class LaneAlphaTests
    {
        // BPM 240: a measure is a second; the branch starts at 1 s and is decided at 0 s.
        const string Branching = "BPM:240\nCOURSE:Oni\n#START\n#LANE_ALPHA 50\n0,\n#BRANCHSTART p,50,80\n"
            + "#N\n#LANE_ALPHA 10\n0,\n#E\n0,\n#M\n#LANE_ALPHA 80\n0,\n#BRANCHEND\n0,\n#END";

        [Test]
        public void AChartWithoutCommandsIsOpaque()
        {
            var session = new PlaySession(TjaParser.Parse("BPM:240\nCOURSE:Oni\n#START\n1000,\n#END"));
            Assert.That(session.LaneAlphaAt(0.5), Is.EqualTo(1));
        }

        [Test]
        public void FadesFollowTheChart()
        {
            var session = new PlaySession(TjaParser.Parse(
                "BPM:240\nCOURSE:Oni\n#START\n0,\n#LANE_ALPHA 0 500\n0,\n#LANE_ALPHA 40 -500\n0,\n0,\n#END"));
            Assert.That(session.LaneAlphaAt(0.9), Is.EqualTo(1));
            Assert.That(session.LaneAlphaAt(1.25), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(session.LaneAlphaAt(1.5), Is.EqualTo(0));
            Assert.That(session.LaneAlphaAt(1.75), Is.EqualTo(0.2).Within(1e-9));
            Assert.That(session.LaneAlphaAt(3), Is.EqualTo(0.4).Within(1e-9));
            // A seek back reads the same values: nothing is accumulated.
            Assert.That(session.LaneAlphaAt(1.25), Is.EqualTo(0.5).Within(1e-9));
        }

        [TestCase(BranchRoute.Normal, 0.1)]
        [TestCase(BranchRoute.Expert, 0.5)]
        [TestCase(BranchRoute.Master, 0.8)]
        public void ARouteCountsOnceItIsChosen(BranchRoute route, double alpha)
        {
            var session = new PlaySession(TjaParser.Parse(Branching), 0, route);
            Assert.That(session.LaneAlphaAt(1.5), Is.EqualTo(0.5), "Undecided: only the common command.");
            Assert.That(session.LaneAlphaAt(1.5, preview: true), Is.EqualTo(alpha).Within(1e-9));
            session.Advance(1.5, true);
            Assert.That(session.LaneAlphaAt(1.5), Is.EqualTo(alpha).Within(1e-9));
        }

        [Test]
        public void AMissingRouteUsesTheOneDrawnInItsPlace()
        {
            // No #E: an expert player is shown #N, and its command.
            var chart = TjaParser.Parse("BPM:240\nCOURSE:Oni\n#START\n0,\n#BRANCHSTART p,50,80\n"
                + "#N\n#LANE_ALPHA 10\n0,\n#M\n#LANE_ALPHA 80\n0,\n#BRANCHEND\n0,\n#END");
            var session = new PlaySession(chart, 0, BranchRoute.Expert);
            session.Advance(1.5, true);
            Assert.That(session.LaneAlphaAt(1.5), Is.EqualTo(0.1).Within(1e-9));
        }
    }
}

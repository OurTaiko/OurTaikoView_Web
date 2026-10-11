using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class ScoreRankTests
    {
        [Test]
        public void UnknownChartNeverGuessesKiwamiAndLocalCoursesCacheSeparately()
        {
            Assert.That(ScoreRankUtil.FromScore(1000000), Is.EqualTo(ScoreRank.None));
            Assert.That(ScoreRankUtil.FromScore(950000), Is.EqualTo(ScoreRank.PurpleMiyabi));
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            var first = new TextAsset("BPM:120\nCOURSE:Easy\n#START\n1111,\n#END\nCOURSE:Oni\n#START\n1110,\n#END");
            var changed = new TextAsset("BPM:120\nCOURSE:Oni\n#START\n1111,\n#END");
            try
            {
                song.chart = first;
                Assert.That(ScoreRankUtil.FromScore(1000000, song.RankThreshold(Difficulty.Easy)), Is.EqualTo(ScoreRank.Kiwami));
                Assert.That(ScoreRankUtil.FromScore(1000000, song.RankThreshold(Difficulty.Oni)), Is.EqualTo(ScoreRank.PurpleMiyabi));
                Assert.That(song.RankThreshold(Difficulty.Oni), Is.EqualTo(new PlaySession(song.Parse("Oni")).KiwamiThreshold));
                song.chart = changed;
                Assert.That(ScoreRankUtil.FromScore(1000000, song.RankThreshold(Difficulty.Oni)), Is.EqualTo(ScoreRank.Kiwami));
            }
            finally { Object.DestroyImmediate(song); Object.DestroyImmediate(first); Object.DestroyImmediate(changed); }
        }

        [TestCase(-1, 0)] [TestCase(499999, 0)] [TestCase(500000, 1)]
        [TestCase(599999, 1)] [TestCase(600000, 2)] [TestCase(699999, 2)] [TestCase(700000, 3)]
        [TestCase(799999, 3)] [TestCase(800000, 4)] [TestCase(899999, 4)] [TestCase(900000, 5)]
        [TestCase(949999, 5)] [TestCase(950000, 6)] [TestCase(999999, 6)]
        [TestCase(1000000, 7)] [TestCase(int.MaxValue, 7)]
        public void ScoreThresholds(int score, int rank) => Assert.That((int)ScoreRankUtil.FromScore(score, 1000000), Is.EqualTo(rank));

        [TestCase("1110,", "", 333340, 1000020)]
        [TestCase("1111,", "", 250000, 1000000)]
        [TestCase("1000,\n5000,\n0080,\n1000,", "", 497470, 1000940)]
        [TestCase("1234,\n5008,\n7008,\n9008,", "BALLOON:150,2", 246820, 1000480)]
        [TestCase("0000,", "", 1000000, 1000000)]
        public void KiwamiUsesActualChartBudgetAndRoundedRollEstimate(string body, string metadata, int baseScore, int ceiling)
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\nLEVEL:8\n" + metadata + "\n#START\n" + body + "\n#END");
            var session = new PlaySession(chart);
            Assert.That(session.BaseScore, Is.EqualTo(baseScore), "The scoring policy is unchanged.");
            Assert.That(session.KiwamiThreshold, Is.EqualTo(ceiling));
            Assert.That((int)ScoreRankUtil.FromScore(ceiling - 1, ceiling), Is.EqualTo(6));
            Assert.That((int)ScoreRankUtil.FromScore(ceiling, ceiling), Is.EqualTo(7));
            Assert.That(PlaySession.PracticeAt(chart, 2, session).KiwamiThreshold, Is.EqualTo(ceiling),
                "Seeking must retain the whole-chart ceiling, not just the remaining notes.");
        }

        [Test]
        public void ResultCarriesTheSameDynamicLineAsPlay()
        {
            var chart = TjaParser.Parse("BPM:120\nCOURSE:Oni\n#START\n1110,\n#END");
            var session = new PlaySession(chart);
            session.Advance(3, true);
            var result = PlayResult.From(session, "dynamic-rank", false);
            Assert.That(result.KiwamiThreshold, Is.EqualTo(1000020));
            Assert.That((int)result.Rank, Is.EqualTo(7));
            result.Score = 1000000;
            Assert.That((int)result.Rank, Is.EqualTo(6), "One million alone does not earn Kiwami on this chart.");
            foreach (var pair in new[] { (500000, 1), (600000, 2), (700000, 3), (800000, 4), (900000, 5), (950000, 6) })
                Assert.That((int)ScoreRankUtil.FromScore(pair.Item1, result.KiwamiThreshold), Is.EqualTo(pair.Item2));
        }

        [TestCase(false)] [TestCase(true)]
        public void RankPrecedesCrownEvenOnFailedGauge(bool clear)
        {
            var sequence = new ResultSequence(new PlayResult { Score = 950000, IsClear = clear });
            sequence.Update(1300); sequence.Update(2300);
            Assert.That(sequence.CrownAtMs - sequence.RankAtMs, Is.EqualTo(ResultSequence.RankDurationMs).Within(.001));
            var cues = sequence.Update(sequence.RankAtMs.Value).Select(c => c.Cue).ToArray();
            Assert.That(cues, Has.Member(ResultCue.ScoreRank));
            Assert.That(cues, Has.No.Member(ResultCue.Crown));
            Assert.That(sequence.Update(sequence.RankAtMs.Value + 1).Select(c => c.Cue), Has.No.Member(ResultCue.ScoreRank));
            cues = sequence.Update(sequence.CrownAtMs.Value).Select(c => c.Cue).ToArray();
            Assert.That(cues.Contains(ResultCue.Crown), Is.EqualTo(clear));
        }

        [TestCase(false)] [TestCase(true)]
        public void SkipSettlesRankWithoutReplayingItsSound(bool duringRank)
        {
            var sequence = new ResultSequence(new PlayResult { Score = 950000, IsClear = true });
            sequence.Update(1300); sequence.Update(2300);
            if (duringRank) sequence.Update(sequence.RankAtMs.Value + 200);
            Assert.That(sequence.Skip(), Is.True);
            var cues = sequence.Update(sequence.Now + 1).Select(c => c.Cue).ToArray();
            Assert.That(cues, Has.No.Member(ResultCue.ScoreRank));
            Assert.That(sequence.RankAtMs, Is.LessThanOrEqualTo(sequence.Now));
            Assert.That(sequence.RevealEndMs, Is.GreaterThan(0));
        }

    }
}

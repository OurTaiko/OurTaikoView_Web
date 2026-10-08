using System;

namespace OurTaiko
{
    // Play events a branch condition counts, raised by PlaySession as each result is applied
    // within the game loop's update. #SECTION and every branch decision raise Reset.
    public sealed class BranchFeed
    {
        // A normal note's result and the points it scored.
        public event Action<Judgment, int> NoteJudged;
        // One drumroll or balloon hit and the points it scored.
        public event Action<ChartNote, int> LongHit;
        public event Action Reset;

        internal void Judge(Judgment result, int points) => NoteJudged?.Invoke(result, points);
        internal void Hit(ChartNote note, int points) => LongHit?.Invoke(note, points);
        internal void Clear() => Reset?.Invoke();
    }

    // A #BRANCHSTART condition: counts play since the last reset and reports the value
    // compared against the expert and master thresholds when the branch is decided.
    public interface IBranchCondition
    {
        double Value(ChartBranch branch);
    }

    public static class BranchConditions
    {
        // One condition per BranchCondition, all fed together so a reset clears every count.
        // heldRollHits gives the hits on an active drumroll that spans the branch's decision.
        public static IBranchCondition[] Create(BranchFeed feed, Func<ChartBranch, int> heldRollHits)
        {
            var conditions = new IBranchCondition[3];
            conditions[(int)BranchCondition.Accuracy] = new AccuracyCondition(feed);
            conditions[(int)BranchCondition.Drumroll] = new DrumrollCondition(feed, heldRollHits);
            conditions[(int)BranchCondition.Score] = new ScoreCondition(feed);
            return conditions;
        }
    }

    // p: truncated percentage of normal notes, 良 = 1, 可 = 0.5, 不可 and misses = 0.
    public sealed class AccuracyCondition : IBranchCondition
    {
        int notes;
        double points;

        public AccuracyCondition(BranchFeed feed)
        {
            feed.NoteJudged += (result, _) =>
            {
                notes++;
                points += result == Judgment.Good ? 1 : result == Judgment.Ok ? 0.5 : 0;
            };
            feed.Reset += () => { notes = 0; points = 0; };
        }

        public double Value(ChartBranch branch) => notes == 0 ? 0 : Math.Max(0, Math.Min(100, (int)(points / notes * 100)));
    }

    // r: drumroll (5/6) hits; balloons and kusudama do not count. As in the reference player,
    // a drumroll still running at the decision counts all its hits if that is more.
    public sealed class DrumrollCondition : IBranchCondition
    {
        readonly Func<ChartBranch, int> heldRollHits;
        int hits;

        public DrumrollCondition(BranchFeed feed, Func<ChartBranch, int> heldRollHits)
        {
            this.heldRollHits = heldRollHits;
            feed.LongHit += (note, _) => { if (!note.IsBalloon) hits++; };
            feed.Reset += () => hits = 0;
        }

        public double Value(ChartBranch branch) => Math.Max(hits, heldRollHits(branch));
    }

    // s: points scored, from notes, drumrolls and balloons alike.
    public sealed class ScoreCondition : IBranchCondition
    {
        int branchScores;

        public ScoreCondition(BranchFeed feed)
        {
            feed.NoteJudged += (_, points) => branchScores += points;
            feed.LongHit += (_, points) => branchScores += points;
            feed.Reset += () => branchScores = 0;
        }

        public double Value(ChartBranch branch) => branchScores;
    }
}

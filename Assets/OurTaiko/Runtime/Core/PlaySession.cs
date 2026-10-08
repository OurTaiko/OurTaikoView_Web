using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // Pure C#: independent of frame rate, rendering and audio, and testable without a scene.
    public sealed class PlaySession
    {
        public const double GoodWindow = 0.0250250015258789, OkWindow = 0.0750750045776367, BadWindow = 0.108441665649414;
        public readonly TaikoChart Chart;
        public double JudgeOffset { get; }
        public BranchRoute? ForcedBranch { get; }
        // Results are written only here, so every change also moves Version.
        public IReadOnlyList<bool> Resolved => resolved;
        // Normal notes judged 不可 because the window passed without a hit.
        public IReadOnlyList<bool> Missed => missed;
        public IReadOnlyList<int> LongHits => longHits;
        readonly bool[] resolved, missed;
        readonly int[] longHits;
        public int Score => scoring.Total;
        public int BaseScore => scoring.BaseScore;
        public int Combo { get; private set; }
        public int MaxCombo { get; private set; }
        public int Good { get; private set; }
        public int Ok { get; private set; }
        public int Bad { get; private set; }
        public int Rolls { get; private set; }
        public double Gauge => gauge.Value;
        public double GaugePoints => gauge.Points;
        public int GaugePercent => gauge.Percent;
        public double ClearThreshold => gauge.ClearThreshold;
        public bool IsClear => gauge.IsClear;
        public bool IsGaugeFull => gauge.IsFull;
        public BranchRoute CurrentBranch { get; private set; } = BranchRoute.Normal;
        public IReadOnlyList<BranchRoute> BranchHistory => branchHistory;
        public double LastBranchValue { get; private set; }
        // Changes whenever a note's result or a branch choice changes, so a frame can skip an unchanged redraw.
        public int Version { get; private set; }
        public event Action<int, Judgment> Judged;
        public event Action<ChartBranch, BranchRoute> BranchSelected;
        readonly ShinuchiScore scoring;
        readonly SoulGauge gauge;
        readonly double goodWindow, okWindow, badWindow;
        readonly int[] selectedRoutes;
        readonly List<BranchRoute> branchHistory = new List<BranchRoute>();
        readonly List<TimelineEvent> timeline = new List<TimelineEvent>();
        int nextEvent;
        // Notes are sorted by time (TjaParser), so only two groups can still owe a result: the
        // unresolved notes already reached (pending, ascending index) and the notes from head on.
        // Every note before head and not pending is resolved or on a decided unselected route.
        readonly bool timeSorted;
        int head;
        List<int> pending = new List<int>(), revisit = new List<int>();
        readonly BranchFeed branchFeed = new BranchFeed();
        readonly IBranchCondition[] conditions;
        double practiceStart = double.NegativeInfinity;

        sealed class TimelineEvent
        {
            public double Time;
            public ChartBranch Branch;
            public ChartSection Section;
            public int Priority => Branch != null ? 1 : Section.BranchId < 0 ? 0 : 2;
        }

        // A fixed route (practice) plays every branch on ResolveRoute(route) and evaluates no
        // condition. Without one, branches are judged by their p, r or s condition; the reference
        // player does not define omitted routes, so such charts are refused, not guessed.
        public PlaySession(TaikoChart chart, double judgeOffset = 0, BranchRoute? forcedBranch = null)
        {
            if (!forcedBranch.HasValue)
                foreach (var branch in chart.Branches)
                    if (!branch.HasAllRoutes)
                        throw new NotSupportedException("A branch without #E or #M can only be played on a fixed route, as in practice.");
            ForcedBranch = forcedBranch;
            JudgeOffset = judgeOffset;
            Chart = chart; resolved = new bool[chart.Notes.Count]; missed = new bool[chart.Notes.Count]; longHits = new int[chart.Notes.Count];
            var statistics = new ChartStatistics(chart);
            scoring = new ShinuchiScore(statistics);
            gauge = new SoulGauge(statistics.JudgeableNotes, chart.Course, chart.Level);
            conditions = BranchConditions.Create(branchFeed, HeldRollHits);
            bool easy = chart.Course == "Easy" || chart.Course == "Normal";
            goodWindow = easy ? 0.0417083358764648 : GoodWindow;
            okWindow = easy ? 0.108441665649414 : OkWindow;
            badWindow = easy ? 0.125125 : BadWindow;
            selectedRoutes = Enumerable.Repeat(-1, chart.Branches.Count).ToArray();
            timeSorted = true;
            for (int i = 0; i < chart.Notes.Count; i++)
                if (double.IsNaN(chart.Notes[i].Time) || i > 0 && chart.Notes[i].Time < chart.Notes[i - 1].Time) timeSorted = false;
            foreach (var branch in chart.Branches) timeline.Add(new TimelineEvent { Time = branch.DecisionTime, Branch = branch });
            foreach (var section in chart.Sections) timeline.Add(new TimelineEvent { Time = section.Time, Section = section });
            // Stable order preserves authored checkpoint order when several become ready together.
            timeline = timeline.OrderBy(e => e.Time).ThenBy(e => e.Priority).ToList();
        }

        public bool IsActive(ChartNote note) => IsActive(note.BranchId, note.Route);

        // Start a fresh practice attempt without replaying judgments, sounds or missed-note penalties.
        // Preserve already chosen branches before the cursor, and recalculate future checkpoints.
        public static PlaySession PracticeAt(TaikoChart chart, double time, PlaySession previous = null, BranchRoute? forcedBranch = null)
        {
            var session = new PlaySession(chart, previous?.JudgeOffset ?? 0, forcedBranch) { practiceStart = time };
            while (session.nextEvent < session.timeline.Count && session.timeline[session.nextEvent].Time <= time)
            {
                var item = session.timeline[session.nextEvent++];
                if (item.Branch == null) continue;
                var route = forcedBranch.HasValue ? item.Branch.ResolveRoute(forcedBranch.Value)
                    : previous?.SelectedRoute(item.Branch.Id) ?? BranchRoute.Normal;
                session.selectedRoutes[item.Branch.Id] = (int)route;
                session.CurrentBranch = route;
                session.branchHistory.Add(route);
            }
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                bool past = (note.IsLong ? note.EndTime : note.Time) < time - session.JudgeOffset - 1e-7;
                session.resolved[i] = past;
                // Past notes still scroll out naturally when browsing backwards/forwards.
                session.missed[i] = past && !note.IsLong;
            }
            return session;
        }

        public bool IsPracticePreviewActive(ChartNote note) => IsPracticePreviewActive(note.BranchId, note.Route);
        bool IsPracticePreviewActive(int branchId, BranchRoute route) => branchId < 0
            || route == (ForcedBranch.HasValue ? Chart.Branches[branchId].ResolveRoute(ForcedBranch.Value)
                : SelectedRoute(branchId) ?? BranchRoute.Normal);

        // Like handle_gogotime: the latest #GOGOSTART / #GOGOEND reached on the played route,
        // independent of notes, so GOGO starts and ends exactly at its commands.
        public bool IsGogo(double time, bool preview = false)
        {
            bool on = false;
            foreach (var gogo in Chart.Gogos)
            {
                if (gogo.Time > time) break;
                if (preview ? IsPracticePreviewActive(gogo.BranchId, gogo.Route) : IsActive(gogo.BranchId, gogo.Route)) on = gogo.On;
            }
            return on;
        }
        bool IsActive(int branchId, BranchRoute route) => branchId < 0 || selectedRoutes[branchId] == (int)route;
        public BranchRoute? SelectedRoute(int branchId) => selectedRoutes[branchId] < 0 ? (BranchRoute?)null : (BranchRoute)selectedRoutes[branchId];

        public void Advance(double time, bool auto)
        {
            // Split a long frame at each reset/decision. Future hits must not affect an
            // earlier checkpoint, and inactive routes must never enter its statistics.
            while (nextEvent < timeline.Count && timeline[nextEvent].Time <= time)
            {
                var item = timeline[nextEvent++];
                AdvanceNotes(item.Time - 1e-9, auto);
                if (item.Branch != null) SelectBranch(item.Branch);
                else if (IsActive(item.Section.BranchId, item.Section.Route)) branchFeed.Clear();
            }
            AdvanceNotes(time, auto);
        }

        // Hits so far on an active drumroll that spans the decision, for r conditions.
        int HeldRollHits(ChartBranch branch)
        {
            int hits = 0;
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                var note = Chart.Notes[i];
                if (IsActive(note) && note.IsLong && !note.IsBalloon && note.Time <= branch.DecisionTime && branch.DecisionTime < note.EndTime)
                    hits = Math.Max(hits, longHits[i]);
            }
            return hits;
        }

        void SelectBranch(ChartBranch branch)
        {
            double value = ForcedBranch.HasValue ? 0 : conditions[(int)branch.Condition].Value(branch);
            var chosen = value >= branch.ExpertThreshold && value < branch.MasterThreshold && branch.ExpertThreshold >= 0
                ? BranchRoute.Expert : value >= branch.MasterThreshold ? BranchRoute.Master : BranchRoute.Normal;
            chosen = branch.ResolveRoute(ForcedBranch ?? chosen);
            selectedRoutes[branch.Id] = (int)chosen;
            CurrentBranch = chosen; LastBranchValue = value; branchHistory.Add(chosen); Version++;
            branchFeed.Clear();
            BranchSelected?.Invoke(branch, chosen);
        }

        void AdvanceNotes(double time, bool auto)
        {
            // B changes manual judgment and timeout together; chart events and autoplay stay on A.
            if (!auto) time -= JudgeOffset;
            if (!timeSorted)
            {
                for (int i = 0; i < Chart.Notes.Count; i++) AdvanceNote(i, time, auto);
                return;
            }
            // Same index order as a full scan: every pending note is before head.
            revisit.Clear();
            foreach (int i in pending) if (AdvanceNote(i, time, auto)) revisit.Add(i);
            for (; head < Chart.Notes.Count && Chart.Notes[head].Time <= time; head++)
                if (AdvanceNote(head, time, auto)) revisit.Add(head);
            (pending, revisit) = (revisit, pending);
        }

        // Returns whether the note may still need a later visit: unresolved and active, or on
        // a branch not decided yet (it may become active once the branch is chosen).
        bool AdvanceNote(int i, double time, bool auto)
        {
            var note = Chart.Notes[i];
            if (resolved[i]) return false;
            if (!IsActive(note)) return selectedRoutes[note.BranchId] < 0;
            if (note.Time > time) return true;
            if (note.IsLong)
            {
                if (auto && time >= practiceStart)
                {
                    int expected = (int)(Math.Max(0, Math.Min(time, note.EndTime) - Math.Max(note.Time, practiceStart)) * 15) + 1;
                    while (!resolved[i] && longHits[i] < expected) HitLong(i);
                }
                if (time > note.EndTime && !resolved[i]) { resolved[i] = true; Version++; }
            }
            else if (auto) Resolve(i, Judgment.Good);
            else if (time - note.Time > badWindow) { missed[i] = true; Resolve(i, Judgment.Bad); }
            return !resolved[i];
        }

        public Judgment Hit(bool ka, double time)
        {
            Advance(time, false);
            time -= JudgeOffset;
            // check_note: don and ka are separate lanes, so a press judges the front of
            // its own colour's lane regardless of a pending note of the other colour.
            int target = NextInLane(ka, 0);
            if (target >= 0 && time > Chart.Notes[target].Time + okWindow)
            {
                // A front note already past 可 yields to the next note of its lane once that
                // one is within 可, unless any other pending note lies between them.
                int next = NextInLane(ka, target + 1);
                if (next >= 0 && time > Chart.Notes[next].Time - okWindow && !PendingBetween(target, next)) target = next;
            }
            if (target >= 0)
            {
                double delta = Math.Abs(Chart.Notes[target].Time - time);
                if (delta <= badWindow)
                {
                    Judgment result = delta <= goodWindow ? Judgment.Good : delta <= okWindow ? Judgment.Ok : Judgment.Bad;
                    Resolve(target, result); return result;
                }
            }
            int roll = HittableLong(ka, time);
            if (roll >= 0) { HitLong(roll); return Judgment.Roll; }
            return Judgment.None;
        }

        bool Pending(int i) => !resolved[i] && IsActive(Chart.Notes[i]);
        bool CanHitLong(int i, bool ka, double time)
        {
            var n = Chart.Notes[i];
            return Pending(i) && n.IsLong && time >= Math.Max(n.Time, practiceStart) && time <= n.EndTime && (!n.IsBalloon || !ka);
        }

        // Earliest long note this press can count for. A hittable one has started, so it is
        // pending or among the started notes from head.
        int HittableLong(bool ka, double time)
        {
            int from = 0;
            if (timeSorted)
            {
                foreach (int i in pending) if (CanHitLong(i, ka, time)) return i;
                from = head;
            }
            for (int i = from; i < Chart.Notes.Count && (!timeSorted || Chart.Notes[i].Time <= time); i++)
                if (CanHitLong(i, ka, time)) return i;
            return -1;
        }

        // Earliest pending don (ka = false) or ka note from index start; big notes share the lane.
        int NextInLane(bool ka, int start)
        {
            if (timeSorted)
            {
                foreach (int i in pending) if (i >= start && IsLaneNote(i, ka)) return i;
                start = Math.Max(start, head);
            }
            for (int i = start; i < Chart.Notes.Count; i++)
                if (IsLaneNote(i, ka)) return i;
            return -1;
        }
        bool IsLaneNote(int i, bool ka) => Pending(i) && !Chart.Notes[i].IsLong && Chart.Notes[i].IsKa == ka;

        bool PendingBetween(int first, int last)
        {
            for (int i = first + 1; i < last; i++)
                if (Pending(i)) return true;
            return false;
        }

        void HitLong(int i)
        {
            var n = Chart.Notes[i]; longHits[i]++; Rolls++; Version++;
            branchFeed.Hit(n, scoring.AddLongHit());
            if (n.IsBalloon && longHits[i] == n.BalloonHits) resolved[i] = true;
            Judged?.Invoke(i, Judgment.Roll);
        }

        void Resolve(int i, Judgment result)
        {
            resolved[i] = true; Version++;
            if (result == Judgment.Bad) { Bad++; Combo = 0; }
            else
            {
                if (result == Judgment.Good) Good++; else Ok++;
                Combo++; MaxCombo = Math.Max(MaxCombo, Combo);
            }
            int points = scoring.ApplyJudgment(result);
            gauge.ApplyJudgment(result);
            branchFeed.Judge(result, points);
            Judged?.Invoke(i, result);
        }
    }
}

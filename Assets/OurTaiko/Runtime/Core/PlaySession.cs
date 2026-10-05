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
        public readonly bool[] Resolved;
        // Normal notes judged 不可 because the window passed without a hit.
        public readonly bool[] Missed;
        public readonly int[] LongHits;
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
        public event Action<int, Judgment> Judged;
        public event Action<ChartBranch, BranchRoute> BranchSelected;
        readonly ShinuchiScore scoring;
        readonly SoulGauge gauge;
        readonly double goodWindow, okWindow, badWindow;
        readonly int[] selectedRoutes;
        readonly List<BranchRoute> branchHistory = new List<BranchRoute>();
        readonly List<TimelineEvent> timeline = new List<TimelineEvent>();
        int nextEvent, branchNotes, branchRolls;
        double branchPoints;
        double practiceStart = double.NegativeInfinity;

        sealed class TimelineEvent
        {
            public double Time;
            public ChartBranch Branch;
            public ChartSection Section;
            public int Priority => Branch != null ? 1 : Section.BranchId < 0 ? 0 : 2;
        }

        public PlaySession(TaikoChart chart, double judgeOffset = 0)
        {
            JudgeOffset = judgeOffset;
            Chart = chart; Resolved = new bool[chart.Notes.Count]; Missed = new bool[chart.Notes.Count]; LongHits = new int[chart.Notes.Count];
            var statistics = new ChartStatistics(chart);
            scoring = new ShinuchiScore(statistics);
            gauge = new SoulGauge(statistics.JudgeableNotes, chart.Course, chart.Level);
            bool easy = chart.Course == "Easy" || chart.Course == "Normal";
            goodWindow = easy ? 0.0417083358764648 : GoodWindow;
            okWindow = easy ? 0.108441665649414 : OkWindow;
            badWindow = easy ? 0.125125 : BadWindow;
            selectedRoutes = Enumerable.Repeat(-1, chart.Branches.Count).ToArray();
            foreach (var branch in chart.Branches) timeline.Add(new TimelineEvent { Time = branch.DecisionTime, Branch = branch });
            foreach (var section in chart.Sections) timeline.Add(new TimelineEvent { Time = section.Time, Section = section });
            // Stable order preserves authored checkpoint order when several become ready together.
            timeline = timeline.OrderBy(e => e.Time).ThenBy(e => e.Priority).ToList();
        }

        public bool IsActive(ChartNote note) => IsActive(note.BranchId, note.Route);

        // Start a fresh practice attempt without replaying judgments, sounds or missed-note penalties.
        // Preserve already chosen branches before the cursor, and recalculate future checkpoints.
        public static PlaySession PracticeAt(TaikoChart chart, double time, PlaySession previous = null)
        {
            var session = new PlaySession(chart, previous?.JudgeOffset ?? 0) { practiceStart = time };
            while (session.nextEvent < session.timeline.Count && session.timeline[session.nextEvent].Time <= time)
            {
                var item = session.timeline[session.nextEvent++];
                if (item.Branch == null) continue;
                var route = chart.ForcedBranch.HasValue ? item.Branch.ResolveRoute(chart.ForcedBranch.Value)
                    : previous?.SelectedRoute(item.Branch.Id) ?? BranchRoute.Normal;
                session.selectedRoutes[item.Branch.Id] = (int)route;
                session.CurrentBranch = route;
                session.branchHistory.Add(route);
            }
            for (int i = 0; i < chart.Notes.Count; i++)
            {
                var note = chart.Notes[i];
                bool past = (note.IsLong ? note.EndTime : note.Time) < time - session.JudgeOffset - 1e-7;
                session.Resolved[i] = past;
                // Past notes still scroll out naturally when browsing backwards/forwards.
                session.Missed[i] = past && !note.IsLong;
            }
            return session;
        }

        public bool IsPracticePreviewActive(ChartNote note) => note.BranchId < 0
            || note.Route == (Chart.ForcedBranch.HasValue ? Chart.Branches[note.BranchId].ResolveRoute(Chart.ForcedBranch.Value)
                : SelectedRoute(note.BranchId) ?? BranchRoute.Normal);
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
                else if (IsActive(item.Section.BranchId, item.Section.Route)) ResetBranchStats();
            }
            AdvanceNotes(time, auto);
        }

        void ResetBranchStats() { branchPoints = 0; branchNotes = 0; branchRolls = 0; }

        void SelectBranch(ChartBranch branch)
        {
            double value;
            if (Chart.ForcedBranch.HasValue) value = 0;
            else if (branch.Condition == BranchCondition.Accuracy)
                value = branchNotes == 0 ? 0 : Math.Max(0, Math.Min(100, (int)(branchPoints / branchNotes * 100)));
            else
            {
                int activeRollHits = 0;
                for (int i = 0; i < Chart.Notes.Count; i++)
                {
                    var note = Chart.Notes[i];
                    if (IsActive(note) && note.IsLong && !note.IsBalloon && note.Time <= branch.DecisionTime && branch.DecisionTime < note.EndTime)
                        activeRollHits = Math.Max(activeRollHits, LongHits[i]);
                }
                value = Math.Max(branchRolls, activeRollHits);
            }
            var chosen = value >= branch.ExpertThreshold && value < branch.MasterThreshold && branch.ExpertThreshold >= 0
                ? BranchRoute.Expert : value >= branch.MasterThreshold ? BranchRoute.Master : BranchRoute.Normal;
            chosen = branch.ResolveRoute(Chart.ForcedBranch ?? chosen);
            selectedRoutes[branch.Id] = (int)chosen;
            CurrentBranch = chosen; LastBranchValue = value; branchHistory.Add(chosen);
            ResetBranchStats();
            BranchSelected?.Invoke(branch, chosen);
        }

        void AdvanceNotes(double time, bool auto)
        {
            // B changes manual judgment and timeout together; chart events and autoplay stay on A.
            if (!auto) time -= JudgeOffset;
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                if (Resolved[i] || !IsActive(Chart.Notes[i])) continue;
                var note = Chart.Notes[i];
                if (note.Time > time) continue;
                if (note.IsLong)
                {
                    if (auto && time >= practiceStart)
                    {
                        int expected = (int)(Math.Max(0, Math.Min(time, note.EndTime) - Math.Max(note.Time, practiceStart)) * 15) + 1;
                        while (!Resolved[i] && LongHits[i] < expected) HitLong(i);
                    }
                    if (time > note.EndTime) Resolved[i] = true;
                }
                else if (auto) Resolve(i, Judgment.Good);
                else if (time - note.Time > badWindow) { Missed[i] = true; Resolve(i, Judgment.Bad); }
            }
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
            for (int i = 0; i < Chart.Notes.Count; i++)
            {
                var n = Chart.Notes[i];
                if (!Resolved[i] && IsActive(n) && n.IsLong && time >= Math.Max(n.Time, practiceStart) && time <= n.EndTime && (!n.IsBalloon || !ka))
                { HitLong(i); return Judgment.Roll; }
            }
            return Judgment.None;
        }

        bool Pending(int i) => !Resolved[i] && IsActive(Chart.Notes[i]);

        // Earliest pending don (ka = false) or ka note from index start; big notes share the lane.
        int NextInLane(bool ka, int start)
        {
            for (int i = start; i < Chart.Notes.Count; i++)
                if (Pending(i) && !Chart.Notes[i].IsLong && Chart.Notes[i].IsKa == ka) return i;
            return -1;
        }

        bool PendingBetween(int first, int last)
        {
            for (int i = first + 1; i < last; i++)
                if (Pending(i)) return true;
            return false;
        }

        void HitLong(int i)
        {
            var n = Chart.Notes[i]; LongHits[i]++; Rolls++; scoring.AddLongHit();
            if (!n.IsBalloon) branchRolls++;
            if (n.IsBalloon && LongHits[i] == n.BalloonHits) Resolved[i] = true;
            Judged?.Invoke(i, Judgment.Roll);
        }

        void Resolve(int i, Judgment result)
        {
            Resolved[i] = true;
            branchNotes++;
            branchPoints += result == Judgment.Good ? 1 : result == Judgment.Ok ? 0.5 : 0;
            if (result == Judgment.Bad) { Bad++; Combo = 0; }
            else
            {
                if (result == Judgment.Good) Good++; else Ok++;
                Combo++; MaxCombo = Math.Max(MaxCombo, Combo);
            }
            scoring.ApplyJudgment(result);
            gauge.ApplyJudgment(result);
            Judged?.Invoke(i, result);
        }
    }
}

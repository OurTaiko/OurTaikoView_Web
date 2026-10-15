using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum NoteKind { Don = 1, Ka, BigDon, BigKa, Roll, BigRoll, Balloon = 7, Kusudama = 9 }
    public enum Judgment { None, Good, Ok, Bad, Roll }
    public enum BranchRoute { Normal, Expert, Master }
    // How a note scrolls (OpenTaiko #NMSCROLL / #BMSCROLL / #HBSCROLL): by its own time and BPM, or
    // by the beat clock shared with the notes around it, ignoring (BM) or applying (HB) its #SCROLL.
    public enum ScrollMode { Normal, BMScroll, HBScroll }
    // #BRANCHSTART conditions as OpenTaiko names them: p accuracy, pp 良 percentage, jp/jg/jb
    // 良/可/不可 counts (d is a big-note jp), r drumroll hits, rb balloon hits, s score.
    // None keeps the current route (an empty or unreadable condition).
    public enum BranchCondition { Accuracy, Drumroll, Score, PercentPerfect, JudgePerfect, JudgeGood, JudgeBad, BalloonHits, None }

    public sealed class ChartNote
    {
        public NoteKind Kind;
        public double Time, EndTime, Beat, Bpm, ScrollX = 1, ScrollY, TailBpm;
        public ScrollMode ScrollMode;
        // OpenTaiko's fBMSCROLLTime in measures of four beats: the beat clock at the note (and at a
        // long note's tail). Written measures advance it whatever the BPM; #DELAY at its BPM.
        public double ScrollClock, EndScrollClock;
        // OpenTaiko #SUDDEN, in seconds before the hit time: the note is drawn from SuddenShow
        // and starts to move at SuddenMove, standing where it will start until then. Infinite
        // when not set; bar lines never have them.
        public double SuddenShow = double.PositiveInfinity, SuddenMove = double.PositiveInfinity;
        // TJAP3's quirk: SUDDEN with an appearance but no move time hides rolls and balloons.
        public bool SuddenHidesRoll => !double.IsInfinity(SuddenShow) && double.IsPositiveInfinity(SuddenMove);
        public int BalloonHits;
        // Frame of notes/moji drawn under the note; assigned by NoteMoji.Assign.
        public int Moji;
        public int BranchId = -1;
        public BranchRoute Route;
        public bool Display = true, IsBranchStart;
        public bool IsLong => (int)Kind >= 5;
        public bool IsBalloon => Kind == NoteKind.Balloon || Kind == NoteKind.Kusudama;
        public bool IsKa => Kind == NoteKind.Ka || Kind == NoteKind.BigKa;
        // A 5/6 drumroll: a long note that is not a balloon.
        public bool IsDrumroll => Kind == NoteKind.Roll || Kind == NoteKind.BigRoll;
        // A big don or big ka (3/4).
        public bool IsBigNote => Kind == NoteKind.BigDon || Kind == NoteKind.BigKa;
    }

    // One object of an authored note list in source order. A long note appears
    // twice: its head, then its 8 tail (at EndTime, with the BPM in force there).
    public readonly struct ChartEntry
    {
        public readonly ChartNote Note;
        public readonly bool IsTail;
        public ChartEntry(ChartNote note, bool isTail) { Note = note; IsTail = isTail; }
        public bool IsBar => Note.Kind == 0;
        public double Time => IsTail ? Note.EndTime : Note.Time;
        public double Bpm => IsTail ? Note.TailBpm : Note.Bpm;
    }

    public sealed class ChartBranch
    {
        public int Id;
        // DecisionTime: when OpenTaiko judges the condition, at or before Time (see TjaParser).
        public double Time, EndTime, DecisionTime, ExpertThreshold, MasterThreshold;
        public BranchCondition Condition;
        // An upper-case condition (P, R, JP…) counts only big notes, big drumrolls or kusudama.
        public bool BigOnly;
        // A fourth argument `l`: the master threshold is an upper bound (value < master → 達人).
        public bool Less;
        // Routes with #LEVELHOLD: choosing one keeps it for every later branch (OpenTaiko).
        public readonly bool[] HoldRoutes = new bool[3];
        public readonly ChartNote[] FirstEntries = new ChartNote[3];
        // The route drawn for each requested one. An omitted #E uses #N and an omitted #M uses
        // #E, matching Fanmade's image renderer; a complete branch maps every route to itself.
        public readonly BranchRoute[] Routes = { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master };
        public BranchRoute ResolveRoute(BranchRoute requested) => Routes[(int)requested];
        public bool HasAllRoutes => Routes[1] == BranchRoute.Expert && Routes[2] == BranchRoute.Master;
    }

    // From Time on a route's beat clock runs at Bpm from Clock. Common points (BranchId -1) are on
    // every route: the chart's start, each common #BPMCHANGE, and every branch's start and end.
    public sealed class ChartClockPoint
    {
        // Time is on whole milliseconds (compared as OpenTaiko does); RawMs is OpenTaiko's
        // unrounded bpm_change_time from #START, which the clock runs from.
        public double Time, RawMs, Clock, Bpm;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    public sealed class ChartSection
    {
        public double Time;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    // A #GOGOSTART / #GOGOEND at its own time, whether or not a note falls there.
    public sealed class ChartGogo
    {
        public double Time;
        public bool On;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    // Beat/tempo anchor, including delays between notes or inside an empty measure.
    // Beat stays fixed from Time until ResumeTime; tempo changes resume immediately.
    public sealed class ChartTempo
    {
        public double Time, Bpm, Beat, ResumeTime;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    public sealed class TaikoChart
    {
        public string Title = "Untitled", Subtitle = "", Course = "Oni";
        public int Level;
        public double Bpm = 120, Offset, Duration;
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartNote> Bars = new List<ChartNote>();
        public readonly List<ChartBranch> Branches = new List<ChartBranch>();
        public readonly List<ChartSection> Sections = new List<ChartSection>();
        // The beat clock's BPM points (OpenTaiko listBPM): ScrollClockTimeline reads them per route.
        public readonly List<ChartClockPoint> ScrollClocks = new List<ChartClockPoint>();
        // #JPOSSCROLL moves of the judge, in time order and linked (JudgeShift).
        public readonly List<ChartJudgeMove> JudgeMoves = new List<ChartJudgeMove>();
        // Times of #LEVELHOLD outside a route: from there the route being played is kept.
        public readonly List<double> LevelHolds = new List<double>();
        // Sorted by time; events at the same time keep their source order.
        public readonly List<ChartGogo> Gogos = new List<ChartGogo>();
        public readonly List<ChartTempo> Tempos = new List<ChartTempo>();
        // OurTaikoPlayer's NoteLists: the common part, then one per route of each branch,
        // each with its bar lines and long-note tails in place.
        public readonly List<List<ChartEntry>> NoteLists = new List<List<ChartEntry>>();
        public readonly List<string> Warnings = new List<string>();
    }
}

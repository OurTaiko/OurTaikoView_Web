using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum NoteKind { Don = 1, Ka, BigDon, BigKa, Roll, BigRoll, Balloon = 7, Kusudama = 9 }
    public enum Judgment { None, Good, Ok, Bad, Roll }
    public enum BranchRoute { Normal, Expert, Master }
    public enum BranchCondition { Accuracy, Drumroll, Score }

    public sealed class ChartNote
    {
        public NoteKind Kind;
        public double Time, EndTime, Bpm, ScrollX = 1, ScrollY, TailBpm;
        public int BalloonHits;
        // Frame of notes/moji drawn under the note; assigned by NoteMoji.Assign.
        public int Moji;
        public int BranchId = -1;
        public BranchRoute Route;
        public bool Display = true, IsBranchStart;
        public bool IsLong => (int)Kind >= 5;
        public bool IsBalloon => Kind == NoteKind.Balloon || Kind == NoteKind.Kusudama;
        public bool IsKa => Kind == NoteKind.Ka || Kind == NoteKind.BigKa;
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
        public double Time, EndTime, ArmTime, DecisionTime, ExpertThreshold, MasterThreshold;
        public BranchCondition Condition;
        public readonly ChartNote[] FirstEntries = new ChartNote[3];
        // The route drawn for each requested one. An omitted #E uses #N and an omitted #M uses
        // #E, matching Fanmade's image renderer; a complete branch maps every route to itself.
        public readonly BranchRoute[] Routes = { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master };
        public BranchRoute ResolveRoute(BranchRoute requested) => Routes[(int)requested];
        public bool HasAllRoutes => Routes[1] == BranchRoute.Expert && Routes[2] == BranchRoute.Master;
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

    public sealed class TaikoChart
    {
        public string Title = "Untitled", Subtitle = "", Course = "Oni";
        public int Level;
        public double Bpm = 120, Offset, Duration;
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartNote> Bars = new List<ChartNote>();
        public readonly List<ChartBranch> Branches = new List<ChartBranch>();
        public readonly List<ChartSection> Sections = new List<ChartSection>();
        // Sorted by time; events at the same time keep their source order.
        public readonly List<ChartGogo> Gogos = new List<ChartGogo>();
        // OurTaikoPlayer's NoteLists: the common part, then one per route of each branch,
        // each with its bar lines and long-note tails in place.
        public readonly List<List<ChartEntry>> NoteLists = new List<List<ChartEntry>>();
        public readonly List<string> Warnings = new List<string>();
    }
}

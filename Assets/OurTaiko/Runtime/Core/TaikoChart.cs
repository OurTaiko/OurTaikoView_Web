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
        public bool Gogo;
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
        public readonly BranchRoute[] Routes = { BranchRoute.Normal, BranchRoute.Expert, BranchRoute.Master };
        public BranchRoute ResolveRoute(BranchRoute requested) => Routes[(int)requested];
    }

    public sealed class ChartSection
    {
        public double Time;
        public int BranchId = -1;
        public BranchRoute Route;
    }

    public sealed class TaikoChart
    {
        public BranchRoute? ForcedBranch;
        public string Title = "Untitled", Subtitle = "", Course = "Oni";
        public int Level;
        public double Bpm = 120, Offset, Duration;
        public readonly List<ChartNote> Notes = new List<ChartNote>();
        public readonly List<ChartNote> Bars = new List<ChartNote>();
        public readonly List<ChartBranch> Branches = new List<ChartBranch>();
        public readonly List<ChartSection> Sections = new List<ChartSection>();
        // OurTaikoPlayer's NoteLists: the common part, then one per route of each branch,
        // each with its bar lines and long-note tails in place.
        public readonly List<List<ChartEntry>> NoteLists = new List<List<ChartEntry>>();
        public readonly List<string> Warnings = new List<string>();
    }
}

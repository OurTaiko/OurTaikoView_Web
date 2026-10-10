using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // The text under each note, by the arcade rules (OurTaikoLogs/OurTaikoPlay AGENTS.md, 音符文字).
    // Frames of notes/moji: ドン ド コ カッ カ ドン(大) カッ(大) 連打ー 連打(大)ー ふうせん ーっ!! くすだま.
    public static class NoteMoji
    {
        public const int Don = 0, DonShort = 1, Ko = 2, Ka = 3, KaShort = 4, BigDon = 5, BigKa = 6,
            Roll = 7, BigRoll = 8, Balloon = 9, Tail = 10, Kusudama = 11;

        // Gaps in quarter-note beats.
        const double Eighth = 0.5, TwentyFourth = 1.0 / 6, Tolerance = 1e-3;
        // コ needs this many small dons: exactly 3, an odd run from 11, or any run from 10 before a long note.
        const int Triple = 3, LongOddRun = 11, RunBeforeLong = 10;

        public static void Assign(TaikoChart chart)
        {
            foreach (var list in chart.NoteLists) Assign(list);
        }

        // Each list is processed alone, so streams never join the common part and a branch route.
        public static void Assign(List<ChartEntry> entries)
        {
            var notes = new List<ChartNote>();
            foreach (var entry in entries)
                if (!entry.IsBar && !entry.IsTail) notes.Add(entry.Note);

            for (int start = 0, end; start < notes.Count; start = end + 1)
            {
                // A stream is a run of gaps shorter than an eighth. Notes outside such runs
                // stand alone, except that consecutive ones exactly an eighth apart form a stream.
                end = start;
                while (Joins(notes, end, dense: true)) end++;
                bool dense = end > start;
                if (!dense)
                    while (Joins(notes, end, dense: false)) end++;
                AssignStream(notes, start, end, dense);
            }
        }

        // Whether notes[i + 1] continues the stream of notes[i]. A long note may end a stream but never continues one.
        static bool Joins(List<ChartNote> notes, int i, bool dense)
        {
            if (i + 1 >= notes.Count || notes[i].IsLong) return false;
            double gap = Gap(notes, i);
            if (dense) return gap < Eighth - Tolerance;
            // An eighth stream stops before the first note of a dense one.
            return Math.Abs(gap - Eighth) <= Tolerance && !Joins(notes, i + 1, dense: true);
        }

        static double Gap(List<ChartNote> notes, int i) => notes[i + 1].Beat - notes[i].Beat;

        static void AssignStream(List<ChartNote> notes, int start, int end, bool dense)
        {
            for (int i = start; i <= end; i++)
            {
                var note = notes[i];
                // The last note is ドン／カッ unless it follows within a twenty-fourth.
                bool full = i == end && (i == start || Gap(notes, i - 1) > TwentyFourth + Tolerance);
                note.Moji = note.Kind == NoteKind.Don ? (full ? Don : DonShort)
                    : note.Kind == NoteKind.Ka ? (full ? Ka : KaShort)
                    : Base(note.Kind);
            }

            // コ only in dense streams of small dons, optionally ended by a long note.
            if (!dense) return;
            bool beforeLong = notes[end].IsLong;
            int dons = end - start + (beforeLong ? 0 : 1);
            for (int i = start; i < start + dons; i++)
                if (notes[i].Kind != NoteKind.Don) return;
            bool alternate = beforeLong ? dons >= RunBeforeLong : dons == Triple || (dons >= LongOddRun && dons % 2 == 1);
            if (!alternate) return;
            for (int i = start + 1; i < start + dons; i += 2) notes[i].Moji = Ko;
        }

        static int Base(NoteKind kind) => kind switch
        {
            NoteKind.Ka => Ka,
            NoteKind.BigDon => BigDon,
            NoteKind.BigKa => BigKa,
            NoteKind.Roll => Roll,
            NoteKind.BigRoll => BigRoll,
            NoteKind.Balloon => Balloon,
            NoteKind.Kusudama => Kusudama,
            _ => Don,
        };
    }
}

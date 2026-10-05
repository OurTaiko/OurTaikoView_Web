using System;
using System.Collections.Generic;

namespace OurTaiko
{
    // tja.cpp modifier_moji / find_streams / get_note_interval_type: the text under each note.
    // Frames of notes/moji: ドン ド コ カッ カ ドン(大) カッ(大) 連打ー 連打(大)ー ふうせん ーっ!! くすだま.
    public static class NoteMoji
    {
        public const int Don = 0, DonShort = 1, Ko = 2, Ka = 3, KaShort = 4, BigDon = 5, BigKa = 6,
            Roll = 7, BigRoll = 8, Balloon = 9, Tail = 10, Kusudama = 11;

        // Interval values of the original enum, as subdivisions of a 4/4 measure.
        static readonly int[] StreamIntervals = { 8, 12, 16, 24, 32 };
        static readonly int[] Divisions = { 8, 16, 12, 24, 32, 4 };
        const double Tolerance = 0.015;

        public static void Assign(TaikoChart chart)
        {
            foreach (var list in chart.NoteLists) Assign(list);
        }

        // Each list is processed alone, so streams never join the common part and a branch route.
        public static void Assign(List<ChartEntry> entries)
        {
            foreach (var entry in entries)
                if (!entry.IsBar && !entry.IsTail) entry.Note.Moji = Base(entry.Note.Kind);
            foreach (int interval in StreamIntervals)
            {
                foreach (var (start, length) in FindStreams(entries, interval))
                {
                    for (int i = start; i < start + length - 1; i++)
                    {
                        if (IsKind(entries[i], NoteKind.Don)) entries[i].Note.Moji = DonShort;
                        else if (IsKind(entries[i], NoteKind.Ka)) entries[i].Note.Moji = KaShort;
                    }
                    if (length == 3 && IsKind(entries[start], NoteKind.Don) && IsKind(entries[start + 1], NoteKind.Don)
                        && IsKind(entries[start + 2], NoteKind.Don))
                        entries[start + 1].Note.Moji = Ko;
                }
            }
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

        static bool IsKind(ChartEntry entry, NoteKind kind) => !entry.IsBar && !entry.IsTail && entry.Note.Kind == kind;

        // Long-note heads end a stream; bar lines and tails take part like notes.
        static bool IsLongHead(ChartEntry entry) => !entry.IsBar && !entry.IsTail && entry.Note.IsLong;

        static List<(int start, int length)> FindStreams(List<ChartEntry> entries, int interval)
        {
            var streams = new List<(int, int)>();
            int i = 0;
            while (i < entries.Count - 1)
            {
                if (IsLongHead(entries[i])) { i++; continue; }
                int start = i, length = 1;
                while (i < entries.Count - 1)
                {
                    if (IsLongHead(entries[i + 1])) break;
                    if (Interval(entries[i + 1].Time - entries[i].Time, entries[i].Bpm) != interval) break;
                    length++; i++;
                }
                if (length >= 2) streams.Add((start, length));
                i++;
            }
            return streams;
        }

        // Classified in the original order; 4 is a quarter note and 0 is unknown.
        public static int Interval(double seconds, double bpm)
        {
            if (bpm == 0) return 0;
            double measure = 240.0 / bpm;
            foreach (int division in Divisions)
                if (Math.Abs(seconds - measure / division) < Tolerance) return division;
            return 0;
        }
    }
}

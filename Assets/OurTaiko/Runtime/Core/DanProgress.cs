using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // What one song of a course has counted: the song's own counts, not the course's so far. The one
    // exception is MaxCombo, the highest running course combo during the song: as in YataiDON the
    // combo carries on from the songs before.
    public struct DanSongStats
    {
        public int Good, Ok, Bad, Rolls, Score, MaxCombo;
        // Kept apart from Good/Ok/Bad as in PlaySession; a mine hit spoils a clean run.
        public int AdLibGood, AdLibOk, Mines;

        public static DanSongStats operator +(DanSongStats a, DanSongStats b) => new DanSongStats
        {
            Good = a.Good + b.Good, Ok = a.Ok + b.Ok, Bad = a.Bad + b.Bad, Rolls = a.Rolls + b.Rolls,
            AdLibGood = a.AdLibGood + b.AdLibGood, AdLibOk = a.AdLibOk + b.AdLibOk, Mines = a.Mines + b.Mines,
            Score = a.Score + b.Score, MaxCombo = Math.Max(a.MaxCombo, b.MaxCombo),
        };
    }

    // How an exam's bar reads now (YataiDON DanExamInfo): the counted value or, for less, what is
    // left; the bar's fill and art state; for per-song exams, each reached song's own.
    public sealed class DanExamView
    {
        // HasBorder is false while the current song of a per-song exam has none (null).
        public bool HasBorder;
        public int Red, Gold, Counter;
        public float Progress;
        public string BarState = "empty";
        public bool Failed;
        public readonly int[] SongCounter = new int[DanCourse.MaxSongs];
        public readonly float[] SongProgress = new float[DanCourse.MaxSongs];
        public readonly string[] SongState = { "empty", "empty", "empty" };
    }

    // The exams of a course as its songs are played (YataiDON DanGameScreen): less exams fail the
    // moment their allowance is used up, more exams when their song (or the course) ends. A failed
    // exam ends the course once the song playing is over.
    public sealed class DanProgress
    {
        public readonly DanCourse Course;
        readonly int[] songNotes;
        readonly List<DanSongStats> finished = new List<DanSongStats>();
        readonly bool[] failed;
        DanSongStats current;
        int gaugePercent;

        public int SongIndex => finished.Count;
        public bool IsLastSong => SongIndex >= Course.Songs.Count - 1;
        public IReadOnlyList<DanSongStats> Finished => finished;
        public DanSongStats Current => current;
        public DanSongStats Total => finished.Aggregate(current, (sum, s) => sum + s);
        public int TotalNotes { get; }
        public int RemainingNotes { get { var t = Total; return Math.Max(0, TotalNotes - t.Good - t.Ok - t.Bad); } }
        public bool AnyFailed => failed.Any(f => f);
        // The course is over: its last song ended, or a song ended with an exam failed.
        public bool IsEnded { get; private set; }
        public bool IsFailed(int exam) => failed[exam];

        // songNotes: the judgeable notes of each song, for the gold of 良/hits/combo exams and the
        // near-the-end bar states.
        public DanProgress(DanCourse course, IReadOnlyList<int> songNotes)
        {
            Course = course ?? throw new ArgumentNullException(nameof(course));
            if (songNotes.Count != course.Songs.Count) throw new ArgumentException("One note count per song.", nameof(songNotes));
            this.songNotes = songNotes.ToArray();
            TotalNotes = Math.Max(1, this.songNotes.Sum());
            failed = new bool[course.Exams.Count];
        }

        // Every frame: the current song's counts so far and the course gauge. Returns the exams that
        // failed just now (a less exam's allowance used up).
        public IReadOnlyList<int> Update(DanSongStats song, int gauge)
        {
            current = song; gaugePercent = gauge;
            return Check(songEnded: false, courseEnded: false);
        }

        // The current song is over: more exams are judged (per-song ones now, course-wide ones at
        // the last song). Returns the exams that failed just now. Call Next to move on.
        public IReadOnlyList<int> EndSong()
        {
            if (IsEnded) throw new InvalidOperationException("The course is over.");
            var now = Check(songEnded: true, courseEnded: IsLastSong);
            IsEnded = IsLastSong || AnyFailed;
            return now;
        }

        // The course goes on with the next song (only after EndSong, when nothing failed and songs remain).
        public void Next()
        {
            if (IsEnded) throw new InvalidOperationException("The course is over.");
            finished.Add(current);
            current = default;
        }

        List<int> Check(bool songEnded, bool courseEnded)
        {
            var now = new List<int>();
            for (int i = 0; i < Course.Exams.Count; i++)
            {
                if (failed[i]) continue;
                var exam = Course.Exams[i];
                int song = exam.PerSong ? SongIndex : -1;
                if (!(exam.BorderFor(song) is DanBorder border)) continue;
                border = exam.Resolve(border, Notes(song));
                int value = Value(exam, song);
                bool atBoundary = exam.PerSong ? songEnded : courseEnded;
                bool fail = exam.Range == DanExamRange.Less ? value >= border.Red : atBoundary && value < border.Red;
                if (fail) { failed[i] = true; now.Add(i); }
            }
            return now;
        }

        int Notes(int song) => song < 0 ? TotalNotes : songNotes[song];

        DanSongStats StatsOf(int song) => song < finished.Count ? finished[song] : song == SongIndex ? current : default;

        // The value an exam counts over the course (song -1) or over one song. The gauge is the
        // course's either way.
        public int Value(DanExam exam, int song)
        {
            if (exam.Type == DanExamType.Gauge) return gaugePercent;
            var s = song < 0 ? Total : StatsOf(song);
            switch (exam.Type)
            {
                case DanExamType.Good: return s.Good;
                case DanExamType.Ok: return s.Ok;
                case DanExamType.Bad: return s.Bad;
                case DanExamType.Hits: return s.Good + s.Ok + s.Rolls;
                case DanExamType.Score: return s.Score;
                case DanExamType.Combo: return s.MaxCombo;
                case DanExamType.Rolls: return s.Rolls;
                default: throw new ArgumentOutOfRangeException(nameof(exam));
            }
        }

        // YataiDON calculate_dan_info for the HUD (live) and the result (not live).
        public DanExamView View(int index, bool live = true)
        {
            var exam = Course.Exams[index];
            var view = new DanExamView { Failed = failed[index] };
            bool nearEnd = TotalNotes * 0.10 > RemainingNotes, justBeforeEnd = TotalNotes * 0.05 > RemainingNotes;
            bool Fill(int song, out int counter, out float progress, out string state, out int red, out int gold)
            {
                counter = 0; progress = 0; state = "empty"; red = 0; gold = 0;
                if (!(exam.BorderFor(song) is DanBorder b)) return false;
                b = exam.Resolve(b, Notes(song));
                int value = Value(exam, song);
                red = b.Red; gold = b.Gold;
                float p = b.Red > 0 ? (float)value / b.Red : 0;
                if (exam.Range == DanExamRange.Less) { p = 1 - p; counter = Math.Max(0, b.Red - value); }
                else counter = Math.Max(0, value);
                progress = Math.Max(0, Math.Min(1, p));
                state = exam.BarState(b, value, live && (song < 0 || song == SongIndex), nearEnd, justBeforeEnd);
                return true;
            }
            if (!exam.PerSong)
            {
                view.HasBorder = Fill(-1, out view.Counter, out view.Progress, out view.BarState, out view.Red, out view.Gold);
                return view;
            }
            int last = Math.Min(SongIndex, Course.Songs.Count - 1);
            for (int j = 0; j <= last; j++)
                Fill(j, out view.SongCounter[j], out view.SongProgress[j], out view.SongState[j], out _, out _);
            view.HasBorder = Fill(last, out view.Counter, out view.Progress, out view.BarState, out view.Red, out view.Gold);
            return view;
        }

        // 0 failed, 1 red, 2 gold for one exam at the end of the course. A per-song exam takes its
        // worst song; songs without a border do not count.
        public int Tier(int index, bool reachedEverySong)
        {
            if (!reachedEverySong) return 0;
            var exam = Course.Exams[index];
            if (!exam.PerSong) return exam.Tier(exam.Resolve(exam.Course, TotalNotes), Value(exam, -1));
            int tier = 2;
            for (int j = 0; j < Course.Songs.Count; j++)
                if (exam.BorderFor(j) is DanBorder b) tier = Math.Min(tier, exam.Tier(exam.Resolve(b, songNotes[j]), Value(exam, j)));
            return tier;
        }

        // YataiDON odai_result: 0 failed; 1/2/3 red, 4/5/6 gold, each with bads, no bads, no oks and
        // no bads. A course with no exams cannot pass; a skipped one records nothing. Only once the
        // course is over (EndSong), except when skipped.
        public int Verdict(bool skipped = false)
        {
            if (skipped || Course.Exams.Count == 0) return 0;
            if (!IsEnded) throw new InvalidOperationException("The course is not over.");
            bool reached = SongIndex == Course.Songs.Count - 1;
            int check = Enumerable.Range(0, Course.Exams.Count).Min(i => Tier(i, reached));
            var total = Total;
            if (check == 0) return 0;
            int clean = total.Bad > 0 || total.Mines > 0 ? 0 : total.Ok > 0 ? 1 : 2;
            return (check == 2 ? 4 : 1) + clean;
        }
    }
}

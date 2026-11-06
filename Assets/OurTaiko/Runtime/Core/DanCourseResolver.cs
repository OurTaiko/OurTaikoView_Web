using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    public sealed class DanResolvedSong
    {
        public DanSongRef Ref;
        public SongDefinition Song;
        // The block the course plays: its TJA COURSE value as written and its side (ChartSide), for
        // SongDefinition.ParseBlock, and its level. A course pins a server song's side; otherwise it
        // is the side the 1P seat reads, found once here so the run never re-chooses by seat.
        public string Course;
        public string Side = "single";
        public int Level;
        // The played block's content hash on a server's song (its identity for records); empty locally.
        public string ChartHash = "";
        // The course by its canonical name (Easy..Edit), which the soul gauge tables know.
        public string GaugeCourse => SongInfo.CourseName(Ref.Difficulty);
    }

    // A course with its songs found, or the reason it cannot be played (shown greyed out).
    public sealed class DanCourseEntry
    {
        public DanCourse Course;
        public string Path;
        public readonly List<DanResolvedSong> Songs = new List<DanResolvedSong>();
        public string Error;
        // The 外伝 entry at the end of an online page: not a course, but where a code is typed.
        public bool IsGaiden;
        public bool IsPlayable => Error == null;
    }

    // Finds a course's songs: local courses among the local songs (file SHA-256, else title and
    // subtitle), a server's courses among that server's songs by id. Each song is played as the 1P
    // seat reads it.
    public sealed class DanCourseResolver
    {
        readonly IReadOnlyList<SongDefinition> localSongs;
        readonly Func<string, string, SongDefinition> serverSong;
        readonly Dictionary<SongDefinition, SongInfo> infos = new Dictionary<SongDefinition, SongInfo>();
        Dictionary<string, SongDefinition> byHash;

        // serverSong(server base URL, song id) finds a server's song, null when there is none.
        public DanCourseResolver(IReadOnlyList<SongDefinition> localSongs, Func<string, string, SongDefinition> serverSong)
        {
            this.localSongs = localSongs ?? Array.Empty<SongDefinition>();
            this.serverSong = serverSong;
        }

        public DanCourseEntry Resolve(DanCourse course, string path = null)
        {
            var entry = new DanCourseEntry { Course = course, Path = path };
            foreach (var reference in course.Songs)
            {
                string label = reference.Title.Length > 0 ? reference.Title : reference.Sha256.Length > 0 ? reference.Sha256 : reference.Id;
                var song = course.IsLocal ? FindLocal(reference) : serverSong?.Invoke(course.Server, reference.Id);
                string tjaCourse = null, side = "single", chartHash = ""; int level = 0;
                string error = song == null ? "Song not found: " + label : Block(song, reference, out tjaCourse, out side, out level, out chartHash);
                if (error != null) { entry.Error = error.EndsWith(": ") ? error + label : error; entry.Songs.Clear(); return entry; }
                entry.Songs.Add(new DanResolvedSong { Ref = reference, Song = song, Course = tjaCourse, Side = side, Level = level, ChartHash = chartHash });
            }
            return entry;
        }

        string Block(SongDefinition song, DanSongRef reference, out string tjaCourse, out string side, out int level, out string chartHash)
        {
            tjaCourse = null; side = "single"; level = 0; chartHash = "";
            string missing = "No " + SongInfo.CourseName(reference.Difficulty) + " chart: ";
            var online = song.onlineChart;
            if (online != null)
            {
                // The server says whether the block the course named is still the one it was set up on.
                if (!reference.Available) return "Song has changed: ";
                var block = reference.Side.Length > 0
                    ? online.Blocks.Find(b => b.Course == SongInfo.CourseName(reference.Difficulty) && b.Side == reference.Side)
                    : online.Difficulties[(int)reference.Difficulty];
                if (block == null) return missing;
                tjaCourse = block.Course; side = block.Side; level = block.Level; chartHash = block.ChartHash;
                return null;
            }
            var course = Info(song)?.Course(reference.Difficulty);
            if (course == null) return missing;
            tjaCourse = course.Course; side = course.Side; level = course.Level;
            return null;
        }

        SongDefinition FindLocal(DanSongRef reference)
        {
            if (reference.Sha256.Length > 0)
            {
                if (byHash == null)
                {
                    byHash = new Dictionary<string, SongDefinition>(StringComparer.OrdinalIgnoreCase);
                    foreach (var s in localSongs)
                        if (!string.IsNullOrEmpty(s.localChartSha256) && !byHash.ContainsKey(s.localChartSha256)) byHash[s.localChartSha256] = s;
                }
                if (byHash.TryGetValue(reference.Sha256, out var song)) return song;
            }
            if (reference.Title.Length == 0) return null;
            return localSongs.FirstOrDefault(s => Info(s) is SongInfo info && info.Title == reference.Title
                && (reference.Subtitle.Length == 0 || info.Subtitle == reference.Subtitle) && info.Has(reference.Difficulty));
        }

        SongInfo Info(SongDefinition song)
        {
            if (infos.TryGetValue(song, out var info)) return info;
            try { info = song.chart != null ? song.ReadInfo() : null; }
            catch (FormatException) { info = null; }
            return infos[song] = info;
        }
    }
}

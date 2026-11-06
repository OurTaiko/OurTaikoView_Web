using UnityEngine;

namespace OurTaiko
{
    [CreateAssetMenu(menuName = "OurTaiko/Song")]
    public sealed class SongDefinition : ScriptableObject
    {
        public TextAsset chart;
        public AudioClip music;
        [System.NonSerialized] public string audioPath;
        // The background movie (BGMOVIE): in the web player the blob URL of the bytes the parent
        // page transferred. null when the chart has none.
        [System.NonSerialized] public string moviePath;
        [System.NonSerialized] public string localChartSha256;
        // Online charts and authored assets keep their existing identity. Disk charts
        // use their raw file hash; name remains the entry identity for navigation/audio.
        public string ScoreKey => string.IsNullOrEmpty(localChartSha256) ? name : localChartSha256;
        // Browser-decoded Web Audio buffer for the embedded song; released with the song.
        [System.NonSerialized] public int webAudioBuffer;
        NativeAudioSample preparedAudio;
        public bool HasPreparedAudio => preparedAudio != null && !preparedAudio.IsDisposed;
        public void SetPreparedAudio(NativeAudioSample audio) { preparedAudio?.Dispose(); preparedAudio = audio; }
        public NativeAudioSample TakePreparedAudio() { var audio = preparedAudio; preparedAudio = null; return audio?.IsDisposed == true ? null : audio; }
        void OnDisable() { preparedAudio?.Dispose(); preparedAudio = null; }
        public string course = "Oni";
        [Tooltip("Positive values delay judgments relative to the music, in milliseconds.")]
        public float audioOffsetMs;
        public float visualOffsetMs;
        [Tooltip("Song-select board colour: the Nijiiro genre frame (0 default ... 9).")]
        [Range(0, 9)] public int genre;
        [System.NonSerialized] public Online.FanmadeChart onlineChart;
        TextAsset rankChart;
        readonly System.Collections.Generic.Dictionary<string, int> rankThresholds = new System.Collections.Generic.Dictionary<string, int>();

        public int? RankThreshold(Difficulty difficulty)
        {
            if (chart == null) return null;
            if (rankChart != chart) { rankThresholds.Clear(); rankChart = chart; }
            string requested = SongInfo.CourseName(difficulty);
            if (!rankThresholds.TryGetValue(requested, out int threshold))
            {
                threshold = ScoreRankUtil.KiwamiThreshold(Parse(requested));
                rankThresholds[requested] = threshold;
            }
            return threshold;
        }
        // The block a seat plays on a course (ChartSide): its COURSE as written and its side, from
        // the server's block list online and from the file's own blocks offline. A course the
        // song list does not know is asked for as written, on the single side.
        (string course, string side) ResolveBlock(string requested, string seat, bool together)
        {
            var difficulty = SongInfo.DifficultyOf(requested);
            if (!difficulty.HasValue) return (requested, "single");
            if (onlineChart != null)
            {
                var online = onlineChart.Block((int)difficulty.Value, seat, together);
                return online != null ? (online.Course, online.Side) : (requested, SeatBlocks.Reads(seat, together)[0]);
            }
            var block = SongInfo.Read(chart.text).PlayedBlock(difficulty.Value, seat, together);
            return block != null ? (block.Course, block.Side) : (requested, SeatBlocks.Reads(seat, together)[0]);
        }
        public TaikoChart Parse() => Parse(course);
        // seat is the player's position (SeatBlocks.Seats) and together whether two play: alone
        // reads single first, together reads the seat's own side first.
        public TaikoChart Parse(string requestedCourse, string seat = "p1", bool together = false)
        {
            var (blockCourse, side) = ResolveBlock(string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse, seat, together);
            return TjaParser.Parse(chart.text, blockCourse, side);
        }
        // The side a seat plays on a course (SeatBlocks), decided once before the play: the play
        // parses that block and the result uploads to it. A chart that cannot be read gets the
        // seat's first choice; the play scene reports the error.
        public string PlayedSide(string requestedCourse, string seat, bool together)
        {
            if (onlineChart == null && chart == null) return SeatBlocks.Reads(seat, together)[0];
            try { return ResolveBlock(string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse, seat, together).side; }
            catch (System.FormatException) { return SeatBlocks.Reads(seat, together)[0]; }
        }
        // A block named outright (course and side): the side decided before the play, or the one
        // a 段位 course pins. No seat choice is made here; an empty course is the song's own.
        public TaikoChart ParseBlock(string blockCourse, string side)
            => TjaParser.Parse(chart.text, string.IsNullOrEmpty(blockCourse) ? course : blockCourse, side);
        public SongInfo ReadInfo() => SongInfo.Read(chart.text);
        // MOVIEOFFSET: how many seconds after the music starts the movie does (negative: before).
        // Online charts lose their headers when they are made playable; the server keeps it.
        public double MovieOffset() => onlineChart != null ? onlineChart.MovieOffset : chart != null ? ReadInfo().MovieOffset : 0;
        // Display metadata is separate from the parsed chart and score identity. Online songs take it
        // from the server's metadata: their chart is only set once SongLoadingScene downloads it.
        public SongInfo ReadDisplayInfo()
        {
            string language = SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en";
            return onlineChart != null ? onlineChart.ToSongInfo(language) : SongInfo.Read(chart.text, language);
        }
    }
}

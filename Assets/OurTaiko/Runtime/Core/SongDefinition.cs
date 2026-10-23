using UnityEngine;

namespace OurTaiko
{
    [CreateAssetMenu(menuName = "OurTaiko/Song")]
    public sealed class SongDefinition : ScriptableObject
    {
        public TextAsset chart;
        public AudioClip music;
        [System.NonSerialized] public string audioPath;
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
        // The block a seat plays on a course (ChartSide): its COURSE as written and its side. Online
        // the playable copy PlayableTja builds names each block after the API (Oni_1p, Oni_2p) under
        // a plain #START, so it is read on the single side; offline the file's own blocks decide.
        (string course, string side) ResolveBlock(string requested, string seat)
        {
            var difficulty = SongInfo.DifficultyOf(requested);
            if (!difficulty.HasValue) return (requested, "single");
            if (onlineChart != null) return (onlineChart.Difficulties[(int)difficulty.Value]?.Course ?? requested, "single");
            var block = SongInfo.Read(chart.text).Block(difficulty.Value, seat);
            return block != null ? (block.Course, block.Side) : (requested, ChartSide.Reads(seat)[0]);
        }
        public TaikoChart Parse() => Parse(course);
        // seat is the player's position (ChartSide.Seats): 2P reads p2, then single, then p1 blocks.
        public TaikoChart Parse(string requestedCourse, string seat = "p1")
        {
            var (blockCourse, side) = ResolveBlock(string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse, seat);
            return TjaParser.Parse(chart.text, blockCourse, side);
        }
        public SongInfo ReadInfo() => SongInfo.Read(chart.text);
        // Display metadata is separate from the parsed chart and score identity. Online songs take it
        // from the server's metadata: their chart is only set once SongLoadingScene downloads it.
        public SongInfo ReadDisplayInfo()
        {
            string language = SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en";
            return onlineChart != null ? onlineChart.ToSongInfo(language) : SongInfo.Read(chart.text, language);
        }
    }
}

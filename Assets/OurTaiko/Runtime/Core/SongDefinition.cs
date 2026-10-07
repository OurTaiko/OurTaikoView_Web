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
        string ResolveCourse(string requested)
        {
            var difficulty = SongInfo.DifficultyOf(requested);
            return onlineChart != null && difficulty.HasValue ? onlineChart.Difficulties[(int)difficulty.Value]?.Course ?? requested : requested;
        }
        public TaikoChart Parse() => TjaParser.Parse(chart.text, ResolveCourse(course));
        public TaikoChart Parse(string requestedCourse) => TjaParser.Parse(chart.text, ResolveCourse(string.IsNullOrEmpty(requestedCourse) ? course : requestedCourse));
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

using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;
using UnityEngine;
using UnityEngine.Networking;

namespace OurTaiko
{
    // Shown behind the parked rainbow curtain (arcade loading_song frame 55): parses the TJA with
    // the play options applied and loads the song audio into memory, then hands both to
    // SinglePlayScene, whose load opens the curtain. An online song is first downloaded and
    // verified (fanmade.cpp prepare) with its progress on the curtain; Back cancels the download.
    public sealed class SongLoadingScene : MonoBehaviour
    {
        [Tooltip("The title and hints stay up at least this long, however fast the song loads.")]
        [Min(0)] public float minimumSeconds = 2;
        [Tooltip("How long a download error stays on the curtain before returning to the song list.")]
        [Min(0)] public float errorSeconds = 3;

        public bool IsLoaded { get; private set; }
        // Set when an online download failed or was cancelled; the scene then returns instead of playing.
        public string Error { get; private set; }
        CancellationTokenSource downloadCancellation;
        void OnDisable() => downloadCancellation?.Cancel();

        IEnumerator Start()
        {
            var switcher = SceneSwitcher.EnsureInstance();
            var song = switcher.SelectedSong;
            string course = switcher.SelectedCourse;
            if (song == null)
            {
                Debug.LogWarning("No song is selected; returning to Entry.", this);
                switcher.SwitchScene(SceneSwitcher.MenuScene);
                yield break;
            }
            if (!switcher.IsCovered)
            {
                // Entered directly (Play mode on this scene): show the parked curtain at once.
                switcher.ShowSongOnCurtain(song);
                switcher.ParkCurtain();
            }
            double started = GameTimeline.FrameTime;

            var online = OnlineManager.EnsureInstance();
            if (online.IsOnline(song))
            {
                yield return Download(switcher, online, song);
                if (Error != null)
                {
                    switcher.Curtain?.SetStatus(Error, new Color(1, 0.35f, 0.35f));
                    double failedAt = GameTimeline.FrameTime;
                    while (GameTimeline.FrameTime - failedAt < errorSeconds || switcher.IsSwitching) yield return null;
                    switcher.SwitchScene(switcher.ReturnScene);
                    yield break;
                }
            }

            // GameScreen::init_tja, moved behind the curtain: the parse and Player::reset_chart's options.
            try { switcher.SetPreparedChart(song, course, PlayScene.PrepareChart(song, course)); }
            catch (Exception error) { Debug.LogException(error, this); }  // SinglePlayScene parses again and shows it.
            var engine = AudioEngine.EnsureInstance();
            if (engine.Native && song != null && (song.music != null || !string.IsNullOrEmpty(song.audioPath)))
            {
                byte[] encoded = null;
                try { if (string.IsNullOrEmpty(song.audioPath)) encoded = AudioAssetCatalog.Read(song.music); }
                catch (Exception error) { Error = "AUDIO_LOAD_FAILED"; Debug.LogException(error); }
                if (Error == null)
                {
                    string path = song.audioPath;
                    int audioGeneration = engine.Generation;
                    var preparation = Task.Run(() => new NativeAudioSample(encoded ?? System.IO.File.ReadAllBytes(path), engine, true, true, audioGeneration));
                    // A cancelled scene must still release a completed native decode.
                    bool claimed = false;
                    try
                    {
                        while (!preparation.IsCompleted) yield return null;
                        if (preparation.IsCompletedSuccessfully) { song.SetPreparedAudio(preparation.Result); claimed = true; }
                        else { Error = "AUDIO_DECODE_FAILED"; Debug.LogException(preparation.Exception.GetBaseException()); }
                    }
                    finally
                    {
                        if (!claimed) _ = preparation.ContinueWith(t => { if (t.IsCompletedSuccessfully) t.Result.Dispose(); });
                    }
                }
            }
            else
            {
                var clip = song != null ? song.music : null;
                if (clip != null && clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
                while (clip != null && clip.loadState == AudioDataLoadState.Loading) yield return null;
                if (clip != null && clip.loadState == AudioDataLoadState.Failed) Error = "AUDIO_DECODE_FAILED";
            }
            if (Error != null)
            {
                switcher.Curtain?.SetStatus(Error, new Color(1, 0.35f, 0.35f));
                yield return new WaitForSecondsRealtime(errorSeconds);
                while (switcher.IsSwitching) yield return null;
                switcher.SwitchScene(switcher.ReturnScene);
                yield break;
            }
            IsLoaded = true;

            while (GameTimeline.FrameTime - started < minimumSeconds || switcher.IsSwitching) yield return null;
            switcher.Curtain?.SetStatus("");
            switcher.SwitchScene(switcher.SelectedPlayScene);
        }

        IEnumerator Download(SceneSwitcher switcher, OnlineManager online, SongDefinition song)
        {
            var cancel = downloadCancellation = new CancellationTokenSource();
            DownloadProgress latest = null;
            var task = online.Client.PrepareAsync(online.ChartOf(song), cancel.Token, progress => Volatile.Write(ref latest, progress));
            while (!task.IsCompleted)
            {
                // The parked curtain blocks scene input, so Back is read directly.
                if (InputManager.GetKeyDown(InputKey.Back)) cancel.Cancel();
                var progress = Volatile.Read(ref latest);
                if (progress != null) switcher.Curtain?.SetStatus(Describe(progress));
                yield return null;
            }
            downloadCancellation = null;
            cancel.Dispose();
            if (!task.IsCompletedSuccessfully)
            {
                var error = task.Exception?.GetBaseException();
                Debug.LogWarning("Online chart download failed: " + error?.Message);
                Error = error is FanmadeException ? error.Message : "DOWNLOAD_FAILED";
                yield break;
            }
            var (text, audio, chart) = task.Result;
            switcher.Curtain?.SetStatus("音源を読み込み中…");
            if (AudioEngine.EnsureInstance().Native)
            {
                online.SetPrepared(song, chart, text, null);
                song.audioPath = audio;
            }
            else
            {
                string audioUrl = new Uri(audio).AbsoluteUri;
                // Cached objects are extensionless; decode using the chart audio metadata.
                string extension = System.IO.Path.GetExtension(chart.CachedAudioName).ToLowerInvariant();
                var audioType = extension == ".mp3" ? AudioType.MPEG : extension == ".wav" ? AudioType.WAV : AudioType.OGGVORBIS;
#if UNITY_WEBGL && !UNITY_EDITOR
                // Browser fetch cannot open the virtual filesystem through file://.
                string mime = extension == ".mp3" ? "audio/mpeg" : extension == ".wav" ? "audio/wav" : "audio/ogg";
                audioUrl = "data:" + mime + ";base64," + Convert.ToBase64String(System.IO.File.ReadAllBytes(audio));
#endif
                using var request = UnityWebRequestMultimedia.GetAudioClip(audioUrl, audioType);
                ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Error = "AUDIO_DECODE_FAILED";
                    Debug.LogWarning("Could not decode " + audio + ": " + request.error);
                    yield break;
                }
                var clip = DownloadHandlerAudioClip.GetContent(request);
                clip.name = song.name;
                online.SetPrepared(song, chart, text, clip);
                song.audioPath = audio;
            }
            switcher.ShowSongOnCurtain(song);
            switcher.Curtain?.SetStatus("");
        }

        // The download card's two lines (chart / audio) as one status text.
        public static string Describe(DownloadProgress progress)
        {
            if (progress.Step == DownloadProgress.Stage.Checking) return "サーバーを確認中…";
            if (progress.Step == DownloadProgress.Stage.Preparing || progress.Step == DownloadProgress.Stage.Ready) return "譜面を準備中…";
            return "譜面 " + Describe(progress.Chart) + "　音源 " + Describe(progress.Audio);
        }

        static string Describe(FileProgress file)
        {
            switch (file.Status)
            {
                case FileProgress.State.Waiting: return "待機中";
                case FileProgress.State.Verifying: return "確認中";
                case FileProgress.State.Cached: return "キャッシュ済み";
                case FileProgress.State.Complete: return "完了";
                default:
                    return file.Total > 0
                        ? $"{file.Received * 100 / file.Total}% ({Size(file.Received)}/{Size(file.Total)})"
                        : Size(file.Received);
            }
        }

        static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1048576.0:0.0} MB" : $"{bytes / 1024.0:0} KB";
    }
}

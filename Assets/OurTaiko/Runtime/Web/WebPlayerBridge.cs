using System;
using System.Collections;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace OurTaiko
{
    // The browser validates parent origin/source before delivering JSON here.
    public sealed class WebPlayerBridge : MonoBehaviour
    {
        public static WebPlayerBridge Instance { get; private set; }
        public SongDefinition Song { get; private set; }
        PlayScene player;
        Coroutine loading;
        UnityWebRequest request;
        AudioClip decodingClip;
        int decodingBuffer;
        string requestId = "";
        int generation;
        bool ready;
        [Serializable] public sealed class Command
        {
            public string channel, type, requestId;
            public int version;
            public Payload payload;
        }
        [Serializable] public sealed class Payload
        {
            public string chartText, chartUrl, audioUrl, audioType, course = "Oni";
            public bool practice = true, autoPlay, replay;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void OurTaikoViewEmit(string json);
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            Instance = new GameObject("WebPlayerBridge").AddComponent<WebPlayerBridge>();
            DontDestroyOnLoad(Instance.gameObject);
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLInput.captureAllKeyboardInput = false;
#endif
        }
        IEnumerator Start()
        {
            yield return null;
            Emit("ready");
        }
        public void Receive(string json)
        {
            Command command;
            try { command = JsonConvert.DeserializeObject<Command>(json); }
            catch { Emit("error", new { code = "INVALID_MESSAGE" }); return; }
            if (command == null || command.channel != "ourtaiko-view" || command.version != 1) return;
            if (command.type == "hello") { Emit("ready"); return; }
            if (command.type == "load")
            {
                var value = command.payload;
                if (value == null || value.replay || (!value.practice && !value.autoPlay))
                { Emit("error", new { code = value?.replay == true ? "REPLAY_NOT_SUPPORTED" : "INVALID_MODE" }, command.requestId); return; }
                if (!ValidUrl(value.audioUrl) || (string.IsNullOrWhiteSpace(value.chartText) && !ValidUrl(value.chartUrl)))
                { Emit("error", new { code = "INVALID_RESOURCE" }, command.requestId); return; }
                if (value.chartText?.Length > 4 * 1024 * 1024)
                { Emit("error", new { code = "CHART_TOO_LARGE" }, command.requestId); return; }
                if (SceneSwitcher.EnsureInstance().IsSwitching) { Emit("error", new { code = "BUSY" }, command.requestId); return; }
                CancelLoading();
                player?.EmbeddedStop(); player = null; ready = false;
                requestId = command.requestId;
                loading = StartCoroutine(Load(value, generation));
                return;
            }
            if (command.type == "unload") { Exit(); return; }
            if (!ready || player == null) { Emit("error", new { code = "NOT_LOADED" }, command.requestId); return; }
            switch (command.type)
            {
                case "getState": Emit("state", player.EmbeddedState(), command.requestId); break;
                case "start": case "resume": player.EmbeddedStart(); break;
                case "pause": player.EmbeddedPause(); break;
                case "restart": player.EmbeddedRestart(); break;
                default: Emit("error", new { code = "UNKNOWN_COMMAND" }, command.requestId); break;
            }
        }
        static bool ValidUrl(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == "https" || uri.Scheme == "http") && string.IsNullOrEmpty(uri.UserInfo);
        IEnumerator Load(Payload value, int ticket)
        {
            Emit("loading", new { stage = "chart" });
            string chart = value.chartText;
            if (string.IsNullOrWhiteSpace(chart))
            {
                request = UnityWebRequest.Get(value.chartUrl); request.timeout = 60;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) { Fail("CHART_DOWNLOAD_FAILED"); yield break; }
                if (request.downloadedBytes > 4 * 1024 * 1024) { Fail("CHART_TOO_LARGE"); yield break; }
                chart = request.downloadHandler.text;
                request.Dispose(); request = null;
            }
            try
            {
                // Validate exactly as practice plays it: on a fixed route, which is chosen in its menu.
                _ = new PlaySession(TjaParser.Parse(chart, value.course), 0, BranchRoute.Normal);
            }
            catch (Exception error) { Debug.LogException(error); Fail("INVALID_CHART", error.Message); yield break; }
            Emit("loading", new { stage = "audio" });
            string format = (value.audioType ?? System.IO.Path.GetExtension(new Uri(value.audioUrl).AbsolutePath)).TrimStart('.').ToLowerInvariant();
            AudioType type = format == "ogg" ? AudioType.OGGVORBIS : format == "mp3" ? AudioType.MPEG : format == "wav" ? AudioType.WAV : AudioType.UNKNOWN;
            if (type == AudioType.UNKNOWN) { Fail("AUDIO_TYPE_REQUIRED"); yield break; }
#if UNITY_WEBGL && !UNITY_EDITOR
            request = UnityWebRequest.Get(value.audioUrl); request.timeout = 120;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { Fail("AUDIO_DOWNLOAD_OR_DECODE_FAILED"); yield break; }
            var bytes = request.downloadHandler.data;
            request.Dispose(); request = null;
            // Unity audio is disabled in the Web build: the decoded AudioBuffer stays in the browser
            // and Web Audio plays it directly.
            if (AudioEngine.EnsureInstance().Backend != AudioBackend.WebAudio) { Fail("AUDIO_UNAVAILABLE"); yield break; }
            AudioClip clip = null;
            int buffer = decodingBuffer = WebAudio.Decode(bytes);
            bytes = null;
            double decodeDeadline = Time.realtimeSinceStartupAsDouble + 30;
            while (WebAudio.State(buffer) == 0 && Time.realtimeSinceStartupAsDouble < decodeDeadline) yield return null;
            if (WebAudio.State(buffer) != 1) { Fail("AUDIO_DECODE_FAILED"); yield break; }
#else
            request = UnityWebRequestMultimedia.GetAudioClip(value.audioUrl, type);
            request.timeout = 120;
            ((DownloadHandlerAudioClip)request.downloadHandler).streamAudio = false;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success) { Fail("AUDIO_DOWNLOAD_OR_DECODE_FAILED"); yield break; }
            AudioClip clip = decodingClip = DownloadHandlerAudioClip.GetContent(request);
            int buffer = 0;
            // Web's decodeAudioData completes after the HTTP operation and Unity's loadState
            // can still report Loaded before its AudioBuffer has a sample count.
            double decodeDeadline = Time.realtimeSinceStartupAsDouble + 30;
            yield return new WaitForSecondsRealtime(0.1f);
            while (clip != null && clip.length <= 0 && Time.realtimeSinceStartupAsDouble < decodeDeadline)
                yield return new WaitForSecondsRealtime(0.05f);
            request.Dispose(); request = null;
            if (clip == null || clip.length <= 0) { Fail("AUDIO_DECODE_FAILED"); yield break; }
#endif
            decodingClip = null; decodingBuffer = 0;
            if (ticket != generation) { if (clip != null) Destroy(clip); WebAudio.Release(buffer); yield break; }
            var old = Song;
            Song = ScriptableObject.CreateInstance<SongDefinition>();
            Song.name = "Embedded chart";
            Song.chart = new TextAsset(chart); Song.music = clip; Song.webAudioBuffer = buffer; Song.course = value.course;
            var switcher = SceneSwitcher.EnsureInstance();
            while (switcher.IsSwitching) yield return null;
            switcher.ConfigureEmbedded(Song, value.course, value.autoPlay);
            var transition = switcher.SwitchSceneAsync(SceneSwitcher.PracticeScene);
            while (!transition.IsCompleted) yield return null;
            Release(old);
            loading = null;
            if (transition.IsFaulted) Fail("SCENE_LOAD_FAILED");
        }
        public void Attach(PlayScene value)
        {
            player = value; ready = true;
            Emit("loaded", new { duration = value.music.AudioLength() });
        }
        public void Finished(PlayResult result) => Emit("finished", result);
        public void Exit()
        {
            CancelLoading(); player?.EmbeddedStop(); player = null; ready = false;
            var old = Song; Song = null;
            SceneSwitcher.EnsureInstance().ConfigureEmbedded(null, null, false);
            Release(old); Emit("exit");
        }
        void Fail(string code, string detail = null)
        {
            request?.Dispose(); request = null; loading = null;
            if (decodingClip != null) Destroy(decodingClip); decodingClip = null;
            WebAudio.Release(decodingBuffer); decodingBuffer = 0;
            Emit("error", new { code, detail });
        }
        void CancelLoading()
        {
            generation++;
            if (loading != null) StopCoroutine(loading);
            loading = null; request?.Abort(); request?.Dispose(); request = null;
            if (decodingClip != null) Destroy(decodingClip); decodingClip = null;
            WebAudio.Release(decodingBuffer); decodingBuffer = 0;
        }
        static void Release(SongDefinition song)
        {
            if (song == null) return;
            if (song.music != null) Destroy(song.music);
            WebAudio.Release(song.webAudioBuffer); song.webAudioBuffer = 0;
            if (song.chart != null) Destroy(song.chart);
            Destroy(song);
        }
        void Emit(string type, object payload = null, string id = null)
        {
            string json = JsonConvert.SerializeObject(new { channel = "ourtaiko-view", version = 1, type, requestId = id ?? requestId, payload });
#if UNITY_WEBGL && !UNITY_EDITOR
            OurTaikoViewEmit(json);
#else
            Debug.Log(json);
#endif
        }
    }
}

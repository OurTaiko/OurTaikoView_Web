using System;
using System.Collections;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;

namespace OurTaiko
{
    // The browser validates parent origin/source before delivering JSON here.
    public sealed class WebPlayerBridge : MonoBehaviour
    {
        public static WebPlayerBridge Instance { get; private set; }
        public SongDefinition Song { get; private set; }
        PlayScene player;
        Coroutine loading;
        int decodingBuffer;
        string requestId = "";
        int generation;
        bool ready;
        [Serializable] public sealed class Command
        {
            public string channel, type, requestId;
            public Payload payload;
        }
        [Serializable] public sealed class Payload
        {
            public string chartText, audioType, course = "Oni";
            public bool practice = true, autoPlay, replay;
            public bool audioTransferred;
            // setDrumVolume: 0-100, the hit-sound (Drum group) volume.
            public float? volume;
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
            if (command == null || command.channel != "ourtaiko-view") return;
            if (command.type == "hello") { Emit("ready"); return; }
            // Accepted with or without a loaded chart; it also changes hit sounds that are still sounding.
            if (command.type == "setDrumVolume")
            {
                float? volume = command.payload?.volume;
                if (!volume.HasValue || float.IsNaN(volume.Value) || volume < 0 || volume > 100)
                { Emit("error", new { code = "INVALID_VOLUME" }, command.requestId); return; }
                var manager = SettingManager.EnsureInstance();
                var settings = manager.Settings.Clone();
                settings.audio.volume.drum = volume.Value / 100f;
                manager.Set(settings);
                return;
            }
            if (command.type == "load")
            {
                var value = command.payload;
                if (value == null || value.replay || (!value.practice && !value.autoPlay))
                { Emit("error", new { code = value?.replay == true ? "REPLAY_NOT_SUPPORTED" : "INVALID_MODE" }, command.requestId); return; }
                if (!value.audioTransferred || string.IsNullOrEmpty(command.requestId) || string.IsNullOrWhiteSpace(value.chartText))
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
        IEnumerator Load(Payload value, int ticket)
        {
            Emit("loading", new { stage = "chart" });
            string chart = value.chartText;
            try
            {
                // Validate exactly as practice plays it: on a fixed route, which is chosen in its menu.
                _ = new PlaySession(TjaParser.Parse(chart, value.course), 0, BranchRoute.Normal);
            }
            catch (Exception error) { Debug.LogException(error); Fail("INVALID_CHART", error.Message); yield break; }
            Emit("loading", new { stage = "audio" });
            string format = (value.audioType ?? "").TrimStart('.').ToLowerInvariant();
            if (format != "ogg" && format != "mp3" && format != "wav") { Fail("AUDIO_TYPE_REQUIRED"); yield break; }
#if UNITY_WEBGL && !UNITY_EDITOR
            // Encoded bytes arrive in the iframe via postMessage, never via a network
            // request or a JSON/C# byte array. Decoded audio also stays in JavaScript.
            if (AudioEngine.EnsureInstance().Backend != AudioBackend.WebAudio) { Fail("AUDIO_UNAVAILABLE"); yield break; }
            int buffer = decodingBuffer = WebAudio.DecodeTransferred(requestId);
            double decodeDeadline = Time.realtimeSinceStartupAsDouble + 30;
            while (WebAudio.State(buffer) == 0 && Time.realtimeSinceStartupAsDouble < decodeDeadline) yield return null;
            if (WebAudio.State(buffer) != 1) { Fail("AUDIO_DECODE_FAILED"); yield break; }
            decodingBuffer = 0;
            if (ticket != generation) { WebAudio.Release(buffer); yield break; }
            var old = Song;
            Song = ScriptableObject.CreateInstance<SongDefinition>();
            Song.name = "Embedded chart";
            Song.chart = new TextAsset(chart); Song.music = null; Song.webAudioBuffer = buffer; Song.course = value.course;
            var switcher = SceneSwitcher.EnsureInstance();
            while (switcher.IsSwitching) yield return null;
            switcher.ConfigureEmbedded(Song, value.course, value.autoPlay);
            var transition = switcher.SwitchSceneAsync(SceneSwitcher.PracticeScene);
            while (!transition.IsCompleted) yield return null;
            Release(old);
            loading = null;
            if (transition.IsFaulted) Fail("SCENE_LOAD_FAILED");
#else
            Fail("WEB_PLAYER_REQUIRED");
            yield break;
#endif
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
            loading = null;
            WebAudio.Release(decodingBuffer); decodingBuffer = 0;
            Emit("error", new { code, detail });
        }
        void CancelLoading()
        {
            generation++;
            if (loading != null) StopCoroutine(loading);
            loading = null;
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
            string json = JsonConvert.SerializeObject(new { channel = "ourtaiko-view", type, requestId = id ?? requestId, payload });
#if UNITY_WEBGL && !UNITY_EDITOR
            OurTaikoViewEmit(json);
#else
            Debug.Log(json);
#endif
        }
    }
}

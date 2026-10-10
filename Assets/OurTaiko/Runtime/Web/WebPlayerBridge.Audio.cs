using System;
using System.Collections;
using UnityEngine;

namespace OurTaiko
{
    public sealed partial class WebPlayerBridge
    {
        // A request chooses one decode mode. The parent owns the single native -> software retry.
        // Validate built-in sounds too: an MP3 song can load while Ogg drum sounds cannot.
        IEnumerator DecodeAudio(Payload value)
        {
            Emit("loading", new { stage = "audio", audioDecode = value.audioDecode ?? "native" });
            string format = (value.audioType ?? "").TrimStart('.').ToLowerInvariant();
            if (format != "ogg" && format != "mp3" && format != "wav") { Fail("AUDIO_TYPE_REQUIRED"); yield break; }
#if UNITY_WEBGL && !UNITY_EDITOR
            if (AudioEngine.EnsureInstance().Backend != AudioBackend.WebAudio) { Fail("AUDIO_UNAVAILABLE"); yield break; }
            WebAudio.SetDecodeMode(value.audioDecode);
            int buffer = decodingBuffer = WebAudio.DecodeTransferred(requestId);
            double deadline = Time.realtimeSinceStartupAsDouble + 60;
            while (WebAudio.State(buffer) == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            if (WebAudio.State(buffer) != 1) { Fail("AUDIO_DECODE_FAILED", "song"); yield break; }

            var catalog = Resources.Load<AudioAssetCatalog>("NativeAudioCatalog");
            if (catalog == null) { Fail("AUDIO_CATALOG_UNAVAILABLE"); yield break; }
            // Sequential decoding bounds temporary PCM/WASM memory on mobile devices.
            foreach (var entry in catalog.entries)
            {
                int clipBuffer = 0;
                string failure = null;
                try { clipBuffer = WebAudio.Clip(entry.clip); }
                catch (Exception error) { failure = error.Message; }
                if (failure != null) { Fail("AUDIO_ASSET_UNAVAILABLE", failure); yield break; }
                while (WebAudio.State(clipBuffer) == 0 && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
                if (WebAudio.State(clipBuffer) != 1) { Fail("AUDIO_DECODE_FAILED", "sound:" + entry.clip.name); yield break; }
            }
#else
            Fail("WEB_PLAYER_REQUIRED");
            yield break;
#endif
        }
    }
}

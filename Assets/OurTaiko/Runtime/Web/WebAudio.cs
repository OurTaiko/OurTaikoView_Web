using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OurTaiko
{
    // Browser Web Audio output (OurTaikoView.jslib). Decoded AudioBuffers stay in JavaScript and are shared
    // by reference count; each NativeAudioSample owns one voice that starts on the AudioContext clock.
    internal static class WebAudio
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int OurTaikoAudioInit();
        [DllImport("__Internal")] static extern double OurTaikoAudioInfo(int field);
        [DllImport("__Internal")] static extern int OurTaikoAudioDecode(byte[] bytes, int length);
        [DllImport("__Internal")] static extern double OurTaikoAudioBufferInfo(int buffer, int field);
        [DllImport("__Internal")] static extern void OurTaikoAudioRelease(int buffer);
        [DllImport("__Internal")] internal static extern int OurTaikoVoiceCreate(int buffer, bool normalize, bool speedChange);
        [DllImport("__Internal")] internal static extern void OurTaikoVoicePlay(int voice, float volume, bool loop, double position, float speed, double delay, bool scheduled);
        [DllImport("__Internal")] internal static extern void OurTaikoVoiceStop(int voice);
        [DllImport("__Internal")] internal static extern void OurTaikoVoiceSetVolume(int voice, float volume);
        [DllImport("__Internal")] internal static extern double OurTaikoVoiceInfo(int voice, int field);
        [DllImport("__Internal")] internal static extern void OurTaikoVoiceFree(int voice);

        // Bundled clips are decoded once per page; scenes reload with every embedded load.
        static readonly Dictionary<AudioClip, int> clips = new();

        internal static bool Init() => OurTaikoAudioInit() != 0;
        internal static string Describe() => $"WebAudio; {OurTaikoAudioInfo(0):0} Hz; base latency {OurTaikoAudioInfo(1) * 1000:0.#} ms; "
            + $"output latency {OurTaikoAudioInfo(2) * 1000:0.#} ms; measured output lag {OurTaikoAudioInfo(3) * 1000:0.#} ms"
            + (OurTaikoAudioInfo(4) == 0 ? "; waiting for a user gesture" : "");
        // Decoding is asynchronous: poll State until it leaves 0 (1 decoded, -1 failed).
        internal static int Decode(byte[] encoded) => OurTaikoAudioDecode(encoded, encoded.Length);
        internal static int State(int buffer) => (int)OurTaikoAudioBufferInfo(buffer, 0);
        internal static double Duration(int buffer) => OurTaikoAudioBufferInfo(buffer, 1);
        internal static void Release(int buffer) { if (buffer != 0) OurTaikoAudioRelease(buffer); }
        internal static int Clip(AudioClip clip)
        {
            if (clips.TryGetValue(clip, out int buffer) && State(buffer) >= 0) return buffer;
            Release(buffer);
            return clips[clip] = Decode(AudioAssetCatalog.Read(clip));
        }
#else
        internal static void Release(int buffer) { }
#endif
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace OurTaiko
{
    // Decoding mode and built-in clip cache are independent of the playback/voice API.
    internal static partial class WebAudio
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern int OurTaikoAudioDecode(byte[] bytes, int length);
        [DllImport("__Internal")] static extern int OurTaikoAudioDecodeTransferred(string requestId);
        [DllImport("__Internal")] static extern void OurTaikoAudioSetDecodeMode(bool software);
        static readonly Dictionary<AudioClip, int> clips = new();
        static bool softwareDecode;

        internal static void SetDecodeMode(string mode)
        {
            bool software = mode == "software";
            if (software != softwareDecode)
            {
                foreach (int buffer in clips.Values) Release(buffer);
                clips.Clear();
                softwareDecode = software;
            }
            OurTaikoAudioSetDecodeMode(software);
        }

        internal static void CancelPendingClips()
        {
            foreach (var item in clips.Where(item => State(item.Value) != 1).ToArray())
            {
                Release(item.Value);
                clips.Remove(item.Key);
            }
        }

        internal static int Decode(byte[] encoded) => OurTaikoAudioDecode(encoded, encoded.Length);
        internal static int DecodeTransferred(string requestId) => OurTaikoAudioDecodeTransferred(requestId);
        internal static int Clip(AudioClip clip)
        {
            if (clips.TryGetValue(clip, out int buffer) && State(buffer) >= 0) return buffer;
            Release(buffer);
            return clips[clip] = Decode(AudioAssetCatalog.Read(clip));
        }
#endif
    }
}

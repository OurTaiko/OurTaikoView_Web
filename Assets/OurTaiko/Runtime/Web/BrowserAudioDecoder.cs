using System.Runtime.InteropServices;
using UnityEngine;
namespace OurTaiko
{
    // Decode downloaded bytes with the browser, then give Unity a complete PCM clip.
    // This avoids Web GetAudioClip returning zero samples for some external OGGs.
    internal static class BrowserAudioDecoder
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] internal static extern void OurTaikoDecodeBegin(byte[] bytes, int length, int rate);
        [DllImport("__Internal")] internal static extern int OurTaikoDecodeInfo(int field);
        [DllImport("__Internal")] static extern int OurTaikoDecodeCopy([Out] float[] samples, int count);
        [DllImport("__Internal")] internal static extern void OurTaikoDecodeClear();
        internal static AudioClip TakeClip()
        {
            int frames = OurTaikoDecodeInfo(1), channels = OurTaikoDecodeInfo(2), rate = OurTaikoDecodeInfo(3);
            if (frames <= 0 || channels <= 0 || channels > 8 || rate <= 0 || (long)frames * channels > 128 * 1024 * 1024) return null;
            var pcm = new float[frames * channels];
            if (OurTaikoDecodeCopy(pcm, pcm.Length) == 0) return null;
            OurTaikoDecodeClear();
            var clip = AudioClip.Create("Embedded audio", frames, channels, rate, false);
            if (clip.SetData(pcm, 0)) return clip;
            Object.Destroy(clip); return null;
        }
#else
        internal static void OurTaikoDecodeClear() { }
#endif
    }
}

using System;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    // Build-time mapping preserves scene AudioClip references, but BASS receives the original
    // encoded bytes. Resources also works inside Android APKs without Unity audio decoding.
    public sealed class AudioAssetCatalog : ScriptableObject
    {
        [Serializable] public struct Entry { public AudioClip clip; public string resource; }
        public Entry[] entries;
        static AudioAssetCatalog catalog;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCatalog() => catalog = null;
        public static byte[] Read(AudioClip clip)
        {
#if UNITY_EDITOR
            string path = UnityEditor.AssetDatabase.GetAssetPath(clip);
            if (!string.IsNullOrEmpty(path)) return File.ReadAllBytes(path);
#endif
            if (catalog == null) catalog = Resources.Load<AudioAssetCatalog>("NativeAudioCatalog");
            if (catalog != null)
                foreach (var entry in catalog.entries)
                    if (entry.clip == clip)
                    {
                        var asset = Resources.Load<TextAsset>(entry.resource);
                        if (asset == null) throw new FileNotFoundException(entry.resource);
                        var bytes = asset.bytes;
                        Resources.UnloadAsset(asset);
                        return bytes;
                    }
            throw new InvalidOperationException("Original audio file is unavailable: " + clip.name);
        }
    }
}

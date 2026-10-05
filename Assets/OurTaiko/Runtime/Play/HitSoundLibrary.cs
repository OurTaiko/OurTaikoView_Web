using UnityEngine;

namespace OurTaiko
{
    // Nijiiro Sounds/hit_sounds: the 音色 sets, named by neiro_list.txt, each a don and a ka clip
    // (game.cpp loads hit_sounds/<neiro_index>/don.ogg and ka.ogg).
    public sealed class HitSoundLibrary : ScriptableObject
    {
        public string[] names;
        public AudioClip[] don, ka;

        public int Count => names == null ? 0 : names.Length;

        // PlayOptions.Mute, or an index without clips, plays nothing.
        public bool TryGet(int index, out AudioClip donClip, out AudioClip kaClip)
        {
            donClip = kaClip = null;
            if (index < 0 || index >= Count || don == null || ka == null || index >= don.Length || index >= ka.Length) return false;
            donClip = don[index]; kaClip = ka[index];
            return donClip != null && kaClip != null;
        }
    }
}

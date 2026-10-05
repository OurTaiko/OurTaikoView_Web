using System;
using System.Collections.Generic;
using UnityEngine;

namespace OurTaiko
{
    public enum AudioGroup { Effects, Bgm, Track, Drum, Voice }
    public enum SoundPlatform { Desktop, Windows, Android, IOS, Web }

    [Serializable]
    public sealed class SoundVolumes
    {
        // Multipliers preserve the authored mix when loading an older settings file.
        public float master = 1, bgm = 1, track = 1, drum = 1, effects = 1, voice = 1;
        static float Safe(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 1 : Mathf.Clamp(value, 0, 2);
        public float Gain(AudioGroup group) => Safe(master) * Safe(group switch
        {
            AudioGroup.Bgm => bgm, AudioGroup.Track => track, AudioGroup.Drum => drum,
            AudioGroup.Voice => voice, _ => effects,
        });
    }

    public static class SoundSettings
    {
        public static SoundPlatform Platform
        {
            get
            {
#if UNITY_EDITOR_WIN || (UNITY_STANDALONE_WIN && !UNITY_EDITOR)
                return SoundPlatform.Windows;
#elif UNITY_ANDROID && !UNITY_EDITOR
                return SoundPlatform.Android;
#elif UNITY_IOS && !UNITY_EDITOR
                return SoundPlatform.IOS;
#elif UNITY_WEBGL && !UNITY_EDITOR
                return SoundPlatform.Web;
#else
                return SoundPlatform.Desktop;
#endif
            }
        }
        static SettingItem Volume(string name, string description, Func<SoundVolumes, float> get, Action<SoundVolumes, float> set)
        {
            var choices = new string[41];
            for (int i = 0; i < choices.Length; i++) choices[i] = (i * 5) + "%";
            return new SettingItem(name, description + " Applies immediately; 0% mutes this group.", choices,
                s => Mathf.Clamp(Mathf.RoundToInt(get(s.audio.volume) * 20), 0, 40),
                (s, i) => set(s.audio.volume, i / 20f));
        }

        public static IReadOnlyList<SettingItem> Catalog(SoundPlatform platform)
        {
            var backends = platform == SoundPlatform.Windows
                ? new[] { AudioBackend.Automatic, AudioBackend.Bass, AudioBackend.Wasapi, AudioBackend.Asio, AudioBackend.Unity }
                : platform == SoundPlatform.Web ? new[] { AudioBackend.Unity }
                : new[] { AudioBackend.Automatic, AudioBackend.Bass, AudioBackend.Unity };
            return new[]
            {
                Volume("Master Volume", "Overall volume.", v => v.master, (v, n) => v.master = n),
                Volume("BGM Volume", "Music in menus and the result screen.", v => v.bgm, (v, n) => v.bgm = n),
                Volume("Track Volume", "Song playback and song previews.", v => v.track, (v, n) => v.track = n),
                Volume("Drum Volume", "Don and ka during gameplay and drum-sound previews.", v => v.drum, (v, n) => v.drum = n),
                Volume("Effects Volume", "Menu feedback, balloon pops and result effects.", v => v.effects, (v, n) => v.effects = n),
                Volume("Voice Volume", "Entry, song selection, combo and result voices.", v => v.voice, (v, n) => v.voice = n),
                new SettingItem("Output Backend", "Automatic uses WASAPI on Windows and BASS on other native platforms. ASIO requires an installed driver." + " Applies when leaving settings. Advanced audio settings are available in settings.json.",
                Array.ConvertAll(backends, b => b == AudioBackend.Bass ? "BASS" : b == AudioBackend.Wasapi ? "WASAPI" : b == AudioBackend.Asio ? "ASIO" : b.ToString()),
                s => Math.Max(0, Array.IndexOf(backends, s.audio.backend)), (s,i) => s.audio.backend = backends[i]),
            };
        }
    }
}

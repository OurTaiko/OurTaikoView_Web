using System;
using UnityEngine;

namespace OurTaiko
{
    // The global settings saved as settings.json, one section per type of the settings menu.
    // Missing fields keep their defaults, so older files load after new settings are added.
    [Serializable]
    public sealed class GameSettings
    {
        public GeneralSettings general = new GeneralSettings();
        public PlaySettings play = new PlaySettings();
        public DisplaySettings display = new DisplaySettings();
        public AudioOptions audio = new AudioOptions();

        public GameSettings Clone() => FromJson(ToJson());
        public string ToJson() => JsonUtility.ToJson(this, true);

        public static GameSettings FromJson(string json)
        {
            var settings = new GameSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            settings.general ??= new GeneralSettings();
            settings.general.keyboard ??= new KeyboardBindings();
            settings.general.keyboard.Normalize();
            settings.play ??= new PlaySettings();
            settings.display ??= new DisplaySettings();
            settings.audio ??= new AudioOptions();
            settings.audio.volume ??= new SoundVolumes();
            return settings;
        }
    }

    [Serializable]
    public sealed class GeneralSettings
    {
        public static readonly string[] Languages = { "en", "ja", "zh", "zh_tw", "ko" };
        public static readonly string[] LanguageNames = { "English", "日本語", "简体中文", "繁體中文", "Korean" };
        // Only song titles/subtitles use this setting until interface localization is added.
        public string language = "en";
        public KeyboardBindings keyboard = new KeyboardBindings();
        public string Language => Array.IndexOf(Languages, language) >= 0 ? language : "en";
    }

    [Serializable]
    public sealed class PlaySettings
    {
        // The touch drum in SinglePlayScene: enabled and visible, or disabled and hidden.
        public bool singlePlayerDrumPad = true;
        public const int OffsetDefaultMs = 0, OffsetStepMs = 1;
        // A shifts the chart relative to music; B shifts judgment only. Positive means later.
        public int audioOffsetMs = OffsetDefaultMs;
        public int judgeOffsetMs = OffsetDefaultMs;
    }

    [Serializable]
    public sealed class DisplaySettings
    {
        public const int Unlimited = -1;
        public static readonly int[] FrameRates = { 120, 60, Unlimited };

        // Application.targetFrameRate: 120, 60 or Unlimited (-1).
        public int targetFrameRate = 120;
        // Wait for every display refresh (vSyncCount 1). On desktop the refresh rate then replaces
        // targetFrameRate; mobile platforms ignore vSyncCount and always use targetFrameRate.
        public bool vSync;

        // Applies these settings to the running player; render every frame (no frame skipping).
        public void Apply()
        {
            QualitySettings.vSyncCount = vSync ? 1 : 0;
            UnityEngine.Rendering.OnDemandRendering.renderFrameInterval = 1;
            Application.targetFrameRate = TargetFrameRate;
        }

        // An unknown value in settings.json falls back to the 120 FPS default. On mobile -1 means
        // the platform default (30 or 60), so Unlimited asks for more than any display refreshes.
        public int TargetFrameRate
        {
            get
            {
                if (Array.IndexOf(FrameRates, targetFrameRate) < 0) return FrameRates[0];
                if (targetFrameRate == Unlimited && Application.isMobilePlatform) return 1000;
                return targetFrameRate;
            }
        }
    }
}

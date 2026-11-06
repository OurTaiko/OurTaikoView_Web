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
        public AccountSettings accounts = new AccountSettings();

        public GameSettings Clone() => FromJson(ToJson());
        public string ToJson() => JsonUtility.ToJson(this, true);

        public static GameSettings FromJson(string json)
        {
            var settings = new GameSettings();
            if (!string.IsNullOrWhiteSpace(json)) JsonUtility.FromJsonOverwrite(json, settings);
            settings.general ??= new GeneralSettings();
            settings.general.songLibraryPaths ??= Array.Empty<string>();
            settings.general.keyboard ??= new KeyboardBindings();
            settings.general.keyboard.Normalize();
            settings.play ??= new PlaySettings();
            settings.display ??= new DisplaySettings();
            settings.audio ??= new AudioOptions();
            settings.audio.volume ??= new SoundVolumes();
            settings.accounts ??= new AccountSettings();
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
        // File-only option. Additional folders alongside the always-included default Songs
        // folder. Each custom folder must be an absolute path on the current platform.
        public string[] songLibraryPaths = Array.Empty<string>();
        public KeyboardBindings keyboard = new KeyboardBindings();
        public string Language => Array.IndexOf(Languages, language) >= 0 ? language : "en";
        public static readonly LogLevel[] LogLevels = { OurTaiko.LogLevel.Debug, OurTaiko.LogLevel.Info, OurTaiko.LogLevel.Warning, OurTaiko.LogLevel.Error };
        // The least severe messages the log keeps; empty (or unknown) is Log.DefaultLevel.
        public string logLevel = "";
        public LogLevel LogLevel
        {
            get
            {
                LogLevel level = Log.Parse(logLevel, Log.DefaultLevel);
                return Array.IndexOf(LogLevels, level) >= 0 ? level : Log.DefaultLevel;
            }
        }
    }

    // The username of each player's OurTaiko account, filled in on the settings page. Passwords
    // are not kept: the sign-in itself lives in account.dat (OnlineManager.Accounts). The password
    // fields only carry what older versions saved, until it signs in once and is erased.
    [Serializable]
    public sealed class AccountSettings
    {
        public string p1Username = "", p1Password = "";
        public string p2Username = "", p2Password = "";
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
        public const int InputDelayDefaultMs = 0, InputDelayMaxMs = 100;
        // How long a drum press waits before it is judged, heard and shown, like the arcade. 0 is off.
        public int inputDelayMs = InputDelayDefaultMs;
        // Off: every course judges with its own standard windows. On: the 良/可/不可 half widths below
        // apply to every course in every mode (practice too), and no score is saved or uploaded.
        public bool customJudgeWindow;
        public const int JudgeWindowMinMs = 1, JudgeWindowMaxMs = 300;
        // The standard 鬼 windows (25.025, 75.075 and 108.44 ms), rounded.
        public static readonly int[] StandardJudgeMs = { 25, 75, 108 };
        public int judgeGoodMs = 25, judgeOkMs = 75, judgeBadMs = 108;
        public JudgeWindows? CustomJudgeWindows => customJudgeWindow ? JudgeWindows.FromMs(judgeGoodMs, judgeOkMs, judgeBadMs) : null;

        // 0 良, 1 可, 2 不可. A new value moves its neighbours along so that 良 <= 可 <= 不可 holds.
        public int JudgeMs(int window) => window == 0 ? judgeGoodMs : window == 1 ? judgeOkMs : judgeBadMs;
        public void SetJudgeMs(int window, int ms)
        {
            ms = Math.Max(JudgeWindowMinMs, Math.Min(JudgeWindowMaxMs, ms));
            if (window == 0) { judgeGoodMs = ms; judgeOkMs = Math.Max(judgeOkMs, ms); judgeBadMs = Math.Max(judgeBadMs, judgeOkMs); }
            else if (window == 1) { judgeOkMs = ms; judgeGoodMs = Math.Min(judgeGoodMs, ms); judgeBadMs = Math.Max(judgeBadMs, ms); }
            else { judgeBadMs = ms; judgeOkMs = Math.Min(judgeOkMs, ms); judgeGoodMs = Math.Min(judgeGoodMs, judgeOkMs); }
        }
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
        // Play the chart's background movie (BGMOVIE) behind the lanes, and download it for online
        // charts. While it plays the stage art is hidden and the lanes' backgrounds are drawn at
        // movieLaneOpacity percent (OpenTaiko's BGAlpha, 100 of 255).
        public bool backgroundMovie = true;
        public const int MovieLaneOpacityDefault = 40, MovieLaneOpacityStep = 5;
        public int movieLaneOpacity = MovieLaneOpacityDefault;
        public float MovieLaneAlpha => Mathf.Clamp(movieLaneOpacity, 0, 100) / 100f;

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

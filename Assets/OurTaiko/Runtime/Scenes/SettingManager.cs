using System;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    // Global settings holder, created before the first scene and kept across loads like
    // PlayerInfoController. Reads settings.json (persistentDataPath) once, writes it on every change
    // and tells listeners through Changed.
    public sealed class SettingManager : MonoBehaviour
    {
        public const string FileName = "settings.json";
        public static SettingManager Instance { get; private set; }

        public GameSettings Settings { get; private set; } = new GameSettings();
        // null for unsaved settings (tests); otherwise where Settings is read from and written to.
        public string FilePath { get; private set; }
        public event Action<GameSettings> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        public static SettingManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            // Also handles entering Play mode with scene/domain reload disabled.
            var existing = FindFirstObjectByType<SettingManager>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(SettingManager)).AddComponent<SettingManager>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            var settings = new GameSettings();
            settings.audio.backend = AudioBackend.Unity;
            UseUnsaved(settings);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // A missing file is created with the defaults so it can be found and edited.
        public void Load(string path)
        {
            FilePath = path;
            var settings = new GameSettings();
            try
            {
                if (File.Exists(path)) settings = GameSettings.FromJson(File.ReadAllText(path));
                else Write(settings);
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable settings: " + error.Message); }
            Apply(settings);
        }

        public void Set(GameSettings settings)
        {
            settings = settings?.Clone() ?? new GameSettings();
            Write(settings);
            Apply(settings);
        }

        // Tests use settings that are never written to the player's file.
        public void UseUnsaved(GameSettings settings)
        {
            FilePath = null;
            Apply(settings?.Clone() ?? new GameSettings());
        }

        void Apply(GameSettings settings)
        {
            Settings = settings;
            Changed?.Invoke(Settings);
        }

        void Write(GameSettings settings)
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, settings.ToJson());
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporary, FilePath);
        }
    }
}

using System;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    // Global player-info holder, created before the first scene and kept across loads. It reads
    // player.json once when initialized and tells every nameplate when the data changes.
    public sealed class PlayerInfoController : MonoBehaviour
    {
        public const string FileName = "player.json";
        public static PlayerInfoController Instance { get; private set; }

        public PlayerInfo Info { get; private set; } = new PlayerInfo();
        // null for unsaved data (tests); otherwise where Info is read from and written to.
        public string FilePath { get; private set; }
        public event Action<PlayerInfo> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() => EnsureInstance();

        public static PlayerInfoController EnsureInstance()
        {
            if (Instance != null) return Instance;
            // Also handles entering Play mode with scene/domain reload disabled.
            var existing = FindFirstObjectByType<PlayerInfoController>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(PlayerInfoController)).AddComponent<PlayerInfoController>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UseUnsaved(new PlayerInfo());
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // A missing file is created with the defaults (scores.cpp's seeded player) so it can be edited.
        public void Load(string path)
        {
            FilePath = path;
            var info = new PlayerInfo();
            try
            {
                if (File.Exists(path)) info = PlayerInfo.FromJson(File.ReadAllText(path));
                else Write(info);
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable player info: " + error.Message); }
            Apply(info);
        }

        // Reads the file again, e.g. after it was edited while the game runs.
        public void Reload()
        {
            if (!string.IsNullOrEmpty(FilePath)) Load(FilePath);
        }

        public void Set(PlayerInfo info)
        {
            info = info?.Clone() ?? new PlayerInfo();
            Write(info);
            Apply(info);
        }

        // Tests use data that is never written to the player's file.
        public void UseUnsaved(PlayerInfo info)
        {
            FilePath = null;
            Apply(info?.Clone() ?? new PlayerInfo());
        }

        void Apply(PlayerInfo info)
        {
            Info = info;
            Changed?.Invoke(Info);
        }

        void Write(PlayerInfo info)
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            string directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, info.ToJson());
            if (File.Exists(FilePath)) File.Delete(FilePath);
            File.Move(temporary, FilePath);
        }
    }
}

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace OurTaiko
{
    // Global holder of the local songs (OurTaikoPlayer's tja_path), created before the first scene
    // and kept across loads like OnlineManager. LocalSongScanner reads persistentDataPath/Songs on a
    // worker thread; ServerLogin starts a rescan each time, and the first read waits for it. Each
    // chart keeps its SongDefinition across rescans, so the selected song survives a refresh.
    public sealed class LocalSongLibrary : MonoBehaviour
    {
        public static LocalSongLibrary Instance { get; private set; }
        public static string DefaultRoot => Path.Combine(Application.persistentDataPath, "Songs");

        public string Root { get; private set; }
        // Charts outside box.def folders, by file name; then one folder per top-level box.def folder.
        public IReadOnlyList<SongDefinition> Songs { get { Complete(); return songs; } }
        public IReadOnlyList<SongFolder> Folders { get { Complete(); return folders; } }

        readonly List<SongDefinition> songs = new List<SongDefinition>();
        readonly List<SongFolder> folders = new List<SongFolder>();
        readonly Dictionary<string, SongDefinition> byKey = new Dictionary<string, SongDefinition>();
        Task<LocalSongCatalog> scan;
        bool scanned;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() { } // Embedded player does not start desktop catalog services.

        public static LocalSongLibrary EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindFirstObjectByType<LocalSongLibrary>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(LocalSongLibrary)).AddComponent<LocalSongLibrary>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Root = DefaultRoot;
            // Created up front so players can find where to put their songs.
            try { Directory.CreateDirectory(Root); }
            catch (System.Exception error) { Debug.LogWarning("Could not create the songs folder: " + error.Message); }
        }

        void OnDestroy()
        {
            if (Instance != this) return;
            Clear();
            Instance = null;
        }

        // Reads another songs folder from the next access on (tests use a temporary copy).
        public void UseRoot(string root)
        {
            Root = root;
            scan = null; scanned = false;
            Clear();
        }

        // Starts a rescan in the background; Songs and Folders wait for it.
        public void Refresh()
        {
            string root = Root;
            string language = SettingManager.Instance != null ? SettingManager.Instance.Settings.general.Language : "en";
            scan = Task.Run(() => LocalSongScanner.Scan(root, language));
        }

        void Complete()
        {
            if (scan == null && !scanned) Refresh();
            if (scan == null) return;
            var task = scan;
            scan = null;
            scanned = true;
            LocalSongCatalog catalog;
            try { catalog = task.Result; }
            catch (System.AggregateException error)
            {
                Debug.LogWarning("Could not read the songs folder: " + error.GetBaseException().Message);
                return;
            }
            Apply(catalog);
        }

        void Apply(LocalSongCatalog catalog)
        {
            var keep = new HashSet<string>();
            songs.Clear(); folders.Clear();
            foreach (var chart in catalog.Songs) songs.Add(Song(chart, 0, keep));
            foreach (var folder in catalog.Folders)
            {
                int genre = Online.OnlineManager.GenreFrame(folder.Genre);
                folders.Add(new SongFolder
                {
                    Key = folder.Key, Title = folder.Title, Genre = genre,
                    Songs = folder.Charts.Select(chart => Song(chart, genre, keep)).ToArray(),
                });
            }
            foreach (string key in byKey.Keys.Where(k => !keep.Contains(k)).ToList())
            {
                Release(byKey[key]);
                byKey.Remove(key);
            }
        }

        SongDefinition Song(LocalChart chart, int genre, HashSet<string> keep)
        {
            if (!byKey.TryGetValue(chart.Key, out var song))
            {
                song = ScriptableObject.CreateInstance<SongDefinition>();
                song.name = chart.Key;
                song.hideFlags = HideFlags.DontSave;
                byKey[chart.Key] = song;
            }
            keep.Add(chart.Key);
            if (song.chart == null || song.chart.text != chart.Text)
            {
                if (song.chart != null) Destroy(song.chart);
                song.chart = new TextAsset(chart.Text) { name = chart.Key, hideFlags = HideFlags.DontSave };
            }
            song.audioPath = chart.AudioPath;
            song.genre = genre;
            var oni = chart.Info.Course(Difficulty.Oni);
            song.course = (oni ?? chart.Info.Courses[chart.Info.Courses.Count - 1]).Course;
            return song;
        }

        static void Release(SongDefinition song)
        {
            if (song.chart != null) Destroy(song.chart);
            Destroy(song);
        }

        void Clear()
        {
            foreach (var song in byKey.Values) Release(song);
            byKey.Clear(); songs.Clear(); folders.Clear();
        }
    }
}

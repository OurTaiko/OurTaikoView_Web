using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace OurTaiko.Online
{
    // A server category as a song-select folder: its songs in API order (shared SongDefinitions).
    public sealed class OnlineFolder
    {
        public string Key = "", Title = "", ServerName = "";
        public int Genre;
        public SongDefinition[] Songs = Array.Empty<SongDefinition>();
    }

    // Global holder of the online servers, created before the first scene and kept across loads
    // (like SettingManager). It reads servers.json, owns the FanmadeClient, pumps its score
    // uploads every frame and turns the connected catalog into SongDefinitions and category
    // folders for song select.
    public sealed class OnlineManager : MonoBehaviour
    {
        public static OnlineManager Instance { get; private set; }

        public ServerList Servers { get; private set; } = new ServerList();
        // null for unsaved data (tests); otherwise where Servers is read from and written to.
        public string FilePath { get; private set; }
        public FanmadeClient Client { get; private set; }
        // The connected catalog as playable songs; the same instance for a chart until Reset.
        public IReadOnlyList<SongDefinition> Songs => songs;
        // One folder per server category, in server then bootstrap order.
        public IReadOnlyList<OnlineFolder> Folders => folders;
        // The folder song select had open, reopened when it comes back from a song; reset by ServerLogin.
        public string OpenFolderKey { get; set; }

        readonly List<SongDefinition> songs = new List<SongDefinition>();
        readonly List<OnlineFolder> folders = new List<OnlineFolder>();
        readonly Dictionary<string, (SongDefinition Song, FanmadeChart Chart)> byKey = new Dictionary<string, (SongDefinition, FanmadeChart)>();
        readonly Dictionary<SongDefinition, FanmadeChart> charts = new Dictionary<SongDefinition, FanmadeChart>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap() { } // Embedded player has no server login or upload queue.

        public static OnlineManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            // Also handles entering Play mode with scene/domain reload disabled.
            var existing = FindFirstObjectByType<OnlineManager>();
            if (existing != null) { existing.Initialize(); return Instance; }
            new GameObject(nameof(OnlineManager)).AddComponent<OnlineManager>();
            return Instance;
        }

        void Awake() => Initialize();

        void Initialize()
        {
            if (Instance == this) return;
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            UseCache(Path.Combine(Application.persistentDataPath, "cache", "fanmade"),
                Path.Combine(Application.persistentDataPath, "scores.sqlite3"));
            Load(Path.Combine(Application.persistentDataPath, ServerList.FileName));
        }

        void Update() => Client?.Update();

        void OnDestroy()
        {
            if (Instance != this) return;
            Client?.Dispose();
            ClearSongs();
            Instance = null;
        }

        public IEnumerable<ServerConfig> EnabledServers => Servers.servers.Where(s => s.enabled);

        // A missing file is created with the built-in servers so it can be edited.
        public void Load(string path)
        {
            FilePath = path;
            ServerList list = null;
            try
            {
                list = ServerList.Read(path) ?? new ServerList();
                if (list.AddBuiltIn() || !File.Exists(path)) list.Write(path);
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable server list: " + error.Message); }
            Servers = list ?? ServerList.Default();
        }

        // Tests use servers (and a cache folder) that never touch the player's files.
        public void UseUnsaved(ServerList list, string cacheRoot = null)
        {
            FilePath = null;
            Servers = list ?? new ServerList();
            if (cacheRoot != null) UseCache(cacheRoot);
            else Disconnect();
        }

        void UseCache(string root, string databasePath = null)
        {
            Client?.Dispose();
            ClearSongs();
            Client = new FanmadeClient(root, databasePath);
        }

        // Remembers the account ServerLogin used, in the same servers.json entry.
        public void Remember(int index, string username, string password, bool autoLogin)
        {
            var server = Servers.servers[index];
            server.username = username ?? ""; server.password = password ?? ""; server.autoLogin = autoLogin;
            if (string.IsNullOrEmpty(FilePath)) return;
            try { Servers.Write(FilePath); }
            catch (Exception error) { Debug.LogWarning("Could not save the server list: " + error.Message); }
        }

        // Forgets every connection and catalog song (ServerLogin starts over each time).
        public void Disconnect()
        {
            Client.Reset();
            ClearSongs();
        }

        // Publishes the client's catalog as songs, reusing the instances of charts already listed.
        public void RefreshSongs()
        {
            var keep = new HashSet<string>();
            songs.Clear(); charts.Clear();
            foreach (var chart in Client.Charts.SelectMany(c => !c.IsSingle ? new[] { c.ForPlayer("P1"), c.ForPlayer("P2") }.Where(x => x.IsPlayable) : new[] { c }))
            {
                string key = SongKey(chart);
                keep.Add(key);
                if (!byKey.TryGetValue(key, out var entry))
                {
                    var song = ScriptableObject.CreateInstance<SongDefinition>();
                    song.name = key;
                    song.hideFlags = HideFlags.DontSave;
                    owned.Add(song);
                    entry = (song, chart);
                }
                entry.Chart = chart;
                entry.Song.onlineChart = chart;
                // A copy downloaded earlier may be an older version; loading downloads the chart again.
                SetChartText(entry.Song, null);
                entry.Song.genre = GenreFrame(chart.Genre);
                entry.Song.course = chart.Difficulties.First(d => d != null).Course;
                byKey[key] = entry;
                songs.Add(entry.Song);
                charts[entry.Song] = chart;
            }
            foreach (string key in byKey.Keys.Where(k => !keep.Contains(k)).ToList()) byKey.Remove(key);
            folders.Clear();
            foreach (var category in Client.Categories)
                folders.Add(new OnlineFolder
                {
                    Key = category.Server + "/" + category.Id, Title = category.Title, ServerName = category.ServerName,
                    Genre = GenreFrame(category.Genre),
                    Songs = category.ChartIds.SelectMany(id => songs.Where(song => charts[song].Server == category.Server && charts[song].Id == id)).ToArray(),
                });
        }

        public static string SongKey(FanmadeChart chart) => "fanmade/" + chart.Server + "/" + chart.Id + (chart.SelectedPlayer.Length > 0 ? "/" + chart.SelectedPlayer : "");

        public bool IsOnline(SongDefinition song) => song != null && charts.ContainsKey(song);
        public FanmadeChart ChartOf(SongDefinition song) => song != null && charts.TryGetValue(song, out var chart) ? chart : null;

        // After a download: the song plays the verified copy (the chart may be a newer version).
        public void SetPrepared(SongDefinition song, FanmadeChart chart, string playableTja, AudioClip music)
        {
            charts[song] = chart;
            song.onlineChart = chart;
            if (byKey.ContainsKey(song.name)) byKey[song.name] = (song, chart);
            SetChartText(song, playableTja);
            if (song.music != null && song.music != music && owned.Remove(song.music)) Destroy(song.music);
            song.music = music;
            if (music != null && !owned.Contains(music)) owned.Add(music);
        }

        // null drops the chart: the song lists from its metadata until SongLoadingScene downloads it.
        void SetChartText(SongDefinition song, string text)
        {
            if (song.chart != null && owned.Remove(song.chart)) Destroy(song.chart);
            song.chart = text == null ? null : new TextAsset(text) { name = song.name, hideFlags = HideFlags.DontSave };
            if (song.chart != null) owned.Add(song.chart);
        }

        void ClearSongs()
        {
            foreach (var item in owned) if (item != null) Destroy(item);
            owned.Clear(); songs.Clear(); byKey.Clear(); charts.Clear(); folders.Clear();
            OpenFolderKey = null;
        }

        // box.def GENRE names (enums.h GENRE_MAP) to the Nijiiro bar_genre frame (GENRE_TO_REF_FRAME).
        public static int GenreFrame(string genre)
        {
            switch ((genre ?? "").Trim().ToUpperInvariant())
            {
                case "J-POP": case "POP": case "ポップス": return 1;
                case "ANIME": case "アニメ": return 2;
                case "GAME": case "GAME MUSIC": case "ゲームミュージック": return 3;
                case "NAMCO": case "NAMCO ORIGINAL": case "ナムコオリジナル": return 4;
                case "CLASSICAL": case "CLASSIC": case "クラシック": return 5;
                case "VARIETY": case "バラエティー": case "バラエティ": return 6;
                case "CHILDREN": case "どうよう": return 7;
                case "VOCALOID": case "ボーカロイド": return 8;
                default: return 0;
            }
        }
    }
}

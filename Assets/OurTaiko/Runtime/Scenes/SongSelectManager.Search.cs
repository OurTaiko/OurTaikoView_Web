using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OurTaiko.Online;

namespace OurTaiko
{
    public sealed partial class SongSelectManager
    {
        public bool SearchDialogOpen { get; set; }
        internal void PlaySearchSound(Sound sound)
        {
            if (SearchDialogOpen && !Switcher.IsInputBlocked) Play(sound);
        }
        public bool SearchActive { get; private set; }
        public SongSearchQuery SearchQuery { get; private set; } = new SongSearchQuery();
        public string SearchStatus { get; private set; } = "";
        public int SearchCount => searchSongs.Count;
        public event Action WheelRebuilt;
        public event Action SearchRequested;
        public string SearchFolderTitle => SearchWords("Search / Sort", "検索・並べ替え", "搜索／排序");
        public string SearchFolderDescription => SearchWords("Keyword · Difficulty · Stars · FC / AP", "Keyword・むずかしさ・★・FC / AP", "Keyword · 难度 · 星级 · FC / AP");
        static string SearchWords(string en, string ja, string zh)
        {
            string language = SettingManager.Instance?.Settings.general.Language ?? "en";
            return language == "ja" ? ja : language.StartsWith("zh") ? zh : en;
        }
        static WheelItem NewSearchItem() => new WheelItem { Kind = ItemKind.Search };
        readonly List<SongDefinition> searchSongs = new List<SongDefinition>();
        int searchVersion;

        public async Task SearchAsync(SongSearchQuery query, CancellationToken cancel)
        {
            int version = ++searchVersion;
            var online = OnlineManager.EnsureInstance();
            var library = LocalSongLibrary.EnsureInstance();
            var local = library.Songs.Concat(library.Folders.SelectMany(f => f.Songs)).Distinct().Where(query.MatchesLocal).ToList();
            var replies = await online.Client.SearchAsync(query, cancel);
            cancel.ThrowIfCancellationRequested();
            if (version != searchVersion) return;
            var found = local;
            foreach (var reply in replies)
                foreach (var chart in reply.Charts)
                    foreach (var player in chart.IsSingle ? new[] { chart } : new[] { chart.ForPlayer("P1"), chart.ForPlayer("P2") })
                        if (player.IsPlayable && query.Matches(player.ToSongInfo(null))) found.Add(online.SearchSong(player));
            bool NeedsClear(SongDefinition song) => song.onlineChart != null ? online.Client.SearchNeedsClear(song.onlineChart, query)
                : query.Order != SongSearchOrder.Default && song.ReadDisplayInfo().Courses.Where(query.Matches)
                    .Any(c => (int)(SongScores.Get(song, c.Difficulty)?.crown ?? Crown.None) < (query.Order == SongSearchOrder.UnFullCombo ? 2 : 3));
            searchSongs.Clear();
            searchSongs.AddRange(found.Distinct().OrderByDescending(NeedsClear));
            var errors = replies.Where(r => r.Error != null).Select(r => r.Endpoint.Config.DisplayName + ": " + r.Error).ToList();
            foreach (var config in online.EnabledServers)
                if (!online.Client.Endpoints.Any(e => e.IsConnected && e.Config.baseUrl.TrimEnd('/') == (config.baseUrl ?? "").TrimEnd('/') && e.Config.username == (config.username ?? "")))
                    errors.Add(config.DisplayName + ": offline");
            SearchStatus = string.Join(" · ", errors);
            SearchQuery = query; SearchActive = true;
            BuildSearchWheel(null);
            WheelRebuilt?.Invoke();
        }

        void BuildSearchWheel(SongDefinition selected)
        {
            items.Clear(); openFolder = -1; openAt = -1; openCount = 0;
            foreach (var song in searchSongs.Where(s => s != null))
            {
                var item = SongItem(song, -1);
                string source = song.onlineChart == null ? "Local" : OnlineManager.Instance.Client.Endpoint(song.onlineChart.Server)?.Config.DisplayName ?? "Online";
                item.Info.Subtitle = string.IsNullOrEmpty(item.Info.Subtitle) ? source : item.Info.Subtitle + " · " + source;
                items.Add(item);
            }
            items.Add(NewSearchItem());
            items.Add(new WheelItem { Kind = ItemKind.Back });
            Focused = Math.Max(0, items.FindIndex(i => selected != null && i.Song == selected));
        }

        public void CancelSearch() => searchVersion++;
        void ClearSearchState()
        {
            CancelSearch(); SearchActive = false; SearchDialogOpen = false;
            SearchQuery = new SongSearchQuery(); SearchStatus = ""; searchSongs.Clear();
        }
        public void ClearSearch()
        {
            ClearSearchState();
            var ready = CourseReady;
            Load(hitSoundCount); CourseReady = ready;
            Focused = items.FindIndex(i => i.Kind == ItemKind.Search);
            WheelRebuilt?.Invoke();
        }
    }
}

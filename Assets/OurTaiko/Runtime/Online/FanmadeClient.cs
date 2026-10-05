using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // fanmade.cpp Client without rendering or Unity state, so it can be tested against a fixture
    // server. Difference: every category is fetched when a server connects (OurTaikoPlayer waits
    // for the server folder to open); song select shows each category as a folder.
    //
    // Cache layout under the root, as in OurTaikoPlayer:
    //   objects/<endpoint>/<chart>/<version>/  original.tja, audio.ogg|mp3, play.tja
    // Pending uploads live in the separate PendingScoreUploads SQLite table.
    public sealed partial class FanmadeClient : IDisposable
    {
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);
        public readonly string CacheRoot;
        public string Status { get { lock (sync) return status; } }
        // Bumped whenever charts or scores change, so views know to refresh.
        public long Revision => Interlocked.Read(ref revision);
        // Categories loaded / total of the running ConnectAsync (a large server takes a while).
        public (int Done, int Total) CatalogProgress { get { lock (sync) return catalogProgress; } }
        public void ResetCatalogProgress() { lock (sync) catalogProgress = (0, 0); }
        public bool IsUploading => uploads != null && !uploads.IsCompleted;

        readonly object sync = new object();
        readonly List<FanmadeEndpoint> endpoints = new List<FanmadeEndpoint>();
        readonly Dictionary<string, List<FanmadeChart>> charts = new Dictionary<string, List<FanmadeChart>>();
        readonly Dictionary<string, List<FanmadeCategory>> categoryLists = new Dictionary<string, List<FanmadeCategory>>();
        string status = "";
        (int, int) catalogProgress;
        long revision;
        Task uploads;
        DateTime retryAt = DateTime.MinValue;

        public PendingScoreQueue UploadQueue { get; }
        public FanmadeClient(string cacheRoot, string databasePath = null)
        {
            CacheRoot = Path.GetFullPath(cacheRoot);
            UploadQueue = new PendingScoreQueue(databasePath ?? Path.Combine(CacheRoot, "scores.sqlite3"),
                Path.Combine(CacheRoot, "pending"));
        }

        public IReadOnlyList<FanmadeEndpoint> Endpoints { get { lock (sync) return endpoints.ToArray(); } }

        // Every chart of every connected server, in server then category order, one entry per chart id.
        public IReadOnlyList<FanmadeChart> Charts
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => charts.ContainsKey(e.Id)).SelectMany(e => charts[e.Id]).ToArray();
            }
        }

        // Every category of every connected server, in server then bootstrap order (empty ones included).
        public IReadOnlyList<FanmadeCategory> Categories
        {
            get
            {
                lock (sync)
                    return endpoints.Where(e => categoryLists.ContainsKey(e.Id)).SelectMany(e => categoryLists[e.Id]).ToArray();
            }
        }

        public FanmadeChart Chart(string server, string id)
        {
            lock (sync) return charts.TryGetValue(server, out var list) ? list.Find(c => c.Id == id) : null;
        }

        public FanmadeEndpoint Endpoint(string id) { lock (sync) return endpoints.Find(e => e.Id == id); }

        void SetStatus(string text) { lock (sync) status = text; }

        // Drops every server and its catalog (uploads finish first). Pending scores stay on disk.
        public void Reset()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            try { uploads?.Wait(); } catch (AggregateException) { }
#endif
            uploads = null;
            lock (sync)
            {
                foreach (var e in endpoints) e.Dispose();
                endpoints.Clear(); charts.Clear(); categoryLists.Clear();
                retryAt = DateTime.MinValue;
            }
            Interlocked.Increment(ref revision);
        }

        // A second connected entry for the same API and account is reused, as in bootstrap().
        public FanmadeEndpoint Add(ServerConfig config)
        {
            var endpoint = new FanmadeEndpoint(config);
            lock (sync)
            {
                int existing = endpoints.FindIndex(e => e.Id == endpoint.Id);
                if (existing >= 0 && endpoints[existing].IsConnected) { endpoint.Dispose(); return endpoints[existing]; }
                // A retry with another password replaces the endpoint that has not connected.
                if (existing >= 0) { endpoints[existing].Dispose(); endpoints.RemoveAt(existing); }
                endpoints.Add(endpoint);
            }
            return endpoint;
        }

        // Logs in (unless `guest`), then loads the bootstrap and every category. A rejected login
        // throws HTTP_401/403 without touching the catalog; the caller may then retry as a guest.
        public async Task ConnectAsync(FanmadeEndpoint e, bool guest, CancellationToken cancel = default)
        {
            if (!e.HasValidUrl()) throw new FanmadeException("SERVER_URL_INVALID");
            await e.Transport.WaitAsync(cancel);
            try
            {
                lock (sync) { e.IsConnected = false; charts.Remove(e.Id); categoryLists.Remove(e.Id); }
                SetStatus(e.Config.DisplayName + ": loading catalog");
                if (guest) e.BecomeGuest();
                else await e.LoginAsync(cancel);
                var snapshot = Json.Parse(await e.AuthorizedAsync("/api/v1/game/bootstrap", cancel: cancel));
                e.SongIdOnly = snapshot["songIdOnly"]?.Type == JTokenType.Boolean && (bool)snapshot["songIdOnly"];
                e.CourseKeyedDifficulties = snapshot["courseKeyedDifficulties"]?.Type == JTokenType.Boolean && (bool)snapshot["courseKeyedDifficulties"];
                var resourceVersion = snapshot["resourceDownloadVersion"];
                if (resourceVersion != null && (resourceVersion.Type != JTokenType.Integer || (long)resourceVersion < 0 || (long)resourceVersion > 1))
                    throw new FanmadeException("RESOURCE_PROTOCOL_UNSUPPORTED");
                e.ResourceDownloadVersion = (int?)resourceVersion ?? 0;
                e.ScoreReplayV1 = snapshot["scoreReplayVersion"]?.Type == JTokenType.Integer && (int)snapshot["scoreReplayVersion"] == 1;
                if (!(snapshot["categories"] is JArray categoryList) || !(snapshot["scores"] is JArray scoreList))
                    throw new FanmadeException("API_BOOTSTRAP_INVALID");
                var categories = new List<(string Id, string Title, string Genre)>();
                foreach (var v in categoryList)
                {
                    var category = (Json.Str(v, "id"), Json.Str(v, "title"), Json.Str(v, "genre"));
                    if (!ValidCategory(category.Item1) || categories.Any(c => c.Id == category.Item1)) throw new FanmadeException("API_CATEGORY_INVALID");
                    if (v["chartCount"] != null) Json.Number(v, "chartCount");
                    categories.Add(category);
                }
                var scores = new List<FanmadeScore>();
                if (e.IsAuthenticated) foreach (var v in scoreList) scores.Add(FanmadeScore.From(v, e.SongIdOnly));

                var list = new List<FanmadeChart>();
                var folders = new List<FanmadeCategory>();
                var seen = new HashSet<string>();
                lock (sync) catalogProgress = (0, categories.Count);
                foreach (var category in categories)
                {
                    SetStatus(e.Config.DisplayName + ": loading " + category.Title);
                    var result = Json.Parse(await e.AuthorizedAsync("/api/v1/game/categories/" + category.Id + "/charts", cancel: cancel));
                    if (Json.Str(result, "categoryId") != category.Id || !(result["charts"] is JArray categoryCharts))
                        throw new FanmadeException("API_CATEGORY_INVALID");
                    var folder = new FanmadeCategory { Server = e.Id, ServerName = e.Config.DisplayName, Id = category.Id, Title = category.Title, Genre = category.Genre };
                    foreach (var value in categoryCharts)
                    {
                        var chart = FanmadeChart.From(value, e.Id, e.SongIdOnly, e.CourseKeyedDifficulties);
                        if (!chart.IsPlayable || folder.ChartIds.Contains(chart.Id)) continue;
                        folder.ChartIds.Add(chart.Id);
                        if (!seen.Add(chart.Id)) continue;
                        chart.Category = category.Title; chart.Genre = category.Genre;
                        list.Add(chart);
                    }
                    folders.Add(folder);
                    lock (sync) catalogProgress = (catalogProgress.Item1 + 1, categories.Count);
                }
                lock (sync)
                {
                    e.Scores.Clear(); e.Best.Clear();
                    foreach (var score in scores) { e.Scores.Add(score); e.IndexScore(score); }
                    charts[e.Id] = list;
                    categoryLists[e.Id] = folders;
                    e.ChartCount = list.Count;
                    e.IsConnected = true;
                }
                Interlocked.Increment(ref revision);
                SetStatus(e.Config.DisplayName + ": " + list.Count + " songs ready" + (e.IsAuthenticated ? "" : " (guest; scores disabled)"));
            }
            catch (Exception error)
            {
                SetStatus(e.Config.DisplayName + ": " + error.Message);
                throw;
            }
            finally { e.Transport.Release(); }
            Update();
        }

        static bool ValidCategory(string id) => id.Length > 0 && id.Length <= 64 && id[0] >= 'a' && id[0] <= 'z'
            && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

        // The account's best on this chart version and course; null for a guest or a DOUBLE-only course.
        public FanmadeScore Best(FanmadeChart chart, int difficulty)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null || !chart.Difficulties[difficulty].Cloud) return null;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                return e != null && e.Best.TryGetValue(e.ScoreKey(chart.Id, chart.Version, chart.Difficulties[difficulty].Course), out var best) ? best : null;
            }
        }

        // Refreshes the chart's details (the author may have published a new version), then makes
        // sure the original TJA and audio are cached with matching SHA-256, and writes play.tja.
        // `progress` runs on worker threads.
        public async Task<(string Path, FanmadeChart Chart)> PrepareAsync(FanmadeChart selected, CancellationToken cancel = default,
            Action<DownloadProgress> progress = null)
        {
            var e = Endpoint(selected.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            if (e.ResourceDownloadVersion == 1) return await PrepareResourcesAsync(e, selected, cancel, progress);
            var snapshot = new DownloadProgress();
            void Publish() => progress?.Invoke(snapshot.Clone());
            Publish();
            await e.Transport.WaitAsync(cancel);
            try
            {
                SetStatus(e.Config.DisplayName + ": checking file hashes");
                var c = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + selected.Id, cancel: cancel)), selected.Server, e.SongIdOnly, e.CourseKeyedDifficulties);
                c.Category = selected.Category; c.Genre = selected.Genre;
                // A course picked in song select that the new version no longer has stops the load.
                if (!c.IsPlayable) throw new FanmadeException("CHART_NO_PLAYABLE_COURSE");
                string dir = Path.Combine(CacheRoot, "objects", c.Server, c.Id, e.SongIdOnly ? c.TjaHash + "-" + c.AudioHash : c.Version);
                string files = "/api/v1/charts/" + c.Id + (e.SongIdOnly ? "/" : "/versions/" + c.Version + "/");
                snapshot.Step = DownloadProgress.Stage.Files;

                async Task Ensure(string file, string digest, string kind, long limit, FileProgress transfer)
                {
                    if (cancel.IsCancellationRequested) throw new FanmadeException("DOWNLOAD_CANCELLED");
                    if (File.Exists(file))
                    {
                        transfer.Status = FileProgress.State.Verifying; Publish();
                        var cached = await CacheWork(() => File.ReadAllBytes(file), cancel);
                        if (await CacheWork(() => FanmadeEndpoint.Sha256Hex(cached), cancel) == digest)
                        {
                            transfer.Status = FileProgress.State.Cached; transfer.Received = transfer.Total = cached.Length; Publish();
                            return;
                        }
                    }
                    SetStatus(e.Config.DisplayName + ": downloading " + kind);
                    transfer.Status = FileProgress.State.Downloading; transfer.Received = transfer.Total = 0; Publish();
                    var bytes = await e.RequestBytesAsync(files + kind, limit: limit, cancel: cancel, progress: (received, total) =>
                    {
                        if (transfer.Received == received && transfer.Total == total) return;
                        transfer.Received = received; transfer.Total = total; Publish();
                    });
                    transfer.Status = FileProgress.State.Verifying; transfer.Received = transfer.Total = bytes.Length; Publish();
                    if (await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != digest) throw new FanmadeException("DOWNLOAD_HASH_MISMATCH");
                    await CacheWork(() => { WriteAtomic(file, bytes); return true; }, cancel);
                    transfer.Status = FileProgress.State.Complete; Publish();
                }

                await Ensure(Path.Combine(dir, "original.tja"), c.TjaHash, "tja", 4L * 1024 * 1024, snapshot.Chart);
                string audio = Path.Combine(dir, c.CachedAudioName), oldAudio = Path.Combine(dir, "audio.ogg");
                // Older clients stored MP3 bytes as .ogg; reuse only a verified cache.
                if (audio != oldAudio && !File.Exists(audio) && File.Exists(oldAudio)
                    && FanmadeEndpoint.Sha256Hex(File.ReadAllBytes(oldAudio)) == c.AudioHash)
                    File.Move(oldAudio, audio);
                await Ensure(audio, c.AudioHash, "audio", 256L * 1024 * 1024, snapshot.Audio);
                snapshot.Step = DownloadProgress.Stage.Preparing; Publish();
                string playable = Path.Combine(dir, "play.tja");
                string text = PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(Path.Combine(dir, "original.tja")), c.Encoding), c);
                WriteAtomic(playable, new UTF8Encoding(false).GetBytes(text));
                lock (sync)
                {
                    if (charts.TryGetValue(c.Server, out var list))
                    {
                        int index = list.FindIndex(x => x.Id == c.Id);
                        if (index >= 0) list[index] = c;
                    }
                }
                Interlocked.Increment(ref revision);
                SetStatus(e.Config.DisplayName + ": ready");
                snapshot.Step = DownloadProgress.Stage.Ready; Publish();
                return (playable, selected.SelectedPlayer.Length > 0 ? c.ForPlayer(selected.SelectedPlayer) : c);
            }
            finally { e.Transport.Release(); }
        }

        // Queues a finished play of a logged-in account, then sends it in the background. Guests,
        // DOUBLE-only courses and unknown charts are not uploaded. The request body and its
        // idempotency key are fixed when queued, so retries never create a second score.
        public bool Submit(FanmadeChart chart, int difficulty, FanmadeScore score, PlayRecord replay = null)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null || !chart.Difficulties[difficulty].Cloud) return false;
            var e = Endpoint(chart.Server);
            if (e == null || !e.IsConnected || !e.IsAuthenticated) return false;
            var body = new JObject
            {
                ["songId"] = chart.Id, ["versionId"] = chart.Version, ["difficulty"] = chart.Difficulties[difficulty].Course,
                ["good"] = score.Good, ["ok"] = score.Ok, ["bad"] = score.Bad, ["score"] = score.Score,
                ["drumroll"] = score.Drumroll, ["max_combo"] = score.MaxCombo,
                ["ClearStatus"] = score.ClearStatus,
            };
            if (e.SongIdOnly) body.Remove("versionId");
            if (e.ScoreReplayV1) body["replay_data"] = replay != null ? replay.ToJson() : JValue.CreateNull();
            try
            {
                UploadQueue.Enqueue(e.Id, RandomKey(), body.ToString(Newtonsoft.Json.Formatting.None), chart.TjaHash, chart.AudioHash);
                lock (sync) retryAt = DateTime.MinValue;
                Update();
                return true;
            }
            catch (Exception error) { SetStatus("Score queue error: " + error.Message); return false; }
        }

        // Called every frame: starts a background upload pass at most every 30 s and never waits for HTTP.
        public void Update()
        {
            lock (sync)
            {
                if (uploads != null && !uploads.IsCompleted) return;
                if (uploads != null && uploads.IsFaulted) status = "Score upload error: " + uploads.Exception?.GetBaseException().Message;
                var now = DateTime.UtcNow;
                if (endpoints.Count == 0 || now < retryAt) return;
                retryAt = now + RetryInterval;
#if UNITY_WEBGL && !UNITY_EDITOR
                uploads = DrainAsync();
#else
                uploads = Task.Run(DrainAsync);
#endif
            }
        }

        // Starts the next upload pass at once instead of after the 30 s retry interval.
        public void RetryNow()
        {
            lock (sync) retryAt = DateTime.MinValue;
            Update();
        }

        public Task WaitForUploadsAsync() => uploads ?? Task.CompletedTask;

        async Task DrainAsync()
        {
            foreach (var e in Endpoints)
            {
                if (!e.IsConnected || !e.IsAuthenticated) continue;
                foreach (var queued in UploadQueue.Pending(e.Id))
                {
                    try
                    {
                        string body = queued.Body;
                        FanmadeScore score;
                        await e.Transport.WaitAsync();
                        try
                        {
                            if (e.SongIdOnly)
                            {
                                var payload = Json.Parse(body);
                                if (!Json.HexId(queued.TjaHash ?? "", 64) || !Json.HexId(queued.AudioHash ?? "", 64))
                                { UploadQueue.Reject(queued.Key); continue; }
                                var current = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + Json.Str(payload, "songId"))), e.Id, true, e.CourseKeyedDifficulties);
                                if (e.ResourceDownloadVersion == 1)
                                {
                                    var manifest = await e.ResourcesAsync(current.Id);
                                    if (manifest.Tja.Hash != current.TjaHash || manifest.Audio.Hash != current.AudioHash)
                                        throw new FanmadeException("CHART_UPDATING");
                                }
                                if (current.TjaHash != queued.TjaHash || current.AudioHash != queued.AudioHash)
                                { UploadQueue.Reject(queued.Key); continue; }
                                if (payload.Remove("versionId"))
                                {
                                    body = payload.ToString(Newtonsoft.Json.Formatting.None);
                                    UploadQueue.UpdateBody(queued.Key, body);
                                }
                            }
                            score = FanmadeScore.From(Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", body, queued.Key)), e.SongIdOnly);
                        }
                        finally { e.Transport.Release(); }
                        lock (sync)
                        {
                            if (!e.Scores.Any(s => s.Id == score.Id)) e.Scores.Add(score);
                            e.IndexScore(score);
                            status = e.Config.DisplayName + ": score uploaded";
                        }
                        Interlocked.Increment(ref revision);
                        UploadQueue.Remove(queued.Key);
                    }
                    catch (HttpStatusException error)
                    {
                        SetStatus(e.Config.DisplayName + ": score pending (" + error.Message + ")");
                        // 409 (the chart changed version mid-play) and other permanent refusals are kept aside.
                        if (error.Status >= 400 && error.Status < 500 && error.Status != 401 && error.Status != 408 && error.Status != 429)
                        {
                            UploadQueue.Reject(queued.Key);
                            SetStatus(e.Config.DisplayName + ": score rejected (" + error.Message + "), saved locally");
                        }
                        else break;
                    }
                    catch (Exception error) { SetStatus(e.Config.DisplayName + ": score pending (" + error.Message + ")"); break; }
                }
            }
        }

        public int PendingCount(FanmadeEndpoint e) => UploadQueue.Pending(e.Id).Length;

        static string RandomKey()
        {
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return FanmadeEndpoint.Sha256Hex(bytes);
        }

        static Task<T> CacheWork<T>(Func<T> work, CancellationToken cancel)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            cancel.ThrowIfCancellationRequested();
            return Task.FromResult(work());
#else
            return Task.Run(work, cancel);
#endif
        }

        static void WriteAtomic(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() => Reset();
    }
}

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
    // Cache layout under the root: objects/<first two hex digits>/<sha256>, one verified file per
    // content hash (TJA, audio or preview), shared by every server, account and chart. Paths stay
    // short enough for Windows' 260-character limit. The playable TJA is never written to disk.
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
            UploadQueue = new PendingScoreQueue(databasePath ?? Path.Combine(CacheRoot, "scores.sqlite3"));
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
                ResetRankThresholds();
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
        // Only servers declaring the current game protocol are accepted.
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
                if (!SupportsProtocol(snapshot)) throw new FanmadeException("SERVER_PROTOCOL_UNSUPPORTED");
                if (!(snapshot["categories"] is JArray categoryList) || !(snapshot["scores"] is JArray scoreList))
                    throw new FanmadeException("API_BOOTSTRAP_INVALID");
                var categories = new List<(string Id, string Title, string Genre)>();
                foreach (var v in categoryList)
                {
                    var category = (Json.Str(v, "id"), Json.Str(v, "title"), Json.Str(v, "genre"));
                    if (!ValidCategory(category.Item1) || categories.Any(c => c.Id == category.Item1)) throw new FanmadeException("API_CATEGORY_INVALID");
                    Json.Number(v, "chartCount");
                    categories.Add(category);
                }
                var scores = new List<FanmadeScore>();
                if (e.IsAuthenticated) foreach (var v in scoreList) scores.Add(FanmadeScore.From(v));

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
                        var chart = FanmadeChart.From(value, e.Id);
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

        // Course-keyed difficulties, signed resource links and replay uploads.
        static bool SupportsProtocol(JObject snapshot) =>
            snapshot["courseKeyedDifficulties"]?.Type == JTokenType.Boolean && (bool)snapshot["courseKeyedDifficulties"]
            && snapshot["resourceDownloadVersion"]?.Type == JTokenType.Integer && (long)snapshot["resourceDownloadVersion"] == 1
            && snapshot["scoreReplayVersion"]?.Type == JTokenType.Integer && (long)snapshot["scoreReplayVersion"] == 1;

        static bool ValidCategory(string id) => id.Length > 0 && id.Length <= 64 && id[0] >= 'a' && id[0] <= 'z'
            && id.All(c => (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '-');

        // The account's best on this chart and course; null for a guest.
        public FanmadeScore Best(FanmadeChart chart, int difficulty)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null) return null;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                return e != null && e.Best.TryGetValue(FanmadeEndpoint.ScoreKey(chart.Id, chart.Difficulties[difficulty].Course), out var best) ? best : null;
            }
        }

        // Queues a finished play of a logged-in account, then sends it in the background. Guests
        // and unknown charts are not uploaded. The request body and its idempotency key are fixed
        // when queued, so retries never create a second score.
        public bool Submit(FanmadeChart chart, int difficulty, FanmadeScore score, PlayRecord replay = null)
        {
            if (chart == null || difficulty < 0 || difficulty >= 5 || chart.Difficulties[difficulty] == null) return false;
            var e = Endpoint(chart.Server);
            if (e == null || !e.IsConnected || !e.IsAuthenticated) return false;
            var body = new JObject
            {
                ["songId"] = chart.Id, ["difficulty"] = chart.Difficulties[difficulty].Course,
                ["good"] = score.Good, ["ok"] = score.Ok, ["bad"] = score.Bad, ["score"] = score.Score,
                ["drumroll"] = score.Drumroll, ["max_combo"] = score.MaxCombo,
                ["ClearStatus"] = score.ClearStatus,
                ["replay_data"] = replay != null ? replay.ToJson() : JValue.CreateNull(),
            };
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
                        FanmadeScore score;
                        await e.Transport.WaitAsync();
                        try
                        {
                            // A score belongs to the files it was played on; if they changed since, keep it aside.
                            if (!Json.HexId(queued.TjaHash ?? "", 64) || !Json.HexId(queued.AudioHash ?? "", 64))
                            { UploadQueue.Reject(queued.Key); continue; }
                            var current = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + Json.Str(Json.Parse(queued.Body), "songId"))), e.Id);
                            var manifest = await e.ResourcesAsync(current.Id);
                            if (manifest.Tja.Hash != current.TjaHash || manifest.Audio.Hash != current.AudioHash)
                                throw new FanmadeException("CHART_UPDATING");
                            if (current.TjaHash != queued.TjaHash || current.AudioHash != queued.AudioHash)
                            { UploadQueue.Reject(queued.Key); continue; }
                            score = FanmadeScore.From(Json.Parse(await e.AuthorizedAsync("/api/v1/game/scores", queued.Body, queued.Key)));
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
                        // 409 (the chart's files changed mid-play) and other permanent refusals are kept aside.
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

        // The cached file for a content hash (the manifest only accepts 64-digit hex hashes).
        string ObjectPath(string hash) => Path.Combine(CacheRoot, "objects", hash.Substring(0, 2), hash);

        static bool Matches(string path, FanmadeResource resource)
        {
            if (!File.Exists(path) || new FileInfo(path).Length != resource.Size) return false;
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == resource.Hash;
        }

        // Publishes verified bytes atomically. Two downloads of the same object can race (a preview
        // requested again before the first finished, or one file on two servers): when the other
        // one already published a matching file, or holds it open, that file is kept.
        static void StoreObject(string path, byte[] bytes, FanmadeResource resource)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".part";
            try
            {
                File.WriteAllBytes(temporary, bytes);
                try
                {
                    if (File.Exists(path)) File.Replace(temporary, path, null);
                    else File.Move(temporary, path);
                }
                catch (IOException) when (Matches(path, resource)) { }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        public void Dispose() => Reset();
    }
}

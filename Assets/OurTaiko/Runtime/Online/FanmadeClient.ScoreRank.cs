using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        readonly Dictionary<string, (Task<int[]> Task, DateTime RetryAt)> rankThresholds =
            new Dictionary<string, (Task<int[]>, DateTime)>();
        CancellationTokenSource rankCancellation = new CancellationTokenSource();

        // Polling from song select never blocks on disk, parsing, or the network. The blocks
        // and their content hashes are the cache identity, including DOUBLE sides.
        public int? RankThreshold(FanmadeChart chart, int difficulty)
        {
            if (chart == null || difficulty < 0 || difficulty >= chart.Difficulties.Length || chart.Difficulties[difficulty] == null) return null;
            string key = chart.Server + "/" + chart.Id + "/" + chart.BlocksKey + "/"
                + string.Join(",", chart.Difficulties.Select(d => d?.Name ?? ""));
            lock (sync)
            {
                if (!rankThresholds.TryGetValue(key, out var entry)
                    || entry.Task.IsCompleted && !entry.Task.IsCompletedSuccessfully && DateTime.UtcNow >= entry.RetryAt)
                {
                    _ = entry.Task?.Exception;
                    entry = (PrepareRankThresholdsAsync(chart, rankCancellation.Token), DateTime.UtcNow + RetryInterval);
                    rankThresholds[key] = entry;
                }
                if (entry.Task.IsCompletedSuccessfully) return entry.Task.Result[difficulty];
                if (entry.Task.IsFaulted) _ = entry.Task.Exception;
                return null;
            }
        }

        void ResetRankThresholds()
        {
            rankCancellation.Cancel(); rankCancellation.Dispose();
            rankCancellation = new CancellationTokenSource();
            rankThresholds.Clear();
        }

        // Fetch only the verified TJA. Reuse the same content-addressed cache and playable
        // course mapping as SongLoadingScene; never download a song's audio just for rank.
        public async Task<int[]> PrepareRankThresholdsAsync(FanmadeChart selected, CancellationToken cancel = default)
        {
            var endpoint = Endpoint(selected.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    // The chart must still be the one the catalog showed (same blocks, same content)
                    // before anything is downloaded.
                    var current = FanmadeChart.From(Json.Parse(await endpoint.AuthorizedAsync("/api/v1/charts/" + selected.Id, cancel: cancel)), endpoint.Id);
                    if (current.Id != selected.Id || current.BlocksKey != selected.BlocksKey) throw new FanmadeException("CHART_UPDATING");
                    var manifest = await endpoint.ResourcesAsync(selected.Id, cancel);
                    var resource = manifest.Tja;
                    string path = ObjectPath(resource.Hash);
                    try
                    {
                        if (!await CacheWork(() => Matches(path, resource), cancel))
                        {
                            var bytes = await endpoint.ResourceBytesAsync(resource, cancel);
                            if (bytes.LongLength != resource.Size || await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != resource.Hash)
                                throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
                            await CacheWork(() => { cancel.ThrowIfCancellationRequested(); StoreObject(path, bytes, resource); return true; }, cancel);
                        }
                        return await CacheWork(() =>
                        {
                            string text = PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(path)), selected);
                            return selected.Difficulties.Select(d =>
                            {
                                cancel.ThrowIfCancellationRequested();
                                return d == null ? 0 : ScoreRankUtil.KiwamiThreshold(TjaParser.Parse(text, d.Course, d.Side));
                            }).ToArray();
                        }, cancel);
                    }
                    catch (HttpStatusException error) when (attempt == 0 && (error.Status == 403 || error.Status == 404)) { }
                }
            }
            finally { endpoint.Transport.Release(); }
            throw new FanmadeException("RESOURCE_UNAVAILABLE");
        }
    }
}

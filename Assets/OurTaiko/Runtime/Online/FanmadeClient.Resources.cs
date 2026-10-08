using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        // Refreshes the chart's details (the author may have replaced its files), then makes sure
        // the TJA and audio from the signed resource links are cached with matching size and
        // SHA-256. Returns the playable TJA (built from the API metadata, kept in memory only) and
        // the audio object's path. `progress` runs on worker threads.
        public async Task<(string Tja, string AudioPath, FanmadeChart Chart)> PrepareAsync(FanmadeChart selected, CancellationToken cancel = default,
            Action<DownloadProgress> progress = null)
        {
            var e = Endpoint(selected.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            var state = new DownloadProgress();
            void Publish() { cancel.ThrowIfCancellationRequested(); progress?.Invoke(state.Clone()); }
            Publish();
            await e.Transport.WaitAsync(cancel);
            try
            {
                // One shared restart budget: mismatch, expiring link, 403/404 and transient failure.
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var c = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + selected.Id, cancel: cancel)), e.Id);
                    if (c.Id != selected.Id) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
                    c.Category = selected.Category; c.Genre = selected.Genre;
                    var manifest = await e.ResourcesAsync(c.Id, cancel);
                    if (c.TjaHash != manifest.Tja.Hash || c.AudioHash != manifest.Audio.Hash)
                    { if (attempt == 0) continue; throw new FanmadeException("CHART_UPDATING"); }
                    if (!c.IsPlayable) throw new FanmadeException("CHART_NO_PLAYABLE_COURSE");
                    c.AudioName = manifest.Audio.ContentType == "audio/ogg" ? "audio.ogg" : "audio.mp3";
                    async Task<string> Ensure(FanmadeResource resource, FileProgress transfer)
                    {
                        string file = ObjectPath(resource.Hash);
                        transfer.Status = FileProgress.State.Verifying; Publish();
                        if (await CacheWork(() => Matches(file, resource), cancel))
                        { transfer.Status = FileProgress.State.Cached; transfer.Received = transfer.Total = resource.Size; Publish(); return file; }
                        if (manifest.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(45)) throw new FanmadeException("RESOURCE_LINK_EXPIRED");
                        transfer.Status = FileProgress.State.Downloading; transfer.Total = resource.Size; transfer.Received = 0; Publish();
                        var bytes = await e.ResourceBytesAsync(resource, cancel, (received, total) => { transfer.Received = received; transfer.Total = total; Publish(); });
                        transfer.Status = FileProgress.State.Verifying; Publish();
                        if (bytes.LongLength != resource.Size || await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != resource.Hash)
                            throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
                        await CacheWork(() => { cancel.ThrowIfCancellationRequested(); StoreObject(file, bytes, resource); return true; }, cancel);
                        transfer.Status = FileProgress.State.Complete; Publish(); return file;
                    }
                    try
                    {
                        state.Step = DownloadProgress.Stage.Files; Publish();
                        string original = await Ensure(manifest.Tja, state.Chart);
                        string audio = await Ensure(manifest.Audio, state.Audio);
                        state.Step = DownloadProgress.Stage.Preparing; Publish();
                        // Rebuilt on every preparation: it depends on the API metadata, not only on the files.
                        string text = await CacheWork(() =>
                        {
                            cancel.ThrowIfCancellationRequested();
                            return PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(original), c.Encoding), c);
                        }, cancel);
                        lock (sync)
                        {
                            if (charts.TryGetValue(c.Server, out var list))
                            { int index = list.FindIndex(x => x.Id == c.Id); if (index >= 0) list[index] = c; }
                        }
                        Interlocked.Increment(ref revision);
                        state.Step = DownloadProgress.Stage.Ready; Publish(); SetStatus(e.Config.DisplayName + ": ready");
                        return (text, audio, selected.SelectedPlayer.Length > 0 ? c.ForPlayer(selected.SelectedPlayer) : c);
                    }
                    catch (HttpStatusException error) when (attempt == 0 && (error.Status == 403 || error.Status == 404 || error.Status >= 500)) { }
                    catch (FanmadeException error) when (attempt == 0 && (error.Message == "RESOURCE_LINK_EXPIRED" || error.Message == "NETWORK_TIMEOUT")) { }
                }
                throw new FanmadeException("RESOURCE_UNAVAILABLE");
            }
            finally { e.Transport.Release(); }
        }
    }
}

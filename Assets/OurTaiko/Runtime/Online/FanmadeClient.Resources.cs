using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        async Task<(string Path, FanmadeChart Chart)> PrepareResourcesAsync(FanmadeEndpoint e, FanmadeChart selected,
            CancellationToken cancel, Action<DownloadProgress> progress)
        {
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
                    var c = FanmadeChart.From(Json.Parse(await e.AuthorizedAsync("/api/v1/charts/" + selected.Id, cancel: cancel)), e.Id, e.SongIdOnly, e.CourseKeyedDifficulties);
                    if (c.Id != selected.Id) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
                    c.Category = selected.Category; c.Genre = selected.Genre;
                    var manifest = await e.ResourcesAsync(c.Id, cancel);
                    if (c.TjaHash != manifest.Tja.Hash || c.AudioHash != manifest.Audio.Hash)
                    { if (attempt == 0) continue; throw new FanmadeException("CHART_UPDATING"); }
                    if (!c.IsPlayable) throw new FanmadeException("CHART_NO_PLAYABLE_COURSE");
                    c.AudioName = manifest.Audio.ContentType == "audio/ogg" ? "audio.ogg" : "audio.mp3";
                    string root = Path.Combine(CacheRoot, "objects", e.Id, c.Id);
                    async Task<string> Ensure(FanmadeResource resource, string kind, string name, FileProgress transfer)
                    {
                        string file = Path.Combine(root, kind, resource.Hash, name);
                        transfer.Status = FileProgress.State.Verifying; Publish();
                        bool Matches(string candidate)
                        {
                            if (!File.Exists(candidate) || new FileInfo(candidate).Length != resource.Size) return false;
                            using var stream = File.OpenRead(candidate);
                            using var sha = System.Security.Cryptography.SHA256.Create();
                            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == resource.Hash;
                        }
                        if (await CacheWork(() => Matches(file), cancel))
                        { transfer.Status = FileProgress.State.Cached; transfer.Received = transfer.Total = resource.Size; Publish(); return file; }
                        // Older cache directories are only trusted after re-reading their actual bytes.
                        var legacy = await CacheWork(() =>
                        {
                            if (!Directory.Exists(root)) return null;
                            foreach (string directory in Directory.GetDirectories(root))
                            {
                                if (!Json.HexId(Path.GetFileName(directory), 32)) continue;
                                string candidate = Path.Combine(directory, name);
                                if (Matches(candidate)) return candidate;
                            }
                            return (string)null;
                        }, cancel);
                        if (legacy != null)
                        {
                            await CacheWork(() => { WriteAtomic(file, File.ReadAllBytes(legacy)); return true; }, cancel);
                            transfer.Status = FileProgress.State.Cached; transfer.Received = transfer.Total = resource.Size; Publish(); return file;
                        }
                        if (manifest.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(45)) throw new FanmadeException("RESOURCE_LINK_EXPIRED");
                        transfer.Status = FileProgress.State.Downloading; transfer.Total = resource.Size; transfer.Received = 0; Publish();
                        var bytes = await e.ResourceBytesAsync(resource, cancel, (received, total) => { transfer.Received = received; transfer.Total = total; Publish(); });
                        transfer.Status = FileProgress.State.Verifying; Publish();
                        if (bytes.LongLength != resource.Size || await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != resource.Hash)
                            throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
                        await CacheWork(() => { cancel.ThrowIfCancellationRequested(); WriteAtomic(file, bytes); return true; }, cancel);
                        transfer.Status = FileProgress.State.Complete; Publish(); return file;
                    }
                    try
                    {
                        state.Step = DownloadProgress.Stage.Files; Publish();
                        string original = await Ensure(manifest.Tja, "tja", "original.tja", state.Chart);
                        string audio = await Ensure(manifest.Audio, "audio", c.CachedAudioName, state.Audio);
                        state.Step = DownloadProgress.Stage.Preparing; Publish();
                        // Publish a complete, unique pair only after both content objects are verified.
                        string ready = Path.Combine(root, "plays", c.TjaHash + "-" + c.AudioHash);
                        string playable = Path.Combine(ready, "play.tja");
                        await CacheWork(() =>
                        {
                            cancel.ThrowIfCancellationRequested();
                            string text = PlayableTja.Build(PlayableTja.ToUtf8(File.ReadAllBytes(original), c.Encoding), c);
                            Directory.CreateDirectory(ready);
                            string destination = Path.Combine(ready, c.CachedAudioName);
                            // Verify the playable copy as well; a corrupt alias must never bypass hashing.
                            bool valid = false;
                            if (File.Exists(destination) && new FileInfo(destination).Length == manifest.Audio.Size)
                            {
                                using var stream = File.OpenRead(destination);
                                using var sha = System.Security.Cryptography.SHA256.Create();
                                valid = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() == c.AudioHash;
                            }
                            if (!valid)
                            {
                                string temporary = destination + ".part";
                                try
                                {
                                    File.Copy(audio, temporary, true);
                                    cancel.ThrowIfCancellationRequested();
                                    if (File.Exists(destination)) File.Replace(temporary, destination, null);
                                    else File.Move(temporary, destination);
                                }
                                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                            }
                            cancel.ThrowIfCancellationRequested();
                            WriteAtomic(playable, new UTF8Encoding(false).GetBytes(text));
                            return true;
                        }, cancel);
                        lock (sync)
                        {
                            if (charts.TryGetValue(c.Server, out var list))
                            { int index = list.FindIndex(x => x.Id == c.Id); if (index >= 0) list[index] = c; }
                        }
                        Interlocked.Increment(ref revision);
                        state.Step = DownloadProgress.Stage.Ready; Publish(); SetStatus(e.Config.DisplayName + ": ready");
                        return (playable, selected.SelectedPlayer.Length > 0 ? c.ForPlayer(selected.SelectedPlayer) : c);
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

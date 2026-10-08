using System;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeClient
    {
        // Signed links are refreshed on each selection; only verified content is cached.
        // Never construct a URL from the catalog's bucket key or forward API credentials.
        public async Task<string> PreparePreviewAsync(FanmadeChart selected, CancellationToken cancel = default)
        {
            var endpoint = Endpoint(selected.Server) ?? throw new FanmadeException("SERVER_NOT_CONNECTED");
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var manifest = await endpoint.ResourcesAsync(selected.Id, cancel);
                    var resource = manifest.Preview;
                    if (resource == null) throw new FanmadeException("PREVIEW_UNAVAILABLE");
                    string path = ObjectPath(resource.Hash);
                    try
                    {
                        bool valid = await CacheWork(() => Matches(path, resource), cancel);
                        if (valid) { cancel.ThrowIfCancellationRequested(); return path; }
                        if (manifest.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(45)) throw new FanmadeException("RESOURCE_LINK_EXPIRED");
                        var bytes = await endpoint.ResourceBytesAsync(resource, cancel);
                        if (bytes.LongLength != resource.Size || await CacheWork(() => FanmadeEndpoint.Sha256Hex(bytes), cancel) != resource.Hash)
                            throw new FanmadeException("DOWNLOAD_INTEGRITY_FAILED");
                        await CacheWork(() => { cancel.ThrowIfCancellationRequested(); StoreObject(path, bytes, resource); return true; }, cancel);
                        cancel.ThrowIfCancellationRequested();
                        return path;
                    }
                    catch (HttpStatusException e) when (attempt == 0 && (e.Status == 403 || e.Status == 404)) { }
                    catch (FanmadeException e) when (attempt == 0 && e.Message == "RESOURCE_LINK_EXPIRED") { }
                }
            }
            finally { endpoint.Transport.Release(); }
            throw new FanmadeException("PREVIEW_UNAVAILABLE");
        }
    }
}

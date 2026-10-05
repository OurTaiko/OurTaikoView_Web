using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed class FanmadeResource
    {
        public string Url, HeadUrl, Hash, ContentType;
        public long Size;
    }

    public sealed class FanmadeResources
    {
        public DateTimeOffset ExpiresAt;
        public FanmadeResource Tja, Audio, Preview;
        public static FanmadeResources Parse(JObject value, string chartId, Action<string> validateUrl)
        {
            if (Json.Str(value, "chartId") != chartId) throw new FanmadeException("RESOURCE_CHART_MISMATCH");
            if (!DateTimeOffset.TryParse(Json.Str(value, "expiresAt"), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var expires)) throw new FanmadeException("RESOURCE_EXPIRY_INVALID");
            if (!(value["resources"] is JObject resources)) throw new FanmadeException("RESOURCE_MANIFEST_INVALID");
            FanmadeResource Read(string kind, long limit)
            {
                var item = resources[kind] as JObject ?? throw new FanmadeException("RESOURCE_MISSING");
                var r = new FanmadeResource { Url = Json.Str(item, "url"), HeadUrl = Json.Str(item, "headUrl"),
                    Hash = Json.Str(item, "sha256"), ContentType = Json.Str(item, "contentType"), Size = Json.Number(item, "size") };
                if (!Json.HexId(r.Hash, 64) || r.Size <= 0 || r.Size > limit) throw new FanmadeException("RESOURCE_METADATA_INVALID");
                validateUrl(r.Url); validateUrl(r.HeadUrl);
                return r;
            }
            var result = new FanmadeResources { ExpiresAt = expires, Tja = Read("tja", 4L * 1024 * 1024), Audio = Read("audio", 256L * 1024 * 1024) };
            if (result.Audio.ContentType != "audio/ogg" && result.Audio.ContentType != "audio/mpeg") throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            if (resources["preview"] != null) {
                result.Preview = Read("preview", 32L * 1024 * 1024);
                if (result.Preview.ContentType != "audio/ogg") throw new FanmadeException("API_AUDIO_FORMAT_UNSUPPORTED");
            }
            return result;
        }
    }

    public sealed partial class FanmadeEndpoint
    {
        public void ValidateResourceUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new FanmadeException("RESOURCE_URL_INVALID");
            if (uri.Scheme == "https") return;
#if UNITY_INCLUDE_TESTS
            if (AllowLoopbackResourcesForTests && uri.Scheme == "http" && uri.IsLoopback) return;
#endif
            throw new FanmadeException("RESOURCE_URL_INVALID");
        }
        public async Task<FanmadeResources> ResourcesAsync(string chartId, CancellationToken cancel = default)
        {
            if (!Json.HexId(chartId, 32)) throw new FanmadeException("API_ID_INVALID");
            return FanmadeResources.Parse(Json.Parse(await AuthorizedAsync("/api/v1/charts/" + chartId + "/resources", cancel: cancel)), chartId, ValidateResourceUrl);
        }
    }
}

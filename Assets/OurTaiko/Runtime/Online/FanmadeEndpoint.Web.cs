#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeEndpoint
    {
        async Task<byte[]> RequestWebBytesAsync(string path, string body, string key, long limit,
            CancellationToken cancel, Action<long, long> progress, bool external = false)
        {
            if (disposed || cancel.IsCancellationRequested) throw new FanmadeException("DOWNLOAD_CANCELLED");
            // Browser fetch owns TLS, proxy selection and CORS. Never block its main thread.
            using var request = new UnityWebRequest(external ? path : Config.baseUrl + path, string.IsNullOrEmpty(body) ? "GET" : "POST");
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Accept", "application/json");
            if (!external && !string.IsNullOrEmpty(token)) request.SetRequestHeader("Authorization", "Bearer " + token);
            if (!external && !string.IsNullOrEmpty(key)) request.SetRequestHeader("Idempotency-Key", key);
            if (!string.IsNullOrEmpty(body))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.redirectLimit = 0;
            double deadline = GameTimeline.Realtime + (external || path.Contains("/versions/") ? 120 : 15);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                if (disposed || cancel.IsCancellationRequested)
                {
                    request.Abort();
                    throw new FanmadeException("DOWNLOAD_CANCELLED");
                }
                if (GameTimeline.Realtime > deadline)
                {
                    request.Abort();
                    throw new FanmadeException("NETWORK_TIMEOUT");
                }
                long.TryParse(request.GetResponseHeader("Content-Length"), out long total);
                long received = (long)request.downloadedBytes;
                if (received > limit || total > limit)
                {
                    request.Abort();
                    throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                }
                progress?.Invoke(received, total);
                await Task.Yield();
            }
            if (disposed || cancel.IsCancellationRequested) throw new FanmadeException("DOWNLOAD_CANCELLED");
            int status = (int)request.responseCode;
            if (external && status != 200) throw new HttpStatusException(status);
            if (status > 0 && (status < 200 || status >= 300)) throw new HttpStatusException(status);
            if (request.result != UnityWebRequest.Result.Success) throw new FanmadeException("NETWORK_ERROR");
            var bytes = request.downloadHandler.data;
            if (bytes.LongLength > limit) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
            progress?.Invoke(bytes.LongLength, bytes.LongLength);
            return bytes;
        }
    }
}
#endif

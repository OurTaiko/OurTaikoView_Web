using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace OurTaiko.Online
{
    public sealed partial class FanmadeEndpoint
    {
        // No API credentials, cookies, login recovery or redirects on the resource channel.
        public async Task<byte[]> ResourceBytesAsync(FanmadeResource resource, CancellationToken cancel,
            Action<long, long> progress = null)
        {
            ValidateResourceUrl(resource.Url);
#if UNITY_WEBGL && !UNITY_EDITOR
            return await RequestWebBytesAsync(resource.Url, "", "", resource.Size, cancel, progress, external: true);
#else
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(HttpMethod.Get, resource.Url);
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                if ((int)response.StatusCode != 200) throw new HttpStatusException((int)response.StatusCode);
                long? length = response.Content.Headers.ContentLength;
                if (length.HasValue && length.Value != resource.Size) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                // The manifest size has already passed the business limit. One bounded buffer.
                var bytes = new byte[checked((int)resource.Size)];
                using var stream = await response.Content.ReadAsStreamAsync();
                int received = 0;
                while (received < bytes.Length)
                {
                    int read = await stream.ReadAsync(bytes, received, Math.Min(81920, bytes.Length - received), linked.Token);
                    if (read == 0) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                    received += read; progress?.Invoke(received, resource.Size);
                }
                if (await stream.ReadAsync(new byte[1], 0, 1, linked.Token) != 0) throw new FanmadeException("DOWNLOAD_SIZE_MISMATCH");
                return bytes;
            }
            catch (OperationCanceledException) { throw new FanmadeException(cancel.IsCancellationRequested ? "DOWNLOAD_CANCELLED" : "NETWORK_TIMEOUT"); }
            // Do not retain transport exceptions: their messages may contain signed URLs.
            catch (HttpRequestException error) { throw new FanmadeException(NetworkCode(error)); }
            catch (IOException error) { throw new FanmadeException(NetworkCode(error)); }
#endif
        }
    }
}

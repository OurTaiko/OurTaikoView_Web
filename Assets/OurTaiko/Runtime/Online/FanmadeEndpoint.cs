using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    // One configured server and account (fanmade.cpp Endpoint + request/login/authorized), on
    // System.Net.Http as in MajdataPlay. Redirects are refused, TLS is verified, an empty proxy is
    // an explicit direct connection, and the bearer token lives only in memory.
    public sealed partial class FanmadeEndpoint : IDisposable
    {
        public const long DefaultLimit = 64L * 1024 * 1024;
        public readonly ServerConfig Config;
        // sha256(base_url + "\n" + username): accounts on one server keep separate caches and queues.
        public readonly string Id;
        public bool IsConnected { get; internal set; }
        public bool IsAuthenticated => authenticated;
        public int ChartCount { get; internal set; } = -1;
        public string Nickname { get; private set; } = "";
        // Serializes HTTP per endpoint, like the C++ http_mutex.
        internal readonly SemaphoreSlim Transport = new SemaphoreSlim(1, 1);

#if UNITY_WEBGL && !UNITY_EDITOR
        bool disposed;
#else
        readonly HttpClient http;
#endif
        volatile bool authenticated;
        string token = "";

        public FanmadeEndpoint(ServerConfig config)
        {
            Config = config.Clone();
            Config.baseUrl = (Config.baseUrl ?? "").TrimEnd('/');
            Config.username ??= ""; Config.password ??= ""; Config.httpProxy ??= "";
            Id = Sha256Hex(Encoding.UTF8.GetBytes(Config.baseUrl + "\n" + Config.username));
#if !UNITY_WEBGL || UNITY_EDITOR
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            if (string.IsNullOrEmpty(Config.httpProxy)) handler.UseProxy = false;
            else { handler.UseProxy = true; handler.Proxy = new WebProxy(new Uri(Config.httpProxy)); }
            // Per-request timeouts below; the client-wide one only bounds pathological cases.
            http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
#endif
        }

        public void Dispose()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            disposed = true;
#else
            http.Dispose();
#endif
        }

        public static string Sha256Hex(byte[] bytes)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(bytes);
            var hex = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) hex.Append(b.ToString("x2"));
            return hex.ToString();
        }

        public bool HasValidUrl()
        {
            string url = Config.baseUrl;
            return (url.StartsWith("http://", StringComparison.Ordinal) || url.StartsWith("https://", StringComparison.Ordinal))
                && url.IndexOfAny(new[] { '?', '#', '@', ' ', '\r', '\n' }) < 0 && Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        // Empty credentials stay a guest without a request. Rejected credentials (401/403) leave
        // guest mode; other failures keep the account's pending scores eligible for retry.
        public async Task LoginAsync(CancellationToken cancel = default)
        {
            token = "";
            if (!Config.HasCredentials) { authenticated = false; return; }
            var body = new JObject { ["username"] = Config.username, ["password"] = Config.password };
            try
            {
                var reply = Json.Parse(await RequestAsync("/api/v1/game/login", body.ToString(Newtonsoft.Json.Formatting.None), cancel: cancel));
                string accessToken = Json.Str(reply, "accessToken");
                if (!Json.HexId(accessToken, 64)) throw new FanmadeException("API_TOKEN_INVALID");
                token = accessToken;
                authenticated = true;
                var user = reply["user"] as JObject;
                string nickname = user?["nickname"]?.Type == JTokenType.String ? (string)user["nickname"] : "";
                Nickname = nickname.Length > 0 ? nickname : Config.username;
            }
            catch (HttpStatusException error)
            {
                if (error.Status == 401 || error.Status == 403) authenticated = false;
                throw;
            }
        }

        public void BecomeGuest() { token = ""; authenticated = false; }

        // An expired token logs in once more and repeats the request.
        public async Task<string> AuthorizedAsync(string path, string body = "", string key = "", CancellationToken cancel = default)
        {
            try { return await RequestAsync(path, body, key, cancel: cancel); }
            catch (HttpStatusException error) when (error.Status == 401 && authenticated)
            {
                await LoginAsync(cancel);
                return await RequestAsync(path, body, key, cancel: cancel);
            }
        }

        public async Task<string> RequestAsync(string path, string body = "", string key = "", long limit = DefaultLimit,
            CancellationToken cancel = default, Action<long, long> progress = null)
        {
            var bytes = await RequestBytesAsync(path, body, key, limit, cancel, progress);
            return new UTF8Encoding(false).GetString(bytes);
        }

        public async Task<byte[]> RequestBytesAsync(string path, string body = "", string key = "", long limit = DefaultLimit,
            CancellationToken cancel = default, Action<long, long> progress = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return await RequestWebBytesAsync(path, body, key, limit, cancel, progress);
#else
            if (cancel.IsCancellationRequested) throw new FanmadeException("DOWNLOAD_CANCELLED");
            // Files come from signed resource links (ResourceBytesAsync); API calls are small.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancel, timeout.Token);
            using var request = new HttpRequestMessage(string.IsNullOrEmpty(body) ? HttpMethod.Get : HttpMethod.Post, Config.baseUrl + path);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (!string.IsNullOrEmpty(key)) request.Headers.TryAddWithoutValidation("Idempotency-Key", key);
            if (!string.IsNullOrEmpty(body)) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            try
            {
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linked.Token);
                int status = (int)response.StatusCode;
                if (status < 200 || status >= 300) throw new HttpStatusException(status);
                long total = response.Content.Headers.ContentLength ?? 0;
                if (total > limit) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[81920];
                var output = new MemoryStream(total > 0 ? (int)Math.Min(total, int.MaxValue) : 0);
                progress?.Invoke(0, total);
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, linked.Token);
                    if (read == 0) break;
                    if (read > limit - output.Length) throw new FanmadeException("RESPONSE_SIZE_LIMIT_EXCEEDED");
                    output.Write(buffer, 0, read);
                    progress?.Invoke(output.Length, total);
                }
                return output.ToArray();
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw new FanmadeException("DOWNLOAD_CANCELLED"); }
            catch (OperationCanceledException error) { throw new FanmadeException("NETWORK_TIMEOUT", error); }
            catch (HttpRequestException error) { throw new FanmadeException(NetworkCode(error), error); }
            catch (IOException error) { throw new FanmadeException(NetworkCode(error), error); }
#endif
        }

        // The distinct DNS / connect / TLS codes OurTaikoPlayer shows instead of one network error.
        static string NetworkCode(Exception error)
        {
            for (var e = error; e != null; e = e.InnerException)
            {
                if (e is AuthenticationException) return "TLS_CERTIFICATE_VERIFY_FAILED";
                if (e is SocketException socket)
                    switch (socket.SocketErrorCode)
                    {
                        case SocketError.HostNotFound: case SocketError.NoData: case SocketError.TryAgain: return "NETWORK_DNS_ERROR";
                        case SocketError.ConnectionRefused: case SocketError.NetworkUnreachable: case SocketError.HostUnreachable: return "NETWORK_CONNECT_ERROR";
                        case SocketError.TimedOut: return "NETWORK_TIMEOUT";
                    }
                if (e is WebException web)
                    switch (web.Status)
                    {
                        case WebExceptionStatus.NameResolutionFailure: return "NETWORK_DNS_ERROR";
                        case WebExceptionStatus.ProxyNameResolutionFailure: return "NETWORK_PROXY_DNS_ERROR";
                        case WebExceptionStatus.ConnectFailure: return "NETWORK_CONNECT_ERROR";
                        case WebExceptionStatus.Timeout: return "NETWORK_TIMEOUT";
                        case WebExceptionStatus.TrustFailure: return "TLS_CERTIFICATE_VERIFY_FAILED";
                        case WebExceptionStatus.SecureChannelFailure: return "TLS_HANDSHAKE_FAILED";
                    }
            }
            return "NETWORK_ERROR";
        }

        // Account scores of the bootstrap, kept only for a logged-in account.
        internal readonly List<FanmadeScore> Scores = new List<FanmadeScore>();
        internal readonly Dictionary<string, FanmadeScore> Best = new Dictionary<string, FanmadeScore>();
        internal static string ScoreKey(string song, string difficulty) => song + "/" + difficulty;

        internal void IndexScore(FanmadeScore score)
        {
            var key = ScoreKey(score.Song, score.Difficulty);
            if (!Best.TryGetValue(key, out var best) || score.Score > best.Score) Best[key] = score;
        }
    }
}

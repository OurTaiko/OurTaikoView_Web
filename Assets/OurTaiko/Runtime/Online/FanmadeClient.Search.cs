using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace OurTaiko.Online
{
    public sealed class FanmadeSearchResult
    {
        public FanmadeEndpoint Endpoint;
        public readonly List<FanmadeChart> Charts = new List<FanmadeChart>();
        public string Error;
    }

    public sealed partial class FanmadeClient
    {
        // Endpoints run concurrently; one failure never discards the other servers' results.
        public Task<FanmadeSearchResult[]> SearchAsync(SongSearchQuery query, CancellationToken cancel = default)
            => Task.WhenAll(Endpoints.Where(e => e.IsConnected).Select(e => SearchEndpointAsync(e, query, cancel)));

        async Task<FanmadeSearchResult> SearchEndpointAsync(FanmadeEndpoint endpoint, SongSearchQuery query, CancellationToken cancel)
        {
            var result = new FanmadeSearchResult { Endpoint = endpoint };
            await endpoint.Transport.WaitAsync(cancel);
            try
            {
                // A difficulty in the UI covers all its sides, as the server's course filter does.
                string path = "/api/v1/game/search?q=" + Uri.EscapeDataString(query.Keyword) + "&order=" + query.ApiOrder;
                if (query.Difficulty.HasValue) path += "&course=" + SongInfo.CourseName(query.Difficulty.Value);
                if (query.Level > 0) path += "&level=" + query.Level;
                var reply = Json.Parse(await endpoint.AuthorizedAsync(path, cancel: cancel));
                if (!(reply["items"] is JArray items) || Json.Number(reply, "total") != items.Count)
                    throw new FanmadeException("API_SEARCH_INVALID");
                var seen = new HashSet<string>();
                foreach (var value in items)
                {
                    var chart = FanmadeChart.From(value, endpoint.Id);
                    if (!chart.IsPlayable || !seen.Add(chart.Id)) continue;
                    var known = Chart(endpoint.Id, chart.Id);
                    chart.Category = known?.Category ?? ""; chart.Genre = known?.Genre ?? "";
                    result.Charts.Add(chart);
                }
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested) { throw; }
            catch (Exception error) { result.Error = error.Message; result.Charts.Clear(); }
            finally { endpoint.Transport.Release(); }
            return result;
        }

        // Any qualifying play counts, even when a higher-scoring non-FC is the displayed best.
        public bool SearchNeedsClear(FanmadeChart chart, SongSearchQuery query)
        {
            if (query.Order == SongSearchOrder.Default) return false;
            lock (sync)
            {
                var e = endpoints.Find(x => x.Id == chart.Server);
                if (e == null || !e.IsAuthenticated) return false;
                return chart.ToSongInfo(null).Courses().Where(query.Matches).Any(course =>
                {
                    // Plays of replaced files count only while the block has no play of the current ones.
                    string block = chart.Difficulties[(int)course.Difficulty].Name;
                    var plays = e.Scores.Where(s => s.Song == chart.Id && s.Difficulty == block).ToList();
                    if (plays.Any(s => s.IsLatest)) plays.RemoveAll(s => !s.IsLatest);
                    return !plays.Any(s => s.Bad == 0 && (s.Good > 0 || s.Ok > 0) && (query.Order == SongSearchOrder.UnFullCombo || s.Ok == 0));
                });
            }
        }
    }
}

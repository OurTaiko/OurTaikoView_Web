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
                // A difficulty in the UI includes both players of double charts.
                string course = query.Difficulty.HasValue ? SongInfo.CourseName(query.Difficulty.Value) : "";
                var courses = course.Length == 0 ? new[] { "" } : new[] { course, course + "_1p", course + "_2p" };
                var seen = new HashSet<string>();
                foreach (var name in courses)
                {
                    string path = "/api/v1/game/search?q=" + Uri.EscapeDataString(query.Keyword) + "&order=" + query.ApiOrder;
                    if (name.Length > 0) path += "&course=" + name;
                    if (query.Level > 0) path += "&level=" + query.Level;
                    var reply = Json.Parse(await endpoint.AuthorizedAsync(path, cancel: cancel));
                    if (!(reply["items"] is JArray items) || Json.Number(reply, "total") != items.Count)
                        throw new FanmadeException("API_SEARCH_INVALID");
                    foreach (var value in items)
                    {
                        var chart = FanmadeChart.From(value, endpoint.Id);
                        if (!chart.IsPlayable || !seen.Add(chart.Id)) continue;
                        var known = Chart(endpoint.Id, chart.Id);
                        chart.Category = known?.Category ?? ""; chart.Genre = known?.Genre ?? "";
                        result.Charts.Add(chart);
                    }
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
                return chart.ToSongInfo(null).Courses.Where(query.Matches).Any(course =>
                    !e.Scores.Any(s => s.Song == chart.Id && s.Difficulty == course.Course && s.Bad == 0 && (s.Good > 0 || s.Ok > 0)
                        && (query.Order == SongSearchOrder.UnFullCombo || s.Ok == 0)));
            }
        }
    }
}

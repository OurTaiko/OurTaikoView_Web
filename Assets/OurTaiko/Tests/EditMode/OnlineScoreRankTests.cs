using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    public sealed class OnlineScoreRankTests
    {
        FanmadeFixture api;
        FanmadeClient client;
        FanmadeChart selected;
        string cache;

        [SetUp] public void Setup()
        {
            api = new FanmadeFixture();
            var chart = new FanmadeFixture.Chart { Tja = Encoding.UTF8.GetBytes(
                "BPM:120\nCOURSE:Easy\n#START\n1111,\n#END\nCOURSE:Oni\n#START\n1110,\n#END"), Audio = new byte[100] };
            chart.Difficulties.Add(("Easy", 1, ""));
            api.Charts.Add(chart);
            cache = Path.Combine(Path.GetTempPath(), "rank-test-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
            var endpoint = client.Add(api.Server());
            Task.Run(() => client.ConnectAsync(endpoint, true)).GetAwaiter().GetResult();
            selected = client.Charts[0];
        }

        [TearDown] public void Cleanup()
        {
            client.Dispose(); api.Dispose();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }

        int[] Load(FanmadeChart chart) => Task.Run(() => client.PrepareRankThresholdsAsync(chart)).GetAwaiter().GetResult();

        [Test] public void LoadsEveryCourseUsingOnlyVerifiedChartAndReusesDiskCache()
        {
            var thresholds = Load(selected);
            Assert.That(thresholds[0], Is.EqualTo(1000000));
            Assert.That(thresholds[3], Is.EqualTo(1000020));
            Assert.That(ScoreRankUtil.FromScore(1000000, thresholds[3]), Is.EqualTo(ScoreRank.PurpleMiyabi));
            Assert.That(Load(selected), Is.EqualTo(thresholds));
            Assert.That(api.Downloads, Is.EqualTo(1));
            Assert.That(api.Requests.Any(r => r.Contains("/audio")), Is.False);
        }

        [Test] public void PollingDeduplicatesRequestsAndChangedHashInvalidatesTheRules()
        {
            int Wait(FanmadeChart chart) => Task.Run(async () =>
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                int? value;
                while (!(value = client.RankThreshold(chart, 3)).HasValue)
                {
                    if (DateTime.UtcNow > deadline) throw new TimeoutException();
                    await Task.Delay(10);
                }
                return value.Value;
            }).GetAwaiter().GetResult();
            Assert.That(Wait(selected), Is.EqualTo(1000020));
            Assert.That(Wait(selected), Is.EqualTo(1000020));
            Assert.That(api.Downloads, Is.EqualTo(1));
            api.Charts[0].Tja = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(api.Charts[0].Tja).Replace("1110", "1111"));
            var updated = FanmadeChart.From(api.Charts[0].ToJson(), selected.Server);
            Assert.That(Wait(updated), Is.EqualTo(1000000));
            Assert.That(api.Downloads, Is.EqualTo(2));
        }

        [Test] public void StaleCatalogHashDoesNotProduceAnIncorrectRank()
        {
            api.Charts[0].Tja = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(api.Charts[0].Tja).Replace("1110", "1111"));
            Assert.That(Assert.Throws<FanmadeException>(() => Load(selected)).Message, Is.EqualTo("CHART_UPDATING"));
            Assert.That(api.Downloads, Is.Zero);
        }

        [Test] public void CorruptedCachedChartIsVerifiedAndDownloadedAgain()
        {
            Load(selected);
            string path = Path.Combine(cache, "objects", selected.TjaHash.Substring(0, 2), selected.TjaHash);
            var bytes = File.ReadAllBytes(path); bytes[0] ^= 1; File.WriteAllBytes(path, bytes);
            Assert.That(Load(selected)[3], Is.EqualTo(1000020));
            Assert.That(api.Downloads, Is.EqualTo(2));
        }

        [Test] public void DoublePlayerCoursesDoNotShareTheWrongThreshold()
        {
            api.Charts[0].Difficulties.Clear();
            api.Charts[0].Difficulties.Add(("Oni", 8, "P1")); api.Charts[0].Difficulties.Add(("Oni", 8, "P2"));
            api.Charts[0].Tja = Encoding.UTF8.GetBytes("BPM:120\nCOURSE:Oni\nSTYLE:Double\n#START P1\n1110,\n#END\n#START P2\n1111,\n#END");
            var chart = FanmadeChart.From(api.Charts[0].ToJson(), selected.Server);
            Assert.That(Load(chart.ForPlayer("P1"))[3], Is.EqualTo(1000020));
            Assert.That(Load(chart.ForPlayer("P2"))[3], Is.EqualTo(1000000));
        }
    }
}

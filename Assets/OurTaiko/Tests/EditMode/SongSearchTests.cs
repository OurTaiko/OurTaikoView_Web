using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using OurTaiko.Online;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class SongSearchTests
    {
        [Test]
        public void LocalKeywordsIncludeEveryTranslationAndMakerButNotChartCommands()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Original\nTITLEJA:夜の歌\nSUBTITLEZH:中文副标题\nMAKER:Alice\nCOURSE:Easy\nLEVEL:3\n#START\n1000,\n#END\nCOURSE:Oni\nLEVEL:8\n#START\n#SCROLL 123\n1000,\n#END");
            try
            {
                foreach (var word in new[] { "original", "夜の", "中文", "ALICE" }) Assert.That(new SongSearchQuery(word).MatchesLocal(song), Is.True, word);
                Assert.That(new SongSearchQuery("SCROLL").MatchesLocal(song), Is.False);
                Assert.That(new SongSearchQuery("", Difficulty.Easy, 8).MatchesLocal(song), Is.False, "Difficulty and stars must be from the same course.");
                Assert.That(new SongSearchQuery("", Difficulty.Oni, 8).MatchesLocal(song), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(song.chart); UnityEngine.Object.DestroyImmediate(song); }
        }

        [Test]
        public void KeywordLimitCountsUtf8Bytes()
        {
            Assert.DoesNotThrow(() => new SongSearchQuery(new string('歌', 66)));
            Assert.Throws<ArgumentException>(() => new SongSearchQuery(new string('歌', 67)));
            Assert.That(new SongSearchQuery(" a%b_ ").MatchesText("Axxb1"), Is.True);
            Assert.That(new SongSearchQuery(@"100\%").MatchesText("100 percent"), Is.False);
            Assert.That(new SongSearchQuery(@"100\%").MatchesText("100% complete"), Is.True);
            Assert.That(new SongSearchQuery(@"tail\").MatchesText("tail%"), Is.True, "Match the backend wildcard wrapper even for a trailing escape.");
        }

        [Test]
        public void SearchQueriesEveryServerKeepsDuplicateIdsAcrossServersAndReportsPartialFailure()
        {
            using var a = new FanmadeFixture(); using var b = new FanmadeFixture();
            string cache = Path.Combine(Path.GetTempPath(), "ourtaiko-search-" + Guid.NewGuid().ToString("N"));
            using var client = new FanmadeClient(cache);
            var first = new FanmadeFixture.Chart { Title = "夜 & ? song" };
            a.Charts.Add(first); b.Charts.Add(new FanmadeFixture.Chart { Id = first.Id, Title = first.Title });
            try
            {
                Task.Run(async () =>
                {
                    await client.ConnectAsync(client.Add(a.Server()), true);
                    await client.ConnectAsync(client.Add(b.Server()), true);
                    var results = await client.SearchAsync(new SongSearchQuery("夜 & ?", Difficulty.Oni, 8));
                    Assert.That(results.Length, Is.EqualTo(2)); Assert.That(results.All(r => r.Error == null && r.Charts.Count == 1), Is.True);
                    Assert.That(results[0].Charts[0].Server, Is.Not.EqualTo(results[1].Charts[0].Server));
                    b.Fail["/api/v1/game/search"] = 500;
                    results = await client.SearchAsync(new SongSearchQuery("夜"));
                    Assert.That(results[0].Charts.Count, Is.EqualTo(1)); Assert.That(results[1].Error, Is.EqualTo("HTTP_500"));
                    using var cancel = new CancellationTokenSource(); cancel.Cancel();
                    Assert.CatchAsync<OperationCanceledException>(async () => await client.SearchAsync(new SongSearchQuery(), cancel.Token));
                }).GetAwaiter().GetResult();
            }
            finally { client.Dispose(); Directory.Delete(cache, true); }
        }

        [Test]
        public void DifficultySearchIncludesDoubleCoursesAndUsesAnyQualifyingScore()
        {
            using var fixture = new FanmadeFixture();
            string cache = Path.Combine(Path.GetTempPath(), "ourtaiko-search-" + Guid.NewGuid().ToString("N"));
            using var client = new FanmadeClient(cache);
            var chart = new FanmadeFixture.Chart { Title = "Double", Difficulties = { } };
            chart.Difficulties.Clear(); chart.Difficulties.Add(("Oni", 8, "P1")); chart.Difficulties.Add(("Hard", 6, "P2"));
            fixture.Charts.Add(chart);
            fixture.AddAccountScore("don", chart, "Oni_1p", 1000, 2);
            fixture.AccountScores[0]["good"] = 10; fixture.AccountScores[0]["ok"] = 1; fixture.AccountScores[0]["bad"] = 0;
            fixture.AddAccountScore("don", chart, "Oni_1p", 2000, 1);
            fixture.AccountScores[1]["bad"] = 1;
            try
            {
                Task.Run(async () =>
                {
                    await client.ConnectAsync(client.Add(fixture.Server("don", "katsu")), false);
                    var query = new SongSearchQuery("", Difficulty.Oni, 8, SongSearchOrder.UnFullCombo);
                    var reply = (await client.SearchAsync(query)).Single();
                    Assert.That(reply.Error, Is.Null); Assert.That(reply.Charts.Count, Is.EqualTo(1));
                    var p1 = reply.Charts[0].ForPlayer("P1"); var p2 = reply.Charts[0].ForPlayer("P2");
                    Assert.That(query.Matches(p1.ToSongInfo(null)), Is.True); Assert.That(query.Matches(p2.ToSongInfo(null)), Is.False);
                    Assert.That(client.SearchNeedsClear(p1, query), Is.False, "Earlier FC counts even though the highest score has a miss.");
                    Assert.That(client.SearchNeedsClear(p1, new SongSearchQuery("", Difficulty.Oni, 8, SongSearchOrder.UnPerfect)), Is.True);
                }).GetAwaiter().GetResult();
            }
            finally { client.Dispose(); Directory.Delete(cache, true); }
        }
    }
}

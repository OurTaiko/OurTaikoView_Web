using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using OurTaiko.Online;

namespace OurTaiko.Tests
{
    // FanmadeClient against the local fixture API, after OurTaikoPlayer's tests/fanmade/client.cpp.
    public sealed class FanmadeClientTests
    {
        FanmadeFixture fixture;
        FanmadeClient client;
        string cache;
        FanmadeFixture.Chart first, second;

        [SetUp]
        public void SetUp()
        {
            fixture = new FanmadeFixture();
            first = new FanmadeFixture.Chart { Title = "First", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja()), Audio = new byte[] { 1, 2, 3, 4 }, Categories = new[] { "game", "pop" } };
            second = new FanmadeFixture.Chart { Title = "Second", Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Hard")), Audio = new byte[] { 5, 6 }, Categories = new[] { "pop" } };
            second.Difficulties[0] = ("Hard", 5, "");
            fixture.Charts.Add(first); fixture.Charts.Add(second);
            cache = Path.Combine(Path.GetTempPath(), "ourtaiko-fanmade-" + Guid.NewGuid().ToString("N"));
            client = new FanmadeClient(cache);
        }

        [TearDown]
        public void TearDown()
        {
            client.Dispose();
            fixture.Dispose();
            if (Directory.Exists(cache)) Directory.Delete(cache, true);
        }

        // The client's awaits must not resume on the blocked Editor main thread.
        static void Run(Func<Task> body) => Task.Run(body).GetAwaiter().GetResult();
        static T Run<T>(Func<Task<T>> body) => Task.Run(body).GetAwaiter().GetResult();

        FanmadeEndpoint Connect(bool guest, string user = "don", string password = "katsu")
        {
            var endpoint = client.Add(fixture.Server(user, password));
            Run(() => client.ConnectAsync(endpoint, guest));
            return endpoint;
        }

        [Test]
        public void GuestLoadsEveryCategoryAsOneFlatListWithoutLoggingIn()
        {
            var endpoint = Connect(guest: true);
            Assert.That(endpoint.IsConnected, Is.True);
            Assert.That(endpoint.IsAuthenticated, Is.False);
            Assert.That(fixture.Requests, Has.None.EqualTo("POST /api/v1/game/login"));
            // First is in both categories but listed once, under the first category.
            Assert.That(client.Charts.Select(c => c.Title), Is.EqualTo(new[] { "First", "Second" }));
            Assert.That(client.Charts[0].Genre, Is.EqualTo("GAME"));
            Assert.That(client.Charts[1].Genre, Is.EqualTo("J-POP"));
            Assert.That(endpoint.ChartCount, Is.EqualTo(2));
            // Each category keeps its own list for its song-select folder; First is in both.
            Assert.That(client.Categories.Select(c => c.Title), Is.EqualTo(new[] { "Game", "Pop" }));
            Assert.That(client.Categories[0].ChartIds, Is.EqualTo(new[] { first.Id }));
            Assert.That(client.Categories[1].ChartIds, Is.EqualTo(new[] { first.Id, second.Id }));
            Assert.That(client.Categories[1].ServerName, Is.EqualTo("Fixture"));
            var info = client.Charts[0].ToSongInfo("en");
            Assert.That(info.Title, Is.EqualTo("First"));
            Assert.That(info.Course(Difficulty.Oni).Level, Is.EqualTo(8));
        }

        [Test]
        public void LoginLoadsTheAccountsBestScores()
        {
            fixture.AddAccountScore("don", first, "Oni", 500000);
            fixture.AddAccountScore("don", first, "Oni", 700000);
            fixture.AddAccountScore("other", first, "Oni", 900000);
            var endpoint = Connect(guest: false);
            Assert.That(endpoint.IsAuthenticated, Is.True);
            Assert.That(endpoint.Nickname, Is.EqualTo("DON"));
            Assert.That(client.Best(client.Charts[0], (int)Difficulty.Oni).Score, Is.EqualTo(700000));
            Assert.That(client.Best(client.Charts[0], (int)Difficulty.Hard), Is.Null);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ClearStatusSurvivesBootstrapSubmissionAndResponse(int clearStatus)
        {
            fixture.AddAccountScore("don", first, "Oni", 700000, clearStatus);
            Connect(guest: false);
            var chart = client.Charts[0];
            Assert.That(client.Best(chart, (int)Difficulty.Oni).ClearStatus, Is.EqualTo(clearStatus));
            Assert.That(client.Submit(chart, (int)Difficulty.Oni,
                new FanmadeScore { Score = 800000, Good = 100, Bad = 10, ClearStatus = clearStatus }), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That(body["ClearStatus"]?.Type, Is.EqualTo(JTokenType.Integer));
            Assert.That((int)body["ClearStatus"], Is.EqualTo(clearStatus));
            Assert.That(client.Best(chart, (int)Difficulty.Oni).ClearStatus, Is.EqualTo(clearStatus));
        }

        [Test]
        public void RejectedCredentialsThrowAndAGuestRetryStillConnects()
        {
            var endpoint = client.Add(fixture.Server("don", "wrong"));
            var error = Assert.Throws<HttpStatusException>(() => Run(() => client.ConnectAsync(endpoint, guest: false)));
            Assert.That(error.Status, Is.EqualTo(401));
            Assert.That(endpoint.IsConnected, Is.False);
            Assert.That(client.Charts, Is.Empty);
            Run(() => client.ConnectAsync(endpoint, guest: true));
            Assert.That(endpoint.IsConnected && !endpoint.IsAuthenticated, Is.True);
            Assert.That(client.Charts.Count, Is.EqualTo(2));
            // A retry with another password replaces the unconnected endpoint of the same account.
            var retry = client.Add(fixture.Server("don", "katsu"));
            Assert.That(retry, Is.SameAs(endpoint), "A connected endpoint is reused.");
        }

        [Test]
        public void AnUnreachableServerReportsANetworkCode()
        {
            var endpoint = client.Add(new ServerConfig { name = "Down", baseUrl = "http://127.0.0.1:9" });
            var error = Assert.Throws<FanmadeException>(() => Run(() => client.ConnectAsync(endpoint, guest: true)));
            Assert.That(error.Message, Does.StartWith("NETWORK_"));
            var invalid = client.Add(new ServerConfig { name = "Bad", baseUrl = "ftp://example.com" });
            Assert.That(Assert.Throws<FanmadeException>(() => Run(() => client.ConnectAsync(invalid, guest: true))).Message, Is.EqualTo("SERVER_URL_INVALID"));
        }

        [Test]
        public void PrepareDownloadsVerifiesCachesAndRepairsFiles()
        {
            Connect(guest: true);
            var chart = client.Charts[0];
            var (play, audio, prepared) = Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            Assert.That(File.ReadAllBytes(audio), Is.EqualTo(first.Audio));
            Assert.That(fixture.Requests.Where(r => r.StartsWith("GET /files/")).ToArray(), Has.Length.EqualTo(2), "Files come from the signed links.");
            Assert.That(play, Does.Contain("TITLE:First\n"));
            Assert.That(play, Does.Contain("TITLEJA:First JA\n"));
            Assert.That(play, Does.Contain("WAVE:audio.ogg\n"));
            Assert.That(play, Does.Contain("COURSE:Oni\nLEVEL:8\nSTYLE:Single\n"));
            Assert.That(play, Does.Not.Contain("Original Title"));
            Assert.That(TjaParser.Parse(play, "Oni").Title, Is.EqualTo("First"));

            // Objects are keyed by content hash alone, with short paths (Windows' MAX_PATH), and
            // the playable TJA never reaches the disk.
            string hash = FanmadeFixture.Sha(first.Audio);
            Assert.That(audio, Is.EqualTo(Path.Combine(Path.GetFullPath(cache), "objects", hash.Substring(0, 2), hash)));
            Assert.That(Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Select(f => f.Substring(cache.Length)).Max(f => f.Length), Is.LessThanOrEqualTo(80));
            Assert.That(Directory.GetFiles(Path.Combine(cache, "objects"), "*", SearchOption.AllDirectories), Has.Length.EqualTo(2));

            // A cache hit downloads nothing; a damaged object is fetched again.
            Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(2));
            File.WriteAllBytes(audio, new byte[] { 9 });
            Run(() => client.PrepareAsync(chart));
            Assert.That(fixture.Downloads, Is.EqualTo(3));
            Assert.That(File.ReadAllBytes(audio), Is.EqualTo(first.Audio));

            // Another account on the same server reuses the verified objects.
            using var other = new FanmadeClient(cache, Path.Combine(cache, "other.sqlite3"));
            var guest = other.Add(fixture.Server(name: "Guest"));
            Run(() => other.ConnectAsync(guest, true));
            Assert.That(Run(() => other.PrepareAsync(other.Charts[0])).AudioPath, Is.EqualTo(audio));
            Assert.That(fixture.Downloads, Is.EqualTo(3));
        }

        [Test]
        public void PrepareTakesTheAuthorsReplacedFilesAndReportsProgress()
        {
            Connect(guest: true);
            var listed = client.Charts[0];
            first.Tja = Encoding.UTF8.GetBytes(FanmadeFixture.SimpleTja("Oni", 2));
            first.Title = "First v2";
            DownloadProgress last = null;
            int updates = 0;
            var (play, _, prepared) = Run(() => client.PrepareAsync(listed, progress: p => { last = p; updates++; }));
            Assert.That(prepared.TjaHash, Is.EqualTo(FanmadeFixture.Sha(first.Tja)));
            Assert.That(prepared.Title, Is.EqualTo("First v2"));
            Assert.That(client.Charts[0].TjaHash, Is.EqualTo(prepared.TjaHash), "The catalog entry follows the replaced files.");
            Assert.That(TjaParser.Parse(play, "Oni").Notes.Count, Is.EqualTo(8));
            Assert.That(last.Step, Is.EqualTo(DownloadProgress.Stage.Ready));
            Assert.That(last.Audio.Status, Is.EqualTo(FileProgress.State.Complete));
            Assert.That(updates, Is.GreaterThan(4));
        }

        [Test]
        public void AnExpiredTokenLogsInAgain()
        {
            Connect(guest: false);
            fixture.Invalidate();
            Run(() => client.PrepareAsync(client.Charts[0]));
            Assert.That(fixture.Requests.Count(r => r == "POST /api/v1/game/login"), Is.EqualTo(2));
        }

        [Test]
        public void ScoresQueueRetryWithTheSameKeyAndCarryTheReplay()
        {
            var endpoint = Connect(guest: false);
            var chart = client.Charts[0];
            fixture.ScoreFailures.Enqueue(503);
            var record = new PlayRecord { AudioOffsetMs = 12, VisualOffsetMs = -3 };
            record.Inputs.Add((100.5, 1)); record.Inputs.Add((100.5, 3)); record.Inputs.Add((-20, 0));
            Assert.That(client.Submit(chart, (int)Difficulty.Oni, new FanmadeScore { Good = 3, Score = 3000, MaxCombo = 3, ClearStatus = 3 }, record), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.EqualTo(1), "A temporary failure keeps the queued request.");
            string queued = client.UploadQueue.Pending(endpoint.Id)[0].Body;

            client.RetryNow();
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.EqualTo(0));
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That(body.ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo(queued), "Retries send the original body.");
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni"));
            Assert.That((long)body["max_combo"], Is.EqualTo(3));
            Assert.That((int)body["ClearStatus"], Is.EqualTo(3));
            var replay = (JObject)body["replay_data"];
            Assert.That((int)replay["version"], Is.EqualTo(1));
            Assert.That((int)replay["audio_offset_ms"], Is.EqualTo(12));
            Assert.That(replay["inputs"].ToString(Newtonsoft.Json.Formatting.None), Is.EqualTo("[[100.5,1],[100.5,3],[-20.0,0]]"));
            Assert.That(client.Best(chart, (int)Difficulty.Oni).Score, Is.EqualTo(3000));
        }

        [Test]
        public void APermanentRefusalIsKeptAsRejected()
        {
            var endpoint = Connect(guest: false);
            fixture.ScoreFailures.Enqueue(409);
            client.Submit(client.Charts[0], (int)Difficulty.Oni, new FanmadeScore { Score = 1 });
            Run(() => client.WaitForUploadsAsync());
            Assert.That(client.PendingCount(endpoint), Is.Zero);
            Assert.That(client.UploadQueue.RejectedCount(endpoint.Id), Is.EqualTo(1));
        }

        [Test]
        public void GuestsNeverQueueAndDoubleCoursesUploadPerPlayer()
        {
            Connect(guest: true);
            Assert.That(client.Submit(client.Charts[0], (int)Difficulty.Oni, new FanmadeScore()), Is.False, "Guests never queue scores.");

            client.Reset();
            first.Difficulties[0] = ("Oni", 8, "P1");
            first.Difficulties.Add(("Oni", 7, "P2"));
            first.Tja = Encoding.UTF8.GetBytes("BPM:120\nCOURSE:Oni\n#START P1\n1,\n#END\n#START P2\n2,\n#END\n");
            Connect(guest: false);
            var player1 = client.Charts[0].ForPlayer("P1");
            Assert.That(client.Submit(player1, (int)Difficulty.Oni, new FanmadeScore { Score = 5 }), Is.True);
            Run(() => client.WaitForUploadsAsync());
            Assert.That(fixture.AcceptedScores.TryDequeue(out var body), Is.True);
            Assert.That((string)body["difficulty"], Is.EqualTo("Oni_1p"));
            Assert.That(body["replay_data"].Type, Is.EqualTo(JTokenType.Null), "Every score carries replay_data.");
            Assert.That(body.ContainsKey("versionId"), Is.False);
        }

        [TestCase("courseKeyedDifficulties")]
        [TestCase("resourceDownloadVersion")]
        [TestCase("scoreReplayVersion")]
        public void AServerWithoutTheCurrentProtocolIsRefused(string capability)
        {
            fixture.Protocol.Remove(capability);
            var endpoint = client.Add(fixture.Server());
            var error = Assert.Throws<FanmadeException>(() => Run(() => client.ConnectAsync(endpoint, guest: true)));
            Assert.That(error.Message, Is.EqualTo("SERVER_PROTOCOL_UNSUPPORTED"));
            Assert.That(endpoint.IsConnected, Is.False);
            Assert.That(client.Charts, Is.Empty);
        }

        [Test]
        public void PlayableTjaKeepsTheApiCourses()
        {
            var chart = FanmadeChart.From(new FanmadeFixture.Chart
            {
                Title = "Blocks", AudioName = "x.MP3",
                Difficulties = new System.Collections.Generic.List<(string, int, string)> { ("Hard", 4, ""), ("Oni", 9, "") },
            }.ToJson(), "server");
            string original = "TITLE:Old\nBPM:150\nCOURSE:Easy\nLEVEL:1\n#START\n1,\n#END\nCOURSE:Hard\nLEVEL:2\nSCOREINIT:1000\n#START\n2,\n#END\n"
                + "COURSE:Oni\n#START\n3,\n#END\n";
            string play = PlayableTja.Build(original, chart);
            Assert.That(play, Does.StartWith("MAKER:Tester\nTITLE:Blocks\nSUBTITLE:\nTITLEJA:Blocks JA\nWAVE:audio.mp3\n"));
            Assert.That(play, Does.Not.Contain("COURSE:Easy"));
            Assert.That(play, Does.Contain("COURSE:Hard\nLEVEL:4\nSTYLE:Single\nBPM:150\nSCOREINIT:1000\n#START\n2,\n#END\n"));
            Assert.That(play, Does.Contain("COURSE:Oni\nLEVEL:9\nSTYLE:Single\nBPM:150\n#START\n3,\n#END\n"));
            Assert.Throws<FanmadeException>(() => PlayableTja.Build("COURSE:Hard\n#START\n1,\n#END\n", chart), "TJA_BLOCK_MISMATCH");

            var pair = FanmadeChart.From(new FanmadeFixture.Chart
            {
                Title = "Pair", Difficulties = new System.Collections.Generic.List<(string, int, string)> { ("Oni", 9, "P1"), ("Oni", 8, "P2") },
            }.ToJson(), "server");
            play = PlayableTja.Build("BPM:150\nCOURSE:Oni\nSTYLE:Double\n#START P1\n3,\n#END\n#START P2\n4,\n#END\n", pair);
            Assert.That(play, Does.Contain("COURSE:Oni_1p\nLEVEL:9\nSTYLE:Double\nBPM:150\n#START P1\n3,\n#END\n"));
            Assert.That(play, Does.Contain("COURSE:Oni_2p\nLEVEL:8\nSTYLE:Double\nBPM:150\n#START P2\n4,\n#END\n"));
        }

        [Test]
        public void SongInfoComesStraightFromTheApiMetadata()
        {
            var json = new FanmadeFixture.Chart
            {
                Title = "Meta", Subtitle = "--Sub", Bpm = 180, DemoStart = 12.5,
                Difficulties = new System.Collections.Generic.List<(string, int, string)> { ("Hard", 4, ""), ("Edit", 10, "") },
            }.ToJson();
            json["subtitleTranslations"] = new JObject { ["ja"] = "++副題" };
            json["difficulties"][1]["branching"] = true;
            var info = FanmadeChart.From(json, "server").ToSongInfo("ja");
            Assert.That((info.Title, info.Subtitle, info.Bpm, info.DemoStart), Is.EqualTo(("Meta JA", "副題", 180.0, 12.5)));
            Assert.That(info.Courses.Select(c => (c.Difficulty, c.Course, c.Level)),
                Is.EqualTo(new[] { (Difficulty.Hard, "Hard", 4), (Difficulty.Ura, "Edit", 10) }));
            Assert.That(info.Courses.Select(c => c.IsBranching), Is.EqualTo(new[] { false, true }));
            ((JObject)json["difficulties"][0]).Remove("branching");
            Assert.That(Assert.Throws<FanmadeException>(() => FanmadeChart.From(json, "server")).Message, Is.EqualTo("API_DIFFICULTY_INVALID"), "branching is required.");
            json["difficulties"][0]["branching"] = false;
            Assert.That(FanmadeChart.From(json, "server").ToSongInfo("ko").Title, Is.EqualTo("Meta"), "Missing translations show the original.");

            var keyed = new FanmadeFixture.Chart
            {
                Title = "Double", Difficulties = new System.Collections.Generic.List<(string, int, string)> { ("Oni", 9, "P1"), ("Oni", 8, "P2") },
            }.ToJson();
            keyed["difficulties"] = new JArray(new JObject { ["course"] = "Oni_1p", ["level"] = 9, ["maker"] = "", ["branching"] = false },
                new JObject { ["course"] = "Oni_2p", ["level"] = 8, ["maker"] = "", ["branching"] = true });
            var p2 = FanmadeChart.From(keyed, "server").ForPlayer("P2").ToSongInfo("en");
            Assert.That(p2.Title, Is.EqualTo("Double P2"));
            Assert.That(p2.Courses.Select(c => (c.Difficulty, c.Course, c.Level, c.IsBranching)), Is.EqualTo(new[] { (Difficulty.Oni, "Oni_2p", 8, true) }));
            Assert.That(FanmadeChart.From(keyed, "server").ForPlayer("P1").ToSongInfo("en").Course(Difficulty.Oni).IsBranching, Is.False);
        }

        [Test]
        public void DownloadedChartsRequireValidUtf8()
        {
            Assert.That(PlayableTja.ToUtf8(Encoding.UTF8.GetBytes("テスト中文")), Is.EqualTo("テスト中文"));
            foreach (var bytes in new[] {
                new byte[] { 0xff }, new byte[] { 0xe3, 0x81 },
                new byte[] { 0x83, 0x65, 0x83, 0x58, 0x83, 0x67 } })
            {
                var error = Assert.Throws<FanmadeException>(() => PlayableTja.ToUtf8(bytes));
                Assert.That(error.Message, Is.EqualTo("TJA_ENCODING_INVALID"));
            }
        }

        [Test]
        public void ReplayRecordsThatAreTooLongOrInvalidAreSentAsNull()
        {
            var record = new PlayRecord();
            record.Inputs.Add((double.NaN, 1));
            Assert.That(record.ToJson().Type, Is.EqualTo(JTokenType.Null));
            Assert.That(PlayRecord.TypeOf(isKa: true, right: false), Is.EqualTo(0));
            Assert.That(PlayRecord.TypeOf(isKa: false, right: false), Is.EqualTo(1));
            Assert.That(PlayRecord.TypeOf(isKa: false, right: true), Is.EqualTo(2));
            Assert.That(PlayRecord.TypeOf(isKa: true, right: true), Is.EqualTo(3));
        }

        [Test]
        public void ServerListHasBothBuiltInServersAndGenresMapToBoardFrames()
        {
            var list = ServerList.Default();
            Assert.That(list.servers.Select(s => s.baseUrl), Is.EqualTo(new[] { "https://fanmade.ourtaiko.org", "https://ese-backend.llx.life" }));
            Assert.That(list.servers.All(s => s.enabled), Is.True);
            // An existing list keeps its entries (and edits) and only gains the missing built-in address.
            var edited = new ServerList { servers = { new ServerConfig { name = "Mine", baseUrl = "https://fanmade.ourtaiko.org/", enabled = false } } };
            Assert.That(edited.AddBuiltIn(), Is.True);
            Assert.That(edited.servers.Select(s => s.name), Is.EqualTo(new[] { "Mine", "ESE" }));
            Assert.That(edited.servers[0].enabled, Is.False);
            Assert.That(edited.AddBuiltIn(), Is.False);
            Assert.That(OnlineManager.GenreFrame("GAME"), Is.EqualTo(3));
            Assert.That(OnlineManager.GenreFrame("ボーカロイド"), Is.EqualTo(8));
            Assert.That(OnlineManager.GenreFrame("Unknown"), Is.EqualTo(0));
        }
    }
}

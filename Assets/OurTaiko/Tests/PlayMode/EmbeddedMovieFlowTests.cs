using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // The background movie on Unity's VideoPlayer (WebMoviePlayer) in the embedded PracticeScene: an
    // 8 s 320×180 30 fps H.264 test pattern (Fixtures/Movie~, kept out of the asset import). The
    // browser build reads a blob: URL the same way the Editor reads this path.
    public sealed class EmbeddedMovieFlowTests
    {
        [UnityTest]
        public IEnumerator PracticeShowsTheMovieOnTheSongClock()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Embedded movie\nBPM:120\nMOVIEOFFSET:1\nCOURSE:Oni\nLEVEL:1\n#START\n1000,\n1000,\n1000,\n1000,\n1000,\n1000,\n#END");
            song.music = AudioClip.Create("Movie test", 44100 * 14, 1, 44100, false);
            song.moviePath = Path.GetFullPath("Assets/OurTaiko/Tests/Fixtures/Movie~/testsrc.mp4");
            try
            {
                var bridge = WebPlayerBridge.Instance;
                typeof(WebPlayerBridge).GetProperty(nameof(WebPlayerBridge.Song)).SetValue(bridge, song);
                SceneSwitcher.EnsureInstance().ConfigureEmbedded(song, "Oni", true);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.PracticeScene);
                var play = Object.FindAnyObjectByType<PlayScene>();
                yield return Wait(() => play.Session != null && !SceneSwitcher.Instance.IsInputBlocked);
                var movie = play.movie;
                Assert.That(movie, Is.Not.Null);
                Assert.That(movie.player, Is.InstanceOf<WebMoviePlayer>());
                Assert.That(movie.HasMovie, Is.True);
                Assert.That(movie.stageArt.Length, Is.EqualTo(9));
                Assert.That(movie.stageArt.All(g => g.alpha == 0), Is.True);
                Assert.That(movie.laneBackgrounds.All(g => Mathf.Approximately(g.alpha, .4f)), Is.True);
                yield return Wait(() => movie.player.IsPrepared);
                play.EmbeddedStart();
                yield return Wait(() => play.SongTime >= 3);
                Assert.That(movie.IsShowing, Is.True);
                Assert.That(movie.player.IsPlaying, Is.True);
                Assert.That(movie.player.Position, Is.EqualTo(play.SongTime - 1).Within(.1));
                play.EmbeddedPause();
                yield return null; yield return null;
                Assert.That(movie.player.IsPlaying, Is.False);
            }
            finally
            {
                var play = Object.FindAnyObjectByType<PlayScene>();
                if (play != null) Object.Destroy(play.gameObject);
                Object.Destroy(song.chart); Object.Destroy(song.music); Object.Destroy(song);
            }
        }

        static IEnumerator Wait(System.Func<bool> condition)
        {
            double deadline = Time.realtimeSinceStartupAsDouble + 20;
            while (!condition()) { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
        }
    }
}

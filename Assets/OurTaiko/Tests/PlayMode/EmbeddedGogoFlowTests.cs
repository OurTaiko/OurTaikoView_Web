using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class EmbeddedGogoFlowTests
    {
        [UnityTest] public IEnumerator PracticeRendersGogoWhileSeeking() => Check(false);
        [UnityTest] public IEnumerator AutoPlayRendersGogoWhileSeeking() => Check(true);

        static IEnumerator Check(bool autoPlay)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Embedded GOGO\nBPM:240\nCOURSE:Oni\nLEVEL:5\n#START\n1000,\n#GOGOSTART\n1000,\n1000,\n#GOGOEND\n1000,\n#END");
            song.music = AudioClip.Create("GOGO test", 44100 * 6, 1, 44100, false);
            try
            {
                var bridge = WebPlayerBridge.Instance;
                Assert.That(bridge, Is.Not.Null);
                typeof(WebPlayerBridge).GetProperty(nameof(WebPlayerBridge.Song)).SetValue(bridge, song);
                SceneSwitcher.EnsureInstance().ConfigureEmbedded(song, "Oni", autoPlay);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.PracticeScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                double deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (play.Session == null || SceneSwitcher.Instance.IsInputBlocked)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                play.EmbeddedPause();
                Assert.That(play.IsPaused, Is.True);
                play.enabled = false; // Exercise the actual rendering path at deterministic chart times.
                var render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.NonPublic | BindingFlags.Instance);
                foreach (double time in new[] { 0.5, 1.04, 1.2, 1.6, 3.0, 1.04, 0.5 })
                {
                    render.Invoke(play, new object[] { time });
                    Assert.That(play.gogo.fire.enabled, Is.EqualTo(time >= 1 && time < 3));
                    Assert.That(play.gogo.lane.enabled, Is.EqualTo(play.gogo.fire.enabled));
                    Assert.That(play.gogo.explosions.Any(i => i.enabled), Is.EqualTo(time >= 1 && time < 1.5));
                    var sprite = play.gogo.fire.sprite;
                    render.Invoke(play, new object[] { time });
                    Assert.That(play.gogo.fire.sprite, Is.SameAs(sprite), "Paused frames retain their animation phase.");
                }
                render.Invoke(play, new object[] { 1.2 });
                play.EmbeddedRestart();
                play.enabled = true;
                yield return null;
                Assert.That(play.gogo.fire.enabled || play.gogo.lane.enabled || play.gogo.explosions.Any(i => i.enabled), Is.False);
            }
            finally
            {
                if (WebPlayerBridge.Instance != null) WebPlayerBridge.Instance.Exit();
                Object.Destroy(song.music);
                Object.Destroy(song.chart);
                Object.Destroy(song);
            }
        }
    }
}

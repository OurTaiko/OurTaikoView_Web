using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    // OurTaikoPlay's LaneAlphaFlowTests in the embedded PracticeScene: #LANE_ALPHA fades the lane
    // background, the branch route's tint and the GOGO lane together; nothing else on the lane.
    public sealed class EmbeddedLaneAlphaFlowTests
    {
        [UnityTest] public IEnumerator PracticeFadesTheLaneBackgroundWhileSeeking() => Exercise(false);
        [UnityTest] public IEnumerator AutoPlayFadesTheLaneBackgroundWhileSeeking() => Exercise(true);

        static IEnumerator Exercise(bool autoPlay)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            // BPM 240: 0 over 1–1.5 s, then (with GOGO) 40 % over 1.5–2 s.
            song.chart = new TextAsset("TITLE:Lane Alpha Test\nBPM:240\nCOURSE:Oni\nLEVEL:5\n#START\n1000,\n#LANE_ALPHA 0 500\n1000,\n"
                + "#GOGOSTART\n#LANE_ALPHA 40% -500\n1000,\n#GOGOEND\n1000,\n#END");
            song.music = AudioClip.Create("Lane alpha test", 44100 * 6, 1, 44100, false);
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
                play.enabled = false; // Keep the clock still while exercising the actual scene rendering path.
                play.pausePanel.SetActive(false);
                play.practiceView.panel.SetActive(false);
                var render = typeof(PlayScene).GetMethod("RenderNotes", BindingFlags.NonPublic | BindingFlags.Instance);
                var lane = play.firstLane;
                var expected = new[] { lane.transform.Find("LaneBackground").gameObject, lane.branchLane.background.gameObject, lane.gogo.lane.gameObject };
                var times = new[] { 0.5, 1.25, 1.5, 1.75, 2.5, 1.25, 0.5 };
                var alphas = new[] { 1, 0.5f, 0, 0.2f, 0.4f, 0.5f, 1 };
                Dictionary<CanvasGroup, float> others = null;
                for (int i = 0; i < times.Length; i++)
                {
                    render.Invoke(play, new object[] { times[i] });
                    foreach (var target in expected)
                        Assert.That(target.GetComponent<CanvasGroup>().alpha, Is.EqualTo(alphas[i]).Within(1e-5), $"{target.name} at {times[i]} s");
                    others ??= lane.GetComponentsInChildren<CanvasGroup>(true).Where(g => !expected.Contains(g.gameObject)).ToDictionary(g => g, g => g.alpha);
                    foreach (var other in others)
                        Assert.That(other.Key.alpha, Is.EqualTo(other.Value), $"{other.Key.name} is not the lane background ({times[i]} s).");
                }
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

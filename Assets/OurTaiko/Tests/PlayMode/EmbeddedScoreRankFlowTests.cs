using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace OurTaiko.Tests
{
    public sealed class EmbeddedScoreRankFlowTests
    {
        [UnityTest] public IEnumerator PracticeShowsDynamicRanksAndClearsOnRestart() => Check(false);
        [UnityTest] public IEnumerator AutoPlayShowsDynamicRanksAndClearsOnRestart() => Check(true);

        static IEnumerator Check(bool autoPlay)
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Embedded Rank\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n"
                + "1111,\n1111,\n1111,\n1111,\n1111,\n1000,\n0000,\n#END");
            song.music = AudioClip.Create("Rank test", 44100 * 15, 1, 44100, false);
            try
            {
                // Supply the decoded song at the bridge boundary; the browser decoder is Web-only.
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
                var badge = play.scoreCounter.scoreRank;
                Assert.That(badge, Is.Not.Null);
                Assert.That(badge.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(((RectTransform)badge.transform).anchoredPosition, Is.EqualTo(new Vector2(152, 118)));
                Assert.That(badge.rank.image.rectTransform.sizeDelta, Is.EqualTo(Vector2.one * 208));
                Assert.That(badge.GetComponent<Canvas>().overrideSorting, Is.True);
                Assert.That(badge.GetComponent<Canvas>().sortingOrder,
                    Is.GreaterThan(play.judgeCounter.GetComponentInParent<Canvas>().sortingOrder));
                Assert.That(badge.rank.DisplayedRank, Is.EqualTo(ScoreRank.None));
                Assert.That(play.Session.KiwamiThreshold, Is.EqualTo(1000020));
                for (int i = 0; i < 21; i++)
                {
                    if (autoPlay) play.Session.Advance(play.Session.Chart.Notes[i].Time, true);
                    else play.Session.Hit(false, play.Session.Chart.Notes[i].Time);
                    int expected = i < 10 ? 0 : i < 12 ? 1 : i < 14 ? 2 : i < 16 ? 3 : i < 18 ? 4 : i == 18 ? 5 : i == 19 ? 6 : 7;
                    Assert.That((int)badge.rank.DisplayedRank, Is.EqualTo(expected), $"After note {i + 1}");
                }
                Assert.That(badge.rank.image.sprite.name, Is.EqualTo("s86"));
                Assert.That(badge.rank.image.raycastTarget || badge.rank.group.blocksRaycasts, Is.False);
                play.scoreCounter.Show(1000000, play.Session.KiwamiThreshold);
                Assert.That(badge.rank.DisplayedRank, Is.EqualTo(ScoreRank.PurpleMiyabi));
                play.scoreCounter.Show(1000020, play.Session.KiwamiThreshold);
                Assert.That(badge.rank.DisplayedRank, Is.EqualTo(ScoreRank.Kiwami));
                yield return new WaitForSecondsRealtime(.3f);
                Assert.That(badge.rank.group.alpha, Is.EqualTo(1).Within(.001));
                play.EmbeddedRestart();
                Assert.That(play.Session.Score, Is.Zero);
                Assert.That(badge.rank.DisplayedRank, Is.EqualTo(ScoreRank.None));
                Assert.That(badge.rank.image.enabled, Is.False);
                play.MovePractice(1);
                Assert.That(play.Session.KiwamiThreshold, Is.EqualTo(1000020));
                Assert.That(badge.rank.DisplayedRank, Is.EqualTo(ScoreRank.None));
                badge.ShowScore(500000, 100);
                badge.ShowTime(100.25);
                Assert.That(badge.rank.group.alpha, Is.EqualTo(1).Within(.001));
                badge.ShowScore(599999, 102);
                badge.ShowTime(103);
                Assert.That(badge.rank.group.alpha, Is.Zero, "Same-rank increments do not restart the clip.");
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

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    // OurTaikoPlay's PlayfieldMaskFlowTests in the embedded PracticeScene: whatever the iframe's
    // shape, the black bars and the 1920x1080 viewport tile the screen.
    public sealed class EmbeddedPlayfieldMaskFlowTests
    {
        [UnityTest]
        public IEnumerator BarsCoverEverythingOutsideTheViewport()
        {
            var song = ScriptableObject.CreateInstance<SongDefinition>();
            song.chart = new TextAsset("TITLE:Playfield mask\nBPM:120\nCOURSE:Oni\nLEVEL:1\n#START\n1000,\n1000,\n#END");
            song.music = AudioClip.Create("Playfield mask test", 44100 * 6, 1, 44100, false);
            try
            {
                var bridge = WebPlayerBridge.Instance;
                Assert.That(bridge, Is.Not.Null);
                typeof(WebPlayerBridge).GetProperty(nameof(WebPlayerBridge.Song)).SetValue(bridge, song);
                SceneSwitcher.EnsureInstance().ConfigureEmbedded(song, "Oni", false);
                yield return SceneManager.LoadSceneAsync(SceneSwitcher.PracticeScene);
                var play = Object.FindFirstObjectByType<PlayScene>();
                double deadline = Time.realtimeSinceStartupAsDouble + 15;
                while (play.Session == null || SceneSwitcher.Instance.IsInputBlocked)
                { Assert.That(Time.realtimeSinceStartupAsDouble, Is.LessThan(deadline)); yield return null; }
                yield return null;

                var mask = Object.FindAnyObjectByType<PlayfieldMask>();
                Assert.That(mask, Is.Not.Null);
                Assert.That(mask.gameObject.scene, Is.EqualTo(play.gameObject.scene));
                Assert.That(mask.Viewport.name, Is.EqualTo(PlayfieldMask.ViewportName));
                var canvas = mask.GetComponent<Canvas>();
                Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.sortingOrder, Is.GreaterThan(play.GetComponentInParent<Canvas>()?.sortingOrder ?? 0));

                var root = mask.Viewport.GetComponentInParent<Canvas>().rootCanvas;
                var camera = root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
                var corners = new Vector3[4];
                mask.Viewport.GetWorldCorners(corners);
                Vector2 min = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
                Vector2 max = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
                float covered = (max.x - min.x) * (max.y - min.y);
                foreach (var image in mask.GetComponentsInChildren<Image>())
                {
                    Assert.That(image.color, Is.EqualTo(Color.black));
                    Assert.That(image.raycastTarget, Is.False);
                    var size = image.rectTransform.rect.size;
                    covered += size.x * size.y;
                    var bar = new Rect(image.rectTransform.anchoredPosition, size);
                    var inside = Rect.MinMaxRect(min.x + 1, min.y + 1, max.x - 1, max.y - 1);
                    Assert.That(bar.width == 0 || bar.height == 0 || !bar.Overlaps(inside), Is.True, image.name);
                }
                Assert.That(covered, Is.EqualTo((float)Screen.width * Screen.height).Within(Screen.width + Screen.height + 4f));
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

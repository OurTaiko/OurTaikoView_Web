using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class ScoreAdditionClipTests
    {
        [TestCase("PracticeScene")]
        public void SavedAnimationMatchesNijiiroTimesAndScorePosition(string name)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + name + ".unity");
            try
            {
                var counter = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayScene>(true)).Single().firstLane.scoreCounter;
                var row = counter.additionTemplate;
                Assert.That(row, Is.Not.Null);
                var clip = row.GetComponent<ClipSampler>().clip;
                Assert.That(clip.length, Is.EqualTo(.44674f).Within(.00001));
                row.Begin(1234, counter.digits, 0);
                foreach (var sample in new (double time, float alpha, float x, float y)[] {
                    (.025, .5f, 20.625f, -58.5f),
                    (.1, 1f, 0f, -58.5f), (.145, 1f, 0f, -58.5f),
                    (.15, 1f, 0f, -58.5f), (.31236, 1f, 0f, -60f),
                    (.40674, .5f, 0f, -69f) })
                {
                    row.ShowTime(sample.time);
                    Assert.That(row.GetComponent<CanvasGroup>().alpha, Is.EqualTo(sample.alpha).Within(.001));
                    Assert.That(row.horizontalOffset, Is.EqualTo(sample.x).Within(.001));
                    Assert.That(row.verticalOffset, Is.EqualTo(sample.y).Within(.001));
                    var root = (RectTransform)row.transform;
                    // Every digit sits on the same line (no per-digit fan).
                    for (int i = 0; i < 4; i++)
                    {
                        var digit = row.digits[i].rectTransform;
                        var corner = root.anchoredPosition + new Vector2(-root.sizeDelta.x * root.pivot.x, root.sizeDelta.y * (1 - root.pivot.y))
                            + digit.anchoredPosition + new Vector2(-digit.sizeDelta.x * digit.pivot.x, digit.sizeDelta.y * (1 - digit.pivot.y));
                        Assert.That(corner.x, Is.EqualTo(ScoreCounterLayout.DigitLeft(i, 4) + sample.x).Within(.001));
                        Assert.That(corner.y, Is.EqualTo(-ScoreCounterLayout.DigitTop - sample.y).Within(.001));
                    }
                }
                row.ShowTime(.447);
                Assert.That(row.gameObject.activeSelf, Is.False);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

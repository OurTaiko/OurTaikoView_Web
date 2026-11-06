using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace OurTaiko.Tests
{
    public sealed class PlayDrawOrderTests
    {
        [TestCase("PracticeScene")]
        public void AllSavedGraphicsKeepOriginalDrawOrder(string name)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + name + ".unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlaySceneBase>(true)).Single();
                var root = play.firstLane.noteLayer.GetComponentInParent<Canvas>().rootCanvas;
                var graphics = root.GetComponentsInChildren<Graphic>(true);
                var expected = File.ReadAllLines("Assets/OurTaiko/Tests/Fixtures/" + name + "DrawOrder.txt")
                    .Where(line => !line.StartsWith("#") && line.Length > 0).ToArray();
                // Stable ordering retains hierarchy order within each independently sorted Canvas.
                var actual = graphics.OrderBy(g => Order(g.transform))
                    .Select(g => AnimationUtility.CalculateTransformPath(g.transform, root.transform)
                        .Replace("/LaneForeground", "").Replace("/Hud", "")
                        .Replace("/LaneHud2P", "").Replace("/LaneHud", "")).ToArray();
                // After a deliberate scene change, copy this file over the fixture.
                Directory.CreateDirectory("TestResults");
                File.WriteAllLines("TestResults/" + name + "DrawOrder.actual.txt", actual);
                Assert.That(actual, Is.EqualTo(expected), "Every graphic, including prefab children and hidden effects, must preserve its original relation.");
                foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
                    if (rect != root.transform) Assert.That(rect.localPosition.z, Is.Zero, rect.name);

                int bars = play.firstLane.barLayer.GetComponent<SortingGroup>().sortingOrder;
                int notes = play.firstLane.noteLayer.GetComponent<SortingGroup>().sortingOrder;
                int moji = play.firstLane.mojiLayer.GetComponent<SortingGroup>().sortingOrder;
                Assert.That(Order(play.firstLane.hitFace.transform), Is.LessThan(bars));
                Assert.That(bars, Is.LessThan(notes));
                Assert.That(notes, Is.LessThan(moji));
                Assert.That(moji, Is.LessThan(Order(play.firstLane.hitRing.transform)));
                Assert.That(moji, Is.LessThan(Order(play.firstLane.judgment.transform.parent.Find("PlayerCover"))));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static int Order(Transform transform)
        {
            for (var node = transform; node != null; node = node.parent)
            {
                var canvas = node.GetComponent<Canvas>();
                if (canvas != null && (canvas.isRootCanvas ||
                    new SerializedObject(canvas).FindProperty("m_OverrideSorting").boolValue)) return canvas.sortingOrder;
            }
            throw new AssertionException("Graphic is missing its drawing Canvas.");
        }
    }
}

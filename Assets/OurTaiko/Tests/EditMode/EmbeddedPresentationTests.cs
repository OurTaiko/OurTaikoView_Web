using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;

namespace OurTaiko.Tests
{
    public sealed class EmbeddedPresentationTests
    {
        [TestCase(50)]
        [TestCase(100)]
        [TestCase(200)]
        [TestCase(5000)]
        public void ComboAnnouncementKeepsEachVoiceAlignedAfterAddingFifty(int combo)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PracticeScene.unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                var announce = play.comboAnnounce;
                Assert.That(announce.voices.Length, Is.EqualTo(51));
                Assert.That(announce.voices.All(v => v != null), Is.True);
                Assert.That(announce.Announce(combo, 0).name, Is.EqualTo(combo + "_1p"));
                Assert.That(announce.Combo, Is.EqualTo(combo));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

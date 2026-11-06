using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class HitFaceTests
    {
        const string PrefabPath = "Assets/OurTaiko/Generated/HitFace.prefab";
        const string RingPrefabPath = "Assets/OurTaiko/Generated/HitRing.prefab";

        [Test]
        public void RingPrefabHoldsOneClipPerVariantWithAdditiveBlend()
        {
            var view = AssetDatabase.LoadAssetAtPath<HitRingView>(RingPrefabPath);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.image, Is.SameAs(view.GetComponent<UnityEngine.UI.Image>()));
            Assert.That(view.image.material.shader.name, Is.EqualTo("Mobile/Particles/Additive"));
            Assert.That(((RectTransform)view.transform).sizeDelta, Is.EqualTo(new Vector2(336, 336)));
            Assert.That(view.GetComponent<ClipSampler>(), Is.Not.Null);
            Assert.That(new[] { view.good, view.ok, view.goodBig, view.okBig }.Select(c => c.name),
                Is.EqualTo(new[] { "HitRingGood", "HitRingOk", "HitRingGoodBig", "HitRingOkBig" }));
        }

        [Test]
        public void PrefabHoldsTheFourNijiiroFaces()
        {
            var view = AssetDatabase.LoadAssetAtPath<HitFaceView>(PrefabPath);
            Assert.That(view, Is.Not.Null);
            Assert.That(view.image, Is.SameAs(view.GetComponent<UnityEngine.UI.Image>()));
            Assert.That(new[] { view.good, view.ok, view.goodBig, view.okBig }.Select(s => s.name),
                Is.EqualTo(new[] { "hit_effect_good", "hit_effect_ok", "hit_effect_good_big", "hit_effect_ok_big" }));
        }

        [Test]
        public void PracticeSceneUsesAPrefabInstanceAtTheJudgePoint()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PracticeScene.unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayScene>(true)).Single();
                var face = play.firstLane.hitFace;
                Assert.That(face, Is.Not.Null);
                // HitFace is nested inside NoteLane.prefab now; the original source is still HitFace.prefab.
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(face.gameObject),
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath)));
                var rect = (RectTransform)face.transform;
                Assert.That(rect.parent, Is.SameAs(play.firstLane.noteLayer.parent.parent));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(510, -2)));
                Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(216, 216)));
                Assert.That(rect.parent.Find("HitFlash"), Is.Null);
                // Player::draw: face under the notes; ring over the notes and lane cover, under the drum.
                Assert.That(rect.GetSiblingIndex() + 1, Is.EqualTo(play.firstLane.noteLayer.parent.GetSiblingIndex()));
                var ring = (RectTransform)play.firstLane.hitRing.transform;
                Assert.That(PrefabUtility.GetCorrespondingObjectFromOriginalSource(ring.gameObject),
                    Is.SameAs(AssetDatabase.LoadAssetAtPath<GameObject>(RingPrefabPath)));
                Assert.That(ring.parent, Is.SameAs(play.firstLane.judgment.transform.parent));
                Assert.That(ring.anchoredPosition, Is.EqualTo(new Vector2(450, 58)));
                Assert.That(ring.GetComponentInParent<Canvas>().sortingOrder, Is.GreaterThan(play.firstLane.mojiLayer.GetComponent<UnityEngine.Rendering.SortingGroup>().sortingOrder));
                Assert.That(ring.GetSiblingIndex(), Is.GreaterThan(ring.parent.Find("PlayerCover").GetSiblingIndex()));
                Assert.That(ring.GetSiblingIndex() + 1, Is.EqualTo(ring.parent.Find("Drum").GetSiblingIndex()));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

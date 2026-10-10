using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The backdrop dancers: the troupe's shared playhead (Nijiiro ArcadeDancerGroup), the frames
    // redrawn on one canvas per variant, and the clips and prefabs built from the rig.
    public sealed class DancerTests
    {
        const string Art = "Assets/OurTaiko/Art/background/dancer/dancer_0/";
        const string Prefabs = "Assets/OurTaiko/Generated/Dancers/";
        static readonly int[] FrameCounts = { 19, 19, 27, 23, 23 };

        // 150 BPM is 100 rig frames a second.
        static DancerTroupe TroupeAt(double frame, out double time)
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray());
            troupe.Advance(0, 150);
            time = frame / 100;
            troupe.Advance(time, 150);
            return troupe;
        }

        static int[] Frames(DancerTroupe troupe) => Enumerable.Range(0, troupe.Slots).Select(troupe.FrameOf).ToArray();

        [Test]
        public void ThreeHopInTogetherAndLoopTheDance()
        {
            var troupe = new DancerTroupe(Enumerable.Repeat(new DancerRig(800, 80, 720), 5).ToArray());
            troupe.Advance(10, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 0, 0, 0, -1, -1 }));
            troupe.Advance(10.795, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 79, 79, 79, -1, -1 }));
            troupe.Advance(10.805, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 80, 80, 80, -1, -1 }));
            troupe.Advance(17.195, 150);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(719));
            troupe.Advance(17.305, 150);
            Assert.That(troupe.FrameOf(0), Is.EqualTo(90), "The dance loops without replaying the hop in.");
        }

        [Test]
        public void TheFourthWaitsForThePhraseAndJoinsOnTheSharedFrame()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(4);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1));
            troupe.Advance(time + 0.595, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1), "Earned at 100, it hops in from 160.");
            troupe.Advance(time + 1.005, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(40));
            troupe.Advance(time + 1.505, 150);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 250, 250, 250, 250, -1 }));
        }

        [Test]
        public void LeavingFinishesThePhraseThenPlaysTheWayOut()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(4);
            troupe.Advance(time + 1.505, 150);
            troupe.SetCount(3);
            Assert.That(troupe.Active, Is.EqualTo(3));
            Assert.That(troupe.FrameOf(3), Is.EqualTo(250));
            troupe.Advance(time + 2.195, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(319));
            troupe.Advance(time + 2.305, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(730));
            troupe.Advance(time + 2.995, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(799));
            troupe.Advance(time + 3.005, 150);
            Assert.That(troupe.FrameOf(3), Is.EqualTo(-1));
            Assert.That(troupe.FrameOf(0), Is.EqualTo(400));
        }

        [Test]
        public void ADancerLostBeforeItLandsNeverArrives()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.SetCount(5);
            troupe.Advance(time + 1.005, 150);
            Assert.That(troupe.FrameOf(4), Is.EqualTo(40));
            troupe.SetCount(3);
            Assert.That(Frames(troupe), Is.EqualTo(new[] { 200, 200, 200, -1, -1 }));
            troupe.SetCount(1);
            Assert.That(troupe.Active, Is.EqualTo(3), "Three always stay.");
        }

        [Test]
        public void ThePlayheadFollowsTheTempoAndNeverRunsBackwards()
        {
            var troupe = TroupeAt(100, out double time);
            troupe.Advance(time - 5, 150);
            Assert.That(troupe.Frame, Is.EqualTo(100).Within(1e-6));
            troupe.Advance(time - 4, 300);
            Assert.That(troupe.Frame, Is.EqualTo(300).Within(1e-6));
            troupe.Advance(time - 3, -75);
            Assert.That(troupe.Frame, Is.EqualTo(350).Within(1e-6));
            troupe.Advance(time - 2, 0);
            Assert.That(troupe.Frame, Is.EqualTo(430).Within(1e-6), "No tempo plays at 120.");
        }

        [Test]
        public void TheGaugeEarnsTheFourthAtHalfAndTheFifthWithTheClear()
        {
            Assert.That(DancerTroupe.CountFor(0, false), Is.EqualTo(3));
            Assert.That(DancerTroupe.CountFor(0.499, false), Is.EqualTo(3));
            Assert.That(DancerTroupe.CountFor(0.5, false), Is.EqualTo(4));
            Assert.That(DancerTroupe.CountFor(0.99, false), Is.EqualTo(4));
            Assert.That(DancerTroupe.CountFor(1, false), Is.EqualTo(5));
            Assert.That(DancerTroupe.CountFor(0.7, true), Is.EqualTo(5));
        }

        [Test]
        public void EveryFrameOfAVariantSharesOneCanvasAndPivot()
        {
            for (int v = 0; v < FrameCounts.Length; v++)
            {
                var sprites = Enumerable.Range(0, FrameCounts[v])
                    .Select(i => AssetDatabase.LoadAssetAtPath<Sprite>($"{Art}{v}_loop/{i}.png")).ToArray();
                Assert.That(sprites, Has.None.Null, "variant " + v);
                Assert.That(sprites.Select(s => s.rect.size).Distinct().Count(), Is.EqualTo(1), "canvas of variant " + v);
                Assert.That(sprites.Select(s => s.pivot).Distinct().Count(), Is.EqualTo(1), "pivot of variant " + v);
                // Each sprite is its whole file; the atlas it is drawn from does not change that.
                foreach (var sprite in sprites)
                {
                    ((TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite))).GetSourceTextureWidthAndHeight(out int width, out int height);
                    Assert.That(sprite.rect.size, Is.EqualTo(new Vector2(width, height)), sprite.name);
                }
            }
        }

        // A layer's saved rect already matches every frame, so only the sprite changes between frames.
        [Test]
        public void EachPrefabLayerIsTheVariantCanvasPivotedOnTheRigOrigin()
        {
            for (int v = 0; v < FrameCounts.Length; v++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Dancer0_{v}.prefab");
                Assert.That(prefab, Is.Not.Null);
                var view = prefab.GetComponent<DancerView>();
                Assert.That((view.frames, view.dance, view.leave), Is.EqualTo((800, 80, 720)));
                Assert.That(prefab.GetComponent<ClipSampler>().clip.name, Is.EqualTo("Dancer0_" + v));
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{Art}{v}_loop/0.png");
                var layers = prefab.GetComponentsInChildren<Image>(true);
                Assert.That(layers.Length, Is.EqualTo(v == 2 ? 2 : 1));
                foreach (var layer in layers)
                {
                    Assert.That(layer.rectTransform.sizeDelta, Is.EqualTo(sprite.rect.size));
                    Assert.That(Vector2.Scale(layer.rectTransform.pivot, sprite.rect.size), Is.EqualTo(sprite.pivot));
                }
            }
        }

        static void Shown(int variant, int frame, System.Action<RectTransform[]> check)
        {
            var dancer = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}Dancer0_{variant}.prefab"));
            try
            {
                dancer.GetComponent<DancerView>().Show(frame);
                check(dancer.GetComponentsInChildren<Image>(true).Select(i => i.rectTransform).ToArray());
            }
            finally { Object.DestroyImmediate(dancer); }
        }

        [Test]
        public void OneAtlasHoldsEveryFrame()
        {
            var atlas = AssetDatabase.LoadAssetAtPath<UnityEngine.U2D.SpriteAtlas>(Prefabs + "Dancer0.spriteatlasv2");
            Assert.That(atlas, Is.Not.Null);
            Assert.That(atlas.spriteCount, Is.EqualTo(FrameCounts.Sum()));
        }

        static string SpriteOf(RectTransform layer) => layer.GetComponent<Image>().sprite.name;

        [Test]
        public void TheClipHoldsEachRigKeyUntilTheNext()
        {
            // dancer_0.lua variant 0: {0,{{0,-5,296,1,1,0,1}}}, {26,{{1,-5,-260,...}}}, {51,{{0,-5,-176,1,0.92708,...}}},
            // {80,{{3,-5,-185,...}}}, {100,{{4,-5,-185,...}}}.
            Shown(0, 0, l => { Assert.That(SpriteOf(l[0]), Is.EqualTo("0")); Assert.That(l[0].anchoredPosition, Is.EqualTo(new Vector2(-5, -296))); });
            Shown(0, 26, l => { Assert.That(SpriteOf(l[0]), Is.EqualTo("1")); Assert.That(l[0].anchoredPosition.y, Is.EqualTo(260)); });
            Shown(0, 51, l => { Assert.That(l[0].anchoredPosition.y, Is.EqualTo(176)); Assert.That(l[0].localScale.y, Is.EqualTo(0.92708f).Within(1e-5)); });
            Shown(0, 79, l => { Assert.That(SpriteOf(l[0]), Is.EqualTo("0")); Assert.That(l[0].localScale.y, Is.EqualTo(1)); });
            Shown(0, 80, l => { Assert.That(SpriteOf(l[0]), Is.EqualTo("3")); Assert.That(l[0].anchoredPosition, Is.EqualTo(new Vector2(-5, 185))); });
            Shown(0, 99, l => Assert.That(SpriteOf(l[0]), Is.EqualTo("3")));
            Shown(0, 100, l => Assert.That(SpriteOf(l[0]), Is.EqualTo("4")));
        }

        [Test]
        public void ThePracticeSceneStandsFiveDancersBehindTheFooter()
        {
            // The embedded player has only PracticeScene.
            foreach (string path in new[] { "Assets/Scenes/PracticeScene.unity" })
            {
                var scene = EditorSceneManager.OpenPreviewScene(path);
                try
                {
                    var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                    var troupe = play.dancers;
                    Assert.That(troupe.slots.Select(s => s.anchoredPosition), Is.EqualTo(new[] { 960, 640, 1280, 319, 1601 }.Select(x => new Vector2(x, -1005))));
                    Assert.That(troupe.dancers.Select(d => d.GetComponent<ClipSampler>().clip.name),
                        Is.EqualTo(Enumerable.Range(0, 5).Select(v => "Dancer0_" + v)));
                    Assert.That(troupe.dancers.Select((d, i) => d.transform.parent == troupe.slots[i]), Has.All.True);
                    var footer = troupe.transform.parent.Find("Footer");
                    Assert.That(troupe.transform.GetSiblingIndex(), Is.EqualTo(footer.GetSiblingIndex() - 1));
                    // The centre dancer is drawn last.
                    Assert.That(troupe.slots.Select(s => s.GetSiblingIndex()), Is.EqualTo(new[] { 4, 3, 2, 1, 0 }));
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
        }
    }
}

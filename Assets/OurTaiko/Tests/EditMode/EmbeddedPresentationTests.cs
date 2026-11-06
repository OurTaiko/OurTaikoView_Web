using System.Linq;
using UnityEngine;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace OurTaiko.Tests
{
    public sealed class EmbeddedPresentationTests
    {
        [TestCase(BranchRoute.Normal)]
        [TestCase(BranchRoute.Expert)]
        [TestCase(BranchRoute.Master)]
        public void PracticeLaneSeekSettlesTheLabelBeforeRewinding(BranchRoute route)
        {
            WithPlay(play =>
            {
                var lane = play.firstLane.branchLane;
                lane.Initialize(true);
                lane.Select(BranchRoute.Master, 30);
                lane.ShowTime(30.05);
                lane.SetImmediate(route);
                lane.ShowTime(10);
                Assert.That(lane.currentLabel.sprite, Is.SameAs(route == BranchRoute.Normal ? lane.normalLabel
                    : route == BranchRoute.Expert ? lane.expertLabel : lane.masterLabel));
                Assert.That(lane.currentLabel.color.a, Is.EqualTo(1).Within(.01));
                Assert.That(lane.background.enabled, Is.EqualTo(route != BranchRoute.Normal));
                Assert.That(lane.levelChange.enabled, Is.False);
            });
        }

        [Test]
        public void AllNoteKindsHaveTheSharedExpressionFrames()
        {
            WithPlay(play =>
            {
                Assert.That(play.alternateNoteSprites.Length, Is.EqualTo(play.noteSprites.Length));
                for (int kind = 1; kind <= 7; kind++)
                {
                    var normal = play.noteSprites[kind];
                    var alternate = play.alternateNoteSprites[kind];
                    Assert.That(alternate, Is.Not.Null);
                    Assert.That(alternate.texture, Is.SameAs(normal.texture));
                    Assert.That(alternate.rect.size, Is.EqualTo(normal.rect.size));
                    Assert.That(alternate.rect.x, Is.EqualTo(normal.rect.x + normal.rect.width));
                    Assert.That(alternate.rect.y, Is.EqualTo(normal.rect.y));
                }
                Assert.That(play.alternateNoteSprites[9], Is.SameAs(play.noteSprites[9]));
            });
        }

        [Test]
        public void CountersShareDigitsAndKeepTheirPlaces()
        {
            WithPlay(play =>
            {
                var counter = play.firstLane.drumrollCounter;
                Assert.That(counter, Is.Not.Null);
                Assert.That(counter.gameObject.activeSelf, Is.False);
                Assert.That(counter.digitSprites, Is.EqualTo(play.firstLane.balloonCounter.digitSprites));
                Assert.That(counter.digitSprites.Length, Is.EqualTo(10));
                Assert.That(counter.digitSprites.All(d => d.rect.size == new Vector2(96, 112)), Is.True);
                Assert.That(counter.digitSprites[0].texture.mipmapCount, Is.GreaterThan(1));
                Assert.That(counter.digitSprites[0].texture.filterMode, Is.EqualTo(FilterMode.Trilinear));
                Assert.That(play.firstLane.balloonCounter.digitSize, Is.EqualTo(new Vector2(77, 90)));
                var lane = (RectTransform)play.firstLane.noteLayer.parent.parent;
                var balloon = (RectTransform)play.firstLane.balloonCounter.transform;
                // Over the gauge and under the pause button: the game's canvases, see PlayDrawOrderTests.
                Assert.That(balloon.anchorMin, Is.EqualTo(lane.anchorMin));
                Assert.That(balloon.anchorMax, Is.EqualTo(lane.anchorMax));
                Assert.That(balloon.sizeDelta, Is.EqualTo(lane.sizeDelta));
                Assert.That(balloon.anchoredPosition, Is.EqualTo(lane.anchoredPosition));
                Assert.That(play.firstLane.balloonCounter.body.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(645, 81)));
                Assert.That(counter.bubble.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(296, 271)));
                Assert.That(counter.number.anchoredPosition, Is.EqualTo(new Vector2(520, 215)));
            });
        }

        [Test]
        public void CounterAnimationRestartsPerHitAndClearsAfterFade()
        {
            WithPlay(play =>
            {
                var view = play.firstLane.drumrollCounter;
                view.RecordHit(2, 14, 10);
                Assert.That(view.Count, Is.EqualTo(14));
                Assert.That(view.NoteIndex, Is.EqualTo(2));
                var digits = view.number.GetComponentsInChildren<UnityEngine.UI.Image>();
                Assert.That(digits.Select(d => d.sprite), Is.EqualTo(new[] { view.digitSprites[1], view.digitSprites[4] }));
                Assert.That(digits.All(d => !d.raycastTarget), Is.True);
                Assert.That(view.bubble.raycastTarget || view.visuals.blocksRaycasts, Is.False);
                view.ShowTime(10.05);
                Assert.That(digits[0].rectTransform.sizeDelta.y, Is.EqualTo(124).Within(.01));
                view.ShowTime(12.615);
                Assert.That(view.visuals.alpha, Is.EqualTo(.5f).Within(.01));
                view.RecordHit(3, 1, 12.615);
                Assert.That(view.Count, Is.EqualTo(1));
                Assert.That(view.NoteIndex, Is.EqualTo(3));
                Assert.That(view.visuals.alpha, Is.EqualTo(1));
                view.ShowTime(15.4);
                Assert.That(view.Count, Is.Zero);
                Assert.That(view.IsVisible || view.gameObject.activeSelf, Is.False);
            });
        }

        // The kusudama's bindings; its place in the draw order is the game's (PlayDrawOrderTests).
        [Test]
        public void KusudamaIsBound()
        {
            WithPlay(play =>
            {
                var view = play.kusudama;
                Assert.That(view, Is.Not.Null);
                Assert.That(play.kusudamaPop, Is.Not.Null);
                Assert.That(view.ballFrames, Has.Length.EqualTo(5).And.All.Not.Null);
                Assert.That(view.digits.Select(d => d.name), Is.EqualTo(Enumerable.Range(0, 10).Select(i => "KusudamaDigit" + i)));
                Assert.That(view.ball.raycastTarget || view.renda.raycastTarget, Is.False);
                Assert.That(view.gameObject.activeSelf, Is.False);
            });
        }

        // The game's scene plays the movie through FFmpeg, which is not on WebGL;
        // SyncPlayPresentation.ApplyBackgroundMovie swaps in WebMoviePlayer on Unity's VideoPlayer.
        [Test]
        public void BackgroundMoviePlaysThroughUnitysVideoPlayer()
        {
            WithPlay(play =>
            {
                Assert.That(play.movie, Is.Not.Null);
                var player = play.movie.player as WebMoviePlayer;
                Assert.That(player, Is.Not.Null);
                Assert.That(player.gameObject, Is.SameAs(play.movie.gameObject));
                Assert.That(player.video, Is.SameAs(player.GetComponent<UnityEngine.Video.VideoPlayer>()));
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(player.gameObject), Is.Zero);
                Assert.That(play.movie.screen, Is.Not.Null);
                Assert.That(play.movie.stageArt, Is.Not.Empty);
                Assert.That(play.movie.laneBackgrounds, Is.Not.Empty);
            });
        }

        // NoteLane.prefab nests the game's Ending, which PracticeScene removes: the Web build keeps the
        // prefab file but none of the art, clips and sounds only Ending uses.
        [Test]
        public void WebBuildLeavesOutWhatOnlyTheRemovedEndingUses()
        {
            var build = System.Type.GetType("OurTaiko.Editor.WebViewBuild, Assembly-CSharp-Editor");
            Assert.That(build, Is.Not.Null);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            System.Collections.Generic.HashSet<string> keep;
            try { keep = (System.Collections.Generic.HashSet<string>)build.GetMethod("KeptAssets").Invoke(null, null); }
            finally { if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup); }
            Assert.That(keep, Does.Contain("Assets/OurTaiko/Generated/NoteLane.prefab"));
            Assert.That(keep, Does.Contain("Assets/OurTaiko/Generated/LaneHud.prefab"));
            Assert.That(keep, Does.Contain("Assets/OurTaiko/Generated/Ending.prefab"));
            Assert.That(keep, Does.Contain("Assets/OurTaiko/Art/game/notes/notes_atlas.png"));
            Assert.That(keep, Does.Contain("Assets/OurTaiko/Audio/kusudama_pop.ogg"));
            Assert.That(keep.Where(p => p.StartsWith("Assets/OurTaiko/Art/game/ending/")
                || p.StartsWith("Assets/OurTaiko/Audio/game/")
                || p.StartsWith("Assets/OurTaiko/Generated/Clips/Ending")), Is.Empty);
        }

        static void WithPlay(System.Action<PlayScene> check)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/PracticeScene.unity");
            try { check(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

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
                var announce = play.firstLane.comboAnnounce;
                Assert.That(announce.voices.Length, Is.EqualTo(51));
                Assert.That(announce.voices.All(v => v != null), Is.True);
                Assert.That(announce.Announce(combo, 0).name, Is.EqualTo(combo + "_1p"));
                Assert.That(announce.Combo, Is.EqualTo(combo));
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}

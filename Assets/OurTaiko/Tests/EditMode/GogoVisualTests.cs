using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OurTaiko.Tests
{
    public sealed class GogoVisualTests
    {
        static PlaySession Session(string body) => new PlaySession(TjaParser.Parse(
            "BPM:240\nCOURSE:Oni\n#START\n0,\n" + body + "\n0,\n#END"));

        [TestCase("PracticeScene")]
        public void OriginalArtLayoutAndAnimationSurviveForwardAndBackwardSeeking(string name)
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/" + name + ".unity");
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                var view = play.gogo;
                var session = Session("#GOGOSTART\n0,\n#GOGOEND");
                Assert.That(view, Is.Not.Null);
                Assert.That(view.transform.parent.Find("GogoTint"), Is.Null);
                Assert.That(view.transform.GetSiblingIndex(), Is.GreaterThan(view.transform.parent.Find("JudgeCircle").GetSiblingIndex()));
                Assert.That(view.transform.GetSiblingIndex(), Is.LessThan(play.noteLayer.parent.GetSiblingIndex()));
                Assert.That(view.fire.sprite.rect.size, Is.EqualTo(new Vector2(440, 440)));
                Assert.That(view.fire.rectTransform.anchoredPosition, Is.EqualTo(new Vector2(626, -109)));
                Assert.That(view.lane.rectTransform.sizeDelta, Is.EqualTo(new Vector2(1424, 195)));
                Assert.That(view.explosions.Length, Is.EqualTo(6));
                var positions = new[] { new Vector2(-46,336), new Vector2(276,333), new Vector2(599,333),
                    new Vector2(921,333), new Vector2(1244,333), new Vector2(1566,336) };
                for (int i = 0; i < 6; i++)
                {
                    var rect = view.explosions[i].rectTransform;
                    Assert.That(rect.sizeDelta, Is.EqualTo(new Vector2(392,762)));
                    Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(positions[i].x+196,-positions[i].y-381)));
                    Assert.That(rect.localScale.x, Is.EqualTo(i >= 4 ? -1 : 1));
                }
                Assert.That(view.fireLoop.clip.length, Is.EqualTo(0.26672).Within(1e-6));
                Assert.That(view.explosion.clip.length, Is.EqualTo(0.5).Within(1e-6));
                view.ShowTime(session, 0.999, true);
                Assert.That(view.fire.enabled || view.lane.enabled || view.explosions.Any(i => i.enabled), Is.False);
                view.ShowTime(session, 1, true);
                Assert.That(view.fire.transform.localScale.x, Is.EqualTo(3.474).Within(1e-4));
                Assert.That(view.lane.transform.localScale.y, Is.Zero.Within(1e-4));
                Assert.That(view.fire.sprite.name, Is.EqualTo("fire_0"));
                view.ShowTime(session, 1.20004, true);
                Assert.That(view.fire.transform.localScale.x, Is.EqualTo((3.474 + 1.351) / 2).Within(1e-4));
                view.ShowTime(session, 1.4, true);
                Assert.That(view.fire.transform.localScale.x, Is.EqualTo(1.351).Within(1e-4));
                Assert.That(view.lane.transform.localScale.y, Is.EqualTo(1).Within(1e-4));
                Assert.That(view.fire.color.a, Is.EqualTo(0.699).Within(1e-4));
                Assert.That(view.lane.color.a, Is.EqualTo(0.5).Within(1e-4));
                Assert.That(view.explosions.All(i => i.enabled), Is.True);
                view.ShowTime(session, 1.5, true);
                Assert.That(view.explosions.Any(i => i.enabled), Is.False);
                Assert.That(view.fire.enabled, Is.True);
                view.ShowTime(session, 2, true);
                Assert.That(view.fire.enabled || view.lane.enabled, Is.False);
                // A seek back restores entry frames rather than leaving the completed animation.
                view.ShowTime(session, 1.04, true);
                Assert.That(view.fire.sprite.name, Is.EqualTo("fire_1"));
                Assert.That(view.explosions.All(i => i.enabled && i.sprite.name == "explosion_2"), Is.True);
                Assert.That(view.fire.transform.localScale.x, Is.EqualTo(3.474).Within(1e-4));
                var sprite = view.fire.sprite;
                view.ShowTime(session, 1.04, true);
                Assert.That(view.fire.sprite, Is.SameAs(sprite), "Paused sampling must not advance the animation.");
                view.ShowTime(session, 1.267, true);
                Assert.That(view.fire.sprite.name, Is.EqualTo("fire_0"), "The fire loops at the original 266.72 ms boundary.");
                view.ShowTime(session, 0.5, true);
                Assert.That(view.explosions.Any(i => i.enabled) || view.fire.enabled, Is.False);
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void ShortGogoRetainsExplosionAgeAndRepeatedStartRestartsIt()
        {
            var session = Session("#GOGOSTART\n0\n#GOGOEND\n0\n#GOGOSTART\n00,\n#GOGOEND");
            var afterEnd = session.GogoStateAt(1.3);
            Assert.That(afterEnd.Active, Is.False);
            Assert.That(afterEnd.StartedAt, Is.EqualTo(1));
            Assert.That(session.GogoStateAt(1.6).StartedAt, Is.EqualTo(1.5));
            Assert.That(session.GogoStateAt(1.1).StartedAt, Is.EqualTo(1));
            Assert.That(session.GogoStateAt(0.9).StartedAt, Is.EqualTo(double.NegativeInfinity));
        }

        [TestCase(BranchRoute.Normal, false)]
        [TestCase(BranchRoute.Master, true)]
        public void EntryAgeUsesOnlyTheSelectedBranch(BranchRoute route, bool on)
        {
            var chart = TjaParser.Parse("BPM:240\nCOURSE:Oni\n#START\n0,\n#BRANCHSTART p,50,80\n#N\n0,\n#E\n0,\n#M\n#GOGOSTART\n0,\n#BRANCHEND\n0,\n#END");
            var session = new PlaySession(chart, 0, route);
            session.Advance(1.1, true);
            foreach (bool preview in new[] { false, true })
            {
                var state = session.GogoStateAt(1.1, preview);
                Assert.That(state.Active, Is.EqualTo(on));
                Assert.That(state.StartedAt, Is.EqualTo(on ? 1 : double.NegativeInfinity));
            }
        }
    }
}

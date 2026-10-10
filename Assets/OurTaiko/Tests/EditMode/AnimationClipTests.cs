using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace OurTaiko.Tests
{
    // The time-driven visuals stored as clips in Generated/Clips: each is checked by sampling the
    // asset itself through ClipSampler, the same way the views play it.
    public sealed class AnimationClipTests
    {
        const string Clips = "Assets/OurTaiko/Generated/Clips/";
        const string PlayScenePath = "Assets/Scenes/PracticeScene.unity";

        static AnimationClip Clip(string name)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + name + ".anim");
            Assert.That(clip, Is.Not.Null, name);
            return clip;
        }

        // Builds a throwaway object tree, samples the clip on its root and reads the result.
        static T Sampled<T>(AnimationClip clip, double seconds, Func<GameObject, T> read, params string[] children)
        {
            var root = new GameObject("Root", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(CanvasGroup));
            try
            {
                foreach (var child in children)
                {
                    var go = new GameObject(child, typeof(RectTransform), typeof(UnityEngine.UI.Image));
                    go.transform.SetParent(root.transform, false);
                }
                var sampler = ClipSampler.Attach(root, clip);
                sampler.Sample(seconds);
                return read(root);
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Sprite SpriteAt(AnimationClip clip, double seconds, string path = "")
            => Sampled(clip, seconds, go => (path == "" ? go.transform : go.transform.Find(path)).GetComponent<UnityEngine.UI.Image>().sprite,
                path == "" ? new string[0] : new[] { path });

        static void WithPlayScene(Action<PlayScene> check)
        {
            var scene = EditorSceneManager.OpenPreviewScene(PlayScenePath);
            try { check(scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single()); }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void DrumFlashStaysLitFor120Ms()
        {
            var clip = Clip("DrumFlash");
            Assert.That(clip.isLooping, Is.False);
            foreach (var (t, lit) in new[] { (0.0, true), (0.119, true), (0.12, false), (1.0, false) })
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.EqualTo(lit), $"{t} s");
            WithPlayScene(play => Assert.That(play.drumFlashes.All(f => f.GetComponent<ClipSampler>().clip == clip), Is.True));
        }

        [Test]
        public void JudgmentTextStaysOpaqueFor250MsThenHides()
        {
            var clip = Clip("JudgmentFade");
            foreach (var (t, alpha) in new[] { (0.0, 1f), (0.125, 1f), (0.249, 1f), (0.25, 0f), (1.0, 0f) })
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().color.a), Is.EqualTo(alpha).Within(1e-4), $"{t} s");
            WithPlayScene(play =>
            {
                Assert.That(play.judgment.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
                Assert.That((Vector3)(Vector4)play.judgment.color, Is.EqualTo(Vector3.one), "Judgment text stays untinted.");
            });
        }

        [TestCase(PlayScenePath)]
        public void JudgmentTextStretchesUpwardsLikeComboWithoutMovingItsBaseline(string path)
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var play = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayScene>(true)).Single();
                var image = play.judgment;
                var rect = image.rectTransform;
                var sampler = image.GetComponent<ClipSampler>();
                var comboSampler = play.combo.GetComponent<ClipSampler>();
                var corners = new Vector3[4];
                void ReadCorners()
                {
                    // Preview scenes have no initialized CanvasScaler; compare in the lane's space.
                    rect.GetLocalCorners(corners);
                    var matrix = Matrix4x4.TRS(rect.localPosition, rect.localRotation, rect.localScale);
                    for (int i = 0; i < corners.Length; i++) corners[i] = matrix.MultiplyPoint3x4(corners[i]);
                }
                sampler.Sample(0.25);
                ReadCorners();
                var bottom = corners[0];
                float height = corners[1].y - bottom.y;
                float width = corners[3].x - bottom.x;
                foreach (var sprite in play.judgmentSprites)
                {
                    image.sprite = sprite;
                    // Include overshoot, completion and a new hit restarting the same clip.
                    foreach (double t in new[] { 0, .025, .05, .07, .10, .15, .167, .25, .0 })
                    {
                        comboSampler.Sample(Math.Min(t, comboSampler.clip.length));
                        float stretch = play.combo.GetComponent<AnimatedFloat>().value;
                        sampler.Sample(t);
                        ReadCorners();
                        Assert.That(Vector3.Distance(corners[0], bottom), Is.LessThan(.001f), "The bottom edge must not jump.");
                        Assert.That(corners[3].x - bottom.x, Is.EqualTo(width).Within(.001f), "No horizontal stretching.");
                        Assert.That((corners[1].y - bottom.y) / height, Is.EqualTo(1 + stretch / 80).Within(.0001f), $"{sprite.name} at {t}s");
                    }
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void GogoTintPulsesLikeTheSine()
        {
            var clip = Clip("GogoPulse");
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(2 * Mathf.PI / 12).Within(1e-4));
            for (int i = 0; i <= 50; i++)
            {
                double t = clip.length * i / 50;
                Assert.That(Sampled(clip, t, go => go.GetComponent<CanvasGroup>().alpha),
                    Is.EqualTo(0.18f + Mathf.Sin((float)t * 12) * 0.05f).Within(1e-4), $"{t} s");
            }
            WithPlayScene(play => Assert.That(play.gogoTint.GetComponent<ClipSampler>().clip, Is.SameAs(clip)));
        }

        [Test]
        public void HitFaceFollowsNijiiroAnimation28()
        {
            var clip = Clip("HitFace");
            Assert.That(clip.length, Is.EqualTo(0.35).Within(1e-3));
            foreach (var (t, alpha) in new[] { (0.0, 0.5f), (0.03335, 0.75f), (0.0667, 1f), (0.2, 1f), (0.2833 + 0.03335, 0.75f), (0.3499, 0.5f) })
            {
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().color.a), Is.EqualTo(alpha).Within(1e-3), $"{t} s");
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.True, $"{t} s");
            }
            Assert.That(Sampled(clip, 0.35, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.False);
            var prefab = AssetDatabase.LoadAssetAtPath<HitFaceView>("Assets/OurTaiko/Generated/HitFace.prefab");
            Assert.That(prefab.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
        }

        [TestCase("HitRingGood", "outer_good")]
        [TestCase("HitRingOk", "outer_ok")]
        [TestCase("HitRingGoodBig", "outer_good_big")]
        [TestCase("HitRingOkBig", "outer_ok_big")]
        public void HitRingFollowsNijiiroAnimations27And30(string name, string strip)
        {
            var clip = Clip(name);
            Assert.That(clip.length, Is.EqualTo(0.2).Within(1e-4));
            foreach (var (t, frame, alpha) in new[] { (0.0, 0, 1f), (0.0544, 0, 1f), (0.0546, 1, 1f), (0.0728, 2, 1f), (0.091, 3, 1f), (0.1667, 3, 1f), (0.18335, 3, 0.5f) })
            {
                var image = Sampled(clip, t, go => (go.GetComponent<UnityEngine.UI.Image>().sprite, go.GetComponent<UnityEngine.UI.Image>().color.a, go.GetComponent<UnityEngine.UI.Image>().enabled));
                Assert.That(image.Item1.name, Is.EqualTo($"HitRing_{strip}{frame}"), $"{t} s");
                Assert.That(image.Item2, Is.EqualTo(alpha).Within(1e-3), $"{t} s");
                Assert.That(image.Item3, Is.True, $"{t} s");
            }
            Assert.That(Sampled(clip, 0.2, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.False);
        }

        [Test]
        public void GaugeHitEffectFollowsNijiiroAnimationTable()
        {
            var clip = Clip("GaugeHitEffect");
            Assert.That(clip.length, Is.EqualTo(0.383).Within(1e-4));
            (Sprite sprite, float width, Color32 color, float noteAlpha, bool shown) At(double t) => Sampled(clip, t, go =>
            {
                var burst = go.transform.Find("Burst").GetComponent<UnityEngine.UI.Image>();
                var note = go.transform.Find("Note").GetComponent<UnityEngine.UI.Image>();
                return (burst.sprite, burst.rectTransform.sizeDelta.x, (Color32)burst.color, note.color.a, burst.enabled && note.enabled);
            }, "Burst", "Note");
            // 2: frames 0 / 1 / 2 at 33.33 and 66.66 ms.
            Assert.That(new[] { 0.0, 0.0333, 0.05, 0.07, 0.3 }.Select(t => At(t).sprite.name),
                Is.EqualTo(new[] { "GaugeHitEffect0", "GaugeHitEffect0", "GaugeHitEffect1", "GaugeHitEffect2", "GaugeHitEffect2" }));
            // 32: 0.8 until 116.67 ms, then linear to 1.5 over 266 ms.
            Assert.That(At(0.1).width, Is.EqualTo(232 * 0.8f).Within(0.01f));
            Assert.That(At(0.11667 + 0.133).width, Is.EqualTo(232 * 1.15f).Within(0.01f));
            Assert.That(At(0.3828).width, Is.EqualTo(232 * 1.5f).Within(0.01f));
            // Tint by size: yellow at 0.8, orange up to 0.9 (about 38 ms), then red.
            Assert.That(At(0.1).color, Is.EqualTo(new Color32(253, 249, 0, 255)));
            Assert.That(At(0.12).color, Is.EqualTo(new Color32(255, 161, 0, 255)));
            Assert.That(At(0.16).color, Is.EqualTo(new Color32(230, 41, 55, 255)));
            // 33: opaque until 300 ms, gone at 383 ms; the note fades with the burst.
            Assert.That(At(0.3).noteAlpha, Is.EqualTo(1).Within(1e-4));
            Assert.That(At(0.3415).noteAlpha, Is.EqualTo(0.5f).Within(1e-3));
            Assert.That(At(0.3829).shown, Is.True);
            Assert.That(At(0.383).shown, Is.False);
        }

        [Test]
        public void SoulFireCyclesWithTheOverlayFlicker()
        {
            var fire = Clip("SoulFire");
            var overlay = Clip("SoulOverlay");
            Assert.That(fire.length, Is.EqualTo(0.4).Within(1e-4));
            Assert.That(overlay.length, Is.EqualTo(0.4).Within(1e-4));
            for (int frame = 0; frame < 8; frame++)
            {
                double t = frame * 0.05 + 0.025;
                Assert.That(SpriteAt(fire, t).name, Is.EqualTo(frame.ToString()), $"{t} s");
                Assert.That(Sampled(overlay, t, go => go.GetComponent<UnityEngine.UI.Image>().enabled), Is.EqualTo(frame % 4 < 2), $"{t} s");
            }
            WithPlayScene(play =>
            {
                Assert.That(play.soulGauge.fire.GetComponent<ClipSampler>().clip, Is.SameAs(fire));
                Assert.That(play.soulGauge.soulOverlay.GetComponent<ClipSampler>().clip, Is.SameAs(overlay));
            });
        }

        [TestCase("Easy", "easy")]
        [TestCase("Normal", "normal")]
        [TestCase("Hard", "hard")]
        public void SoulRainbowCrossfadesAndFadesIn(string style, string tier)
        {
            var clip = Clip("SoulRainbow" + style);
            Assert.That(clip.length, Is.EqualTo(1.2).Within(1e-4));
            (string a, float aAlpha, string b, float bAlpha) At(double t) => Sampled(clip, t, go =>
            {
                var a = go.transform.Find("RainbowA").GetComponent<UnityEngine.UI.Image>();
                var b = go.transform.Find("RainbowB").GetComponent<UnityEngine.UI.Image>();
                return (a.sprite.name, a.color.a, b.sprite.name, b.color.a);
            }, "RainbowA", "RainbowB");
            // The former code: frame = t / 75 ms, A = cell floor(frame), B = the next cell,
            // fade = t / 450 ms, A alpha = fade, B alpha = fade x frac(frame); looping every 0.6 s.
            foreach (double t in new[] { 0.0, 0.01, 0.0374, 0.1, 0.2, 0.3333, 0.44, 0.46, 0.55, 0.62, 0.7, 0.9, 1.1, 1.19 })
            {
                double frame = t / 0.075 % 8; int first = (int)frame;
                double fade = System.Math.Min(1, t / 0.45);
                var got = At(t);
                Assert.That(got.a, Is.EqualTo($"Rainbow{tier}{first}"), $"{t} s");
                Assert.That(got.b, Is.EqualTo($"Rainbow{tier}{(first + 1) % 8}"), $"{t} s");
                Assert.That(got.aAlpha, Is.EqualTo(fade).Within(1e-3), $"{t} s");
                Assert.That(got.bAlpha, Is.EqualTo(fade * (frame - first)).Within(2e-3), $"{t} s");
            }
            WithPlayScene(play =>
            {
                var gauge = play.soulGauge;
                Assert.That(gauge.styles.Select(s => s.rainbow.name), Is.EqualTo(new[] { "SoulRainbowEasy", "SoulRainbowNormal", "SoulRainbowHard" }));
                Assert.That(gauge.rainbowSampler.name, Is.EqualTo("Rainbow"));
                Assert.That(gauge.rainbowA.transform.parent, Is.SameAs(gauge.rainbowSampler.transform));
                Assert.That(gauge.rainbowB.transform.parent, Is.SameAs(gauge.rainbowSampler.transform));
            });
        }

        [Test]
        public void NewGaugeCellFadesInOver450Ms()
        {
            var clip = Clip("CellFade");
            Assert.That(clip.length, Is.EqualTo(0.45).Within(1e-4));
            foreach (var (t, alpha) in new[] { (0.0, 0f), (0.225, 0.5f), (0.45, 1f) })
                Assert.That(Sampled(clip, t, go => go.GetComponent<UnityEngine.UI.Image>().color.a), Is.EqualTo(alpha).Within(1e-4), $"{t} s");
            WithPlayScene(play => Assert.That(play.soulGauge.cellFade.GetComponent<ClipSampler>().clip, Is.SameAs(clip)));
        }

        [Test]
        public void NameplateRainbowBandCyclesSixFramesEvery300Ms()
        {
            var clip = Clip("NameplateRainbow");
            Assert.That(clip.isLooping, Is.True);
            Assert.That(clip.length, Is.EqualTo(0.3).Within(1e-4));
            for (int frame = 0; frame < 6; frame++)
            {
                double t = frame * 0.05 + 0.025;
                var got = Sampled(clip, t, go =>
                {
                    var band = go.transform.Find("Band").GetComponent<UnityEngine.UI.Image>();
                    var under = go.transform.Find("BandUnder").GetComponent<UnityEngine.UI.Image>();
                    return (band.sprite.name, under.enabled, under.sprite == null ? "" : under.sprite.name);
                }, "BandUnder", "Band");
                Assert.That(got.Item1, Is.EqualTo("NameplateRainbow" + frame), $"{t} s");
                Assert.That(got.Item2, Is.EqualTo(frame > 0), $"{t} s");
                if (frame > 0) Assert.That(got.Item3, Is.EqualTo("NameplateRainbow" + (frame - 1)), $"{t} s");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<NameplateView>("Assets/OurTaiko/Generated/Nameplate.prefab");
            Assert.That(prefab.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
        }

        [Test]
        public void BranchChangeMatchesTheEasedSlideAndBadgePulse()
        {
            var clip = Clip("BranchChange");
            Assert.That(clip.length, Is.EqualTo(1.392).Within(1e-4));
            float P(float t, float delay, float duration) => Mathf.Clamp01((t - delay) / duration);
            float Ease(float p) => p * (2 - p);
            var root = new GameObject("Branch", typeof(RectTransform));
            try
            {
                foreach (var name in new[] { "RouteBackground", "LevelChange", "PreviousRoute", "CurrentRoute" })
                    new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image)).transform.SetParent(root.transform, false);
                var view = root.AddComponent<BranchLaneView>();
                var sampler = root.GetComponent<ClipSampler>();
                sampler.clip = clip;
                var offsets = new SerializedObject(view);
                UnityEngine.UI.Image Image(string name) => root.transform.Find(name).GetComponent<UnityEngine.UI.Image>();
                // The former code, from Nijiiro IDs 41-45.
                foreach (float t in new[] { 0f, 0.03f, 0.05f, 0.1f, 0.12f, 0.15f, 0.2f, 0.2335f, 0.5f, 1.3f, 1.39f })
                {
                    sampler.Sample(t);
                    offsets.Update();
                    float nudge = Ease(P(t, 0, 0.1f)) * 30, fade = P(t, 0.1f, 0.133f), slide = Ease(fade) * 105;
                    Assert.That(offsets.FindProperty("previousOffset").floatValue, Is.EqualTo(nudge - slide).Within(0.05f), $"{t} s");
                    Assert.That(offsets.FindProperty("currentOffset").floatValue, Is.EqualTo(105 - slide).Within(0.05f), $"{t} s");
                    Assert.That(Image("PreviousRoute").color.a, Is.EqualTo(1 - fade).Within(1e-3), $"{t} s");
                    Assert.That(Image("PreviousRoute").enabled, Is.EqualTo(fade < 1), $"{t} s");
                    Assert.That(Image("CurrentRoute").color.a, Is.EqualTo(fade).Within(1e-3), $"{t} s");
                    Assert.That(Image("RouteBackground").color.a, Is.EqualTo(Mathf.Min(fade, 0.5f)).Within(1e-3), $"{t} s");
                    Assert.That(Image("LevelChange").color.a, Is.EqualTo(P(t, 0, 0.116f) - P(t, 1.276f, 0.116f)).Within(1e-3), $"{t} s");
                    Assert.That(Image("LevelChange").transform.localScale.x, Is.EqualTo(1 + 0.2f * (P(t, 0, 0.116f) - P(t, 0.116f, 0.116f))).Within(1e-3), $"{t} s");
                }
            }
            finally { Object.DestroyImmediate(root); }
            WithPlayScene(play => Assert.That(play.branchLane.GetComponent<ClipSampler>().clip, Is.SameAs(clip)));
        }

        [Test]
        public void DrumSqueezeEasesTo95PercentAndBack()
        {
            var clip = Clip("DrumSqueeze");
            Assert.That(clip.length, Is.EqualTo(0.14).Within(1e-4));
            float Ease(double p) => (float)(p * (2 - p));
            foreach (double ms in new[] { 0.0, 20, 35, 70, 90, 105, 139, 140 })
            {
                float expected = ms < 70 ? Mathf.Lerp(1, 0.95f, Ease(ms / 70)) : ms < 140 ? Mathf.Lerp(0.95f, 1, Ease(ms / 70 - 1)) : 1;
                var scale = Sampled(clip, ms / 1000, go => go.transform.localScale);
                Assert.That(scale.x, Is.EqualTo(expected).Within(1e-4), $"{ms} ms");
                Assert.That(scale.y, Is.EqualTo(expected).Within(1e-4), $"{ms} ms");
                Assert.That(scale.z, Is.EqualTo(1));
            }
            WithPlayScene(play => Assert.That(play.pauseButton.transform.parent.GetComponentInChildren<DrumPad>(true)
                .drum.GetComponent<ClipSampler>().clip, Is.SameAs(clip)));
        }

        [Test]
        public void TextStretchRisesThenStepsBack()
        {
            var clip = Clip("TextStretch");
            var root = new GameObject("Digits", typeof(AnimatedFloat));
            try
            {
                var sampler = ClipSampler.Attach(root, clip);
                float At(double ms) { sampler.Sample(ms / 1000); return root.GetComponent<AnimatedFloat>().value; }
                Assert.That(At(0), Is.EqualTo(2).Within(1e-4));
                Assert.That(At(25), Is.EqualTo(7).Within(1e-4));
                Assert.That(At(25.5), Is.EqualTo(7).Within(1e-4), "Whole milliseconds only.");
                Assert.That(At(50), Is.EqualTo(12).Within(1e-4));
                Assert.That(At(51), Is.EqualTo(10));
                Assert.That(At(70), Is.EqualTo(8));
                // The stepped return overshoots below zero for its last frames, as the original does.
                Assert.That(At(165), Is.EqualTo(-2));
                Assert.That(At(166), Is.EqualTo(-4));
                Assert.That(At(167), Is.Zero);
            }
            finally { Object.DestroyImmediate(root); }
            WithPlayScene(play =>
            {
                Assert.That(play.scoreCounter.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
                Assert.That(play.balloonCounter.GetComponent<ClipSampler>().clip, Is.SameAs(clip));
            });
        }

        [Test]
        public void BalloonPopStretchesAndFadesOut()
        {
            var clip = Clip("BalloonPop");
            var stretch = Clip("TextStretch");
            var root = new GameObject("Balloon", typeof(AnimatedFloat), typeof(CanvasGroup));
            var reference = new GameObject("Reference", typeof(AnimatedFloat));
            try
            {
                var sampler = ClipSampler.Attach(root, clip);
                var expected = ClipSampler.Attach(reference, stretch);
                foreach (double ms in new[] { 0.0, 25, 60, 83, 120, 165 })
                {
                    sampler.Sample(ms / 1000);
                    expected.Sample(ms / 1000);
                    Assert.That(root.GetComponent<AnimatedFloat>().value, Is.EqualTo(reference.GetComponent<AnimatedFloat>().value), $"{ms} ms");
                    Assert.That(root.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1 - (float)(ms / 166)).Within(1e-3), $"{ms} ms");
                }
            }
            finally { Object.DestroyImmediate(root); Object.DestroyImmediate(reference); }
            WithPlayScene(play =>
            {
                Assert.That(play.balloonCounter.popClip, Is.SameAs(clip));
                Assert.That(play.balloonCounter.stretchClip, Is.SameAs(stretch));
            });
        }
    }
}

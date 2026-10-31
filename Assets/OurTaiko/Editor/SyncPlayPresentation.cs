using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko.Editor
{
    // Reapply the shared practice presentation without replacing the embedded scene.
    public static class SyncPlayPresentation
    {
        public static void Apply()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string voicePath = "Assets/OurTaiko/Audio/combo/50_1p.ogg";
            if (!File.Exists(voicePath))
            {
                string source = Path.GetFullPath("../OurTaikoPlay/" + voicePath);
                FileUtil.CopyFileOrDirectory(source, voicePath);
                FileUtil.CopyFileOrDirectory(source + ".meta", voicePath + ".meta");
                AssetDatabase.ImportAsset(voicePath, ImportAssetOptions.ForceSynchronousImport);
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OurTaiko/Generated/Clips/JudgmentFade.anim");
                var stretch = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OurTaiko/Generated/Clips/TextStretch.anim");
                var curve = AnimationUtility.GetEditorCurve(stretch, EditorCurveBinding.FloatCurve("", typeof(AnimatedFloat), "value"));
                clip.ClearCurves();
                clip.frameRate = 1000;
                SetSteps(clip, typeof(Image), "m_Color.a", new[] { new Keyframe(0, 1), new Keyframe(.25f, 0) });
                SetSteps(clip, typeof(Transform), "m_LocalScale.y", curve.keys.Select(k => new Keyframe(k.time, 1 + k.value / 80f)).Append(new Keyframe(.25f, 1)).ToArray());
                EditorUtility.SetDirty(clip);
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var rect = play.judgment.rectTransform;
                var pivot = new Vector2(rect.pivot.x, 0);
                rect.anchoredPosition += Vector2.Scale(pivot - rect.pivot, Vector2.Scale(rect.rect.size, rect.localScale));
                rect.pivot = pivot;
                play.judgment.color = new Color(1, 1, 1, 0);
                play.judgment.GetComponent<ClipSampler>().clip = clip;
                var voices = play.comboAnnounce.voices.Where(v => v != null && v.name != "50_1p");
                play.comboAnnounce.voices = voices.Prepend(AssetDatabase.LoadAssetAtPath<AudioClip>(voicePath)).ToArray();
                // All five course sprites use the shared LaneDifficulty slices.
                RemoveDuplicateDifficultySprites();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // The embedded player has only PracticeScene; preserve its bridge and Web Audio bindings.
        [MenuItem("OurTaikoView/Sync Shared Counters")]
        public static void ApplyCounters()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string art = "Assets/OurTaiko/Art/game/drumroll_counter/";
            var digits = AssetDatabase.LoadAllAssetsAtPath(art + "counter.png").OfType<Sprite>()
                .Where(sprite => sprite.name.StartsWith("CounterDigit", StringComparison.Ordinal)).OrderBy(sprite => sprite.name).ToArray();
            var bubble = AssetDatabase.LoadAssetAtPath<Sprite>(art + "bubble.png");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OurTaiko/Generated/Clips/DrumrollCounter.anim");
            if (digits.Length != 10 || bubble == null || clip == null) throw new InvalidOperationException("Import the shared counter assets first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var lane = (RectTransform)play.noteLayer.parent.parent;
                var rig = lane.Find("DrumrollCounter") as RectTransform;
                if (rig == null) rig = CounterRect("DrumrollCounter", lane, Vector2.zero, Vector2.zero);
                rig.anchorMin = Vector2.zero; rig.anchorMax = Vector2.one;
                rig.offsetMin = rig.offsetMax = Vector2.zero;
                PlaceBefore(rig, play.scoreCounter.transform);
                var view = rig.GetComponent<DrumrollCounterView>();
                if (view == null) view = rig.gameObject.AddComponent<DrumrollCounterView>();
                view.visuals = rig.GetComponent<CanvasGroup>();
                if (view.visuals == null) view.visuals = rig.gameObject.AddComponent<CanvasGroup>();
                view.visuals.interactable = view.visuals.blocksRaycasts = false;
                var picture = rig.Find("Bubble") as RectTransform;
                if (picture == null) picture = CounterRect("Bubble", rig, new Vector2(296, 271), bubble.rect.size);
                view.bubble = picture.GetComponent<UnityEngine.UI.Image>();
                if (view.bubble == null) view.bubble = picture.gameObject.AddComponent<UnityEngine.UI.Image>();
                view.bubble.sprite = bubble; view.bubble.raycastTarget = false;
                view.number = rig.Find("Number") as RectTransform;
                if (view.number == null) view.number = CounterRect("Number", rig, new Vector2(520, 215), Vector2.zero);
                view.digitSprites = digits; view.digitSize = new Vector2(96, 112);
                if (rig.GetComponent<Animator>() == null) rig.gameObject.AddComponent<Animator>();
                rig.GetComponent<ClipSampler>().clip = clip;
                view.ResetDisplay();
                play.drumrollCounter = view;
                play.balloonCounter.digitSprites = digits;
                play.balloonCounter.digitSize = new Vector2(77, 90);
                var balloon = (RectTransform)play.balloonCounter.transform;
                balloon.SetParent(lane.parent, false);
                balloon.anchorMin = lane.anchorMin; balloon.anchorMax = lane.anchorMax;
                balloon.pivot = lane.pivot; balloon.sizeDelta = lane.sizeDelta;
                balloon.anchoredPosition3D = lane.anchoredPosition3D;
                balloon.localScale = lane.localScale; balloon.localRotation = lane.localRotation;
                PlaceBefore(balloon, play.pauseButton.transform);
                EditorUtility.SetDirty(play);
                EditorUtility.SetDirty(play.balloonCounter);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                // Every surviving balloon now references the new shared sheet.
                AssetDatabase.DeleteAsset("Assets/OurTaiko/Art/game/balloon/counter.png");
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        [MenuItem("OurTaikoView/Sync Note Expressions")]
        public static void ApplyNoteExpressions()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string atlas = "Assets/OurTaiko/Art/game/notes/notes_atlas.png";
            // Keep the original sprite IDs and crops from the shared source atlas.
            FileUtil.ReplaceFile(Path.GetFullPath("../OurTaikoPlay/" + atlas + ".meta"), atlas + ".meta");
            AssetDatabase.ImportAsset(atlas, ImportAssetOptions.ForceSynchronousImport);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(atlas).OfType<Sprite>().ToDictionary(s => s.name);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                play.alternateNoteSprites = new Sprite[play.noteSprites.Length];
                for (int kind = 1; kind <= 7; kind++) play.alternateNoteSprites[kind] = sprites["Note" + kind + "Alternate"];
                play.alternateNoteSprites[9] = play.noteSprites[9];
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // The arcade dancer troupe: clips, prefabs, frames and atlas are copied from OurTaikoPlay;
        // this gives PracticeScene the Dancers group that source's ApplyDancers builds, at the
        // source scene's saved height. A saved group is kept.
        [MenuItem("OurTaikoView/Sync Dancers")]
        public static void ApplyDancers()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                if (play.dancers != null) return;
                var footer = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<RectTransform>(true))
                    .Single(r => r.name == "Footer" && r.parent.name.StartsWith("Viewport"));
                foreach (var old in footer.parent.Cast<Transform>().Where(t => System.Text.RegularExpressions.Regex.IsMatch(t.name, @"^Dancer\d$")).ToArray())
                    UnityEngine.Object.DestroyImmediate(old.gameObject);
                // Hops start below the screen; the group clips them to the design area.
                var group = CounterRect("Dancers", footer.parent, new Vector2(0, 33.33f), new Vector2(1920, 1080));
                group.SetSiblingIndex(footer.GetSiblingIndex());
                group.gameObject.AddComponent<RectMask2D>();
                var view = group.gameObject.AddComponent<DancerTroupeView>();
                // dancer_0.lua pos and posy, in spawn order: centre, left, right, far left, far right.
                int[] x = { 960, 640, 1280, 319, 1601 };
                view.slots = new RectTransform[x.Length];
                view.dancers = new DancerView[x.Length];
                // Far dancers first, so the centre one ends up in front.
                for (int i = x.Length - 1; i >= 0; i--)
                {
                    view.slots[i] = CounterRect("Slot" + (i + 1), group, new Vector2(x[i], -1005), Vector2.zero);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/OurTaiko/Generated/Dancers/Dancer0_{i}.prefab");
                    view.dancers[i] = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, view.slots[i])).GetComponent<DancerView>();
                }
                play.dancers = view;
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // The kusudama ball: art, slices and kusudama_pop are copied from OurTaikoPlay. Source scenes put
        // it under the Viewport on a canvas sorted above the HUD and below the pause panel; the web
        // scene draws one canvas in hierarchy order, so it goes after the last HUD piece instead.
        [MenuItem("OurTaikoView/Sync Kusudama")]
        public static void ApplyKusudama()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string art = "Assets/OurTaiko/Art/game/kusudama/";
            var frames = Enumerable.Range(0, 5).Select(i => AssetDatabase.LoadAssetAtPath<Sprite>($"{art}kusudama/{i}.png")).ToArray();
            var digits = AssetDatabase.LoadAllAssetsAtPath(art + "counter.png").OfType<Sprite>()
                .Where(s => s.name.StartsWith("KusudamaDigit", StringComparison.Ordinal)).OrderBy(s => s.name).ToArray();
            var renda = AssetDatabase.LoadAssetAtPath<Sprite>(art + "renda.png");
            var pop = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/OurTaiko/Audio/kusudama_pop.ogg");
            if (frames.Any(f => f == null) || digits.Length != 10 || renda == null || pop == null)
                throw new InvalidOperationException("Import the shared kusudama assets first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var after = (RectTransform)play.balloonCounter.transform;
                var root = play.kusudama != null ? (RectTransform)play.kusudama.transform
                    : CounterRect("Kusudama", after.parent, Vector2.zero, new Vector2(1920, 1080));
                root.SetSiblingIndex(after.GetSiblingIndex() + 1);
                var view = root.GetComponent<KusudamaView>() ?? root.gameObject.AddComponent<KusudamaView>();
                view.ballFrames = frames;
                view.digits = digits;
                view.ball = KusudamaImage(root, "Ball", frames[0], new Vector2(2040, 1872));
                view.renda = KusudamaImage(root, "Renda", renda, new Vector2(360, 144));
                view.number = root.Find("Number") as RectTransform ?? CounterRect("Number", root, Vector2.zero, new Vector2(1920, 1080));
                root.gameObject.SetActive(false);
                play.kusudama = view;
                play.kusudamaPop = pop;
                EditorUtility.SetDirty(view);
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static Image KusudamaImage(RectTransform root, string name, Sprite sprite, Vector2 size)
        {
            var rect = root.Find(name) as RectTransform ?? CounterRect(name, root, Vector2.zero, size);
            rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        [MenuItem("OurTaikoView/Sync Score Rank")]
        public static void ApplyScoreRank()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string folder = "Assets/OurTaiko/Generated/ScoreRank/";
            string[] names = { "1_White", "2_Bronze", "3_Silver", "4_Gold", "5_Sui", "6_Miyabi", "7_Kiwami" };
            var icons = names.Select(n => AssetDatabase.LoadAssetAtPath<GameObject>(folder + n + ".prefab")).ToArray();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OurTaiko/Generated/Clips/PlayScoreRank.anim");
            if (icons.Any(i => i == null) || clip == null) throw new InvalidOperationException("Import the shared rank assets first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var counter = UnityEngine.Object.FindFirstObjectByType<PlayScene>().scoreCounter;
                var view = counter.scoreRank;
                if (view == null)
                {
                    var root = CounterRect("PlayScoreRank", counter.transform, new Vector2(152, 118), Vector2.zero);
                    root.pivot = Vector2.one * .5f;
                    view = root.gameObject.AddComponent<PlayScoreRankView>();
                    view.GetComponent<ClipSampler>().clip = clip;
                    var icon = (GameObject)PrefabUtility.InstantiatePrefab(icons[5], root);
                    icon.name = "ScoreRank";
                    view.rank = icon.AddComponent<ScoreRankView>();
                    view.rank.image = icon.GetComponent<UnityEngine.UI.Image>();
                    var rect = view.rank.image.rectTransform;
                    rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = Vector2.one * 208;
                    view.rank.icons = icons;
                    view.rank.group = icon.AddComponent<CanvasGroup>();
                    view.rank.group.interactable = view.rank.group.blocksRaycasts = false;
                    view.rank.Show(ScoreRank.None);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(icon);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(view.rank.image);
                    counter.scoreRank = view;
                }
                var canvas = view.GetComponent<Canvas>();
                if (canvas == null) canvas = view.gameObject.AddComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = counter.GetComponentInParent<Canvas>().sortingOrder + 2;
                EditorUtility.SetDirty(counter);
                EditorUtility.SetDirty(canvas);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // Original GOGO art, importer slices and clips are copied unchanged from OurTaikoPlay.
        // Rebind only the GOGO subtree; never replace the embedded scene or its bridge/audio setup.
        [MenuItem("OurTaikoView/Sync GOGO Effects")]
        public static void ApplyGogo()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string art = "Assets/OurTaiko/Art/game/gogo_time/";
            const string clips = "Assets/OurTaiko/Generated/Clips/";
            var fire = AssetDatabase.LoadAllAssetsAtPath(art + "fire.png").OfType<Sprite>().Single(s => s.name == "fire_0");
            var burst = AssetDatabase.LoadAllAssetsAtPath(art + "explosion.png").OfType<Sprite>().Single(s => s.name == "explosion_0");
            var laneSprite = AssetDatabase.LoadAssetAtPath<Sprite>(art + "lane_effect.png");
            var entryClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clips + "GogoEntry.anim");
            var fireClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clips + "GogoFire.anim");
            var burstClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clips + "GogoExplosion.anim");
            if (laneSprite == null || entryClip == null || fireClip == null || burstClip == null)
                throw new InvalidOperationException("Import all shared GOGO assets first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var lane = (RectTransform)play.branchLane.transform.parent;
                var old = lane.Find("GogoTint");
                if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
                var root = lane.Find("GogoEffects") as RectTransform ?? CounterRect("GogoEffects", lane, Vector2.zero, Vector2.zero);
                var view = root.GetComponent<GogoView>() ?? root.gameObject.AddComponent<GogoView>();
                view.lane = GogoImage(root, "Lane", laneSprite, new Vector2(496,-109.5f), new Vector2(1424,195), new Vector2(0,0.5f));
                view.lane.color = new Color(1,1,1,0.5f);
                view.fire = GogoImage(root, "Fire", fire, new Vector2(626,-109), new Vector2(440,440), Vector2.one * 0.5f);
                view.fire.color = new Color(1,1,1,0.699f);
                view.entry = GogoSampler(root, entryClip);
                view.fireLoop = GogoSampler(view.fire.transform, fireClip);
                var explosion = root.Find("Explosions") as RectTransform ?? CounterRect("Explosions", root, Vector2.zero, Vector2.zero);
                explosion.anchoredPosition = -lane.anchoredPosition;
                if (explosion.GetComponent<CanvasGroup>() == null) explosion.gameObject.AddComponent<CanvasGroup>();
                var positions = new[] { new Vector2(-46,336), new Vector2(276,333), new Vector2(599,333),
                    new Vector2(921,333), new Vector2(1244,333), new Vector2(1566,336) };
                view.explosions = new UnityEngine.UI.Image[6];
                for (int i = 0; i < 6; i++)
                {
                    var image = GogoImage(explosion, "Burst" + i, burst,
                        new Vector2(positions[i].x+196,-positions[i].y-381), new Vector2(392,762), Vector2.one * 0.5f);
                    image.transform.localScale = new Vector3(i >= 4 ? -1 : 1, 1, 1);
                    view.explosions[i] = image;
                }
                view.explosion = GogoSampler(explosion, burstClip);
                root.SetSiblingIndex(lane.Find("JudgeCircle").GetSiblingIndex() + 1);
                view.ResetDisplay();
                play.gogo = view;
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.DeleteAsset(clips + "GogoPulse.anim");
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static UnityEngine.UI.Image GogoImage(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, Vector2 pivot)
        {
            var rect = parent.Find(name) as RectTransform ?? CounterRect(name, parent, position, size);
            rect.pivot = pivot; rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = rect.GetComponent<UnityEngine.UI.Image>() ?? rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite; image.raycastTarget = false;
            return image;
        }

        static ClipSampler GogoSampler(Transform target, AnimationClip clip)
        {
            if (target.GetComponent<Animator>() == null) target.gameObject.AddComponent<Animator>();
            var sampler = target.GetComponent<ClipSampler>() ?? target.gameObject.AddComponent<ClipSampler>();
            sampler.clip = clip;
            return sampler;
        }

        static RectTransform CounterRect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = position; rect.sizeDelta = size;
            return rect;
        }

        static void PlaceBefore(Transform item, Transform next)
        {
            int index = next.GetSiblingIndex();
            item.SetSiblingIndex(item.GetSiblingIndex() < index ? index - 1 : index);
        }

        static void SetSteps(AnimationClip clip, Type type, string property, Keyframe[] keys)
        {
            var curve = new AnimationCurve(keys);
            for (int i = 0; i < keys.Length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Constant);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", type, property), curve);
        }

        // OurTaikoPlay's BackgroundMovie (BGMOVIE behind the lanes), on Unity's VideoPlayer: a black
        // backdrop and the movie between the stage art and the title, the stage art and the lane
        // backgrounds each under a CanvasGroup that BackgroundMovieView fades.
        [MenuItem("OurTaikoView/Sync Background Movie")]
        public static void ApplyBackgroundMovie()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var viewport = play.title.transform.parent;
                var root = viewport.Find("BackgroundMovie") as RectTransform;
                if (root == null)
                {
                    root = new GameObject("BackgroundMovie", typeof(RectTransform)).GetComponent<RectTransform>();
                    root.SetParent(viewport, false);
                    root.SetSiblingIndex(play.title.transform.GetSiblingIndex());
                }
                Stretch(root);
                var black = MovieGraphic<Image>(root, "Blackout");
                Stretch(black.rectTransform);
                black.color = Color.black;
                var screen = MovieGraphic<RawImage>(root, "Screen");
                screen.rectTransform.anchorMin = screen.rectTransform.anchorMax = screen.rectTransform.pivot = new Vector2(.5f, .5f);
                screen.rectTransform.anchoredPosition = Vector2.zero;
                screen.rectTransform.sizeDelta = new Vector2(1920, 1080);
                var player = root.GetComponent<WebMoviePlayer>();
                if (player == null) player = root.gameObject.AddComponent<WebMoviePlayer>();
                player.video = root.GetComponent<UnityEngine.Video.VideoPlayer>();
                var view = root.GetComponent<BackgroundMovieView>();
                if (view == null) view = root.gameObject.AddComponent<BackgroundMovieView>();
                view.player = player; view.screen = screen; view.blackout = black;
                string[] stage = { "Backdrop", "FestivalBackground", "FestivalLights", "Dancers", "Footer" };
                view.stageArt = viewport.Cast<Transform>().Where(t => stage.Contains(t.name) || t.name.StartsWith("HeaderPattern", StringComparison.Ordinal)).Select(Group).ToArray();
                var lane = viewport.Find("NoteLane");
                view.laneBackgrounds = new[] { "LaneBackground", "BranchIndicator/RouteBackground", "GogoEffects/Lane" }
                    .Select(name => lane.Find(name) ?? throw new InvalidOperationException("NoteLane/" + name + " is missing.")).Select(Group).ToArray();
                play.movie = view;
                EditorUtility.SetDirty(player);
                EditorUtility.SetDirty(view);
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("PracticeScene: BackgroundMovie with " + view.stageArt.Length + " stage art and " + view.laneBackgrounds.Length + " lane backgrounds");
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        static T MovieGraphic<T>(RectTransform root, string name) where T : Graphic
        {
            var rect = root.Find(name) as RectTransform;
            if (rect == null) { rect = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer)).GetComponent<RectTransform>(); rect.SetParent(root, false); }
            var graphic = rect.GetComponent<T>();
            if (graphic == null) graphic = rect.gameObject.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        static CanvasGroup Group(Transform target)
        {
            var group = target.GetComponent<CanvasGroup>();
            return group != null ? group : target.gameObject.AddComponent<CanvasGroup>();
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        // The Kadon (G), OurTaikoPlay a7b65ab and dc98eda: its purple note and the moji カドン. The images
        // are copied from OurTaikoPlay; the new slices stand where the existing ones leave room, so
        // none of those move. noteSprites stays indexed by NoteKind (Kadon is 16).
        [MenuItem("OurTaikoView/Sync Kadon")]
        public static void ApplyKadon()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            const string atlas = "Assets/OurTaiko/Art/game/notes/notes_atlas.png", moji = "Assets/OurTaiko/Art/game/notes/moji.png";
            foreach (string image in new[] { atlas, moji })
            {
                FileUtil.ReplaceFile(Path.GetFullPath("../OurTaikoPlay/" + image), image);
                AssetDatabase.ImportAsset(image, ImportAssetOptions.ForceSynchronousImport);
            }
            AddSlice(atlas, "Note16", new Rect(384, 1728, 192, 192));
            AddSlice(atlas, "Note16Alternate", new Rect(384, 1536, 192, 192));
            AddSlice(moji, "Moji12", new Rect(0, 576, 256, 48));
            var notes = AssetDatabase.LoadAllAssetsAtPath(atlas).OfType<Sprite>().ToDictionary(s => s.name);
            var texts = AssetDatabase.LoadAllAssetsAtPath(moji).OfType<Sprite>().ToDictionary(s => s.name);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/PracticeScene.unity");
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                play.noteSprites = WithSprite(play.noteSprites, (int)NoteKind.Kadon, notes["Note16"]);
                play.alternateNoteSprites = WithSprite(play.alternateNoteSprites, (int)NoteKind.Kadon, notes["Note16Alternate"]);
                play.mojiSprites = WithSprite(play.mojiSprites, NoteMoji.Kadon, texts["Moji12"]);
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        // Sets one entry, growing the array with empty entries up to it.
        static Sprite[] WithSprite(Sprite[] sprites, int index, Sprite sprite)
        {
            var grown = new Sprite[Math.Max(sprites.Length, index + 1)];
            Array.Copy(sprites, grown, sprites.Length);
            grown[index] = sprite;
            return grown;
        }

        // Adds a centred slice unless the sheet has one of that name.
        static void AddSlice(string path, string name, Rect rect)
        {
            var importer = AssetImporter.GetAtPath(path);
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("Sprite data provider unavailable.");
            provider.InitSpriteEditorDataProvider();
            var rects = provider.GetSpriteRects();
            if (rects.Any(r => r.name == name)) return;
            var slice = new SpriteRect { name = name, rect = rect, alignment = SpriteAlignment.Center, pivot = new Vector2(0.5f, 0.5f), spriteID = GUID.Generate() };
            provider.SetSpriteRects(rects.Append(slice).ToArray());
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(names.GetNameFileIdPairs().Append(new SpriteNameFileIdPair(name, slice.spriteID)).ToArray());
            provider.Apply();
            importer.SaveAndReimport();
        }

        static void RemoveDuplicateDifficultySprites()
        {
            var importer = AssetImporter.GetAtPath("Assets/OurTaiko/Art/game/lane/lane_difficulty.png");
            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("Sprite data provider unavailable.");
            provider.InitSpriteEditorDataProvider();
            var capability = provider.GetDataProvider<ISpriteFrameEditCapability>();
            if (capability == null || !capability.GetEditCapability().HasCapability(EEditCapability.CreateAndDeleteSprite))
                throw new InvalidOperationException("Importer does not support deleting duplicate slices.");
            var kept = provider.GetSpriteRects().Where(r => r.name.StartsWith("LaneDifficulty", StringComparison.Ordinal)).ToArray();
            if (kept.Length != 5) throw new InvalidOperationException("Expected five LaneDifficulty sprites.");
            provider.SetSpriteRects(kept);
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            names?.SetNameFileIdPairs(names.GetNameFileIdPairs().Where(p => kept.Any(r => r.name == p.name)).ToArray());
            provider.Apply();
            importer.SaveAndReimport();
        }
    }
}

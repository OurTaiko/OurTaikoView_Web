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
            // One build serves every browser: DXT5 where the GPU has it, unpacked by Unity elsewhere.
            const string atlasPath = "Assets/OurTaiko/Generated/Dancers/Dancer0.spriteatlasv2";
            var importer = (UnityEditor.U2D.SpriteAtlasImporter)AssetImporter.GetAtPath(atlasPath);
            var web = importer.GetPlatformSettings("WebGL");
            if (!web.overridden || web.format != TextureImporterFormat.DXT5 || web.maxTextureSize != 2048 || web.compressionQuality != 100)
            {
                web.overridden = true; web.format = TextureImporterFormat.DXT5; web.maxTextureSize = 2048; web.compressionQuality = 100;
                importer.SetPlatformSettings(web);
                importer.SaveAndReimport();
            }
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

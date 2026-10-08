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

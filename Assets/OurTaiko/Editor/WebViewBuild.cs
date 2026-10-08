using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
namespace OurTaiko.Editor
{
    public static class WebViewBuild
    {
        const string Scene = "Assets/Scenes/PracticeScene.unity";
        const string AudioRoot = "Assets/OurTaikoWebAudioBuild";
        [MenuItem("OurTaikoView/Build Web")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
            PlayerSettings.productName = "OurTaikoView_Web";
            PlayerSettings.companyName = "OurTaiko";
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Low);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.template = "PROJECT:OurTaikoView";
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 2048;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;
            // Loading must continue while focus is on the host mode/branch controls.
            // The iframe explicitly pauses gameplay on blur and document visibility changes.
            PlayerSettings.runInBackground = true;
            // Keep the authored scene dependencies and explicitly loaded shared resources.
            var roots = new List<string> { Scene };
            roots.AddRange(AssetDatabase.GetAllAssetPaths().Where(x => x.StartsWith("Assets/") && (x.Contains("/Resources/") || x.StartsWith("Assets/Settings/")) && !AssetDatabase.IsValidFolder(x)));
            var keep = new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true));
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(x => x.StartsWith("Assets/Scenes/") && x.EndsWith(".unity") && x != Scene).ToArray()) AssetDatabase.DeleteAsset(path);
            foreach (var path in AssetDatabase.GetAllAssetPaths().Where(x => (x.StartsWith("Assets/OurTaiko/Art/") || x.StartsWith("Assets/OurTaiko/Audio/") || x.StartsWith("Assets/OurTaiko/Generated/") || x.StartsWith("Assets/OurTaiko/Songs/")) && !AssetDatabase.IsValidFolder(x) && !keep.Contains(x)).ToArray()) AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Builds");
            BuildReport report;
            // The player outputs only through Web Audio; without Unity audio, no Unity AudioContext is created.
            // The Editor keeps Unity audio for in-Editor playback.
            bool unityAudio = SetUnityAudio(false);
            try
            {
                GenerateAudioCatalog();
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, target = BuildTarget.WebGL, locationPathName = "Builds/Web", options = BuildOptions.None });
            }
            finally
            {
                AssetDatabase.DeleteAsset(AudioRoot);
                SetUnityAudio(unityAudio);
            }
            File.WriteAllText("Builds/report.json", JsonUtility.ToJson(new Result { result = report.summary.result.ToString(), errors = report.summary.totalErrors, bytes = report.summary.totalSize }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Web build failed");
        }
        // Returns whether Unity audio was enabled before the change.
        static bool SetUnityAudio(bool enabled)
        {
            var manager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/AudioManager.asset")[0]);
            var disabled = manager.FindProperty("m_DisableAudio");
            bool previous = !disabled.boolValue;
            disabled.boolValue = !enabled;
            manager.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return previous;
        }
        // Web Audio decodes the original encoded files; scene AudioClips are only lookup keys.
        // Runs after pruning, so only clips the player still references are packaged.
        static void GenerateAudioCatalog()
        {
            AssetDatabase.DeleteAsset(AudioRoot);
            Directory.CreateDirectory(AudioRoot + "/Resources/NativeAudio");
            var entries = new List<AudioAssetCatalog.Entry>();
            foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/OurTaiko" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid), resource = "NativeAudio/" + guid;
                File.Copy(path, AudioRoot + "/Resources/" + resource + ".bytes", true);
                entries.Add(new AudioAssetCatalog.Entry { clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path), resource = resource });
            }
            AssetDatabase.Refresh();
            var catalog = ScriptableObject.CreateInstance<AudioAssetCatalog>();
            catalog.entries = entries.ToArray();
            AssetDatabase.CreateAsset(catalog, AudioRoot + "/Resources/NativeAudioCatalog.asset");
            AssetDatabase.SaveAssets();
        }
        [Serializable] sealed class Result { public string result; public int errors; public ulong bytes; }
    }
}

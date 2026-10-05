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
        [MenuItem("OurTaikoView/Build Web")]
        public static void Build()
        {
            var scene = EditorSceneManager.OpenScene(Scene);
            var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
            play.defaultSong = null;
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
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { Scene }, target = BuildTarget.WebGL, locationPathName = "Builds/Web", options = BuildOptions.None });
            File.WriteAllText("Builds/report.json", JsonUtility.ToJson(new Result { result = report.summary.result.ToString(), errors = report.summary.totalErrors, bytes = report.summary.totalSize }, true));
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Web build failed");
        }
        [Serializable] sealed class Result { public string result; public int errors; public ulong bytes; }
    }
}

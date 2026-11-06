using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Video;

namespace OurTaiko.Editor
{
    // PracticeScene is OurTaikoPlay's, copied verbatim with the assets it references (same GUIDs).
    // What the Web player does differently in it is applied here, after each copy.
    public static class SyncPlayPresentation
    {
        const string Scene = "Assets/Scenes/PracticeScene.unity";

        public static void Apply() => ApplyBackgroundMovie();

        // The game's BackgroundMovie plays through MajdataPlay's FFmpeg player (FFmpegMoviePlayer and
        // FFmpegVideoPlayer), which does not run on WebGL and is not in this project, so its scripts
        // come over missing. They are replaced by WebMoviePlayer on Unity's VideoPlayer (the browser's
        // <video> decodes the blob URL the parent page transferred). BackgroundMovieView, its screen,
        // blackout, stage art and lane backgrounds stay as the game set them.
        [MenuItem("OurTaikoView/Sync Background Movie")]
        public static void ApplyBackgroundMovie()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode first.");
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
                if (EditorSceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save current scene edits first.");
            var setup = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var scene = EditorSceneManager.OpenScene(Scene);
                var play = UnityEngine.Object.FindFirstObjectByType<PlayScene>();
                var view = play.movie != null ? play.movie : UnityEngine.Object.FindFirstObjectByType<BackgroundMovieView>(FindObjectsInactive.Include);
                if (view == null) throw new InvalidOperationException(Scene + " has no BackgroundMovieView.");
                var root = view.gameObject;
                int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(root);
                foreach (var other in root.GetComponents<MoviePlayer>().Where(p => p is not WebMoviePlayer)) UnityEngine.Object.DestroyImmediate(other);
                var video = root.GetComponent<VideoPlayer>();
                if (video == null) video = root.AddComponent<VideoPlayer>();
                var player = root.GetComponent<WebMoviePlayer>();
                if (player == null) player = root.AddComponent<WebMoviePlayer>();
                player.video = video;
                view.player = player;
                play.movie = view;
                EditorUtility.SetDirty(player);
                EditorUtility.SetDirty(view);
                EditorUtility.SetDirty(play);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("PracticeScene: " + root.name + " plays through WebMoviePlayer (" + removed + " missing scripts removed)");
            }
            finally { if (setup.Any(s => s.isLoaded && s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }
    }
}

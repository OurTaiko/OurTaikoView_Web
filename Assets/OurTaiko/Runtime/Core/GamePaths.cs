using System;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    // Where songs, settings, scores and caches live (MajdataPlay MajEnv.RootPath). Desktop players
    // keep them with the game: beside the executable on Windows and Linux, inside the .app on
    // macOS. iOS uses Documents, which Info.plist shares as On My iPad/iPhone; Android and the
    // Editor use persistentDataPath.
    public static class GamePaths
    {
        static string root;
        // First read on the main thread: it asks Unity for the paths.
        public static string Root => root ??= Resolve();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => root = null;

        static string Resolve()
        {
#if UNITY_STANDALONE && !UNITY_EDITOR
            // dataPath is <name>_Data beside the executable, or <name>.app/Contents on macOS.
            string beside = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (Writable(beside)) return beside;
            Log.Warn("Paths", "The game folder is read-only; saving to " + Application.persistentDataPath);
#endif
            return Application.persistentDataPath;
        }

        // A translocated macOS app or an install under Program Files cannot be written.
        static bool Writable(string directory)
        {
            string probe = Path.Combine(directory, ".write-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(probe, Array.Empty<byte>());
                File.Delete(probe);
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is System.Security.SecurityException) { return false; }
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;

namespace OurTaiko
{
    public static class SongLibraryPaths
    {
        public static StringComparer Comparer => Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

        public static bool TryNormalize(string path, out string normalized)
        {
            normalized = null;
            if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
            try
            {
                // IsPathRooted alone also accepts Windows drive-relative paths (C:Songs).
                if (!Path.IsPathFullyQualified(path)) return false;
                normalized = Path.GetFullPath(path);
                string root = Path.GetPathRoot(normalized);
                if (normalized.Length > root.Length)
                    normalized = normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException ||
                error is IOException || error is System.Security.SecurityException) { return false; }
        }

        public static string[] Resolve(string[] configured, string defaultRoot)
        {
            if (!TryNormalize(defaultRoot, out string normalizedDefault))
                throw new ArgumentException("The default song library path must be absolute.", nameof(defaultRoot));
            var paths = new List<string> { normalizedDefault };
            var seen = new HashSet<string>(Comparer) { normalizedDefault };
            foreach (string path in configured ?? Array.Empty<string>())
            {
                if (!TryNormalize(path, out string normalized))
                {
                    Log.Warn("Songs", "Skipping song library path: an absolute folder path is required.");
                    continue;
                }
                if (seen.Add(normalized)) paths.Add(normalized);
            }
            return paths.ToArray();
        }
    }
}

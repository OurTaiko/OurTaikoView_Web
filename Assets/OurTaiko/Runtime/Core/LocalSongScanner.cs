using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OurTaiko
{
    // One playable TJA found on disk. Key is its path under the songs root without the extension
    // ('/' separated); it identifies the library entry. Sha256 keys scores independently
    // of the entry's location and is calculated from the file's original bytes.
    public sealed class LocalChart
    {
        public string Key, FilePath, Text, AudioPath, MoviePath, Sha256;
        public SongInfo Info;
    }

    // A directory holding a box.def. Folders are its box.def sub-folders, Charts the charts in it and
    // in the plain directories below it; both are sorted by path.
    public sealed class LocalFolder
    {
        public string Key, Title, Genre = "", Path;
        public readonly List<LocalFolder> Folders = new List<LocalFolder>();
        public readonly List<LocalChart> Charts = new List<LocalChart>();
    }

    public sealed class LocalSongCatalog
    {
        public readonly List<LocalChart> Songs = new List<LocalChart>();
        public readonly List<LocalFolder> Folders = new List<LocalFolder>();

        // Charts and folders at every depth, for the scan's log line.
        public int ChartCount => Songs.Count + Folders.Sum(ChartsIn);
        public int FolderCount => Folders.Sum(FoldersIn);
        static int ChartsIn(LocalFolder folder) => folder.Charts.Count + folder.Folders.Sum(ChartsIn);
        static int FoldersIn(LocalFolder folder) => 1 + folder.Folders.Sum(FoldersIn);
    }

    // The songs folder as OpenTaiko reads it (CSongs管理.t曲を検索してリストを作成する, CBoxDef): a
    // directory with a box.def is a folder (BOX) whose own directory is read the same way, so
    // folders nest to any depth; a directory without one is transparent, its charts and box.def
    // folders listing where the directory itself is. Folders and charts each sort by path
    // (tSongListSortByPath); a folder with no chart anywhere beneath it is left out. Only .tja is
    // read, and a chart needs at least one Easy–Edit course.
    public static class LocalSongScanner
    {
        const int MaxDepth = 32;  // Stands in for a loop check on symlinked folders.

        // Merge configured roots in order. Absolute file identities distinguish same-named
        // charts and stay stable when roots are reordered or overlap. The default library
        // retains its original relative entry keys. Scores use the separate content hash.
        public static LocalSongCatalog ScanRoots(string[] roots, string defaultRoot, string language = "en")
        {
            var combined = new LocalSongCatalog();
            var seen = new HashSet<string>(SongLibraryPaths.Comparer);
            SongLibraryPaths.TryNormalize(defaultRoot, out string legacyRoot);
            foreach (string root in roots)
            {
                try
                {
                    // Check each root independently; one unavailable drive must not hide others.
                    using (var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator()) entries.MoveNext();
                    var catalog = Scan(root, language);
                    bool Keep(LocalChart chart)
                    {
                        if (!seen.Add(chart.FilePath)) return false;
                        chart.Key = LibraryKey(chart.FilePath, legacyRoot, false);
                        return true;
                    }
                    bool KeepFolder(LocalFolder folder)
                    {
                        folder.Key = LibraryKey(folder.Path, legacyRoot, true);
                        folder.Charts.RemoveAll(chart => !Keep(chart));
                        folder.Folders.RemoveAll(sub => !KeepFolder(sub));
                        return folder.Charts.Count > 0 || folder.Folders.Count > 0;
                    }
                    combined.Songs.AddRange(catalog.Songs.Where(Keep));
                    combined.Folders.AddRange(catalog.Folders.Where(KeepFolder));
                }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                    error is System.Security.SecurityException)
                {
                    Log.Warn("Songs", $"Skipping song library {root}: {error.Message}");
                }
            }
            return combined;
        }

        static string LibraryKey(string path, string legacyRoot, bool folder)
        {
            string prefix = legacyRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (path.StartsWith(prefix, comparison))
            {
                string relative = Relative(legacyRoot, path);
                return folder ? "local/" + relative : relative.Substring(0, relative.Length - Path.GetExtension(relative).Length);
            }
            if (Path.DirectorySeparatorChar == '\\') path = path.ToUpperInvariant();
            using var hash = SHA256.Create();
            return "local-path/" + BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        public static LocalSongCatalog Scan(string root, string language = "en")
        {
            var catalog = new LocalSongCatalog();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return catalog;
            root = Path.GetFullPath(root);
            Collect(root, root, catalog.Songs, catalog.Folders, language, 0);
            Sort(catalog.Songs, catalog.Folders);
            return catalog;
        }

        // One directory into the lists it belongs to: its charts, then each sub-directory, a box.def
        // one as a folder read on its own and any other one into these same lists.
        static void Collect(string root, string dir, List<LocalChart> charts, List<LocalFolder> folders, string language, int depth)
        {
            foreach (string file in Files(dir)) Add(charts, root, file);
            if (depth >= MaxDepth) return;
            foreach (string child in Directories(dir))
            {
                if (!File.Exists(Path.Combine(child, "box.def")))
                {
                    Collect(root, child, charts, folders, language, depth + 1);
                    continue;
                }
                var folder = ReadBoxDef(child, language);
                folder.Key = "local/" + Relative(root, child);
                folder.Path = child;
                Collect(root, child, folder.Charts, folder.Folders, language, depth + 1);
                Sort(folder.Charts, folder.Folders);
                if (folder.Charts.Count > 0 || folder.Folders.Count > 0) folders.Add(folder);
            }
        }

        static void Sort(List<LocalChart> charts, List<LocalFolder> folders)
        {
            charts.Sort((a, b) => PathCompare(a.FilePath, b.FilePath));
            folders.Sort((a, b) => PathCompare(a.Path, b.Path));
        }

        // CSongListNodeComparerPath compares the absolute paths with string.CompareTo; the invariant
        // culture keeps that order from depending on the player's locale.
        static int PathCompare(string a, string b)
        {
            int byPath = string.Compare(a, b, StringComparison.InvariantCulture);
            return byPath != 0 ? byPath : string.CompareOrdinal(a, b);
        }

        static void Add(List<LocalChart> list, string root, string file)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(file);
                string text = ReadText(bytes);
                var info = SongInfo.Read(text);
                if (info.Blocks.Count == 0)
                {
                    Log.Warn("Songs", $"Skipping song {file}: it has no course (COURSE … #START … #END).");
                    return;
                }
                string audio = string.IsNullOrWhiteSpace(info.Wave) ? null : Path.Combine(Path.GetDirectoryName(file), info.Wave.Trim());
                string movie = string.IsNullOrWhiteSpace(info.Movie) ? null : Path.Combine(Path.GetDirectoryName(file), info.Movie.Trim());
                // Still listed: it plays without music or without the movie.
                if (audio != null && !File.Exists(audio)) Log.Warn("Songs", $"Song {file}: its WAVE {info.Wave.Trim()} is missing.");
                if (movie != null && !File.Exists(movie)) Log.Warn("Songs", $"Song {file}: its BGMOVIE {info.Movie.Trim()} is missing.");
                string relative = Relative(root, file);
                using var hash = SHA256.Create();
                list.Add(new LocalChart
                {
                    Key = relative.Substring(0, relative.Length - Path.GetExtension(relative).Length),
                    Sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(),
                    FilePath = file, Text = text, Info = info,
                    AudioPath = audio != null && File.Exists(audio) ? audio : null,
                    MoviePath = movie != null && File.Exists(movie) ? movie : null,
                });
            }
            // Mono's Path.Combine throws ArgumentException for a WAVE or BGMOVIE with '|', '"', '<'
            // or '>'; one such chart must not fail the whole scan.
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException ||
                error is FormatException || error is ArgumentException || error is NotSupportedException)
            {
                Log.Warn("Songs", $"Skipping song {file}: {error.Message}");
            }
        }

        // tja.cpp test_encodings: a BOM decides; without one the file is UTF-8 when it decodes
        // cleanly, otherwise Shift-JIS (code page 932) where the platform has it.
        public static string ReadText(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { }
            try { return Encoding.GetEncoding(932).GetString(bytes); }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException) { }
            return new UTF8Encoding(false).GetString(bytes);
        }

        // CBoxDef.t読み込み: "#KEY:value" lines, the last of a key winning. #TITLE<LANG> (#TITLEJA,
        // #TITLEZH...) is the title in that language and #TITLE the default one (CLocalizationData);
        // #GENRE is the folder's genre, with OurTaikoPlayer's #COLLECTION standing in when there is
        // none. A folder without a title shows its directory name.
        static LocalFolder ReadBoxDef(string dir, string language)
        {
            var folder = new LocalFolder { Title = Path.GetFileName(dir) };
            string localized = "#TITLE" + new string((language ?? "en").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
            string text, title = null, titleLocalized = null, genre = null, collection = null;
            try { text = ReadText(File.ReadAllBytes(Path.Combine(dir, "box.def"))); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return folder; }
            foreach (string raw in text.Replace("\r", "").Split('\n'))
            {
                string line = raw.TrimStart(' ', '\t');
                int colon = line.IndexOf(':');
                if (!line.StartsWith("#", StringComparison.Ordinal) || colon < 0) continue;
                string key = line.Substring(0, colon).TrimEnd(), value = line.Substring(colon + 1).Trim();
                if (key == "#TITLE") title = value;
                else if (key == localized) titleLocalized = value;
                else if (key == "#GENRE") genre = value;
                else if (key == "#COLLECTION") collection = value;
            }
            if (!string.IsNullOrEmpty(titleLocalized ?? title)) folder.Title = titleLocalized ?? title;
            folder.Genre = genre ?? collection ?? "";
            return folder;
        }

        static IEnumerable<string> Files(string dir)
        {
            try
            {
                return Directory.GetFiles(dir)
                    .Where(f => string.Equals(Path.GetExtension(f), ".tja", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.Ordinal).ToArray();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        static IEnumerable<string> Directories(string dir)
        {
            try { return Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.Ordinal).ToArray(); }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException) { return Array.Empty<string>(); }
        }

        static string Relative(string root, string path) =>
            path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
    }
}

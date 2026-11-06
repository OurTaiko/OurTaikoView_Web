using System;
using System.Collections.Generic;
using System.Linq;

namespace OurTaiko
{
    // A song-select folder: a local box.def folder or a server category. Its sub-folders, then its
    // songs, open inline under it.
    public sealed class SongFolder
    {
        public string Key = "", Title = "", ServerName = "";
        // The endpoint id of a server folder; null for local folders.
        public string Server;
        public int Genre;
        // The box.def folders inside a local folder (OpenTaiko's nested BOX); server folders have none.
        public SongFolder[] Folders = Array.Empty<SongFolder>();
        public SongDefinition[] Songs = Array.Empty<SongDefinition>();
        // A playlist's own picture for the open folder; null without one or until it has loaded.
        public UnityEngine.Sprite Cover;

        // Every song in this folder and the folders inside it.
        public IEnumerable<SongDefinition> AllSongs() => Songs.Concat(Folders.SelectMany(f => f.AllSongs()));
        public int SongCount => Songs.Length + Folders.Sum(f => f.SongCount);
    }
}

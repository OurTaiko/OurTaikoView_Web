using System;

namespace OurTaiko
{
    // A song-select folder: a local box.def folder or a server category. Songs open inline under it.
    public sealed class SongFolder
    {
        public string Key = "", Title = "", ServerName = "";
        public int Genre;
        public SongDefinition[] Songs = Array.Empty<SongDefinition>();
    }
}

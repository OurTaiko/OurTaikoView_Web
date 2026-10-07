using System;
using System.IO;
using System.Linq;
using SQLite;

namespace OurTaiko.Online
{
    // An outbox, never a source of historical best scores. Endpoint includes server AND account.
    public sealed class PendingScoreQueue
    {
        [UnityEngine.Scripting.Preserve, Table("PendingScoreUploads")]
        public sealed class Entry
        {
            [PrimaryKey] public string Key { get; set; }
            [Indexed] public string Endpoint { get; set; }
            [Column("Body")] public string Body { get; set; }
            [Column("Rejected")] public bool Rejected { get; set; }
            public string TjaHash { get; set; }
            public string AudioHash { get; set; }
            [Column("Created")] public long Created { get; set; }
        }
        readonly string path;
        readonly object sync = new();
        public PendingScoreQueue(string path)
        {
            this.path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using var db = Open();
            db.CreateTable<Entry>();
        }
        SQLiteConnection Open() => new SQLiteConnection(path) { BusyTimeout = TimeSpan.FromSeconds(5) };
        public void Enqueue(string endpoint, string key, string body, string tjaHash = null, string audioHash = null)
        {
            lock (sync) { using var db = Open(); db.Insert(new Entry { Endpoint = endpoint, Key = key, Body = body, TjaHash = tjaHash, AudioHash = audioHash, Created = DateTime.UtcNow.Ticks }); }
        }
        public Entry[] Pending(string endpoint)
        {
            lock (sync) { using var db = Open(); return db.Table<Entry>().Where(x => x.Endpoint == endpoint && !x.Rejected).OrderBy(x => x.Created).ToArray(); }
        }
        public int RejectedCount(string endpoint)
        {
            lock (sync) { using var db = Open(); return db.Table<Entry>().Count(x => x.Endpoint == endpoint && x.Rejected); }
        }
        public void Remove(string key) { lock (sync) { using var db = Open(); db.Delete<Entry>(key); } }
        public void Reject(string key)
        {
            lock (sync) { using var db = Open(); db.Execute("UPDATE PendingScoreUploads SET Rejected = 1 WHERE Key = ?", key); }
        }
    }
}

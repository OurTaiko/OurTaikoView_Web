using System;
using System.Collections.Generic;
using System.IO;
using SQLite;
using UnityEngine;

namespace OurTaiko
{
    // Only local charts belong here. Connections are short-lived; the song wheel reads the cache.
    public sealed class ScoreStore
    {
        [UnityEngine.Scripting.Preserve, Table("BestScores")]
        public sealed class Record
        {
            [PrimaryKey] public string key { get; set; }
            [Column("score")] public int score { get; set; }
            [Column("good")] public int good { get; set; }
            [Column("ok")] public int ok { get; set; }
            [Column("bad")] public int bad { get; set; }
            [Column("maxCombo")] public int maxCombo { get; set; }
            [Column("rolls")] public int rolls { get; set; }
            [Column("crown")] public Crown crown { get; set; }
        }

        // Old JSON is read once for local records only, and kept as a recovery copy.
        [Serializable] sealed class LegacyDocument { public List<LegacyRecord> records; }
        [Serializable] sealed class LegacyRecord
        {
            public string key;
            public int score, good, ok, bad, maxCombo, rolls;
            public Crown crown;
        }

        static ScoreStore shared;
        readonly string path;
        readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
        public int Revision { get; private set; }
        public static ScoreStore Shared
        {
            get => shared ??= new ScoreStore(Path.Combine(Application.persistentDataPath, "scores.sqlite3"),
                Path.Combine(Application.persistentDataPath, "scores.json"));
            set => shared = value;
        }

        public ScoreStore(string path, string legacyPath = null)
        {
            this.path = path;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var db = Open();
            db.CreateTable<Record>();
            if (db.ExecuteScalar<int>("PRAGMA user_version") == 0)
            {
                var migrated = new List<Record>();
                if (!string.IsNullOrEmpty(legacyPath) && File.Exists(legacyPath))
                {
                    LegacyDocument document = null;
                    try { document = JsonUtility.FromJson<LegacyDocument>(File.ReadAllText(legacyPath)); }
                    catch (Exception error) { Debug.LogWarning("Could not migrate scores.json: " + error.Message); }
                    if (document?.records != null)
                        foreach (var old in document.records)
                            if (!string.IsNullOrEmpty(old.key) && !SongScores.IsOnlineKey(old.key))
                                migrated.Add(new Record { key = old.key, score = old.score, good = old.good, ok = old.ok,
                                    bad = old.bad, maxCombo = old.maxCombo, rolls = old.rolls, crown = old.crown });
                }
                db.RunInTransaction(() =>
                {
                    foreach (var record in migrated) db.InsertOrReplace(record);
                    db.Execute("PRAGMA user_version = 1");
                });
            }
            foreach (var record in db.Table<Record>()) records[record.key] = record;
        }

        SQLiteConnection Open() => new SQLiteConnection(path) { BusyTimeout = TimeSpan.FromSeconds(5) };
        public static string Key(string chartKey, Difficulty difficulty) => chartKey + "/" + SongInfo.CourseName(difficulty);
        public Record Get(string chartKey, Difficulty difficulty)
            => !SongScores.IsOnlineKey(chartKey) && records.TryGetValue(Key(chartKey, difficulty), out var record) ? record : null;

        public void Save(PlayResult result)
        {
            if (result.AutoPlay || SongScores.IsOnlineKey(result.ChartKey)) return;
            string key = Key(result.ChartKey, result.Difficulty);
            records.TryGetValue(key, out var best);
            result.PreviousBest = best?.score ?? 0;
            bool higher = best == null || result.Score > best.score;
            var next = new Record
            {
                key = key, score = higher ? result.Score : best.score, good = higher ? result.Good : best.good,
                ok = higher ? result.Ok : best.ok, bad = higher ? result.Bad : best.bad,
                maxCombo = higher ? result.MaxCombo : best.maxCombo, rolls = higher ? result.Rolls : best.rolls,
                crown = (Crown)Math.Max((int)(best?.crown ?? Crown.None), (int)result.StoredCrown),
            };
            using (var db = Open()) db.InsertOrReplace(next);
            records[key] = next;
            Revision++;
        }
    }
}

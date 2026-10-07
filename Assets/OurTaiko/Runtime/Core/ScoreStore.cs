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

        static ScoreStore shared;
        readonly string path;
        readonly Dictionary<string, Record> records = new Dictionary<string, Record>();
        public int Revision { get; private set; }
        public static ScoreStore Shared
        {
            get => shared ??= new ScoreStore(Path.Combine(Application.persistentDataPath, "scores.sqlite3"));
            set => shared = value;
        }

        public ScoreStore(string path)
        {
            this.path = path;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            using var db = Open();
            db.CreateTable<Record>();
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

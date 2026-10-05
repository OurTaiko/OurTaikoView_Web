using System;
using System.IO;
using UnityEngine;

namespace OurTaiko
{
    public enum RandomMode { Off, Kimagure, Detarame }

    // The local player's 演奏オプション (scores.h PlayerData modifier_* / neiro_index). Saved as JSON next to
    // the scores, the role scores_manager.save_player_data plays when the option panel closes.
    [Serializable]
    public sealed class PlayOptions
    {
        public const int DefaultSpeed = 10, MinSpeed = 1, MaxSpeed = 40, Mute = -1;
        // One Nijiiro badge per speed value (mod_speed_x1_1 .. mod_speed_x4), in this order.
        public static readonly int[] SpeedBadgeValues = { 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 25, 30, 35, 40 };

        public bool auto;
        public int speed = DefaultSpeed; // tenths: 10 = x1.0
        public bool display;             // ドロン: notes are not drawn
        public bool inverse;             // あべこべ
        public RandomMode random;        // ランダム
        public int neiro;                // hit-sound set; Mute (-1) = 無音

        [NonSerialized] string path;
        static PlayOptions shared;

        // Tests replace it with an unsaved instance so they never touch the player's options.
        public static PlayOptions Shared
        {
            get => shared ??= Load(System.IO.Path.Combine(Application.persistentDataPath, "options.json"));
            set => shared = value;
        }

        public static PlayOptions Load(string path)
        {
            var options = new PlayOptions();
            try
            {
                if (File.Exists(path)) JsonUtility.FromJsonOverwrite(File.ReadAllText(path), options);
            }
            catch (Exception error) { Debug.LogWarning("Ignoring unreadable options file: " + error.Message); }
            options.path = path;
            options.speed = Math.Max(MinSpeed, Math.Min(MaxSpeed, options.speed));
            if (!Enum.IsDefined(typeof(RandomMode), options.random)) options.random = RandomMode.Off;
            if (options.neiro < Mute) options.neiro = 0;
            return options;
        }

        // Writes only options that came from a file path; unsaved instances stay in memory.
        public void Save()
        {
            if (string.IsNullOrEmpty(path)) return;
            string directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(this, true));
            if (File.Exists(path)) File.Delete(path);
            File.Move(temporary, path);
        }

        // modifier.cpp speed_step: 0.1 steps up to 2.0, then 3.0 and 4.0, wrapping at both ends.
        public static int StepSpeed(int value, int direction)
        {
            if (direction > 0)
            {
                if (value >= 40) return 1;
                if (value >= 30) return 40;
                if (value >= 20) return 30;
                return value + 1;
            }
            if (value <= 1) return 40;
            if (value <= 20) return value - 1;
            if (value <= 30) return 20;
            return 30;
        }

        // Index into SpeedBadgeValues of the badge for a speed: the nearest lower value, or -1 at x1.0 and below.
        public static int SpeedBadge(int speed)
        {
            int best = -1;
            for (int i = 0; i < SpeedBadgeValues.Length; i++)
                if (SpeedBadgeValues[i] <= speed) best = i;
            return speed > DefaultSpeed ? best : -1;
        }
    }

    // tja.cpp apply_modifiers, run on the parsed chart before play: inverse, then random, then speed.
    // ドロン only marks notes hidden; bar lines stay visible (the original also hid them).
    public static class ChartModifiers
    {
        // Each don / ka note independently changes colour with this chance (not the original's
        // objects/5 x level pick, which also counted bar lines and rolls).
        public const double KimagureChance = 0.3, DetarameChance = 0.5;

        public static void Apply(TaikoChart chart, PlayOptions options, System.Random random)
        {
            double chance = options.random == RandomMode.Kimagure ? KimagureChance
                : options.random == RandomMode.Detarame ? DetarameChance : 0;
            double speed = options.speed / (double)PlayOptions.DefaultSpeed;
            foreach (var note in chart.Notes)
            {
                if (options.display) note.Display = false;
                if (options.inverse) note.Kind = Swap(note.Kind);
                if (chance > 0 && Swap(note.Kind) != note.Kind && random.NextDouble() < chance) note.Kind = Swap(note.Kind);
                note.ScrollX *= speed;
            }
            foreach (var bar in chart.Bars) bar.ScrollX *= speed;
            // apply_modifiers ends with modifier_moji, so the text follows swapped colours.
            NoteMoji.Assign(chart);
        }

        static NoteKind Swap(NoteKind kind) => kind switch
        {
            NoteKind.Don => NoteKind.Ka,
            NoteKind.Ka => NoteKind.Don,
            NoteKind.BigDon => NoteKind.BigKa,
            NoteKind.BigKa => NoteKind.BigDon,
            _ => kind,
        };
    }
}

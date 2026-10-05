using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Prefer Oni/Ura and cycle between them; otherwise show the hardest recorded lower course.
    // All objects and sprites are authored in SongSelect; this only fills the saved window.
    public sealed class SongBestScoreView : MonoBehaviour
    {
        public CanvasGroup group;
        public Image difficulty;
        public Sprite[] difficultySprites;
        public Image[] digits;
        public Sprite[] numbers;
        public RectTransform scoreCount;
        public JudgeCounterView judgments;
        public float cycleSeconds = 1;
        public bool HasRecord => available.Count != 0;
        public Difficulty DisplayedDifficulty { get; private set; }
        public int DisplayedScore { get; private set; }
        readonly List<(Difficulty Difficulty, ScoreStore.Record Record)> available = new();
        SongDefinition song;
        int localRevision = -1, shown = -1;
        long onlineRevision = -1;
        double started;

        // Saved sample content is visible in the Editor; never flash it before runtime binding.
        void Awake() => group.alpha = 0;

        public void Show(SongDefinition selected, SongInfo info, float alpha, double now)
        {
            int local = SongScores.IsOnline(selected) ? -1 : ScoreStore.Shared.Revision;
            long online = Online.OnlineManager.Instance?.Client.Revision ?? -1;
            if (song != selected || localRevision != local || onlineRevision != online)
            {
                song = selected; localRevision = local; onlineRevision = online;
                started = now; shown = -1; available.Clear();
                if (info != null)
                {
                    foreach (var difficulty in new[] { Difficulty.Oni, Difficulty.Ura })
                    {
                        var record = info.Has(difficulty) ? SongScores.Get(song, difficulty) : null;
                        if (record != null) available.Add((difficulty, record));
                    }
                    if (available.Count == 0)
                        for (var difficulty = Difficulty.Hard; difficulty >= Difficulty.Easy; difficulty--)
                        {
                            var record = info.Has(difficulty) ? SongScores.Get(song, difficulty) : null;
                            if (record == null) continue;
                            available.Add((difficulty, record));
                            break;
                        }
                }
            }
            group.alpha = HasRecord ? alpha : 0;
            if (!HasRecord) return;
            int index = (int)((now - started) / Mathf.Max(.1f, cycleSeconds)) % available.Count;
            if (index == shown) return;
            shown = index;
            var current = available[index];
            DisplayedDifficulty = current.Difficulty; DisplayedScore = current.Record.score;
            difficulty.sprite = difficultySprites[(int)DisplayedDifficulty];
            judgments.Show(current.Record.good, current.Record.ok, current.Record.bad, current.Record.rolls);
            string value = DisplayedScore.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int i = 0; i < digits.Length; i++)
            {
                int position = i - (digits.Length - value.Length);
                digits[i].enabled = position >= 0;
                if (position >= 0)
                    JudgeCounterView.PlaceDigit(digits[i], numbers[value[position] - '0'], position,
                        value.Length, scoreCount.rect.height, judgments.pitch);
            }
        }
    }
}

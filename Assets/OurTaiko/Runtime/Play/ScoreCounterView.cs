using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // ScoreCounter: lane_score_cover, then score_number digits right-aligned at x 255 in the lane. Each
    // change restarts the text stretch (digits grow upwards). Runs on real time like the original's
    // current_ms; Nijiiro's delay_score_addition is off, so the count follows the score at once.
    [RequireComponent(typeof(RectTransform), typeof(ClipSampler), typeof(AnimatedFloat))]
    public sealed class ScoreCounterView : MonoBehaviour
    {
        public Image cover;
        [Tooltip("score_number frames 0-9.")]
        public Sprite[] digits;
        public ScoreAdditionView additionTemplate;
        public PlayScoreRankView scoreRank;

        readonly List<Image> images = new List<Image>();
        readonly List<ScoreAdditionView> additions = new List<ScoreAdditionView>();
        public IReadOnlyList<ScoreAdditionView> Additions => additions;
        int score = -1;
        double changedAt = double.NegativeInfinity;

        public string Text { get; private set; } = "";
        public float Stretch { get; private set; }
        public Image Digit(int index) => images[index];

        public void Show(int value, int kiwamiThreshold = 1000000)
        {
            if (scoreRank != null) scoreRank.ShowScore(value, GameTimeline.FrameTime, kiwamiThreshold);
            if (value == score) return;
            int increase = score >= 0 ? value - score : 0;
            if (increase < 0) ClearAdditions();
            // ScoreCounter starts at 0 without a stretch; later changes restart it.
            if (score >= 0) changedAt = GameTimeline.FrameTime;
            score = value;
            Text = ScoreCounterLayout.Text(value);
            while (images.Count < Text.Length) images.Add(SkinUi.Image("Digit" + images.Count, transform, null));
            for (int i = 0; i < images.Count; i++)
            {
                bool shown = i < Text.Length;
                images[i].enabled = shown;
                if (shown) images[i].sprite = digits[Text[i] - '0'];
            }
            Layout(SampleStretch());
            if (increase > 0 && additionTemplate != null)
            {
                if (additions.Count == 0) additions.Add(additionTemplate);
                var row = additions.Find(item => !item.gameObject.activeSelf);
                if (row == null)
                {
                    row = Instantiate(additionTemplate, additionTemplate.transform.parent);
                    row.name = "ScoreAddition" + additions.Count;
                    additions.Add(row);
                }
                row.Begin(increase, digits, GameTimeline.FrameTime);
            }
        }

        public void ClearAdditions()
        {
            foreach (var row in additions) row.gameObject.SetActive(false);
        }

        void Update()
        {
            if (score < 0) return;
            float stretch = SampleStretch();
            if (stretch != Stretch) Layout(stretch);
            foreach (var row in additions) if (row.gameObject.activeSelf) row.ShowTime(GameTimeline.FrameTime);
        }

        // TextStretch.anim (TextStretchAnimation id 4): how many pixels the digits grow upwards.
        float SampleStretch()
        {
            var sampler = GetComponent<ClipSampler>();
            sampler.Sample(System.Math.Min(GameTimeline.FrameTime - changedAt, sampler.clip.length));
            return GetComponent<AnimatedFloat>().value;
        }

        void Layout(float stretch)
        {
            Stretch = stretch;
            for (int i = 0; i < Text.Length; i++)
            {
                var rect = images[i].rectTransform;
                rect.sizeDelta = new Vector2(ScoreCounterLayout.DigitWidth, ScoreCounterLayout.DigitHeight + stretch);
                rect.TopLeft(ScoreCounterLayout.DigitLeft(i, Text.Length), ScoreCounterLayout.DigitTop - stretch);
            }
        }
    }
}

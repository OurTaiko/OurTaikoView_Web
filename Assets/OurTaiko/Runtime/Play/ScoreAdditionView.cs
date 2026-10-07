using System;
using UnityEngine;

namespace OurTaiko
{
    // Nijiiro ScoreCounterAnimation IDs 35-39, using the total score's sprites and coordinate system.
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(ClipSampler))]
    public sealed class ScoreAdditionView : MonoBehaviour
    {
        public UnityEngine.UI.Image[] digits;
        // Driven by ScoreAddition.anim, relative to the normal score digit baseline.
        public float horizontalOffset, verticalOffset;
        public string Text { get; private set; } = "";
        double startedAt;

        public void Begin(int points, Sprite[] sprites, double now)
        {
            Text = points.ToString(System.Globalization.CultureInfo.InvariantCulture);
            startedAt = now;
            for (int i = 0; i < digits.Length; i++)
            {
                digits[i].enabled = i < Text.Length;
                if (i < Text.Length) digits[i].sprite = sprites[Text[i] - '0'];
            }
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            // Unity resolves a nested Canvas only after its pooled row is activated.
            var canvas = GetComponent<Canvas>();
            if (canvas != null)
            {
                canvas.overrideSorting = true;
                canvas.sortingOrder = transform.parent.GetComponentInParent<Canvas>().sortingOrder + 1;
            }
            ShowTime(now);
        }

        public void ShowTime(double now)
        {
            var sampler = GetComponent<ClipSampler>();
            double elapsed = Math.Max(0, now - startedAt);
            if (elapsed >= sampler.clip.length) { gameObject.SetActive(false); return; }
            sampler.Sample(elapsed);
            for (int i = 0; i < Text.Length; i++)
                digits[i].rectTransform.TopLeft(ScoreCounterLayout.DigitLeft(i, Text.Length) + horizontalOffset,
                    ScoreCounterLayout.DigitTop + verticalOffset);
        }
    }
}

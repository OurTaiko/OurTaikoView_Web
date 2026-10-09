using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace OurTaiko
{
    [RequireComponent(typeof(ClipSampler), typeof(AnimatedFloat))]
    public sealed class DrumrollCounterView : MonoBehaviour
    {
        public UnityEngine.UI.Image bubble;
        public RectTransform number;
        public CanvasGroup visuals;
        public Sprite[] digitSprites;
        public Vector2 digitSize = new Vector2(96, 112);

        public int NoteIndex { get; private set; } = -1;
        public int Count { get; private set; }
        public bool IsVisible => NoteIndex >= 0;
        readonly List<UnityEngine.UI.Image> digits = new List<UnityEngine.UI.Image>();
        double lastHitTime;
        int digitCount;

        public void ResetDisplay()
        {
            NoteIndex = -1;
            Count = 0;
            visuals.alpha = 0;
            gameObject.SetActive(false);
        }

        public void RecordHit(int index, int hits, double time)
        {
            if (hits <= 0) return;
            NoteIndex = index;
            Count = hits;
            lastHitTime = time;
            SetNumber();
            gameObject.SetActive(true);
            ShowTime(time);
        }

        void SetNumber()
        {
            string text = Count.ToString(CultureInfo.InvariantCulture);
            digitCount = text.Length;
            while (digits.Count < digitCount)
            {
                var rect = new GameObject("Digit" + digits.Count, typeof(RectTransform)).GetComponent<RectTransform>();
                rect.SetParent(number, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
                image.raycastTarget = false;
                digits.Add(image);
            }
            // Preserve the fan's layout independently of the shared sheet's source resolution.
            float advance = digitSize.x * (80f / 96f);
            for (int i = 0; i < digits.Count; i++)
            {
                digits[i].gameObject.SetActive(i < digitCount);
                if (i >= digitCount) continue;
                digits[i].sprite = digitSprites[text[i] - '0'];
                digits[i].rectTransform.anchoredPosition = new Vector2(-digitCount * advance / 2 + i * advance, 0);
            }
        }

        public void ShowTime(double time)
        {
            if (!IsVisible) return;
            var sampler = GetComponent<ClipSampler>();
            double elapsed = Math.Max(0, time - lastHitTime);
            if (elapsed >= sampler.clip.length)
            {
                ResetDisplay();
                return;
            }
            sampler.Sample(elapsed);
            float stretch = GetComponent<AnimatedFloat>().value;
            for (int i = 0; i < digitCount; i++)
            {
                var rect = digits[i].rectTransform;
                Vector2 size = digitSize;
                float offset = stretch * size.y / 112f;
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, offset);
                rect.sizeDelta = new Vector2(size.x, size.y + offset);
            }
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Refined judgement counter (user design after the arcade's): a translucent orange panel above the
    // 1P cover with 良 / 可 / 不可 / 連打数 bars (labels cut from result/score/max_combo_ja) and each
    // count right-aligned in the score counter's score_number digits. Replaces the original's
    // percentage JudgeCounter, which Nijiiro does not skin. Everything but the digits is saved in the
    // scene; the digits hang from each row's `counts` rect, right edge on its right edge.
    public sealed class JudgeCounterView : MonoBehaviour
    {
        [Tooltip("score_number frames 0-9 (the score counter's digits).")]
        public Sprite[] digits;
        [Tooltip("Good, ok, bad, drumroll: the digits are right-aligned in each rect and fill its height.")]
        public RectTransform[] counts = new RectTransform[4];
        [Tooltip("Distance between digit starts, as a fraction of the digit height (score counter: 30 / 64).")]
        public float pitch = 30f / 64;

        readonly List<Image>[] images = { new List<Image>(), new List<Image>(), new List<Image>(), new List<Image>() };
        readonly int[] shown = { -1, -1, -1, -1 };

        public string Text(int row) => shown[row] < 0 ? "" : shown[row].ToString();
        public Image Digit(int row, int index) => images[row][index];

        public void Show(int good, int ok, int bad, int drumrolls)
        {
            ShowRow(0, good); ShowRow(1, ok); ShowRow(2, bad); ShowRow(3, drumrolls);
        }

        void ShowRow(int row, int value)
        {
            value = Mathf.Max(0, value);
            if (shown[row] == value) return;
            shown[row] = value;
            var list = images[row];
            if (list.Count == 0) foreach (Transform child in counts[row]) list.Add(child.GetComponent<Image>());
            string text = value.ToString();
            while (list.Count < text.Length) list.Add(SkinUi.Image("Digit" + list.Count, counts[row], null));
            for (int i = 0; i < list.Count; i++)
            {
                bool used = i < text.Length;
                list[i].gameObject.SetActive(used);
                if (used) PlaceDigit(list[i], digits[text[i] - '0'], i, text.Length, counts[row].rect.height, pitch);
            }
        }

        // Digit i of n, right-aligned: its right edge is (n - 1 - i) pitches left of the rect's right edge.
        public static void PlaceDigit(Image digit, Sprite sprite, int index, int count, float height, float pitch)
        {
            digit.sprite = sprite;
            var rect = digit.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0.5f);
            rect.pivot = new Vector2(1, 0.5f);
            float scale = height / sprite.rect.height;
            rect.sizeDelta = sprite.rect.size * scale;
            rect.anchoredPosition = new Vector2(-(count - 1 - index) * pitch * height, 0);
        }
    }
}

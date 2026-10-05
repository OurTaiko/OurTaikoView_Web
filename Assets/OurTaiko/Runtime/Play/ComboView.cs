using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Combo (combo.cpp) on the drum: combo_ja over a row of counter digits centred on the drum. Below
    // goldFrom the digits are white, from silverFrom silver (Nijiiro combo_color_tiers); from goldFrom
    // they are gold with combo_100_ja and the glimmer above them. Hidden below `minimum` (skin_config
    // combo_min). Every change restarts the text stretch (digits grow upwards, real time like the
    // original's current_ms); the glimmer loops on the song clock. Everything but the digit sprites and
    // the visibility is saved in the scene, so positions can be edited there.
    [RequireComponent(typeof(RectTransform), typeof(ClipSampler), typeof(AnimatedFloat))]
    public sealed class ComboView : MonoBehaviour
    {
        [Tooltip("Smaller combos are hidden (Nijiiro skin_config combo_min).")]
        public int minimum = 10;
        [Tooltip("From these combos the digits are silver (counter_100) and gold (counter_gold, with combo_100 and the glimmer).")]
        public int silverFrom = 50, goldFrom = 100;
        [Tooltip("counter / counter_100 / counter_gold frames 0-9.")]
        public Sprite[] whiteDigits, silverDigits, goldDigits;
        public Sprite caption, goldCaption;
        public Image captionImage;
        [Tooltip("The digits are centred on this rect's x and hang from its top; extra digits are added at runtime.")]
        public RectTransform digitRow;
        [Tooltip("skin_config combo_margin: distance between digit starts.")]
        public float pitch = 52;
        [Tooltip("ComboGlimmer.anim: three rows of gleams rising and fading, 500 ms loop.")]
        public ClipSampler glimmer;

        readonly List<Image> digits = new List<Image>();
        int combo = -1;
        double changedAt = double.NegativeInfinity;

        public int Combo => combo;
        public string Text { get; private set; } = "";
        public float Stretch { get; private set; }
        public Image Digit(int index) => digits[index];

        public void Show(int value)
        {
            if (value == combo) return;
            // update_count: any change restarts the stretch, the first count does not.
            if (combo >= 0) changedAt = GameTimeline.FrameTime;
            combo = value;
            bool shown = value >= minimum;
            if (gameObject.activeSelf != shown) gameObject.SetActive(shown);
            if (!shown) return;
            bool gold = value >= goldFrom;
            var sprites = gold ? goldDigits : value >= silverFrom ? silverDigits : whiteDigits;
            captionImage.sprite = gold ? goldCaption : caption;
            glimmer.gameObject.SetActive(gold);
            Text = value.ToString();
            if (digits.Count == 0) foreach (Transform child in digitRow) digits.Add(child.GetComponent<Image>());
            while (digits.Count < Text.Length) digits.Add(SkinUi.Image("Digit" + digits.Count, digitRow, null));
            for (int i = 0; i < digits.Count; i++)
            {
                bool used = i < Text.Length;
                digits[i].gameObject.SetActive(used);
                if (used) digits[i].sprite = sprites[Text[i] - '0'];
            }
            Layout(SampleStretch());
        }

        public void ShowTime(double songTime)
        {
            if (!gameObject.activeSelf) return;
            float stretch = SampleStretch();
            if (stretch != Stretch) Layout(stretch);
            if (glimmer.gameObject.activeSelf) glimmer.SampleLoop(songTime);
        }

        // TextStretch.anim (TextStretchAnimation id 5, same 50 ms as the score counter's).
        float SampleStretch()
        {
            var sampler = GetComponent<ClipSampler>();
            sampler.Sample(System.Math.Min(GameTimeline.FrameTime - changedAt, sampler.clip.length));
            return GetComponent<AnimatedFloat>().value;
        }

        void Layout(float stretch)
        {
            Stretch = stretch;
            for (int i = 0; i < Text.Length; i++) PlaceDigit(digits[i], i, Text.Length, pitch, stretch);
        }

        // x = -(count x pitch)/2 + i x pitch from the row's centre; y - stretch with height + stretch.
        public static void PlaceDigit(Image digit, int index, int count, float pitch, float stretch)
        {
            var rect = digit.rectTransform;
            var size = digit.sprite.rect.size;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(size.x, size.y + stretch);
            rect.anchoredPosition = new Vector2(-count * pitch / 2 + index * pitch - (size.x - pitch) / 2, stretch);
        }
    }
}

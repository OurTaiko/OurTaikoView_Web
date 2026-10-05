using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // ComboAnnounce (combo_announce.cpp with Nijiiro's combo_announce.lua): every 100 combo the scroll
    // (announce_bg_1p) shows the count in announce_digit_1p and コンボ!, fading in over 100 ms, holding
    // until 1666.67 ms and fading out over 100 ms (ComboAnnounce.anim, song clock); a newer announce
    // replaces it. The combo's voice (Sounds/game/combo/<n>_1p) is returned once per announce. Drawn
    // after the soul gauge like the original's draw_overlays; positions are saved in the scene.
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup), typeof(ClipSampler))]
    public sealed class ComboAnnounceView : MonoBehaviour
    {
        const float Cell = 104, GroupCentre = 204, PitchWide = 64, PitchNarrow = 54, NarrowScale = 0.85f, NarrowX0 = -70;
        const float TextWide = 392, TextNarrow = 398, TextDx = -84, TextWidth = 123;

        [Tooltip("announce_digit_1p frames 0-9.")]
        public Sprite[] digits;
        [Tooltip("The digits and コンボ! are laid out from this rect's top-left (announce_digit x 362, y -196).")]
        public RectTransform number;
        public Image text;
        [Tooltip("Voices for 100, 200, ... combo: element i is (i + 1) x 100.")]
        public AudioClip[] voices;

        readonly List<Image> images = new List<Image>();
        double startedAt;

        public int Combo { get; private set; }
        public bool IsShowing => gameObject.activeSelf;

        // Combo % 100 == 0 starts a new announce; returns its voice (null if there is none).
        public AudioClip Announce(int combo, double songTime)
        {
            Combo = combo;
            startedAt = songTime;
            Layout(combo.ToString());
            gameObject.SetActive(true);
            ShowTime(songTime);
            int index = combo / 100 - 1;
            return voices != null && index >= 0 && index < voices.Length ? voices[index] : null;
        }

        public void ShowTime(double songTime)
        {
            if (!gameObject.activeSelf) return;
            var sampler = GetComponent<ClipSampler>();
            double elapsed = songTime - startedAt;
            if (elapsed >= sampler.clip.length) { gameObject.SetActive(false); return; }
            sampler.Sample(System.Math.Max(0, elapsed));
        }

        public void Hide() => gameObject.SetActive(false);

        // layout(): up to 3 digits 64 apart at full width, 4 condensed to 0.85 and 54 apart, more
        // condensed further to fit; コンボ! follows, squeezed by the same factor.
        public void Layout(string count)
        {
            int n = count.Length;
            float scale, pitch, firstCentre, textX;
            if (n <= 3) { scale = 1; pitch = PitchWide; firstCentre = GroupCentre - PitchWide * (n - 1) * 0.5f; textX = TextWide; }
            else if (n == 4) { scale = NarrowScale; pitch = PitchNarrow; firstCentre = GroupCentre + NarrowX0; textX = TextNarrow; }
            else
            {
                float k = 4f / n;
                pitch = PitchNarrow * k; scale = NarrowScale * k;
                firstCentre = GroupCentre + 92 - pitch * (n - 1); textX = TextNarrow;
            }
            float width = Cell * scale;
            if (images.Count == 0) foreach (Transform child in number) if (child != text.transform) images.Add(child.GetComponent<Image>());
            while (images.Count < n) images.Add(SkinUi.Image("Digit" + images.Count, number, null));
            for (int i = 0; i < images.Count; i++)
            {
                bool used = i < n;
                images[i].gameObject.SetActive(used);
                if (!used) continue;
                images[i].sprite = digits[count[i] - '0'];
                Place(images[i].rectTransform, firstCentre + pitch * i - width / 2, 0, width, Cell);
            }
            // announce_text sits at y -137, 59 below the digits' -196.
            Place(text.rectTransform, textX + scale * TextDx, 59, TextWidth * scale, text.sprite.rect.height);
            text.transform.SetAsLastSibling();
        }

        static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x, -y);
        }
    }
}

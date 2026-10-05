using System;
using UnityEngine;

namespace OurTaiko
{
    public sealed class SoulGaugeView : MonoBehaviour
    {
        [Serializable]
        public sealed class Style
        {
            public Sprite border, empty, grid;
            [Tooltip("SoulRainbow<Style>.anim: the full-gauge crossfade on RainbowA/RainbowB.")]
            public AnimationClip rainbow;
            public float clearLabelX;
        }

        public Style[] styles;
        public UnityEngine.UI.Image border, empty, red, clearCap, goldTop, goldBottom;
        public UnityEngine.UI.Image rainbowA, rainbowB, cellFade, grid, clearLabel, soul, fire, soulOverlay;
        [Tooltip("The Rainbow group holding RainbowA and RainbowB.")]
        public ClipSampler rainbowSampler;
        public Sprite redFade, capFade, goldFade, clearLit, clearDark, soulLit, soulDark;

        public const int Cells = 50, CellWidth = 21;
        // The rainbow clip: a 0.6 s intro (the 450 ms fade-in over the 75 ms crossfades), then a
        // 0.6 s loop of the eight frames that repeats for as long as the gauge stays full.
        const double RainbowIntro = 0.6, RainbowLoop = 0.6;
        public bool IsClear => points >= clearPoints;
        public bool IsFull => points >= SoulGauge.MaximumPoints;
        public int FilledCells => (int)Math.Floor(points * Cells / SoulGauge.MaximumPoints);
        double points, previousPoints, threshold, clearPoints, cellChangedAt = double.NegativeInfinity;
        double rainbowStartedAt = double.NaN;
        Style style;

        public void Initialize(double clearThreshold)
        {
            threshold = clearThreshold;
            clearPoints = clearThreshold * SoulGauge.MaximumPoints;
            style = styles[threshold < 0.7 ? 0 : threshold < 0.8 ? 1 : 2];
            border.sprite = style.border;
            empty.sprite = style.empty;
            grid.sprite = style.grid;
            rainbowSampler.clip = style.rainbow;
            clearLabel.rectTransform.anchoredPosition = new Vector2(style.clearLabelX, 74);
            points = previousPoints = 0;
            cellChangedAt = double.NegativeInfinity;
            rainbowStartedAt = double.NaN;
            ShowTime(0);
        }

        public void SetValue(double next, double time) => SetPoints(next * SoulGauge.MaximumPoints, time);

        // Called for every normal judgment. Long-note hits do not restart gauge cell animations.
        public void SetPoints(double next, double time)
        {
            next = Math.Max(0, Math.Min(SoulGauge.MaximumPoints, next));
            previousPoints = points;
            cellChangedAt = next > points ? time : double.NegativeInfinity;
            points = next;
            if (!IsFull) rainbowStartedAt = double.NaN;
            else if (double.IsNaN(rainbowStartedAt)) rainbowStartedAt = time;
            ShowTime(time);
        }

        public void ShowTime(double time)
        {
            if (style == null) return;
            int length = FilledCells;
            int clearCell = (int)Math.Round(threshold * Cells);
            // CellFade.anim: the newly filled cell fades in over 450 ms.
            var cellFadeClip = Sampler(cellFade);
            double cellElapsed = time - cellChangedAt;
            bool pending = length > (int)Math.Floor(previousPoints * Cells / SoulGauge.MaximumPoints) && cellElapsed < cellFadeClip.clip.length;
            int solid = pending ? length - 1 : length;

            // Same 50-cell grid and rounded first gold cell as Gauge::draw().
            Width(red, Math.Min(solid, clearCell - 1) * CellWidth);
            clearCap.enabled = IsClear && !(pending && length == clearCell);
            clearCap.rectTransform.anchoredPosition = new Vector2(738 + (clearCell - 1) * CellWidth, 63);
            int goldWidth = Math.Max(0, solid - clearCell) * CellWidth;
            Width(goldTop, goldWidth);
            Width(goldBottom, goldWidth);
            goldTop.rectTransform.anchoredPosition = new Vector2(738 + clearCell * CellWidth, 60);
            goldBottom.rectTransform.anchoredPosition = new Vector2(738 + clearCell * CellWidth, 26);

            rainbowA.enabled = rainbowB.enabled = IsFull;
            if (IsFull)
            {
                double elapsed = Math.Max(0, time - rainbowStartedAt);
                rainbowSampler.Sample(elapsed < RainbowIntro + RainbowLoop ? elapsed : RainbowIntro + (elapsed - RainbowIntro) % RainbowLoop);
            }

            // Nijiiro enables gauge_cell_fade_in: only the newly filled cell fades in.
            // There is no fixed-interval whole-bar yellow flash in the reference code.
            cellFade.enabled = pending;
            if (pending)
            {
                cellFade.sprite = length == clearCell ? capFade : length > clearCell ? goldFade : redFade;
                cellFade.rectTransform.anchoredPosition = new Vector2(738 + (length - 1) * CellWidth, length >= clearCell ? 60 : 27);
                cellFade.rectTransform.sizeDelta = cellFade.sprite.rect.size;
                cellFadeClip.Sample(Math.Max(0, cellElapsed));
            }
            Alpha(grid, 0.15f);
            clearLabel.sprite = IsClear ? clearLit : clearDark;
            soul.sprite = IsClear ? soulLit : soulDark;
            // SoulFire.anim (8 frames, 50 ms each) and SoulOverlay.anim (lit on frames 0, 1, 4, 5)
            // share the song clock.
            fire.enabled = IsFull;
            if (IsFull)
            {
                Sampler(fire).SampleLoop(time);
                Sampler(soulOverlay).SampleLoop(time);
            }
            else soulOverlay.enabled = false;
            Alpha(soulOverlay, 0.5f);
        }

        static ClipSampler Sampler(UnityEngine.UI.Image image) => image.GetComponent<ClipSampler>();

        static void Width(UnityEngine.UI.Image image, int width)
        {
            image.enabled = width > 0;
            var size = image.rectTransform.sizeDelta;
            image.rectTransform.sizeDelta = new Vector2(Math.Max(0, width), size.y);
        }
        static void Alpha(UnityEngine.UI.Image image, float alpha) => image.color = new Color(1, 1, 1, alpha);
    }
}

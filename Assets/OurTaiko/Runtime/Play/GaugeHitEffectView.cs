using UnityEngine;

namespace OurTaiko
{
    // gauge_hit_effect: one burst at a time on the soul badge, drawn over the flying notes.
    // GaugeHitEffect.anim holds the burst frames, size, tint and fade and the note's fade.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class GaugeHitEffectView : MonoBehaviour
    {
        public RectTransform lane;
        public UnityEngine.UI.Image burst, note;

        public bool IsPlaying { get; private set; }
        public bool IsBig { get; private set; }
        ClipSampler sampler;
        double start;

        // A new landing clears the previous burst and starts over.
        public void Play(Sprite noteSprite, bool big, double time)
        {
            note.sprite = noteSprite;
            IsBig = big;
            start = time;
            IsPlaying = true;
        }

        public void ResetDisplay() { IsPlaying = false; burst.enabled = note.enabled = false; }

        public void ShowTime(double time)
        {
            if (sampler == null) sampler = GetComponent<ClipSampler>();
            double t = time - start;
            // Erased on the update the fade-out finishes, so it is never drawn at zero.
            if (IsPlaying && t >= sampler.clip.length) IsPlaying = false;
            if (!IsPlaying)
            {
                burst.enabled = note.enabled = false;
                return;
            }
            var centre = NoteArcView.LaneToLocal(lane, (RectTransform)transform, GaugeHitEffectLayout.CentreX, GaugeHitEffectLayout.CentreY);
            burst.rectTransform.anchoredPosition = centre;
            note.rectTransform.anchoredPosition = centre;
            sampler.Sample(System.Math.Max(0, t));
        }
    }
}

using UnityEngine;

namespace OurTaiko
{
    // The face at the judge point. Judgment only loads hit_effect_* for 良 and 可;
    // 不可, timeouts and roll hits draw no face. HitFace.anim holds its timing.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class HitFaceView : MonoBehaviour
    {
        public UnityEngine.UI.Image image;
        public Sprite good, ok, goodBig, okBig;

        public bool IsPlaying { get; private set; }
        ClipSampler sampler;
        double start;

        void Awake()
        {
            sampler = GetComponent<ClipSampler>();
            image.enabled = false;
        }

        public void Play(Judgment result, bool big, double time)
        {
            if (result != Judgment.Good && result != Judgment.Ok) return;
            image.sprite = result == Judgment.Good ? (big ? goodBig : good) : (big ? okBig : ok);
            start = time;
            IsPlaying = true;
        }

        public void ResetDisplay() { IsPlaying = false; image.enabled = false; }

        public void ShowTime(double time)
        {
            double elapsed = time - start;
            // The clip switches the face off at its end (350 ms): the effect is then removed.
            if (IsPlaying && (elapsed < 0 || elapsed >= sampler.clip.length)) IsPlaying = false;
            if (IsPlaying) sampler.Sample(elapsed);
            else image.enabled = false;
        }
    }
}

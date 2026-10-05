using UnityEngine;

namespace OurTaiko
{
    // The additive ring drawn over the notes at the judge point. Like the face, Judgment only
    // loads outer_* for 良 and 可; 不可, timeouts and roll hits draw no ring. Each variant's clip
    // holds its four outer_* frames (animation 30), fade (animation 27) and 200 ms lifetime.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class HitRingView : MonoBehaviour
    {
        public UnityEngine.UI.Image image;
        public AnimationClip good, ok, goodBig, okBig;

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
            sampler.clip = result == Judgment.Good ? (big ? goodBig : good) : (big ? okBig : ok);
            start = time;
            IsPlaying = true;
        }

        public void ResetDisplay() { IsPlaying = false; image.enabled = false; }

        public void ShowTime(double time)
        {
            double elapsed = time - start;
            if (IsPlaying && (elapsed < 0 || elapsed >= sampler.clip.length)) IsPlaying = false;
            if (IsPlaying) sampler.Sample(elapsed);
            else image.enabled = false;
        }
    }
}

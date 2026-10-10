using UnityEngine;

namespace OurTaiko
{
    // One dancer variant. Its clip is the rig timeline at DancerTroupe.BeatFrames frames per
    // second: which frame each layer shows, where, and how squashed. The frames share one canvas
    // with the rig origin at the layer's pivot, so a frame swap alone keeps the dancer in place.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class DancerView : MonoBehaviour
    {
        [Tooltip("Rig frames in the clip, where the looped dance starts, and where the way out starts.")]
        public int frames, dance, leave;

        public DancerRig Rig => new DancerRig(frames, dance, leave);
        public int Frame { get; private set; } = int.MinValue;
        ClipSampler sampler;

        // A rig frame, or -1 while off stage.
        public void Show(int frame)
        {
            if (frame == Frame) return;
            Frame = frame;
            gameObject.SetActive(frame >= 0);
            if (frame < 0) return;
            if (sampler == null) sampler = GetComponent<ClipSampler>();
            // Mid-frame, clear of the keys' float rounding.
            sampler.Sample((frame + 0.5) / DancerTroupe.BeatFrames);
        }
    }
}

using UnityEngine;

namespace OurTaiko
{
    // Nijiiro game/gogo_time: entry resize (24/76), fire cels (68), and fireworks (23).
    // All clips use time since the applicable START, including when practice seeks backwards.
    public sealed class GogoView : MonoBehaviour
    {
        public UnityEngine.UI.Image lane, fire;
        public UnityEngine.UI.Image[] explosions;
        public ClipSampler entry, fireLoop, explosion;

        void Awake() => ResetDisplay();

        public void ResetDisplay()
        {
            lane.enabled = fire.enabled = false;
            foreach (var image in explosions) image.enabled = false;
        }

        public void ShowTime(PlaySession session, double time, bool preview = false)
        {
            var state = session.GogoStateAt(time, preview);
            double age = time - state.StartedAt;
            lane.enabled = fire.enabled = state.Active;
            if (state.Active)
            {
                entry.Sample(System.Math.Min(age, entry.clip.length));
                fireLoop.SampleLoop(age);
            }
            // Like Fireworks in the reference, END removes the lane/fire but lets an entry
            // explosion already in progress complete. A second START restarts all three.
            bool bursting = age >= 0 && age < explosion.clip.length;
            foreach (var image in explosions) image.enabled = bursting;
            if (bursting) explosion.Sample(age);
        }
    }
}

using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace OurTaiko
{
    // Applies one AnimationClip at a time chosen by code, through a manually evaluated
    // Playables graph (AnimationClip.SampleAnimation does not apply sprite keys). Views keep
    // their own clocks (song time, real time, restarts) and call Sample every frame.
    [ExecuteAlways]
    [RequireComponent(typeof(Animator))]
    public sealed class ClipSampler : MonoBehaviour
    {
        [Tooltip("The clip Sample plays; Play switches it, for example between variants.")]
        public AnimationClip clip;

        PlayableGraph graph;
        AnimationClipPlayable playable;
        AnimationClip bound;

        public static ClipSampler Attach(GameObject target, AnimationClip clip)
        {
            var sampler = target.AddComponent<ClipSampler>();
            sampler.clip = clip;
            return sampler;
        }

        public void Sample(double seconds)
        {
            if (clip == null) return;
            if (!graph.IsValid())
            {
                graph = PlayableGraph.Create(name + " Clip");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                AnimationPlayableOutput.Create(graph, "Clip", GetComponent<Animator>());
                bound = null;
            }
            if (bound != clip)
            {
                if (playable.IsValid()) playable.Destroy();
                playable = AnimationClipPlayable.Create(graph, clip);
                graph.GetOutput(0).SetSourcePlayable(playable);
                bound = clip;
            }
            playable.SetTime(seconds);
            graph.Evaluate();
        }

        // A looping clip on a clock that may start below zero (held on its first frame until then).
        public void SampleLoop(double seconds)
        {
            if (clip == null) return;
            double length = clip.length;
            Sample(length > 0 ? System.Math.Max(0, seconds) % length : 0);
        }

        // Switches to another clip (a variant) and samples it.
        public void Play(AnimationClip next, double seconds)
        {
            clip = next;
            Sample(seconds);
        }

        void OnDisable()
        {
            if (graph.IsValid()) graph.Destroy();
            bound = null;
        }
    }
}

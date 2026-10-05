using UnityEngine;

namespace OurTaiko
{
    // A number written by an AnimationClip (through ClipSampler) for a view to apply, when the
    // animated quantity is not a property of a single component, like a digit row's stretch.
    public sealed class AnimatedFloat : MonoBehaviour
    {
        public float value;
    }
}

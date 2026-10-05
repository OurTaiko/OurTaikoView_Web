using TMPro;
using UnityEngine;

namespace OurTaiko
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class FpsCounter : MonoBehaviour
    {
        const double SampleSeconds = 0.5;
        public float FramesPerSecond { get; private set; }
        TMP_Text label;
        double sampleStarted;
        int frames;

        void Awake() => label = GetComponent<TMP_Text>();

        void OnEnable()
        {
            sampleStarted = GameTimeline.Realtime;
            frames = 0;
            FramesPerSecond = 0;
            label.SetText("FPS --");
        }

        void Update()
        {
            frames++;
            double now = GameTimeline.Realtime;
            double elapsed = now - sampleStarted;
            if (elapsed < SampleSeconds) return;
            // Measure real frames over wall time, independently of song time or pause.
            FramesPerSecond = (float)(frames / elapsed);
            label.SetText("FPS {0:0}", FramesPerSecond);
            sampleStarted = now;
            frames = 0;
        }
    }
}

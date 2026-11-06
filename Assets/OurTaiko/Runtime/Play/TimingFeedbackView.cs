using System;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace OurTaiko
{
    public sealed class TimingFeedbackView : MonoBehaviour
    {
        public TextMeshProUGUI label;
        public const double Duration = .45;
        public static readonly Color EarlyColor = new Color32(55, 155, 255, 255);
        public static readonly Color LateColor = new Color32(255, 75, 65, 255);
        double startedAt = double.NegativeInfinity;
        public void Show(double offsetMs)
        {
            // Keep the raw sign even when a sub-ms value rounds to zero.
            label.text = (offsetMs < 0 ? "-" : "+")
                + Math.Round(Math.Abs(offsetMs), MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "ms";
            label.color = offsetMs < 0 ? EarlyColor : offsetMs > 0 ? LateColor : Color.white;
            label.UseUiFont();
            startedAt = GameTimeline.FrameTime;
            label.alpha = 1;
        }
        public void Clear() { startedAt = double.NegativeInfinity; label.alpha = 0; }
        void Awake() => Clear();
        void Update()
        {
            double elapsed = GameTimeline.FrameTime - startedAt;
            label.alpha = (float)Math.Clamp((Duration - elapsed) / .12, 0, 1);
        }
    }
}

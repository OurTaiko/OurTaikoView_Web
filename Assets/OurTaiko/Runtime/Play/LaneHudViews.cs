using UnityEngine;

namespace OurTaiko
{
    // One seat's HUD (LaneHud.prefab under Hud): the soul gauge, note arcs and the counters that
    // Player::draw_overlays puts on top of everything. They draw above the shared Kusudama, so they
    // cannot live inside NoteLane.prefab and form a second prefab beside it (LaneViews.hud).
    public sealed class LaneHudViews : MonoBehaviour
    {
        public SoulGaugeView soulGauge;
        public NoteArcView noteArcs;
        public GaugeHitEffectView gaugeHitEffect;
        public ComboAnnounceView comboAnnounce;
        public JudgeCounterView judgeCounter;
        public BalloonCounterView balloonCounter;
    }
}

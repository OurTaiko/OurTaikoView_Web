using UnityEngine;

namespace OurTaiko
{
    // One player's lane and the HUD that belongs to it (Player::draw and draw_overlays). PlayScene
    // derives from it, so the play scenes keep the 1P lane on the scene component as before; the
    // two-player scene adds a second LaneViews on the 2P lane (PlayScene.secondLane).
    public class LaneViews : MonoBehaviour
    {
        // Player::draw: lane/lane_difficulty frame = difficulty (Easy..Ura).
        public UnityEngine.UI.Image laneDifficulty;
        public ModifierBadgeView modifierBadges;
        public RectTransform noteLayer, barLayer, mojiLayer;
        public BalloonCounterView balloonCounter;
        public DrumrollCounterView drumrollCounter;
        public UnityEngine.UI.Image judgment;
        public HitFaceView hitFace;
        public HitRingView hitRing;
        public SoulGaugeView soulGauge;
        public NoteArcView noteArcs;
        [Tooltip("drum_don_l/r, drum_kat_l/r; each plays DrumFlash.anim from its own hit.")]
        public UnityEngine.UI.Image[] drumFlashes;
        public ScoreCounterView scoreCounter;
        public ComboView combo;
        public ComboAnnounceView comboAnnounce;
        public JudgeCounterView judgeCounter;
        public BranchLaneView branchLane;
        public GogoView gogo;
        public EndingView ending;
    }
}

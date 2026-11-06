using UnityEngine;

namespace OurTaiko
{
    // One player's lane and the HUD that belongs to it (Player::draw and draw_overlays). It sits on
    // the lane's NoteLane object: 1P's is PlaySceneBase.firstLane, and the two-player scene adds a
    // second one on NoteLane2P (PlayScene.secondLane). The HUD half is the seat's LaneHud (hud).
    public class LaneViews : MonoBehaviour
    {
        // Player::draw: lane/lane_difficulty frame = difficulty (Easy..Ura).
        public UnityEngine.UI.Image laneDifficulty;
        public ModifierBadgeView modifierBadges;
        public RectTransform noteLayer, barLayer, mojiLayer;
        public DrumrollCounterView drumrollCounter;
        public UnityEngine.UI.Image judgment;
        public HitFaceView hitFace;
        public HitRingView hitRing;
        [Tooltip("This seat's LaneHud under Hud (drawn above the shared Kusudama).")]
        public LaneHudViews hud;
        [Tooltip("drum_don_l/r, drum_kat_l/r; each plays DrumFlash.anim from its own hit.")]
        public UnityEngine.UI.Image[] drumFlashes;
        public ScoreCounterView scoreCounter;
        public ComboView combo;
        public TimingFeedbackView timingFeedback;
        public BranchLaneView branchLane;
        public GogoView gogo;
        public EndingView ending;

        // The HUD pieces live on LaneHud; these keep PlayerLane and the views reading them as before.
        public SoulGaugeView soulGauge => hud.soulGauge;
        public NoteArcView noteArcs => hud.noteArcs;
        public ComboAnnounceView comboAnnounce => hud.comboAnnounce;
        public JudgeCounterView judgeCounter => hud.judgeCounter;
        public BalloonCounterView balloonCounter => hud.balloonCounter;
    }
}

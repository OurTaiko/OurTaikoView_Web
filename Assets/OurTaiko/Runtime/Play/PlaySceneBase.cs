using UnityEngine;

namespace OurTaiko
{
    // What a PlayerLane needs from the scene that drives it: the note and judgment art, the drum
    // and pop sounds, the shared kusudama, and the play clock. PlayScene (single, double, practice)
    // and DanPlayScene (段位道場) derive from it; each keeps its own clock, music and flow.
    public abstract class PlaySceneBase : MonoBehaviour
    {
        [Tooltip("1P's lane: the LaneViews on NoteLane. DoublePlayScene's 2P is PlayScene.secondLane.")]
        public LaneViews firstLane;
        public AudioSource music, hitAudio;
        public AudioClip don, ka, balloonPop, kusudamaPop;
        [Tooltip("Played on top of the drum when an Ad-Lib is hit (OpenTaiko's Adlib sound). Left empty until we have our own.")]
        public AudioClip adLibHit;
        [Tooltip("Played on top of the drum when a mine is hit (OpenTaiko's bomb sound). Left empty until we have our own.")]
        public AudioClip mineHit;
        public HitSoundLibrary hitSounds;
        public Sprite[] laneDifficultySprites;
        public Sprite[] noteSprites;
        [Tooltip("Nijiiro expression frame 1, indexed by NoteKind like noteSprites; alternates every eighth note at 50 combo.")]
        public Sprite[] alternateNoteSprites;
        public Sprite[] rollBodySprites, rollTailSprites;
        public Sprite balloonTailSprite;
        public Sprite[] mojiSprites;
        public Sprite mojiRollSprite;
        public Sprite[] judgmentSprites;
        [Tooltip("The kusudama ball over the whole screen, shared by both seats.")]
        public KusudamaView kusudama;

        // The chart time judgments and effects use this frame, in seconds.
        internal abstract double ChartTimeNow { get; }
        internal abstract double JudgeOffset { get; }
        internal abstract bool AcceptsHits { get; }
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class SongBoardView : MonoBehaviour
    {
        [Serializable]
        public sealed class PlateView
        {
            public Difficulty difficulty;
            public CanvasGroup group;
            public Image plate, star, level, branch;
            public TextMeshProUGUI label;
            [HideInInspector] public float authoredContentX;
        }

        public SongDefinition song;
        public CanvasGroup group, contents;
        public Image glow, panel, crown;
        public ScoreRankView scoreRank;
        public Vector2 rankOpenOffset = new Vector2(0, 69);
        public TextMeshProUGUI title, subtitle;
        public PointerRelay click;
        public PlateView[] plates;
        [Tooltip("Additional height when the focused board opens.")]
        public float expansionHeight = 188;
        public Vector2 titleOpenOffset = new Vector2(0, 70);
        public Vector2 crownOpenOffset = new Vector2(0, 87);
        public float platePitch = 182;
        [HideInInspector] public Vector2 authoredWheelPosition;
        public RectTransform Root => (RectTransform)transform;
    }
}

using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    public sealed class SongSelectView : MonoBehaviour
    {
        [Serializable]
        public sealed class CourseCardView
        {
            public Image board, crown, star, level, bar, branch;
            public ScoreRankView scoreRank;
            public Image[] dots;
            public TextMeshProUGUI name;
            public PointerRelay click;
        }

        public SongBoardView boardPrefab;
        public SongBoardView[] songBoards;
        [Tooltip("Pooled for the online category folders and their もどる boards.")]
        public FolderBoardView folderPrefab;
        public CanvasGroup courseGroup;
        public Image mark, backboard, back, option, auto, frame, glow, balloon, uraChange;
        public TextMeshProUGUI header, headerSub;
        public CourseCardView[] cards;
        public OptionPanelView options;
        public NameplateView nameplate;
        public SongSelectOverlayView overlays;
        public SongBestScoreView bestScore;
        [Header("Wheel animation layout")]
        public Vector2 wheelCentre = new Vector2(960, 540);
        public float rowPitch = 135, expandGap = 120, rowCurve = 40;
    }
}

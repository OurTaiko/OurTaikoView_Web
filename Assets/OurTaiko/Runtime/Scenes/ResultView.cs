using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The result screen's saved hierarchy. Layout, static art and sizes are authored in the scene;
    // ResultScene binds these references, fills in the run (title, difficulty, digits, gauge art,
    // message) and drives alpha, scale, frames and the animated offsets from the saved positions.
    public sealed class ResultView : MonoBehaviour
    {
        // One copy of the animated backdrop (Background under the board, FadeIn over it). Clouds
        // and the clear Fuji are saved at their frame-0 places; the drift is added to those.
        [Serializable]
        public sealed class BackgroundView
        {
            public CanvasGroup group;
            public Image[] sky, skyClear;
            public Image fuji, fujiClear;
            public Image[] clouds, cloudsClear;
            public Image header;
        }

        [Serializable]
        public sealed class DigitRow
        {
            [Tooltip("Ones digit first.")]
            public Image[] digits;
        }

        [Header("Backdrop")]
        public BackgroundView background;
        public BackgroundView fadeIn;
        public Image success;

        [Header("Board")]
        public TextMeshProUGUI songTitle;
        public TextMeshProUGUI songNumber;
        public Image difficulty;
        [Tooltip("良, 可, 不可, 連打数, 最大コンボ.")]
        public DigitRow[] judgeRows;
        [Tooltip("Ones digit first.")]
        public Image[] scoreOutline, scoreFill;

        [Header("High score")]
        public RectTransform highScore;
        public CanvasGroup highScoreGroup;
        [Tooltip("Ones digit first.")]
        public Image[] highScoreDigits;

        public ScoreRankView scoreRank;

        [Header("Crown and message")]
        public Image crown;
        public Image crownFade, burstA, burstB, stars, shine, message;
        public TextMeshProUGUI messageText;

        [Header("Gauge")]
        [Tooltip("Gauge art (0 easy, 1 normal, 2 hard/oni) the clear marks are placed for; other arts shift them by their clear cell.")]
        public int gaugeArt = 2;
        public Image unfilled;
        public Image bar, clearTransition, clearTop, clearBottom;
        public Image[] rainbow;
        public Image overlay, clearCaption, soulFire, soul, soulSheen;

        [Header("Overlays")]
        public NameplateView nameplate;
        public TextMeshProUGUI freePlay;
        public Image touchArea;
        public PointerRelay touchRelay;
    }
}

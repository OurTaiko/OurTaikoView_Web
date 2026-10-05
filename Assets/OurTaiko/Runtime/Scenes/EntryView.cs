using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // The Entry screen's saved hierarchy. Layout, sprites, texts and sizes are authored in the
    // scene; EntryScene only binds these references and drives alpha, sprite frames and the board
    // list's slide offsets (added to each board's saved position).
    public sealed class EntryView : MonoBehaviour
    {
        // One mode board: plates, title and comment lines, and its tap area. The title moves
        // between the TitleClosed and TitleOpen markers with the board's openness; the tap area
        // grows from closedHitSize to openHitSize.
        [Serializable]
        public sealed class BoardView
        {
            public RectTransform root;
            [Tooltip("Scene loaded when this board is picked.")]
            public string scene;
            public bool practice;
            public Image cursor, closed, open, flash;
            public TextMeshProUGUI title;
            public TextMeshProUGUI[] info;
            public RectTransform titleOpen, titleClosed;
            public Image hit;
            public PointerRelay hitRelay;
            public Vector2 closedHitSize = new Vector2(964, 157), openHitSize = new Vector2(1050, 436);
        }

        [Header("Background")]
        public Image street;
        public Image streetLit;
        [Tooltip("entry_bg twinkles 0-3 and lantern glows 0-1.")]
        public Image[] twinkles, glows;

        [Header("Credit rows")]
        public RectTransform credit;
        public Image[] creditPills, creditFlashes;
        public TextMeshProUGUI[] creditLabels, creditMessages, creditHighlights;

        [Header("Mode select")]
        [Tooltip("Boards are saved at their first layout: the first board open at the centre, the others closed in their list slots.")]
        public RectTransform modeBoards;
        public SwipeRelay boardSwipe;
        public BoardView[] boards;
        [Tooltip("Mode list's vertical safe area as fractions of the design stage, below the header and above the footer.")]
        public Vector2 modeSafeArea = new Vector2(0.15f, 0.925f);

        [Header("Global chrome")]
        public Image controlGuide;
        public NameplateView nameplate;
        public CanvasGroup nameplateGroup;
        public Image[] timerDigits;
        public TextMeshProUGUI freePlay;
        public Image qrChip, inviteBubble;
        public TextMeshProUGUI invitePlayer, inviteMessage;

        [Header("Touch")]
        public Image touchArea;
        public PointerRelay touchRelay;
        public SwipeRelay touchSwipe;
    }
}

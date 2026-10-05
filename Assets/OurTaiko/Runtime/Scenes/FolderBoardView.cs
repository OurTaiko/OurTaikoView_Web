using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OurTaiko
{
    // Generated/FolderBoard.prefab: a genre folder board (Nijiiro draw_folder_board) or, with its
    // folder layers hidden, the brown もどる board (draw_back_board). SongSelectScene binds pooled
    // instances while such a board is on screen and drives sizes, alpha and the characters.
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class FolderBoardView : MonoBehaviour
    {
        public CanvasGroup group;
        [Tooltip("bar_genre_overlay cursor glow, like the song board's.")]
        public Image glow;
        [Tooltip("folder_graphic (open look) under bar_genre (closed look); the closed one fades out as the board grows.")]
        public Image panelOpen, panelClosed;
        [Tooltip("box_chara left and right halves (960x480 each).")]
        public Image charaLeft, charaRight;
        public TextMeshProUGUI title, count;
        public PointerRelay click;
        [Tooltip("Additional height when the focused board opens (centre 52 -> 240).")]
        public float expansionHeight = 188;
        [Tooltip("text_kanban_title_genre rises by this much when open; the song count sits below the centre.")]
        public Vector2 titleOpenOffset = new Vector2(0, 94), countOpenOffset = new Vector2(0, -49);
        public RectTransform Root => (RectTransform)transform;
    }
}

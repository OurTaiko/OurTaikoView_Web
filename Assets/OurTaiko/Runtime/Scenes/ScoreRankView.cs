using UnityEngine;

namespace OurTaiko
{
    // Seven shared icon prefabs supply the artwork at every size. The saved Image keeps its
    // authored layout; the result screen alone has the extra animation layers.
    public sealed class ScoreRankView : MonoBehaviour
    {
        public UnityEngine.UI.Image image;
        public GameObject[] icons;
        public CanvasGroup group;
        public ScoreRankAnimation animationView;
        public int DisplayedRank { get; private set; }
        public Difficulty DisplayedDifficulty { get; private set; }
        int selected = -1;

        public void Show(int rank, Difficulty difficulty = Difficulty.Oni, double seconds = -1)
        {
            rank = Mathf.Clamp(rank, 0, ScoreRank.Count);
            if (rank != selected)
            {
                selected = rank;
                image.enabled = rank > 0 && animationView == null;
                if (rank > 0) image.sprite = icons[rank - 1].GetComponent<UnityEngine.UI.Image>().sprite;
            }
            DisplayedRank = rank;
            DisplayedDifficulty = difficulty;
            if (animationView != null) animationView.Show(rank, image.sprite, seconds);
        }
    }
}

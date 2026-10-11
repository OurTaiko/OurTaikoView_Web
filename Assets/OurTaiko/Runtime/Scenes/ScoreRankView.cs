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
        public ScoreRank DisplayedRank { get; private set; }
        public Difficulty DisplayedDifficulty { get; private set; }
        int selected = -1;

        public void Show(ScoreRank rank, Difficulty difficulty = Difficulty.Oni, double seconds = -1)
        {
            int index = Mathf.Clamp((int)rank, 0, ScoreRankUtil.Count);
            if (index != selected)
            {
                selected = index;
                image.enabled = index > 0 && animationView == null;
                if (index > 0) image.sprite = icons[index - 1].GetComponent<UnityEngine.UI.Image>().sprite;
            }
            DisplayedRank = (ScoreRank)index;
            DisplayedDifficulty = difficulty;
            if (animationView != null) animationView.Show(index, image.sprite, seconds);
        }
    }
}

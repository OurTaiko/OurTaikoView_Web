using TMPro;
using UnityEngine;

namespace OurTaiko
{
    // Saved only in PracticeScene, which is copied from SinglePlayScene by the Editor migration.
    public sealed class PracticeView : MonoBehaviour
    {
        public GameObject panel;
        public TMP_Text heading, value, hint;
        public UnityEngine.UI.Button previous, next, confirm;
        static readonly string[] Branches = { "普通譜面", "玄人譜面", "達人譜面" };
        public void Show(PracticeStage stage, PracticeProgress progress, BranchRoute branch)
        {
            panel.SetActive(true);
            switch (stage)
            {
                case PracticeStage.Branch:
                    heading.text = "谱面分支";
                    value.text = Branches[(int)branch];
                    hint.text = "咔：切换分支    咚：选择小节";
                    break;
                case PracticeStage.Speed:
                    heading.text = "播放速度";
                    value.text = progress.Speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x";
                    hint.text = "咔：调整速度    咚：开始练习";
                    break;
                default:
                    heading.text = "小节进度";
                    value.text = (progress.Measure + 1) + " / " + progress.Count;
                    hint.text = "咔：前后小节    咚：调整播放速度";
                    break;
            }
        }
    }
}

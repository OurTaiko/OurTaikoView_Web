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
        public void Show(bool speed, PracticeProgress progress)
        {
            panel.SetActive(true);
            heading.text = speed ? "播放速度" : "小节进度";
            value.text = speed ? progress.Speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "x"
                : (progress.Measure + 1) + " / " + progress.Count;
            hint.text = speed ? "咔：调整速度    咚：开始练习" : "咔：前后小节    咚：调整播放速度";
        }
    }
}

using UnityEngine;

namespace OurTaiko
{
    [RequireComponent(typeof(ClipSampler))]
    public sealed class BranchLaneView : MonoBehaviour
    {
        public UnityEngine.UI.Image background, previousLabel, currentLabel, levelChange;
        public Sprite normalLabel, expertLabel, masterLabel, expertBackground, masterBackground, levelUp, levelDown;

        // Written by BranchChange.anim: each label's distance from its saved position, in pixels.
        [SerializeField, HideInInspector] float previousOffset, currentOffset;

        ClipSampler sampler;
        BranchRoute route;
        Vector2 labelPosition;
        double changedAt;
        int direction;
        bool animating;

        void Awake() => labelPosition = currentLabel.rectTransform.anchoredPosition;

        public void Initialize(bool hasBranches)
        {
            gameObject.SetActive(hasBranches);
            if (!hasBranches) return;
            route = BranchRoute.Normal;
            animating = false;
            background.enabled = previousLabel.enabled = levelChange.enabled = false;
            currentLabel.sprite = normalLabel;
            currentLabel.color = Color.white;
            currentLabel.rectTransform.anchoredPosition = labelPosition;
        }

        public void Select(BranchRoute next, double time)
        {
            if (next == route) return;
            direction = next > route ? 1 : -1;
            previousLabel.sprite = currentLabel.sprite;
            previousLabel.enabled = levelChange.enabled = true;
            currentLabel.sprite = next == BranchRoute.Master ? masterLabel
                : next == BranchRoute.Expert ? expertLabel : normalLabel;
            background.sprite = next == BranchRoute.Master ? masterBackground : expertBackground;
            background.enabled = next != BranchRoute.Normal;
            levelChange.sprite = direction > 0 ? levelUp : levelDown;
            route = next;
            changedAt = time;
            animating = true;
            ShowTime(time);
        }

        public void ShowTime(double time)
        {
            if (!animating) return;
            sampler ??= GetComponent<ClipSampler>();
            float elapsed = (float)(time - changedAt);
            float length = sampler.clip.length;
            // BranchChange.anim, from Nijiiro game/animation.json IDs 41–45: 100 ms nudge, then
            // 133 ms slide/crossfade; the level badge pulses and fades out. The clip moves the two
            // labels by offsets from their saved position, signed here by the change's direction.
            sampler.Sample(Mathf.Min(elapsed, length));
            previousLabel.rectTransform.anchoredPosition = labelPosition + Vector2.down * (previousOffset * direction);
            currentLabel.rectTransform.anchoredPosition = labelPosition + Vector2.down * (currentOffset * direction);
            if (elapsed >= length)
            {
                levelChange.enabled = false;
                animating = false;
            }
        }
    }
}

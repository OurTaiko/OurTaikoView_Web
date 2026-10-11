using System;
using UnityEngine;

namespace OurTaiko
{
    // Nijiiro background/bg_objects/rank_badge.lua: restart the HUD clip only when
    // the score enters another rank. Practice can also lower/reset the score.
    [RequireComponent(typeof(ClipSampler))]
    public sealed class PlayScoreRankView : MonoBehaviour
    {
        public ScoreRankView rank;
        ClipSampler sampler;
        double changedAt;

        void Awake() => sampler = GetComponent<ClipSampler>();

        public void ShowScore(int score, double now, int kiwamiThreshold = 1000000)
        {
            ScoreRank next = ScoreRankUtil.FromScore(score, kiwamiThreshold);
            if (next == rank.DisplayedRank) return;
            rank.Show(next);
            changedAt = now;
            ShowTime(now);
        }

        public void ShowTime(double now)
        {
            if (rank.DisplayedRank == 0) return;
            if (sampler == null) sampler = GetComponent<ClipSampler>();
            sampler.Sample(Math.Max(0, Math.Min(now - changedAt, sampler.clip.length)));
        }

        void Update() => ShowTime(GameTimeline.FrameTime);
    }
}

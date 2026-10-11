using System;
using System.Collections.Generic;

namespace OurTaiko
{
    public enum ResultCue
    {
        CountLoopStart, CountLoopStop, AchieveSoul, RowLanded, ScoreLanded,
        HighScore, ScoreRank, Crown, Message, SuccessBackground,
    }

    // The single-player result reveal of scenes/result.cpp + objects/result/player.cpp driving the
    // Nijiiro result_player.lua state machine (frame counts on the cabinet's 120 fps script clock):
    // Fadein -> WaitUpdateTamashiiGage (100 f) -> gauge fill (7 f per cell) -> WaitUpdateScore (100 f)
    // -> rows land every 50 f, the total 100 f after the last row (UpdateScore lasts 500 f)
    // -> ScoreRank (2 s for a ranked score) -> Crown -> MoveResultAction (150 f for a clear).
    // Times are milliseconds since the scene started revealing.
    public sealed class ResultSequence
    {
        public const double Frame = 1000.0 / 120.0;
        public const double FadeInDelayMs = 100, FadeInMs = 316.67;             // result animation 15
        public const double FadeInEndMs = FadeInDelayMs + FadeInMs;
        public const double WaitGaugeMs = 100 * Frame, GaugeCellMs = 7 * Frame, WaitScoreMs = 100 * Frame;
        public const double RowMs = 50 * Frame, ScoreMs = 100 * Frame;
        public const double RankDurationMs = 2000;
        public const double ScoreToCrownMs = 500 * Frame, CrownToActionMs = 150 * Frame;
        public const double EnableSkipMs = 100 * Frame, WaitEffectEndMs = 500 * Frame, WaitNextSceneMs = 500 * Frame;
        public const double AutoNextSceneMs = 3600 * Frame;
        public const int Rows = 5; // good, ok, bad, drumroll, max combo

        readonly PlayResult result;
        readonly List<(ResultCue Cue, int Row)> cues = new List<(ResultCue, int)>();
        double? scoreDelay, rowDelay;
        bool gaugeDone, countLoop, achieved, crownShown, rankShown, messageShown, highScoreShown;

        public ResultSequence(PlayResult result) { this.result = result; }

        public double Now { get; private set; }
        public bool Skipped { get; private set; }
        public int GaugeShown { get; private set; }
        public bool GaugeStarted => Now >= FadeInEndMs;
        public int RowsLanded { get; private set; }
        public double[] RowLandMs { get; } = new double[Rows];
        public double? ScoreLandMs { get; private set; }
        public double? RainbowStartMs { get; private set; }
        public double? RankAtMs { get; private set; }
        public double? CrownAtMs { get; private set; }
        public double? MessageAtMs { get; private set; }
        public double? HighScoreAtMs { get; private set; }
        public bool FadeInFinished => Now >= FadeInEndMs;
        // 0 while the reveal runs (a drum press skips); afterwards the MoveResultAction beat.
        public double RevealEndMs => messageShown ? MessageAtMs.Value : 0;
        public bool CanAdvance => RevealEndMs > 0 && Now >= RevealEndMs + WaitEffectEndMs + WaitNextSceneMs;
        public bool ShouldAutoAdvance => RevealEndMs > 0 && Now >= RevealEndMs + WaitEffectEndMs + AutoNextSceneMs;
        public bool CanSkip => Now >= FadeInEndMs + EnableSkipMs;

        public IReadOnlyList<(ResultCue Cue, int Row)> Update(double now)
        {
            cues.Clear();
            Now = now;
            if (!FadeInFinished) return cues;
            int target = result.Cells;
            double elapsed = now - FadeInEndMs - WaitGaugeMs;
            if (Skipped) GaugeShown = target;
            else if (elapsed > 0)
            {
                if (!countLoop && !gaugeDone) { countLoop = true; cues.Add((ResultCue.CountLoopStart, 0)); }
                GaugeShown = Math.Min(target, (int)Math.Floor(elapsed / GaugeCellMs));
                if (!achieved && result.GaugeState == GaugeState.Full && GaugeShown >= PlayResult.GaugeCells)
                {
                    achieved = true; cues.Add((ResultCue.AchieveSoul, 0));
                }
            }
            if (GaugeShown >= target && !gaugeDone && (Skipped || elapsed > 0))
            {
                gaugeDone = true;
                scoreDelay = now + (Skipped ? 0 : WaitScoreMs);
                StopCountLoop();
            }
            if (result.GaugeState == GaugeState.Full && GaugeShown >= PlayResult.GaugeCells && RainbowStartMs == null)
                RainbowStartMs = now;

            if (scoreDelay.HasValue && now > scoreDelay.Value && rowDelay == null)
            {
                rowDelay = now;
                CrownAtMs = now + ScoreToCrownMs;
                if (result.Rank > 0)
                {
                    RankAtMs = CrownAtMs;
                    CrownAtMs += RankDurationMs;
                }
                MessageAtMs = CrownAtMs + (result.GaugeState != GaugeState.Failed ? CrownToActionMs : 0);
            }
            // ResultPlayer::update_score_animation with count_up_instant: each row lands whole.
            while (!Skipped && rowDelay.HasValue && RowsLanded <= Rows && now > rowDelay.Value)
            {
                if (RowsLanded < Rows) { RowLandMs[RowsLanded] = now; cues.Add((ResultCue.RowLanded, RowsLanded)); }
                else { ScoreLandMs = now; cues.Add((ResultCue.ScoreLanded, 0)); }
                RowsLanded++;
                rowDelay += RowsLanded == Rows ? ScoreMs : RowMs;
                if (RowsLanded > Rows) break;
            }
            if (Skipped && rowDelay.HasValue)
            {
                if (RankAtMs.HasValue) RankAtMs = Math.Min(RankAtMs.Value, now);
                CrownAtMs = Math.Min(CrownAtMs.Value, now);
                MessageAtMs = Math.Min(MessageAtMs.Value, now);
            }
            if (ScoreLandMs.HasValue && !highScoreShown && result.ScoreDifference > 0 && !result.AutoPlay)
            {
                highScoreShown = true; HighScoreAtMs = now; cues.Add((ResultCue.HighScore, 0));
            }
            if (RankAtMs.HasValue && now >= RankAtMs.Value && !rankShown)
            {
                rankShown = true;
                if (!Skipped) cues.Add((ResultCue.ScoreRank, 0));
            }
            if (CrownAtMs.HasValue && now >= CrownAtMs.Value && result.GaugeState != GaugeState.Failed && !crownShown)
            {
                crownShown = true; CrownAtMs = now; cues.Add((ResultCue.Crown, 0));
            }
            if (MessageAtMs.HasValue && now >= MessageAtMs.Value && !messageShown)
            {
                messageShown = true;
                cues.Add((ResultCue.Message, 0));
                if (result.GaugeState != GaugeState.Failed) cues.Add((ResultCue.SuccessBackground, 0));
            }
            return cues;
        }

        // isEffectSkip_: every remaining state collapses at once. The caller stops the count-up loop
        // and plays the single don_big that replaces the per-row sounds.
        public bool Skip()
        {
            if (Skipped || RevealEndMs > 0 || !CanSkip) return false;
            Skipped = true;
            countLoop = false;
            for (int i = RowsLanded; i < Rows; i++) RowLandMs[i] = Now;
            RowsLanded = Rows + 1;
            ScoreLandMs ??= Now;
            return true;
        }

        void StopCountLoop()
        {
            if (!countLoop) return;
            countLoop = false;
            cues.Add((ResultCue.CountLoopStop, 0));
        }
    }
}

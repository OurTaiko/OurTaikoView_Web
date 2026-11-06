using System;

namespace OurTaiko
{
    // Each control applies half the measured bias, at most once per offset for this run.
    public sealed class TimingCalibration
    {
        public int AudioOffsetMs { get; }
        public int JudgeOffsetMs { get; }
        public int? CorrectionMs { get; }
        public int? RecommendedAudioMs => Target(AudioOffsetMs);
        public int? RecommendedJudgeMs => Target(JudgeOffsetMs);
        public bool AudioApplied { get; private set; }
        public bool JudgeApplied { get; private set; }

        public TimingCalibration(TimingStatistics timing, int audioOffsetMs, int judgeOffsetMs)
        {
            AudioOffsetMs = audioOffsetMs; JudgeOffsetMs = judgeOffsetMs;
            if (timing?.MeanMs is double mean)
                CorrectionMs = (int)Math.Round(mean / 2, MidpointRounding.AwayFromZero);
        }
        int? Target(int original)
        {
            if (!CorrectionMs.HasValue) return null;
            long value = (long)original + CorrectionMs.Value;
            return value >= int.MinValue && value <= int.MaxValue ? (int)value : (int?)null;
        }
        public bool Matches(PlaySettings current) => current.audioOffsetMs == (AudioApplied ? RecommendedAudioMs : AudioOffsetMs)
            && current.judgeOffsetMs == (JudgeApplied ? RecommendedJudgeMs : JudgeOffsetMs);
        public bool CanApply(PlaySettings current, bool audio) => !(audio ? AudioApplied : JudgeApplied)
            && CorrectionMs.HasValue && CorrectionMs.Value != 0 && Matches(current)
            && (audio ? RecommendedAudioMs : RecommendedJudgeMs).HasValue;

        public bool TryApply(GameSettings current, bool audio, Action<GameSettings> save)
        {
            if (!CanApply(current.play, audio)) return false;
            var updated = current.Clone();
            if (audio) updated.play.audioOffsetMs = RecommendedAudioMs.Value;
            else updated.play.judgeOffsetMs = RecommendedJudgeMs.Value;
            save(updated);
            // Only consume this run's recommendation after the settings have been saved.
            if (audio) AudioApplied = true; else JudgeApplied = true;
            return true;
        }
    }
}

using System;

namespace OurTaiko
{
    // Song-select crown kinds (yellow_box crown_*); None is never stored for a failed run.
    public enum Crown { None, Clear, FullCombo, DonderfulCombo }

    // Result-screen gauge states of result_player.lua: failed, cleared (norma reached), full.
    public enum GaugeState { Failed, Cleared, Full }

    // Result screen bubble frames: miss, near, success, perfect.
    public enum ResultMessage { Miss, Near, Success, Perfect }

    // Everything the result scene needs from one finished play (src/libs/global_data.h ResultData).
    public sealed class PlayResult
    {
        public const int GaugeCells = 50;

        public string ChartKey = "", Title = "", Subtitle = "", Course = "Oni";
        // The side played (ChartSide), decided before the play; online scores upload to its block.
        public string Side = "single";
        public Difficulty Difficulty = Difficulty.Oni;
        public int Level, Score, Good, Ok, Bad, MaxCombo, Rolls, PreviousBest;
        // Ad-Libs hit 良 and 可 and mines hit, counted apart from Good/Ok/Bad (PlaySession).
        public int AdLibGood, AdLibOk, Mines;
        public int KiwamiThreshold = 1000000;
        public ScoreRank Rank => ScoreRankUtil.FromScore(Score, KiwamiThreshold);
        public double GaugePoints;
        public bool IsClear, IsGaugeFull, AutoPlay;
        // Played with the player's own judge windows (PlaySession.CustomWindows): never saved or uploaded.
        public bool CustomJudge;
        // Only a manual play at the standard windows is saved, uploaded or celebrated as a high score.
        public bool IsRecordable => !AutoPlay && !CustomJudge;
        public TimingStatistics Timing;
        public TimingCalibration Calibration;

        public GaugeState GaugeState => IsGaugeFull ? GaugeState.Full : IsClear ? GaugeState.Cleared : GaugeState.Failed;

        // result_player.lua: floor(gauge_length * 50 / 87), gauge_length = points / max * 87.
        public int Cells => Math.Min(GaugeCells, (int)Math.Floor(GaugePoints * GaugeCells / SoulGauge.MaximumPoints + 1e-4));

        // A full combo has no bad and no mine hit (OpenTaiko nMiss == 0 && nMine == 0); a donderful one no ok either.
        // Ad-Libs never matter.
        bool FullCombo => Bad == 0 && Mines == 0;

        // GameScreen::save_score: DFC with no ok/bad and FC with no bad are stored even for a failed gauge.
        public Crown StoredCrown => FullCombo && Ok == 0 ? Crown.DonderfulCombo : FullCombo ? Crown.FullCombo : IsClear ? Crown.Clear : Crown.None;

        // ResultPlayer ctor picks the same kind, but the result crown only appears for a cleared gauge.
        public Crown ResultCrown => !IsClear ? Crown.None : FullCombo && Ok == 0 ? Crown.DonderfulCombo : FullCombo ? Crown.FullCombo : Crown.Clear;

        // ResultMain.DisplayMessage: chosen from the soul gauge, not the crown.
        public ResultMessage Message => GaugeState == GaugeState.Full ? ResultMessage.Perfect
            : GaugeState == GaugeState.Cleared ? ResultMessage.Success
            : GaugePoints > SoulGauge.MaximumPoints * 0.3 ? ResultMessage.Near : ResultMessage.Miss;

        public int ScoreDifference => Math.Max(0, Score - PreviousBest);

        public static PlayResult From(PlaySession session, string chartKey, bool autoPlay, int? audioOffsetMs = null)
        {
            var chart = session.Chart;
            var timing = TimingStatistics.From(session);
            return new PlayResult
            {
                ChartKey = chartKey, Title = chart.Title, Subtitle = chart.Subtitle, Course = chart.Course,
                Difficulty = SongInfo.DifficultyOf(chart.Course) ?? Difficulty.Oni, Level = chart.Level,
                Score = session.Score, KiwamiThreshold = session.KiwamiThreshold,
                Good = session.Good, Ok = session.Ok, Bad = session.Bad,
                AdLibGood = session.AdLibGood, AdLibOk = session.AdLibOk, Mines = session.MinesHit,
                MaxCombo = session.MaxCombo, Rolls = session.Rolls, GaugePoints = session.GaugePoints,
                IsClear = session.IsClear, IsGaugeFull = session.IsGaugeFull, AutoPlay = autoPlay,
                CustomJudge = session.CustomJudge,
                Timing = timing,
                Calibration = !autoPlay && audioOffsetMs.HasValue
                    ? new TimingCalibration(timing, audioOffsetMs.Value, (int)Math.Round(session.JudgeOffset * 1000)) : null,
            };
        }
    }
}

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
        public Difficulty Difficulty = Difficulty.Oni;
        public int Level, Score, Good, Ok, Bad, MaxCombo, Rolls, PreviousBest;
        public double GaugePoints;
        public bool IsClear, IsGaugeFull, AutoPlay;

        public GaugeState GaugeState => IsGaugeFull ? GaugeState.Full : IsClear ? GaugeState.Cleared : GaugeState.Failed;

        // result_player.lua: floor(gauge_length * 50 / 87), gauge_length = points / max * 87.
        public int Cells => Math.Min(GaugeCells, (int)Math.Floor(GaugePoints * GaugeCells / SoulGauge.MaximumPoints + 1e-4));

        // GameScreen::save_score: DFC with no ok/bad and FC with no bad are stored even for a failed gauge.
        public Crown StoredCrown => Ok == 0 && Bad == 0 ? Crown.DonderfulCombo : Bad == 0 ? Crown.FullCombo : IsClear ? Crown.Clear : Crown.None;

        // ResultPlayer ctor picks the same kind, but the result crown only appears for a cleared gauge.
        public Crown ResultCrown => !IsClear ? Crown.None : Ok == 0 && Bad == 0 ? Crown.DonderfulCombo : Bad == 0 ? Crown.FullCombo : Crown.Clear;

        // ResultMain.DisplayMessage: chosen from the soul gauge, not the crown.
        public ResultMessage Message => GaugeState == GaugeState.Full ? ResultMessage.Perfect
            : GaugeState == GaugeState.Cleared ? ResultMessage.Success
            : GaugePoints > SoulGauge.MaximumPoints * 0.3 ? ResultMessage.Near : ResultMessage.Miss;

        public int ScoreDifference => Math.Max(0, Score - PreviousBest);

        public static PlayResult From(PlaySession session, string chartKey, bool autoPlay)
        {
            var chart = session.Chart;
            return new PlayResult
            {
                ChartKey = chartKey, Title = chart.Title, Subtitle = chart.Subtitle, Course = chart.Course,
                Difficulty = SongInfo.DifficultyOf(chart.Course) ?? Difficulty.Oni, Level = chart.Level,
                Score = session.Score, Good = session.Good, Ok = session.Ok, Bad = session.Bad,
                MaxCombo = session.MaxCombo, Rolls = session.Rolls, GaugePoints = session.GaugePoints,
                IsClear = session.IsClear, IsGaugeFull = session.IsGaugeFull, AutoPlay = autoPlay,
            };
        }
    }
}

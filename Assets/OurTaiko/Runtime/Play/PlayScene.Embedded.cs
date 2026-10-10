using UnityEngine;
namespace OurTaiko
{
    public sealed partial class PlayScene
    {
        public object EmbeddedState() => new { paused = IsPaused, autoPlay, time = SongTime, branch = Session?.CurrentBranch.ToString(), forcedBranch = Session?.ForcedBranch?.ToString(), stage = Practice != null ? PracticeStage.ToString() : null,
            good = Session?.Good ?? 0, bad = Session?.Bad ?? 0, score = Session?.Score ?? 0,
            position = Practice?.Position ?? 0, first = Practice?.First ?? 0, speed = Practice?.Speed ?? 1,
            drumVolume = Mathf.RoundToInt(SettingManager.EnsureInstance().Settings.audio.volume.drum * 100) };
        public void EmbeddedPause()
        {
            // A start that is still scrolling back to its lead-in is cancelled as well.
            if (Session != null && (!IsPaused || practiceRewinding) && !IsFinished) PausePractice(false);
        }
        public void EmbeddedStart()
        {
            if (Session == null || !IsPaused || IsFinished || pausePanel.activeSelf) return;
            pauseOpenedFrame = -1;
            PracticeStage = PracticeStage.Speed;
            ConfirmPractice();
        }
        public void EmbeddedRestart()
        {
            if (Session == null) return;
            if (pausePanel.activeSelf) { pausePanel.SetActive(false); RestorePracticePads(); }
            PausePractice(true);
        }
        public void EmbeddedStop()
        {
            songClock.Pause(); IsPaused = true;
            music.StopAudio(); hitAudio.StopAudio();
            enabled = false;
        }
    }
}

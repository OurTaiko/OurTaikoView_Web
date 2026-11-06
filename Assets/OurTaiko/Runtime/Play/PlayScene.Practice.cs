using System;
using System.Linq;
using UnityEngine;

namespace OurTaiko
{
    public sealed partial class PlayScene
    {
        public PracticeView practiceView;
        public bool IsPractice => practiceView != null;
        public PracticeProgress Practice { get; private set; }
        public PracticeStage PracticeStage { get; private set; }
        public bool ChoosingPracticeSpeed => PracticeStage == PracticeStage.Speed;
        // Practice never evaluates branches: every branch takes the route chosen in the menu.
        public BranchRoute PracticeBranch => practiceBranch;
        BranchRoute practiceBranch;
        PracticeStage FirstPracticeStage => Session.Chart.Branches.Count > 0 ? PracticeStage.Branch : PracticeStage.Measure;
        // The lane is scrolling back to the preparation lead-in; playback starts when it arrives.
        bool practiceRewinding;
        // The offsets in chart seconds at the chosen speed. The clock runs at it whenever it plays, so
        // these agree with ChartTime; a seek to a new speed must use them, not the clock's old rate.
        double AudioOffset => audioOffset * Practice.Speed;
        double VisualOffset => visualOffset * Practice.Speed;
        double PracticeJudgeOffset => judgeOffset * Practice.Speed;

        void InitializePractice()
        {
            Practice = new PracticeProgress();
            practiceView.previous.onClick.AddListener(() => MovePractice(-1));
            practiceView.next.onClick.AddListener(() => MovePractice(1));
            practiceView.confirm.onClick.AddListener(ConfirmPractice);
            PausePractice(true);
        }
        void RefreshPracticeBars() => Practice.SetBars(Session.Chart.Bars
            .Where(Session.IsPracticePreviewActive).Select(bar => bar.Time));

        void TogglePracticePause()
        {
            if (!IsPaused) { PausePractice(false); return; }
            if (practiceRewinding) return;
            if (pausePanel.activeSelf) { Resume(); return; }
            DisableDrumPads();
            pauseButton.interactable = false;
            practiceView.panel.SetActive(false);
            pauseOpenedFrame = Time.frameCount;
            pauseMenu.Show();
        }
        void PausePractice(bool first)
        {
            if (Practice == null) return;
            songClock.Pause(); IsPaused = true; practiceRewinding = false;
            music.StopAudio(); hitAudio.StopAudio();
            PracticeStage = FirstPracticeStage;
            if (first) Session = new PlaySession(Session.Chart, judgeOffset, practiceBranch, 1, judgeWindows);
            RefreshPracticeBars();
            // The cursor is visual chart time, so a bar aligns exactly even with configured offsets.
            double position = first ? Practice.First : SongTime - AudioOffset - VisualOffset;
            Practice.PauseAt(position, GameTimeline.FrameTime);
            songClock.Seek(position + AudioOffset + VisualOffset, Practice.Speed);
            ResetPracticeAttempt(position);
            pauseOpenedFrame = Time.frameCount;
            pauseButton.interactable = true;
            ShowPracticePause();
        }
        void ResetPracticeAttempt(double position)
        {
            var previous = Session;
            Session = PlaySession.PracticeAt(previous.Chart, position + VisualOffset, previous, practiceBranch, Practice.Speed);
            Lane1.ResetBranchLane(position + VisualOffset);
            Lane1.ResetAttempt();
        }
        void ShowPracticePause()
        {
            Practice.Update(GameTimeline.FrameTime);
            RenderNotes(Practice.Position);
            SampleDancers(Practice.Position);
            practiceView.Show(PracticeStage, Practice, PracticeBranch);
        }
        void UpdatePracticePause()
        {
            if (practiceRewinding) { UpdatePracticeRewind(); return; }
            ShowPracticePause();
            foreach (var press in InputManager.PressesThisFrame)
            {
                // Practice is single-player: the 2P drum does not reach its menu.
                if (press.Key == InputKey.LeftKa) { MovePractice(-1); return; }
                if (press.Key == InputKey.RightKa) { MovePractice(1); return; }
                if (press.Key == InputKey.LeftDon || press.Key == InputKey.RightDon || press.Key == InputKey.Confirm)
                { ConfirmPractice(); return; }
            }
        }
        bool CanAdjustPractice => IsPractice && Practice != null && IsPaused && !practiceRewinding && !pausePanel.activeSelf
            && !closingPauseMenu && !switcher.IsInputBlocked && pauseOpenedFrame != Time.frameCount;
        public void MovePractice(int direction)
        {
            if (!CanAdjustPractice) return;
            if (PracticeStage == PracticeStage.Speed) Practice.ChangeSpeed(Math.Sign(direction));
            else if (PracticeStage == PracticeStage.Branch) ChangePracticeBranch(Math.Sign(direction));
            else
            {
                Practice.Move(Math.Sign(direction), GameTimeline.FrameTime);
                ResetPracticeAttempt(Practice.Target);
                songClock.Seek(Practice.Target + AudioOffset + VisualOffset, Practice.Speed);
            }
            ShowPracticePause();
        }
        public void ConfirmPractice()
        {
            if (!CanAdjustPractice) return;
            pauseOpenedFrame = Time.frameCount;
            if (PracticeStage != PracticeStage.Speed) { PracticeStage++; ShowPracticePause(); return; }
            ResetPracticeAttempt(Practice.Target);
            practiceView.panel.SetActive(false);
            practiceRewinding = true;
            // Playback renders ChartTime - VisualOffset, so the scroll ends on the first played frame.
            Practice.Rewind(Practice.PlaybackStart(AudioOffset, VisualOffset, PracticeJudgeOffset) - AudioOffset - VisualOffset,
                GameTimeline.FrameTime);
        }
        void UpdatePracticeRewind()
        {
            Practice.Update(GameTimeline.FrameTime);
            RenderNotes(Practice.Position);
            SampleDancers(Practice.Position);
            if (Practice.Scrolling) return;
            practiceRewinding = false;
            music.pitch = (float)Practice.Speed;
            songClock.Seek(Practice.PlaybackStart(AudioOffset, VisualOffset, PracticeJudgeOffset), Practice.Speed);
            // The preparation lead-in may start in a different section than the practice target.
            Lane1.ResetBranchLane(ChartTime);
            songClock.Resume(GameTimeline.AudioNow);
            IsPaused = false;
            resumeFrame = Time.frameCount;
            StartDueMusic();
        }
        // A new route changes the notes and bar lines ahead, so the attempt and the bar list are rebuilt
        // at the same position.
        void ChangePracticeBranch(int direction)
        {
            var route = (BranchRoute)Math.Max((int)BranchRoute.Normal, Math.Min((int)BranchRoute.Master, (int)PracticeBranch + direction));
            if (route == PracticeBranch) return;
            practiceBranch = route;
            ResetPracticeAttempt(Practice.Target);
            RefreshPracticeBars();
            Practice.PauseAt(Practice.Target, GameTimeline.FrameTime);
        }
        void RestorePracticePads()
        {
            foreach (var pad in pausedPads) if (pad != null) pad.enabled = true;
            pausedPads.Clear(); pauseButton.interactable = true;
        }
        void ClosePracticeMenu()
        {
            if (!pausePanel.activeSelf) return;
            StartCoroutine(ClosePauseMenu(() =>
            {
                RestorePracticePads(); pauseOpenedFrame = Time.frameCount;
                ShowPracticePause();
            }));
        }
    }
}

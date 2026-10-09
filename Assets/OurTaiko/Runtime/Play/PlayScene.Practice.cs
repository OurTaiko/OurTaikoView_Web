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
        double AudioOffset => audioOffset;
        double VisualOffset => visualOffset;

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
            songClock.Pause(); IsPaused = true;
            music.StopAudio(); hitAudio.StopAudio();
            PracticeStage = FirstPracticeStage;
            if (first)
            {
                Session.Judged -= OnJudged;
                Session = new PlaySession(Session.Chart, judgeOffset, practiceBranch);
            }
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
            previous.Judged -= OnJudged;
            Session = PlaySession.PracticeAt(previous.Chart, position + VisualOffset, previous, practiceBranch);
            Session.Judged += OnJudged;
            Record.Inputs.Clear();
            nextAutoRight = true;
            lastCombo = 0;
            balloonCounter.ResetDisplay(); comboAnnounce.Hide();
            drumrollCounter.ResetDisplay();
            soulGauge.Initialize(Session.ClearThreshold);
            hitFace.ResetDisplay(); hitRing.ResetDisplay();
            if (noteArcs != null) noteArcs.ResetDisplay();
            feedbackTime = double.NegativeInfinity;
            judgmentFade ??= judgment.GetComponent<ClipSampler>();
            judgmentFade.Sample(judgmentFade.clip.length);
            for (int i = 0; i < flashedAt.Length; i++) { flashedAt[i] = double.NegativeInfinity; ShowFlash(i); }
            ResetPracticeBranchLane(position + VisualOffset);
            UpdateHud();
        }
        void ResetPracticeBranchLane(double time)
        {
            if (branchLane == null) return;
            branchLane.Initialize(Session.Chart.Branches.Count > 0);
            branchLane.SetImmediate(Session.DisplayBranchAt(time));
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
            ShowPracticePause();
            foreach (var press in InputManager.PressesThisFrame)
            {
                if (press.Key == InputKey.LeftKa) { MovePractice(-1); return; }
                if (press.Key == InputKey.RightKa) { MovePractice(1); return; }
                if (press.Key == InputKey.LeftDon || press.Key == InputKey.RightDon || press.Key == InputKey.Confirm)
                { ConfirmPractice(); return; }
            }
        }
        bool CanAdjustPractice => IsPractice && Practice != null && IsPaused && !pausePanel.activeSelf
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
            music.pitch = (float)Practice.Speed;
            songClock.Seek(Practice.PlaybackStart(AudioOffset, VisualOffset, judgeOffset), Practice.Speed);
            // The preparation lead-in may start in a different section than the practice target.
            ResetPracticeBranchLane(ChartTime);
            songClock.Resume(GameTimeline.AudioNow);
            IsPaused = false;
            practiceView.panel.SetActive(false);
            resumeFrame = Time.frameCount;
            ScheduleMusic();
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
